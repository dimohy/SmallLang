using System.Diagnostics;
using System.Text;
using SmallLang.Compiler.Diagnostics;

namespace SmallLang.Compiler.Tooling;

internal sealed class WindowsLinker(LlvmToolchain toolchain)
{
    public void LinkLlvmIr(string llPath, string outputPath, string workDir)
    {
        var objectPath = Path.Combine(workDir, Path.GetFileNameWithoutExtension(outputPath) + ".obj");
        var importLib = CreateKernel32ImportLibrary(workDir);

        Run(toolchain.Clang,
        [
            "-target",
            "x86_64-pc-windows-msvc",
            "-O3",
            "-fno-addrsig",
            "-mno-stack-arg-probe",
            "-c",
            llPath,
            "-o",
            objectPath
        ]);

        Run(toolchain.LldLink,
        [
            "/nologo",
            "/machine:x64",
            "/subsystem:console",
            "/entry:smalllang_start",
            "/nodefaultlib",
            "/opt:ref",
            "/opt:icf",
            "/fixed",
            "/merge:.rdata=.text",
            "/merge:.pdata=.text",
            "/merge:.xdata=.text",
            objectPath,
            importLib,
            "/out:" + outputPath
        ]);
    }

    private string CreateKernel32ImportLibrary(string workDir)
    {
        var defPath = Path.Combine(workDir, "kernel32.def");
        var libPath = Path.Combine(workDir, "kernel32.lib");
        File.WriteAllText(defPath, """
            LIBRARY kernel32.dll
            EXPORTS
            GetStdHandle
            ReadFile
            WriteFile
            CreateFileA
            CloseHandle
            SetFilePointerEx
            GetFileSizeEx
            GetTickCount64
            GetProcessHeap
            HeapAlloc
            HeapFree
            """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Run(toolchain.LlvmLib,
        [
            "/nologo",
            "/machine:x64",
            "/def:" + defPath,
            "/out:" + libPath
        ]);

        return libPath;
    }

    private static void Run(string fileName, IReadOnlyList<string> arguments)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)
            ?? throw new SmallLangException($"failed to start {fileName}");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var message = new StringBuilder();
            message.AppendLine($"{Path.GetFileName(fileName)} failed with exit code {process.ExitCode}");
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                message.AppendLine(stdout.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                message.AppendLine(stderr.TrimEnd());
            }

            throw new SmallLangException(message.ToString().TrimEnd());
        }
    }
}
