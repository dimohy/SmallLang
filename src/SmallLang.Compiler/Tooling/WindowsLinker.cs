using System.Diagnostics;
using System.Text;
using SmallLang.Compiler.Diagnostics;

namespace SmallLang.Compiler.Tooling;

internal sealed class WindowsLinker(LlvmToolchain toolchain)
{
    public void LinkLlvmIr(string llPath, string outputPath, string workDir, string? optimizationLevel)
    {
        var objectPath = Path.Combine(workDir, Path.GetFileNameWithoutExtension(outputPath) + ".obj");
        var importLib = CreateKernel32ImportLibrary(workDir);
        var shellImportLib = CreateShell32ImportLibrary(workDir);
        var ucrtImportLib = CreateUcrtBaseImportLibrary(workDir);

        Run(toolchain.Clang,
        [
            "-target",
            "x86_64-pc-windows-msvc",
            optimizationLevel ?? "-O3",
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
            shellImportLib,
            ucrtImportLib,
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
            SetEndOfFile
            CreateFileMappingA
            MapViewOfFile
            FlushViewOfFile
            UnmapViewOfFile
            GetTickCount64
            Sleep
            GetCommandLineW
            WideCharToMultiByte
            MultiByteToWideChar
            GetEnvironmentVariableW
            GetLastError
            SetLastError
            LocalFree
            GetProcessHeap
            HeapAlloc
            HeapFree
            CreateThread
            WaitForSingleObject
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

    private string CreateShell32ImportLibrary(string workDir)
    {
        var defPath = Path.Combine(workDir, "shell32.def");
        var libPath = Path.Combine(workDir, "shell32.lib");
        File.WriteAllText(defPath, """
            LIBRARY shell32.dll
            EXPORTS
            CommandLineToArgvW
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

    private string CreateUcrtBaseImportLibrary(string workDir)
    {
        var defPath = Path.Combine(workDir, "ucrtbase.def");
        var libPath = Path.Combine(workDir, "ucrtbase.lib");
        File.WriteAllText(defPath, """
            LIBRARY ucrtbase.dll
            EXPORTS
            _wspawnvp
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
