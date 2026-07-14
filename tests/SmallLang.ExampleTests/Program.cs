using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

var filters = new List<string>();
var skipBootstrap = false;
var jobs = Math.Min(Environment.ProcessorCount, 8);
for (var argumentIndex = 0; argumentIndex < args.Length; argumentIndex++)
{
    switch (args[argumentIndex])
    {
        case "--filter":
            if (++argumentIndex >= args.Length)
            {
                Console.Error.WriteLine("--filter requires a case-insensitive name fragment.");
                return 2;
            }
            filters.Add(args[argumentIndex]);
            break;
        case "--skip-bootstrap":
            skipBootstrap = true;
            break;
        case "--jobs":
            if (++argumentIndex >= args.Length
                || !int.TryParse(args[argumentIndex], out jobs)
                || jobs < 1)
            {
                Console.Error.WriteLine("--jobs requires a positive integer.");
                return 2;
            }
            break;
        default:
            Console.Error.WriteLine($"Unknown test option: {args[argumentIndex]}");
            Console.Error.WriteLine("Usage: dotnet run --project tests/SmallLang.ExampleTests -- [--filter <name>]... [--skip-bootstrap] [--jobs <count>]");
            return 2;
    }
}

var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var expectedDir = Path.Combine(repoRoot, "examples", "expected");
var artifactsDir = Path.Combine(repoRoot, "artifacts", "example-tests");
Directory.CreateDirectory(artifactsDir);
var llvmDir = Path.Combine(repoRoot, ".tools", "llvm-22.1.8");
var clangPath = Path.Combine(llvmDir, "bin", "clang.exe");
var llvmAsPath = Path.Combine(llvmDir, "bin", "llvm-as.exe");
if (!File.Exists(clangPath))
{
    Console.Error.WriteLine($"LLVM toolchain not found: {clangPath}");
    Console.Error.WriteLine("Run scripts/smalllang.ps1 once to install the pinned toolchain.");
    return 1;
}

var compilerProject = Path.Combine(repoRoot, "src", "SmallLang.Compiler", "SmallLang.Compiler.csproj");
var compilerDll = Path.Combine(
    repoRoot,
    "src",
    "SmallLang.Compiler",
    "bin",
    "Release",
    "net11.0",
    "SmallLang.Compiler.dll");
if (!skipBootstrap)
{
    Console.WriteLine("[bootstrap 1/2] Building the Release compiler...");
    Console.Out.Flush();
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
    Console.WriteLine("[bootstrap 1/2] PASS Release compiler build");
    Console.Out.Flush();

    Console.WriteLine("[bootstrap 2/2] Generating and verifying the grammar table...");
    Console.Out.Flush();
    var generatedGrammarPath = Path.Combine(artifactsDir, "smalllang_grammar.generated.sl");
    var grammarBuild = Run(
        "dotnet",
        [compilerDll, "grammar", "build",
            Path.Combine(repoRoot, "syntax", "smalllang.lexer"),
            Path.Combine(repoRoot, "syntax", "smalllang.grammar"),
            "-o", generatedGrammarPath],
        input: null,
        repoRoot);
    if (grammarBuild.ExitCode != 0)
    {
        Console.Error.WriteLine("FAIL grammar table generation");
        Console.Error.WriteLine(grammarBuild.Stdout);
        Console.Error.WriteLine(grammarBuild.Stderr);
        return 1;
    }
    var checkedInGrammarPath = Path.Combine(repoRoot, "syntax", "generated", "smalllang_grammar.sl");
    if (!File.Exists(checkedInGrammarPath)
        || !File.ReadAllBytes(checkedInGrammarPath).SequenceEqual(File.ReadAllBytes(generatedGrammarPath)))
    {
        Console.Error.WriteLine("FAIL generated grammar table is stale; run `smalllang grammar build`");
        return 1;
    }
    Console.WriteLine("[bootstrap 2/2] PASS grammar/table-determinism");
    Console.Out.Flush();
}
else if (!File.Exists(compilerDll))
{
    Console.Error.WriteLine($"Compiler output not found: {compilerDll}");
    Console.Error.WriteLine("Remove --skip-bootstrap so the test runner can build it.");
    return 1;
}

var allExpectedFiles = Directory
    .EnumerateFiles(expectedDir, "*.stdout.txt")
    .Order(StringComparer.Ordinal)
    .ToArray();
var diagnosticDir = Path.Combine(repoRoot, "examples", "diagnostics");
var allDiagnosticFiles = Directory.Exists(diagnosticDir)
    ? Directory.EnumerateFiles(diagnosticDir, "*.sl")
        .Concat(Directory.EnumerateFiles(diagnosticDir, "*.project"))
        .Order(StringComparer.Ordinal)
        .ToArray()
    : [];
var expectedFiles = allExpectedFiles
    .Where(file => MatchesFilters(Path.GetFileName(file)[..^".stdout.txt".Length], filters))
    .OrderByDescending(IsExpensiveSelfHostLlvmTest)
    .ThenBy(file => file, StringComparer.Ordinal)
    .ToArray();
var diagnosticFiles = allDiagnosticFiles
    .Where(file => MatchesFilters("diagnostic/" + Path.GetFileNameWithoutExtension(file), filters))
    .ToArray();

if (allExpectedFiles.Length == 0)
{
    Console.Error.WriteLine("No expected stdout files found.");
    return 1;
}
if (filters.Count > 0 && expectedFiles.Length + diagnosticFiles.Length == 0)
{
    Console.Error.WriteLine($"No example or diagnostic test matched: {string.Join(", ", filters)}");
    return 2;
}

var failures = 0;
var started = 0;
var completed = 0;
var totalTests = expectedFiles.Length + diagnosticFiles.Length;
var progressLock = new object();
Console.WriteLine($"[0/{totalTests}] Running with {jobs} worker(s).");
Console.Out.Flush();

void ReportStarted(string name)
{
    lock (progressLock)
    {
        var current = Interlocked.Increment(ref started);
        Console.WriteLine($"[start {current}/{totalTests}] {name}");
        Console.Out.Flush();
    }
}

void ReportCompleted(string name, bool passed, TimeSpan elapsed)
{
    lock (progressLock)
    {
        var current = Interlocked.Increment(ref completed);
        var writer = passed ? Console.Out : Console.Error;
        writer.WriteLine($"[{current}/{totalTests}] {(passed ? "PASS" : "FAIL")} {name} ({elapsed.TotalSeconds:F2}s)");
        writer.Flush();
    }
}

Parallel.ForEach(
    Partitioner.Create(expectedFiles, loadBalance: true),
    new ParallelOptions { MaxDegreeOfParallelism = jobs },
    expectedFile =>
{
    var stopwatch = Stopwatch.StartNew();
    var name = Path.GetFileName(expectedFile)[..^".stdout.txt".Length];
    var passed = false;
    ReportStarted(name);
    try
    {
    var sourcePath = Path.Combine(repoRoot, "examples", name + ".sl");
    var projectPath = Path.Combine(expectedDir, name + ".project.txt");
    var stdinPath = Path.Combine(expectedDir, name + ".stdin.txt");
    var argumentsPath = Path.Combine(expectedDir, name + ".args.txt");
    var environmentPath = Path.Combine(expectedDir, name + ".env.txt");
    var outputPath = Path.Combine(artifactsDir, name + ".exe");
    var llvmContainsPath = Path.Combine(expectedDir, name + ".llvm.contains.txt");
    var llvmNotContainsPath = Path.Combine(expectedDir, name + ".llvm.not-contains.txt");
    var wasmLlvmContainsPath = Path.Combine(expectedDir, name + ".wasm32.llvm.contains.txt");
    var sourcesPath = Path.Combine(expectedDir, name + ".sources.txt");
    var stdoutLlvmValidationPath = Path.Combine(expectedDir, name + ".stdout.llvm.validate.txt");
    var stdoutLlvmExecutionPath = Path.Combine(expectedDir, name + ".stdout.llvm.execute.txt");
    var verifyLlvm = File.Exists(llvmContainsPath) || File.Exists(llvmNotContainsPath);

    if (!File.Exists(sourcePath) && !File.Exists(projectPath))
    {
        Console.Error.WriteLine($"FAIL {name}: source or project manifest reference not found");
        Interlocked.Increment(ref failures);
        return;
    }

    var compilerArguments = new List<string>
    {
        compilerDll,
        "build"
    };
    if (File.Exists(projectPath))
    {
        var projectArguments = File.ReadLines(projectPath)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => line.Trim())
            .ToArray();
        var projectReference = projectArguments[0];
        compilerArguments.AddRange([
            "--project",
            Path.GetFullPath(projectReference, repoRoot)
        ]);
        compilerArguments.AddRange(projectArguments[1..]);
    }
    else if (File.Exists(sourcesPath))
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
        "--llvm", llvmDir,
        "-O1"
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
        Interlocked.Increment(ref failures);
        return;
    }

    if (verifyLlvm
        && !VerifyLlvmAssertions(
            Path.ChangeExtension(outputPath, ".ll"),
            llvmContainsPath,
            llvmNotContainsPath,
            out var llvmError))
    {
        Console.Error.WriteLine($"FAIL {name}: {llvmError}");
        Interlocked.Increment(ref failures);
        return;
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
            "-O1",
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
            Interlocked.Increment(ref failures);
            return;
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
        Interlocked.Increment(ref failures);
        return;
    }

    if (!StringComparer.Ordinal.Equals(expected, actual))
    {
        Console.Error.WriteLine($"FAIL {name}: stdout mismatch");
        Console.Error.WriteLine("EXPECTED:");
        Console.Error.WriteLine(expected);
        Console.Error.WriteLine("ACTUAL:");
        Console.Error.WriteLine(actual);
        Interlocked.Increment(ref failures);
        return;
    }

    if (File.Exists(stdoutLlvmValidationPath))
    {
        var stdoutLlvmPath = Path.Combine(artifactsDir, name + ".stdout.ll");
        var stdoutBitcodePath = Path.Combine(artifactsDir, name + ".stdout.bc");
        File.WriteAllText(stdoutLlvmPath, actual, new UTF8Encoding(false));
        var llvmAs = Run(llvmAsPath, [stdoutLlvmPath, "-o", stdoutBitcodePath], input: null, repoRoot);
        if (llvmAs.ExitCode != 0)
        {
            Console.Error.WriteLine($"FAIL {name}: stdout is not valid LLVM IR");
            Console.Error.WriteLine(llvmAs.Stdout);
            Console.Error.WriteLine(llvmAs.Stderr);
            Interlocked.Increment(ref failures);
            return;
        }

        if (File.Exists(stdoutLlvmExecutionPath))
        {
            var linkedPath = Path.Combine(artifactsDir, name + ".stdout.exe");
            var link = Run(clangPath, ["-Wno-override-module", stdoutLlvmPath, "-o", linkedPath], input: null, repoRoot);
            if (link.ExitCode != 0)
            {
                Console.Error.WriteLine($"FAIL {name}: stdout LLVM could not be linked");
                Console.Error.WriteLine(link.Stdout);
                Console.Error.WriteLine(link.Stderr);
                Interlocked.Increment(ref failures);
                return;
            }

            var linkedRun = Run(linkedPath, [], input: null, repoRoot);
            var executionExpectation = Normalize(File.ReadAllText(stdoutLlvmExecutionPath, Encoding.UTF8));
            var expectedLinkedStdout = string.IsNullOrWhiteSpace(executionExpectation)
                || StringComparer.Ordinal.Equals(executionExpectation.Trim(), "exit=0")
                ? string.Empty
                : executionExpectation;
            var actualLinkedStdout = Normalize(linkedRun.Stdout);
            if (linkedRun.ExitCode != 0
                || !StringComparer.Ordinal.Equals(expectedLinkedStdout, actualLinkedStdout))
            {
                Console.Error.WriteLine($"FAIL {name}: linked stdout LLVM executable failed");
                Console.Error.WriteLine("EXPECTED:");
                Console.Error.WriteLine(expectedLinkedStdout);
                Console.Error.WriteLine("ACTUAL:");
                Console.Error.WriteLine(linkedRun.Stdout);
                Console.Error.WriteLine(linkedRun.Stderr);
                Interlocked.Increment(ref failures);
                return;
            }
        }
    }

    passed = true;
    }
    finally
    {
        ReportCompleted(name, passed, stopwatch.Elapsed);
    }
});

Parallel.ForEach(diagnosticFiles, new ParallelOptions { MaxDegreeOfParallelism = jobs }, sourcePath =>
{
    var stopwatch = Stopwatch.StartNew();
    var name = Path.GetFileNameWithoutExtension(sourcePath);
    var displayName = $"diagnostic/{name}";
    var passed = false;
    ReportStarted(displayName);
    try
    {
    var expectedPath = Path.Combine(diagnosticDir, name + ".stderr.contains.txt");
    var sourcesPath = Path.Combine(diagnosticDir, name + ".sources.txt");
    var diagnosticTarget = name.Contains("-wasm32-", StringComparison.Ordinal)
        ? "wasm32-browser"
        : "windows-x64";
    if (!File.Exists(expectedPath))
    {
        Console.Error.WriteLine($"FAIL diagnostic/{name}: expected diagnostic file not found");
        Interlocked.Increment(ref failures);
        return;
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
        if (string.Equals(Path.GetExtension(sourcePath), ".project", StringComparison.OrdinalIgnoreCase))
        {
            diagnosticArguments.AddRange(["--project", sourcePath]);
        }
        else
        {
            diagnosticArguments.Add(sourcePath);
        }
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
        Interlocked.Increment(ref failures);
        return;
    }

    passed = true;
    }
    finally
    {
        ReportCompleted(displayName, passed, stopwatch.Elapsed);
    }
});

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

static bool MatchesFilters(string name, IReadOnlyList<string> filters) => filters.Count == 0
    || filters.Any(filter => name.Contains(filter, StringComparison.OrdinalIgnoreCase));

static bool IsExpensiveSelfHostLlvmTest(string path) => Path
    .GetFileName(path)
    .Contains("selfhost-llvm-", StringComparison.Ordinal);

static ProcessResult Run(
    string fileName,
    IReadOnlyList<string> args,
    string? input,
    string workingDirectory,
    IReadOnlyDictionary<string, string>? environment = null)
{
    const int ProcessTimeoutMilliseconds = 5 * 60 * 1000;
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

    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(ProcessTimeoutMilliseconds))
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);
        return new ProcessResult(
            -1,
            stdoutTask.Result,
            $"process timed out after {ProcessTimeoutMilliseconds / 1000} seconds: {fileName}{Environment.NewLine}{stderrTask.Result}");
    }

    Task.WaitAll(stdoutTask, stderrTask);
    return new ProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
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
