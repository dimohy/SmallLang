using System.Diagnostics;
using System.Text;

var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var expectedDir = Path.Combine(repoRoot, "examples", "expected");
var artifactsDir = Path.Combine(repoRoot, "artifacts", "example-tests");
Directory.CreateDirectory(artifactsDir);
var llvmDir = Path.Combine(repoRoot, ".tools", "llvm-22.1.8");
var clangPath = Path.Combine(llvmDir, "bin", "clang.exe");
if (!File.Exists(clangPath))
{
    Console.Error.WriteLine($"LLVM toolchain not found: {clangPath}");
    Console.Error.WriteLine("Run scripts/smalllang.ps1 once to install the pinned toolchain.");
    return 1;
}

var compilerProject = Path.Combine(repoRoot, "src", "SmallLang.Compiler", "SmallLang.Compiler.csproj");
var compilerBuild = Run(
    "dotnet",
    ["build", compilerProject, "-c", "Release", "--nologo", "--no-restore"],
    input: null,
    repoRoot);
if (compilerBuild.ExitCode != 0)
{
    Console.Error.WriteLine("FAIL compiler bootstrap build");
    Console.Error.WriteLine(compilerBuild.Stdout);
    Console.Error.WriteLine(compilerBuild.Stderr);
    return 1;
}

var compilerDll = Path.Combine(
    repoRoot,
    "src",
    "SmallLang.Compiler",
    "bin",
    "Release",
    "net11.0",
    "SmallLang.Compiler.dll");
if (!File.Exists(compilerDll))
{
    Console.Error.WriteLine($"Compiler output not found: {compilerDll}");
    return 1;
}

var expectedFiles = Directory
    .EnumerateFiles(expectedDir, "*.stdout.txt")
    .Order(StringComparer.Ordinal)
    .ToArray();

if (expectedFiles.Length == 0)
{
    Console.Error.WriteLine("No expected stdout files found.");
    return 1;
}

var failures = 0;
foreach (var expectedFile in expectedFiles)
{
    var name = Path.GetFileName(expectedFile)[..^".stdout.txt".Length];
    var sourcePath = Path.Combine(repoRoot, "examples", name + ".sl");
    var stdinPath = Path.Combine(expectedDir, name + ".stdin.txt");
    var argumentsPath = Path.Combine(expectedDir, name + ".args.txt");
    var environmentPath = Path.Combine(expectedDir, name + ".env.txt");
    var outputPath = Path.Combine(artifactsDir, name + ".exe");
    var llvmContainsPath = Path.Combine(expectedDir, name + ".llvm.contains.txt");
    var llvmNotContainsPath = Path.Combine(expectedDir, name + ".llvm.not-contains.txt");
    var wasmLlvmContainsPath = Path.Combine(expectedDir, name + ".wasm32.llvm.contains.txt");
    var sourcesPath = Path.Combine(expectedDir, name + ".sources.txt");
    var verifyLlvm = File.Exists(llvmContainsPath) || File.Exists(llvmNotContainsPath);

    if (!File.Exists(sourcePath))
    {
        Console.Error.WriteLine($"FAIL {name}: source file not found");
        failures++;
        continue;
    }

    var compilerArguments = new List<string>
    {
        compilerDll,
        "build"
    };
    if (File.Exists(sourcesPath))
    {
        compilerArguments.AddRange(File.ReadLines(sourcesPath)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(line => Path.GetFullPath(line.Trim(), repoRoot)));
    }
    else
    {
        compilerArguments.Add(sourcePath);
    }
    compilerArguments.AddRange([
        "-o", outputPath,
        "--target", "windows-x64",
        "--llvm", llvmDir
    ]);
    if (verifyLlvm)
    {
        compilerArguments.Add("--keep-temps");
    }

    var build = Run(
        "dotnet",
        compilerArguments,
        input: null,
        repoRoot);

    if (build.ExitCode != 0)
    {
        Console.Error.WriteLine($"FAIL {name}: compiler exited {build.ExitCode}");
        Console.Error.WriteLine(build.Stdout);
        Console.Error.WriteLine(build.Stderr);
        failures++;
        continue;
    }

    if (verifyLlvm
        && !VerifyLlvmAssertions(
            Path.ChangeExtension(outputPath, ".ll"),
            llvmContainsPath,
            llvmNotContainsPath,
            out var llvmError))
    {
        Console.Error.WriteLine($"FAIL {name}: {llvmError}");
        failures++;
        continue;
    }

    if (File.Exists(wasmLlvmContainsPath))
    {
        var wasmOutputPath = Path.Combine(artifactsDir, name + ".wasm");
        var wasmArguments = new List<string>
        {
            compilerDll, "build", sourcePath,
            "-o", wasmOutputPath,
            "--target", "wasm32-browser",
            "--llvm", llvmDir,
            "--keep-temps"
        };
        var wasmBuild = Run("dotnet", wasmArguments, input: null, repoRoot);
        var wasmLlvmError = string.Empty;
        if (wasmBuild.ExitCode != 0
            || !VerifyLlvmAssertions(
                Path.ChangeExtension(wasmOutputPath, ".ll"),
                wasmLlvmContainsPath,
                Path.Combine(expectedDir, name + ".wasm32.llvm.not-contains.txt"),
                out wasmLlvmError))
        {
            Console.Error.WriteLine($"FAIL {name}: wasm32 LLVM verification failed");
            Console.Error.WriteLine(wasmBuild.Stdout);
            Console.Error.WriteLine(wasmBuild.Stderr);
            Console.Error.WriteLine(wasmLlvmError);
            failures++;
            continue;
        }
    }

    var stdin = File.Exists(stdinPath)
        ? File.ReadAllText(stdinPath, Encoding.UTF8)
        : null;
    var runArguments = File.Exists(argumentsPath)
        ? File.ReadAllLines(argumentsPath, Encoding.UTF8).ToList()
        : [];
    var runEnvironment = File.Exists(environmentPath)
        ? File.ReadAllLines(environmentPath, Encoding.UTF8)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length == 2 ? parts[1] : "", StringComparer.Ordinal)
        : null;
    var run = Run(outputPath, runArguments, stdin, repoRoot, runEnvironment);
    var expected = Normalize(File.ReadAllText(expectedFile, Encoding.UTF8));
    var actual = Normalize(run.Stdout);

    if (run.ExitCode != 0)
    {
        Console.Error.WriteLine($"FAIL {name}: executable exited {run.ExitCode}");
        Console.Error.WriteLine(run.Stderr);
        failures++;
        continue;
    }

    if (!StringComparer.Ordinal.Equals(expected, actual))
    {
        Console.Error.WriteLine($"FAIL {name}: stdout mismatch");
        Console.Error.WriteLine("EXPECTED:");
        Console.Error.WriteLine(expected);
        Console.Error.WriteLine("ACTUAL:");
        Console.Error.WriteLine(actual);
        failures++;
        continue;
    }

    Console.WriteLine($"PASS {name}");
}

var diagnosticDir = Path.Combine(repoRoot, "examples", "diagnostics");
var diagnosticFiles = Directory.Exists(diagnosticDir)
    ? Directory.EnumerateFiles(diagnosticDir, "*.sl").Order(StringComparer.Ordinal).ToArray()
    : [];
foreach (var sourcePath in diagnosticFiles)
{
    var name = Path.GetFileNameWithoutExtension(sourcePath);
    var expectedPath = Path.Combine(diagnosticDir, name + ".stderr.contains.txt");
    var sourcesPath = Path.Combine(diagnosticDir, name + ".sources.txt");
    var diagnosticTarget = name.Contains("-wasm32-", StringComparison.Ordinal)
        ? "wasm32-browser"
        : "windows-x64";
    if (!File.Exists(expectedPath))
    {
        Console.Error.WriteLine($"FAIL diagnostic/{name}: expected diagnostic file not found");
        failures++;
        continue;
    }

    var diagnosticArguments = new List<string> { compilerDll, "build" };
    if (File.Exists(sourcesPath))
    {
        diagnosticArguments.AddRange(File.ReadLines(sourcesPath)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(line => Path.GetFullPath(line.Trim(), repoRoot)));
    }
    else
    {
        diagnosticArguments.Add(sourcePath);
    }
    diagnosticArguments.AddRange([
        "-o", Path.Combine(artifactsDir, "diagnostic-" + name + ".exe"),
        "--target", diagnosticTarget,
        "--llvm", llvmDir
    ]);
    var build = Run(
        "dotnet",
        diagnosticArguments,
        input: null,
        repoRoot);
    var expectedDiagnostic = Normalize(File.ReadAllText(expectedPath, Encoding.UTF8)).Trim();
    var actualDiagnostic = Normalize(build.Stdout + build.Stderr);
    if (build.ExitCode == 0 || !actualDiagnostic.Contains(expectedDiagnostic, StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"FAIL diagnostic/{name}: expected compiler failure containing:");
        Console.Error.WriteLine(expectedDiagnostic);
        Console.Error.WriteLine("ACTUAL:");
        Console.Error.WriteLine(actualDiagnostic);
        failures++;
        continue;
    }

    Console.WriteLine($"PASS diagnostic/{name}");
}

if (failures == 0)
{
    Console.WriteLine($"All {expectedFiles.Length + diagnosticFiles.Length} example tests passed.");
    return 0;
}

Console.Error.WriteLine($"{failures} example test(s) failed.");
return 1;

static string FindRepositoryRoot(string startPath)
{
    for (var current = new DirectoryInfo(startPath); current is not null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "SmallLang.slnx")))
        {
            return current.FullName;
        }
    }

    throw new InvalidOperationException("Could not find repository root.");
}

static ProcessResult Run(
    string fileName,
    IReadOnlyList<string> args,
    string? input,
    string workingDirectory,
    IReadOnlyDictionary<string, string>? environment = null)
{
    using var process = new Process();
    process.StartInfo = new ProcessStartInfo
    {
        FileName = fileName,
        WorkingDirectory = workingDirectory,
        RedirectStandardInput = input is not null,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    foreach (var arg in args)
    {
        process.StartInfo.ArgumentList.Add(arg);
    }
    if (environment is not null)
    {
        foreach (var (name, value) in environment)
        {
            process.StartInfo.Environment[name] = value;
        }
    }

    process.Start();
    if (input is not null)
    {
        process.StandardInput.Write(input);
        process.StandardInput.Close();
    }

    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    return new ProcessResult(process.ExitCode, stdout, stderr);
}

static string Normalize(string value)
{
    return value.Replace("\r\n", "\n", StringComparison.Ordinal);
}

static bool VerifyLlvmAssertions(
    string llvmPath,
    string containsPath,
    string notContainsPath,
    out string error)
{
    if (!File.Exists(llvmPath))
    {
        error = $"LLVM file not found: {llvmPath}";
        return false;
    }

    var llvm = File.ReadAllText(llvmPath, Encoding.UTF8);
    foreach (var expected in ReadAssertions(containsPath))
    {
        if (!llvm.Contains(expected, StringComparison.Ordinal))
        {
            error = $"LLVM does not contain '{expected}'";
            return false;
        }
    }

    foreach (var forbidden in ReadAssertions(notContainsPath))
    {
        if (llvm.Contains(forbidden, StringComparison.Ordinal))
        {
            error = $"LLVM unexpectedly contains '{forbidden}'";
            return false;
        }
    }

    error = "";
    return true;
}

static IEnumerable<string> ReadAssertions(string path)
{
    return File.Exists(path)
        ? File.ReadLines(path, Encoding.UTF8).Where(line => !string.IsNullOrWhiteSpace(line))
        : [];
}

internal sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
