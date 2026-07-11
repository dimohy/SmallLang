using System.Globalization;
using System.Text;
using SmallLang.Compiler.CodeGen;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Lexing;
using SmallLang.Compiler.Parsing;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;
using SmallLang.Compiler.Tooling;

namespace SmallLang.Compiler.Cli;

internal static class CompilerApp
{
    public static int Run(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            Build(options);
            return 0;
        }
        catch (SmallLangException ex)
        {
            Console.Error.WriteLine($"smalllang: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"smalllang: unexpected failure: {ex.Message}");
            return 1;
        }
    }

    private static void Build(CliOptions options)
    {
        var program = LoadProgram(options.SourcePaths);
        var pointerBitWidth = options.Target == CompilationTarget.Wasm32Browser ? 32 : 64;
        var boundProgram = new SemanticCompiler(program, pointerBitWidth).Compile();
        var llvmIr = LlvmIrGenerator.GenerateProgram(boundProgram, options.Target);
        var toolchain = LlvmToolchain.From(options.LlvmHome);

        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)
            ?? Directory.GetCurrentDirectory());

        var workDir = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))
                ?? Directory.GetCurrentDirectory(),
            Path.GetFileNameWithoutExtension(options.OutputPath) + ".sl-tmp");

        if (Directory.Exists(workDir))
        {
            Directory.Delete(workDir, recursive: true);
        }

        Directory.CreateDirectory(workDir);

        try
        {
            var llPath = Path.Combine(workDir, Path.GetFileNameWithoutExtension(options.OutputPath) + ".ll");
            File.WriteAllText(llPath, llvmIr, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            LinkLlvmIr(options, toolchain, llPath, workDir);

            if (options.KeepTemps)
            {
                var keptLlPath = Path.ChangeExtension(options.OutputPath, ".ll");
                File.Copy(llPath, keptLlPath, overwrite: true);
            }
        }
        finally
        {
            if (!options.KeepTemps && Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }

        var exeInfo = new FileInfo(options.OutputPath);
        Console.WriteLine($"Wrote {exeInfo.FullName} ({exeInfo.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes)");
    }

    private static void LinkLlvmIr(CliOptions options, LlvmToolchain toolchain, string llPath, string workDir)
    {
        switch (options.Target)
        {
            case CompilationTarget.WindowsX64:
                new WindowsLinker(toolchain).LinkLlvmIr(llPath, options.OutputPath, workDir);
                break;
            case CompilationTarget.LinuxX64:
                new WslLinuxLinker(toolchain).LinkLlvmIr(llPath, options.OutputPath, workDir);
                break;
            case CompilationTarget.Wasm32Browser:
                new WasmBrowserLinker(toolchain).LinkLlvmIr(llPath, options.OutputPath, workDir);
                break;
            default:
                throw new SmallLangException($"unsupported target '{options.Target}'");
        }
    }

    private static SmallLangProgram LoadProgram(IReadOnlyList<string> sourcePaths)
    {
        var standardLibrary = LoadStandardLibrary(sourcePaths[0]);
        var sourcePrograms = LoadUserPrograms(sourcePaths);
        var executableFiles = sourcePrograms.Where(static source => source.Program.Statements.Count > 0).ToArray();
        if (executableFiles.Length > 1)
        {
            throw new SmallLangException("multiple source files contain executable top-level statements; exactly one root file may define the program entry point");
        }

        return new SmallLangProgram(
            [],
            [],
            standardLibrary.SelectMany(static program => program.Structs)
                .Concat(sourcePrograms.SelectMany(static source => source.Program.Structs))
                .ToArray(),
            standardLibrary.SelectMany(static program => program.Enums)
                .Concat(sourcePrograms.SelectMany(static source => source.Program.Enums))
                .ToArray(),
            standardLibrary.SelectMany(static program => program.Traits)
                .Concat(sourcePrograms.SelectMany(static source => source.Program.Traits))
                .ToArray(),
            standardLibrary.SelectMany(static program => program.Functions)
                .Concat(sourcePrograms.SelectMany(static source => source.Program.Functions))
                .ToArray(),
            sourcePrograms.SelectMany(static source => source.Program.Statements).ToArray());
    }

    private static IReadOnlyList<LoadedSource> LoadUserPrograms(IReadOnlyList<string> sourcePaths)
    {
        var loadedByPath = new Dictionary<string, LoadedSource>(StringComparer.OrdinalIgnoreCase);
        var modules = new Dictionary<string, LoadedSource>(StringComparer.Ordinal);
        foreach (var sourcePath in sourcePaths)
        {
            AddSource(Path.GetFullPath(sourcePath), expectedModule: null, loadedByPath, modules);
        }

        var roots = loadedByPath.Values.Where(static source => source.Program.Statements.Count > 0).ToArray();
        if (roots.Length > 1)
        {
            throw new SmallLangException("multiple source files contain executable top-level statements; exactly one root file may define the program entry point");
        }

        var root = roots.Length == 1
            ? roots[0]
            : loadedByPath[Path.GetFullPath(sourcePaths[0])];
        var moduleRoot = Path.GetDirectoryName(root.Path)
            ?? Directory.GetCurrentDirectory();
        var states = new Dictionary<string, ModuleVisitState>(StringComparer.OrdinalIgnoreCase);
        VisitImports(root, moduleRoot, loadedByPath, modules, states, []);
        return loadedByPath.Values.OrderBy(static source => source.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static LoadedSource AddSource(
        string path,
        string? expectedModule,
        IDictionary<string, LoadedSource> loadedByPath,
        IDictionary<string, LoadedSource> modules)
    {
        if (loadedByPath.TryGetValue(path, out var existing))
        {
            return existing;
        }
        if (!File.Exists(path))
        {
            throw new SmallLangException($"imported module file not found: {path}");
        }

        var program = ParseSourceFile(path, isStandardLibrary: false);
        var moduleName = string.Join('.', program.NamespacePath);
        if (expectedModule is not null && moduleName != expectedModule)
        {
            throw new SmallLangException(
                $"module file '{path}' declares namespace '{moduleName}' but import expects '{expectedModule}'");
        }
        if (moduleName.Length > 0
            && modules.TryGetValue(moduleName, out var duplicate)
            && !StringComparer.OrdinalIgnoreCase.Equals(duplicate.Path, path))
        {
            throw new SmallLangException(
                $"module '{moduleName}' is declared by both '{duplicate.Path}' and '{path}'");
        }

        var loaded = new LoadedSource(path, program);
        loadedByPath.Add(path, loaded);
        if (moduleName.Length > 0)
        {
            modules[moduleName] = loaded;
        }
        return loaded;
    }

    private static void VisitImports(
        LoadedSource source,
        string moduleRoot,
        IDictionary<string, LoadedSource> loadedByPath,
        IDictionary<string, LoadedSource> modules,
        IDictionary<string, ModuleVisitState> states,
        IReadOnlyList<string> chain)
    {
        if (states.TryGetValue(source.Path, out var state))
        {
            if (state == ModuleVisitState.Visiting)
            {
                throw new SmallLangException(
                    "module import cycle: " + string.Join(" -> ", chain.Append(ModuleName(source.Program))));
            }
            return;
        }

        states[source.Path] = ModuleVisitState.Visiting;
        var nextChain = chain.Append(ModuleName(source.Program)).ToArray();
        foreach (var import in source.Program.Imports)
        {
            if (import.Path.Count > 0 && import.Path[0] == "sys")
            {
                continue;
            }

            var moduleName = string.Join('.', import.Path);
            if (!modules.TryGetValue(moduleName, out var imported))
            {
                var modulePath = Path.Combine(
                    moduleRoot,
                    Path.Combine(import.Path.ToArray()) + ".sl");
                imported = AddSource(
                    Path.GetFullPath(modulePath),
                    moduleName,
                    loadedByPath,
                    modules);
            }
            VisitImports(imported, moduleRoot, loadedByPath, modules, states, nextChain);
        }
        states[source.Path] = ModuleVisitState.Visited;
    }

    private static string ModuleName(SmallLangProgram program)
    {
        return program.NamespacePath.Count == 0 ? "<root>" : string.Join('.', program.NamespacePath);
    }

    private static IReadOnlyList<SmallLangProgram> LoadStandardLibrary(string sourcePath)
    {
        var root = FindRepositoryRoot(sourcePath);
        var paths = new[]
        {
            Path.Combine(root, "stdlib", "sys", "runtime.sl"),
            Path.Combine(root, "stdlib", "sys", "io.sl"),
            Path.Combine(root, "stdlib", "sys", "random.sl"),
            Path.Combine(root, "stdlib", "sys", "time.sl"),
            Path.Combine(root, "stdlib", "sys", "file.sl"),
            Path.Combine(root, "stdlib", "sys", "process.sl")
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                throw new SmallLangException($"standard library file not found: {path}");
            }
        }

        return paths
            .Select(static path => ParseSourceFile(path, isStandardLibrary: true))
            .ToArray();
    }

    private static string FindRepositoryRoot(string sourcePath)
    {
        foreach (var start in GetRootSearchStarts(sourcePath))
        {
            for (var current = new DirectoryInfo(start); current is not null; current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "SmallLang.slnx"))
                    && Directory.Exists(Path.Combine(current.FullName, "stdlib")))
                {
                    return current.FullName;
                }
            }
        }

        throw new SmallLangException("could not locate the SmallLang standard library root");
    }

    private static IEnumerable<string> GetRootSearchStarts(string sourcePath)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        yield return Path.GetDirectoryName(sourceFullPath) ?? Directory.GetCurrentDirectory();
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
    }

    private static SmallLangProgram ParseSourceFile(string path, bool isStandardLibrary)
    {
        var sourceText = File.ReadAllText(path, Encoding.UTF8);
        var tokens = new Lexer(sourceText).Lex();
        return new Parser(tokens, isStandardLibrary).Parse();
    }

    private sealed record LoadedSource(string Path, SmallLangProgram Program);

    private enum ModuleVisitState
    {
        Visiting,
        Visited
    }
}
