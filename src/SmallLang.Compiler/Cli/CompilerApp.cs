using System.Globalization;
using System.Text;
using SmallLang.Compiler.CodeGen;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Lexing;
using SmallLang.Compiler.Parsing;
using SmallLang.Compiler.Semantics;
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
        var sourceText = File.ReadAllText(options.SourcePath, Encoding.UTF8);
        var tokens = new Lexer(sourceText).Lex();
        var program = new Parser(tokens).Parse();
        var boundProgram = new SemanticCompiler(program).Compile();
        var llvmIr = LlvmIrGenerator.GenerateWindowsConsoleProgram(boundProgram);
        var toolchain = LlvmToolchain.From(options.LlvmHome);
        var linker = new WindowsLinker(toolchain);

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
            linker.LinkLlvmIr(llPath, options.OutputPath, workDir);

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
}
