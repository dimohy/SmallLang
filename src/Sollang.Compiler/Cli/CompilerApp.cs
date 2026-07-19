using System.Globalization;
using System.Text;
using Sollang.Compiler.CodeGen;
using Sollang.Compiler.Diagnostics;
using Sollang.Compiler.Lexing;
using Sollang.Compiler.Parsing;
using Sollang.Compiler.Semantics;
using Sollang.Compiler.Syntax;
using Sollang.Compiler.Tooling;

namespace Sollang.Compiler.Cli;

internal static class CompilerApp
{
    public static int Run(string[] args)
    {
        try
        {
            if (args is ["--version"] or ["-v"])
            {
                var version = typeof(CompilerApp).Assembly.GetName().Version
                    ?? throw new SollangException("compiler version metadata is missing");
                Console.WriteLine($"Sollang {version.Major}.{version.Minor}");
                return 0;
            }
            if (args.Length >= 2 && args[0] == "grammar" && args[1] == "build")
            {
                GrammarCompiler.Build(args[2..]);
                return 0;
            }
            var options = CliOptions.Parse(args);
            Build(options);
            return 0;
        }
        catch (SollangException ex)
        {
            Console.Error.WriteLine($"sollang: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"sollang: unexpected failure: {ex.Message}");
            return 1;
        }
    }

    private static void Build(CliOptions options)
    {
        var program = LoadProgram(options.SourcePaths, options.Project);
        var pointerBitWidth = options.Target == CompilationTarget.Wasm32Browser ? 32 : 64;
        var boundProgram = new SemanticCompiler(program, pointerBitWidth).Compile();
        var toolchain = LlvmToolchain.From(options.LlvmHome);

        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)
            ?? Directory.GetCurrentDirectory());

        var workDir = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))
                ?? Directory.GetCurrentDirectory(),
            Path.GetFileNameWithoutExtension(options.OutputPath) + ".slg-tmp");

        if (Directory.Exists(workDir))
        {
            Directory.Delete(workDir, recursive: true);
        }

        Directory.CreateDirectory(workDir);

        try
        {
            var llPath = Path.Combine(workDir, Path.GetFileNameWithoutExtension(options.OutputPath) + ".ll");
            using (var writer = new StreamWriter(
                llPath,
                append: false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                LlvmIrGenerator.WriteProgram(boundProgram, options.Target, writer);
            }
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
                new WindowsLinker(toolchain).LinkLlvmIr(llPath, options.OutputPath, workDir, options.OptimizationLevel);
                break;
            case CompilationTarget.LinuxX64:
                new WslLinuxLinker(toolchain).LinkLlvmIr(llPath, options.OutputPath, workDir, options.OptimizationLevel);
                break;
            case CompilationTarget.Wasm32Browser:
                new WasmBrowserLinker(toolchain).LinkLlvmIr(llPath, options.OutputPath, workDir, options.OptimizationLevel);
                break;
            default:
                throw new SollangException($"unsupported target '{options.Target}'");
        }
    }

    private static SollangProgram LoadProgram(
        IReadOnlyList<string> sourcePaths,
        ProjectBuild? project)
    {
        var standardLibrary = LoadStandardLibrary(sourcePaths[0]);
        var sourcePrograms = LoadUserPrograms(sourcePaths, project);
        var executableFiles = sourcePrograms.Where(static source => source.Program.Statements.Count > 0).ToArray();
        if (executableFiles.Length > 1)
        {
            throw new SollangException("multiple source files contain executable top-level statements; exactly one root file may define the program entry point");
        }

        return new SollangProgram(
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

    private static IReadOnlyList<LoadedSource> LoadUserPrograms(
        IReadOnlyList<string> sourcePaths,
        ProjectBuild? project)
    {
        var loadedByPath = new Dictionary<string, LoadedSource>(StringComparer.OrdinalIgnoreCase);
        var modules = new Dictionary<string, LoadedSource>(StringComparer.Ordinal);
        foreach (var sourcePath in sourcePaths)
        {
            AddSource(
                Path.GetFullPath(sourcePath),
                expectedModule: null,
                project?.RootPackage,
                isDependencyRoot: false,
                loadedByPath,
                modules);
        }

        var roots = loadedByPath.Values.Where(static source => source.Program.Statements.Count > 0).ToArray();
        if (roots.Length > 1)
        {
            throw new SollangException("multiple source files contain executable top-level statements; exactly one root file may define the program entry point");
        }

        var root = roots.Length == 1
            ? roots[0]
            : loadedByPath[Path.GetFullPath(sourcePaths[0])];
        var moduleRoot = Path.GetDirectoryName(root.Path)
            ?? Directory.GetCurrentDirectory();
        var packagesByName = project?.Packages.ToDictionary(
            static package => package.Manifest.Name,
            StringComparer.Ordinal)
            ?? new Dictionary<string, ProjectPackage>(StringComparer.Ordinal);
        var states = new Dictionary<string, ModuleVisitState>(StringComparer.OrdinalIgnoreCase);
        VisitImports(root, moduleRoot, packagesByName, loadedByPath, modules, states, []);
        return loadedByPath.Values.OrderBy(static source => source.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static LoadedSource AddSource(
        string path,
        string? expectedModule,
        ProjectPackage? package,
        bool isDependencyRoot,
        IDictionary<string, LoadedSource> loadedByPath,
        IDictionary<string, LoadedSource> modules)
    {
        if (loadedByPath.TryGetValue(path, out var existing))
        {
            if (!SamePackage(existing.Package, package))
            {
                throw new SollangException(
                    $"source file '{path}' belongs to both project '{PackageName(existing.Package)}' "
                    + $"and project '{PackageName(package)}'");
            }
            return existing;
        }
        if (!File.Exists(path))
        {
            throw new SollangException($"imported module file not found: {path}");
        }

        var program = ParseSourceFile(path, isStandardLibrary: false);
        var moduleName = string.Join('.', program.NamespacePath);
        if (expectedModule is not null && moduleName != expectedModule)
        {
            throw new SollangException(
                $"module file '{path}' declares namespace '{moduleName}' but import expects '{expectedModule}'");
        }
        if (isDependencyRoot && program.Statements.Count > 0)
        {
            throw new SollangException(
                $"dependency product '{package!.Product.Name}' contains executable top-level statements: {path}");
        }
        if (moduleName.Length > 0
            && modules.TryGetValue(moduleName, out var duplicate)
            && !StringComparer.OrdinalIgnoreCase.Equals(duplicate.Path, path))
        {
            throw new SollangException(
                $"module '{moduleName}' is declared by both '{duplicate.Path}' and '{path}'");
        }

        var loaded = new LoadedSource(path, program, package);
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
        IReadOnlyDictionary<string, ProjectPackage> packagesByName,
        IDictionary<string, LoadedSource> loadedByPath,
        IDictionary<string, LoadedSource> modules,
        IDictionary<string, ModuleVisitState> states,
        IReadOnlyList<string> chain)
    {
        if (states.TryGetValue(source.Path, out var state))
        {
            if (state == ModuleVisitState.Visiting)
            {
                throw new SollangException(
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
            var importedPackage = source.Package;
            var importRoot = source.Package?.SourceRoot ?? moduleRoot;
            var isDependencyRoot = false;
            if (source.Package is not null
                && source.Package.Dependencies.TryGetValue(import.Path[0], out var dependency))
            {
                importedPackage = dependency;
                importRoot = dependency.SourceRoot;
                isDependencyRoot = import.Path.Count == 1;
            }
            else if (source.Package is not null
                     && packagesByName.ContainsKey(import.Path[0])
                     && !string.Equals(
                         import.Path[0],
                         source.Package.Manifest.Name,
                         StringComparison.Ordinal))
            {
                throw new SollangException(
                    $"project '{source.Package.Manifest.Name}' imports undeclared dependency '{import.Path[0]}'");
            }

            LoadedSource? imported = null;
            if (modules.TryGetValue(moduleName, out var existing)
                && SamePackage(existing.Package, importedPackage))
            {
                imported = existing;
            }
            if (imported is null)
            {
                var modulePath = isDependencyRoot
                    ? importedPackage!.Product.RootSource
                    : Path.Combine(importRoot, Path.Combine(import.Path.ToArray()) + ".slg");
                imported = AddSource(
                    Path.GetFullPath(modulePath),
                    moduleName,
                    importedPackage,
                    isDependencyRoot,
                    loadedByPath,
                    modules);
            }
            VisitImports(
                imported,
                moduleRoot,
                packagesByName,
                loadedByPath,
                modules,
                states,
                nextChain);
        }
        states[source.Path] = ModuleVisitState.Visited;
    }

    private static string ModuleName(SollangProgram program)
    {
        return program.NamespacePath.Count == 0 ? "<root>" : string.Join('.', program.NamespacePath);
    }

    private static bool SamePackage(ProjectPackage? left, ProjectPackage? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }
        return StringComparer.OrdinalIgnoreCase.Equals(left.Manifest.Path, right.Manifest.Path);
    }

    private static string PackageName(ProjectPackage? package) => package?.Manifest.Name ?? "<explicit-sources>";

    private static IReadOnlyList<SollangProgram> LoadStandardLibrary(string sourcePath)
    {
        var root = FindRepositoryRoot(sourcePath);
        var standardLibraryRoot = Path.Combine(root, "stdlib");
        var paths = Directory
            .EnumerateFiles(standardLibraryRoot, "*.slg", SearchOption.AllDirectories)
            .OrderBy(
                path => Path.GetRelativePath(standardLibraryRoot, path),
                StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0)
        {
            throw new SollangException(
                $"standard library contains no Sollang source modules: {standardLibraryRoot}");
        }

        var programs = new List<SollangProgram>(paths.Length);
        var modules = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var relativePath = Path.GetRelativePath(standardLibraryRoot, path);
            var expectedModule = string.Join(
                '.',
                Path.ChangeExtension(relativePath, extension: null)!
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var program = ParseSourceFile(path, isStandardLibrary: true);
            var actualModule = ModuleName(program);
            if (!string.Equals(actualModule, expectedModule, StringComparison.Ordinal))
            {
                throw new SollangException(
                    $"standard library file '{path}' declares namespace '{actualModule}' "
                    + $"but its path requires '{expectedModule}'");
            }
            if (program.Statements.Count > 0)
            {
                throw new SollangException(
                    $"standard library module '{actualModule}' contains executable top-level statements: {path}");
            }
            if (modules.TryGetValue(actualModule, out var duplicatePath))
            {
                throw new SollangException(
                    $"standard library module '{actualModule}' is declared by both "
                    + $"'{duplicatePath}' and '{path}'");
            }

            modules.Add(actualModule, path);
            programs.Add(program);
        }

        return programs;
    }

    private static string FindRepositoryRoot(string sourcePath)
    {
        foreach (var start in GetRootSearchStarts(sourcePath))
        {
            for (var current = new DirectoryInfo(start); current is not null; current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "Sollang.slnx"))
                    && Directory.Exists(Path.Combine(current.FullName, "stdlib")))
                {
                    return current.FullName;
                }
            }
        }

        throw new SollangException("could not locate the Sollang standard library root");
    }

    private static IEnumerable<string> GetRootSearchStarts(string sourcePath)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        yield return Path.GetDirectoryName(sourceFullPath) ?? Directory.GetCurrentDirectory();
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
    }

    private static SollangProgram ParseSourceFile(string path, bool isStandardLibrary)
    {
        var sourceText = File.ReadAllText(path, Encoding.UTF8);
        var tokens = new Lexer(sourceText).Lex();
        return new Parser(tokens, isStandardLibrary).Parse();
    }

    private sealed record LoadedSource(
        string Path,
        SollangProgram Program,
        ProjectPackage? Package);

    private enum ModuleVisitState
    {
        Visiting,
        Visited
    }
}
