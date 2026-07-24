using System.Diagnostics;
using System.Text;
using Sollang.Compiler.Diagnostics;

namespace Sollang.Compiler.Tooling;

internal sealed class WindowsLinker(LlvmToolchain toolchain)
{
    private const long TargetBytesPerPartition = 1024 * 1024;

    public void LinkLlvmIr(string llPath, string outputPath, string workDir, string? optimizationLevel)
    {
        var outputName = Path.GetFileNameWithoutExtension(outputPath);
        var objectPath = Path.Combine(workDir, outputName + ".obj");
        var importLib = CreateKernel32ImportLibrary(workDir);
        var shellImportLib = CreateShell32ImportLibrary(workDir);
        var ucrtImportLib = CreateUcrtBaseImportLibrary(workDir);

        var objects = optimizationLevel == "-O0"
            ? CompileSingleModule(llPath, objectPath, "-O0")
            : CompilePartitioned(llPath, workDir, outputName, optimizationLevel ?? "-O3");

        var linkArguments = new List<string>
        {
            "/nologo",
            "/machine:x64",
            "/subsystem:console",
            "/entry:sollang_start",
            "/nodefaultlib",
            "/opt:ref",
            "/opt:icf",
            "/fixed",
            "/stack:8388608,65536",
            "/merge:.rdata=.text",
            "/merge:.pdata=.text",
            "/merge:.xdata=.text",
        };
        linkArguments.AddRange(objects);
        linkArguments.AddRange([importLib, shellImportLib, ucrtImportLib, "/out:" + outputPath]);
        Run(toolchain.LldLink, linkArguments);
    }

    private IReadOnlyList<string> CompileSingleModule(string llPath, string objectPath, string optimizationLevel)
    {
        Run(toolchain.Clang,
        [
            "-target", "x86_64-pc-windows-msvc", optimizationLevel,
            "-fno-addrsig", "-mno-stack-arg-probe", "-Werror", "-Wno-override-module",
            "-c", llPath, "-o", objectPath
        ]);
        return [objectPath];
    }

    private IReadOnlyList<string> CompilePartitioned(
        string llPath,
        string workDir,
        string outputName,
        string optimizationLevel)
    {
        var processorCount = Math.Max(1, Environment.ProcessorCount);
        var maxPartitionCount = NativeJobLimit(processorCount);
        var llvmByteLength = new FileInfo(llPath).Length;
        // llvm-split and each clang process have a fixed cost. Keep small modules
        // whole and add parallelism only as the amount of IR grows.
        var sizeBasedPartitionCount = Math.Max(
            1,
            (int)Math.Min(
                processorCount,
                ((llvmByteLength - 1) / TargetBytesPerPartition) + 1));
        var partitionCount = Math.Min(maxPartitionCount, sizeBasedPartitionCount);
        if (partitionCount == 1)
        {
            return CompileSingleModule(
                llPath,
                Path.Combine(workDir, outputName + ".obj"),
                optimizationLevel);
        }

        var bitcodePath = Path.Combine(workDir, outputName + ".partition-input.bc");
        var partitionPrefix = Path.Combine(workDir, outputName + ".partition.bc");

        Run(toolchain.Clang,
        [
            "-target", "x86_64-pc-windows-msvc", "-O0", "-flto=full", "-emit-llvm",
            "-Werror", "-Wno-override-module", "-c", llPath, "-o", bitcodePath
        ]);
        Run(toolchain.LlvmSplit,
        [
            "-j", partitionCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--round-robin", "-o", partitionPrefix, bitcodePath
        ]);

        var objectPaths = Enumerable.Range(0, partitionCount)
            .Select(index => Path.Combine(workDir, $"{outputName}.partition.{index}.obj"))
            .ToArray();
        var completed = 0;
        var progressGate = new object();
        var tasks = Enumerable.Range(0, partitionCount).Select(index => Task.Run(() =>
        {
            var partitionPath = partitionPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Run(toolchain.Clang,
            [
                "-target", "x86_64-pc-windows-msvc", optimizationLevel,
                "-fno-addrsig", "-mno-stack-arg-probe", "-Werror", "-Wno-override-module",
                "-x", "ir", "-c", partitionPath, "-o", objectPaths[index]
            ]);
            lock (progressGate)
            {
                completed++;
                Console.WriteLine($"[native {completed}/{partitionCount}] optimized partition");
            }
        })).ToArray();
        Task.WhenAll(tasks).GetAwaiter().GetResult();
        return objectPaths;
    }

    private static int NativeJobLimit(int processorCount)
    {
        var configured = Environment.GetEnvironmentVariable("SOLLANG_NATIVE_JOBS");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return processorCount;
        }

        if (!int.TryParse(configured, out var requested) || requested < 1)
        {
            throw new SollangException("SOLLANG_NATIVE_JOBS must be a positive integer");
        }

        return Math.Min(requested, processorCount);
    }

    private string CreateKernel32ImportLibrary(string workDir)
    {
        var defPath = Path.Combine(workDir, "kernel32.def");
        var libPath = Path.Combine(workDir, "kernel32.lib");
        File.WriteAllText(defPath, """
            LIBRARY kernel32.dll
            EXPORTS
            GetStdHandle
            ExitProcess
            GetConsoleMode
            WriteConsoleW
            CreateProcessW
            GetExitCodeProcess
            ReadFile
            GetOverlappedResult
            WriteFile
            CreateFileA
            CreateFileW
            CloseHandle
            GetCurrentProcess
            DuplicateHandle
            SetFilePointerEx
            GetFileSizeEx
            GetFinalPathNameByHandleW
            GetFileInformationByHandle
            SetEndOfFile
            CreateFileMappingA
            MapViewOfFile
            FlushViewOfFile
            FlushFileBuffers
            MoveFileExA
            UnmapViewOfFile
            GetTickCount64
            Sleep
            GetCommandLineW
            WideCharToMultiByte
            MultiByteToWideChar
            GetEnvironmentVariableW
            GetLastError
            SetLastError
            FindFirstFileA
            FindNextFileA
            FindClose
            LocalFree
            GetProcessHeap
            HeapAlloc
            HeapFree
            CreateThread
            CreateEventA
            SetEvent
            SetConsoleMode
            ReadConsoleInputW
            CancelSynchronousIo
            ResetEvent
            CreateSemaphoreA
            ReleaseSemaphore
            GetActiveProcessorCount
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
            ?? throw new SollangException($"failed to start {fileName}");

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

            throw new SollangException(message.ToString().TrimEnd());
        }
    }
}
