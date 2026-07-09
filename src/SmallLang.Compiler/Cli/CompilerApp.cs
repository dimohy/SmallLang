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
        var program = LoadProgram(options.SourcePath);
        var boundProgram = new SemanticCompiler(program).Compile();
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

    private static SmallLangProgram LoadProgram(string sourcePath)
    {
        var standardLibrary = LoadStandardLibrary(sourcePath);
        var sourceProgram = ParseSourceFile(sourcePath, isStandardLibrary: false);
        return new SmallLangProgram(
            standardLibrary.SelectMany(static program => program.Functions)
                .Concat(sourceProgram.Functions)
                .ToArray(),
            sourceProgram.Statements);
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
            Path.Combine(root, "stdlib", "sys", "file.sl")
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
}
