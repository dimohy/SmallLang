using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

var filters = new List<string>();
var exactFilters = new List<string>();
var affectedFiles = new List<string>();
var suite = TestSuite.Full;
var skipBootstrap = false;
var updateExpected = false;
var compareCompilers = false;
var testTarget = TestTarget.WindowsX64;
var wslDistribution = "Ubuntu";
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
        case "--exact":
            if (++argumentIndex >= args.Length)
            {
                Console.Error.WriteLine("--exact requires a case-insensitive full test name.");
                return 2;
            }
            exactFilters.Add(args[argumentIndex]);
            break;
        case "--affected":
            if (++argumentIndex >= args.Length)
            {
                Console.Error.WriteLine("--affected requires a repository-relative source path.");
                return 2;
            }
            affectedFiles.Add(args[argumentIndex]);
            break;
        case "--suite":
            if (++argumentIndex >= args.Length
                || !Enum.TryParse<TestSuite>(args[argumentIndex], ignoreCase: true, out suite))
            {
                Console.Error.WriteLine("--suite requires fast, reference, semantic, selfhost, llvm, or full.");
                return 2;
            }
            break;
        case "--skip-bootstrap":
            skipBootstrap = true;
            break;
        case "--update-expected":
            updateExpected = true;
            break;
        case "--compare-compilers":
            compareCompilers = true;
            break;
        case "--target":
            if (++argumentIndex >= args.Length
                || !TryParseTestTarget(args[argumentIndex], out testTarget))
            {
                Console.Error.WriteLine("--target requires windows-x64 or linux-x64.");
                return 2;
            }
            break;
        case "--wsl-distribution":
            if (++argumentIndex >= args.Length || string.IsNullOrWhiteSpace(args[argumentIndex]))
            {
                Console.Error.WriteLine("--wsl-distribution requires a WSL distribution name.");
                return 2;
            }
            wslDistribution = args[argumentIndex];
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
            Console.Error.WriteLine("Usage: dotnet run --project tests/Sollang.ExampleTests -- [--filter <fragment>]... [--exact <name>]... [--affected <path>]... [--suite fast|reference|semantic|selfhost|llvm|full] [--target windows-x64|linux-x64] [--wsl-distribution <name>] [--skip-bootstrap] [--update-expected] [--compare-compilers] [--jobs <count>]");
            return 2;
    }
}

if (testTarget == TestTarget.LinuxX64 && updateExpected)
{
    Console.Error.WriteLine("--update-expected is not supported with --target linux-x64; Linux verifies target-neutral behavior without replacing Windows LLVM snapshots.");
    return 2;
}

var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var expectedDir = Path.Combine(repoRoot, "examples", "expected");
var baseArtifactsDir = Path.Combine(repoRoot, "artifacts", "example-tests");
var artifactsDir = testTarget == TestTarget.WindowsX64
    ? baseArtifactsDir
    : Path.Combine(baseArtifactsDir, "linux-x64");
Directory.CreateDirectory(artifactsDir);
var llvmDir = Path.Combine(repoRoot, ".tools", "llvm-22.1.8");
var clangPath = Path.Combine(llvmDir, "bin", "clang.exe");
var llvmAsPath = Path.Combine(llvmDir, "bin", "llvm-as.exe");
if (!File.Exists(clangPath))
{
    Console.Error.WriteLine($"LLVM toolchain not found: {clangPath}");
    Console.Error.WriteLine("Run scripts/sollang.ps1 once to install the pinned toolchain.");
    return 1;
}
if (testTarget == TestTarget.LinuxX64)
{
    var wslCheck = Run("wsl.exe", ["-d", wslDistribution, "--", "true"], input: null, repoRoot);
    if (wslCheck.ExitCode != 0)
    {
        Console.Error.WriteLine($"WSL distribution is unavailable: {wslDistribution}");
        Console.Error.WriteLine(wslCheck.Stderr);
        return 1;
    }
}

var compilerProject = Path.Combine(repoRoot, "src", "Sollang.Compiler", "Sollang.Compiler.csproj");
var compilerDll = Path.Combine(
    repoRoot,
    "src",
    "Sollang.Compiler",
    "bin",
    "Release",
    "net11.0",
    "Sollang.Compiler.dll");
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
    var generatedGrammarPath = Path.Combine(artifactsDir, "sollang_grammar.generated.slg");
    var grammarBuild = Run(
        "dotnet",
        [compilerDll, "grammar", "build",
            Path.Combine(repoRoot, "syntax", "sollang.lexer"),
            Path.Combine(repoRoot, "syntax", "sollang.grammar"),
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
    var checkedInGrammarPath = Path.Combine(repoRoot, "syntax", "generated", "sollang_grammar.slg");
    if (!File.Exists(checkedInGrammarPath)
        || !File.ReadAllBytes(checkedInGrammarPath).SequenceEqual(File.ReadAllBytes(generatedGrammarPath)))
    {
        Console.Error.WriteLine("FAIL generated grammar table is stale; run `sollang grammar build`");
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
var affectedPaths = affectedFiles
    .Select(path => Path.GetFullPath(path, repoRoot))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var allDiagnosticFiles = Directory.Exists(diagnosticDir)
    ? Directory.EnumerateFiles(diagnosticDir, "*.slg")
        .Concat(Directory.EnumerateFiles(diagnosticDir, "*.project"))
        .Concat(Directory.EnumerateFiles(diagnosticDir, "*.workspace"))
        .Order(StringComparer.Ordinal)
        .ToArray()
    : [];
var expectedFiles = allExpectedFiles
    .Where(file => MatchesFilters(Path.GetFileName(file)[..^".stdout.txt".Length], filters, exactFilters))
    .Where(file => MatchesSuite(file, suite))
    .Where(file => MatchesAffectedExpected(file, repoRoot, expectedDir, affectedPaths))
    .OrderByDescending(IsExpensiveSelfHostLlvmTest)
    .ThenBy(file => file, StringComparer.Ordinal)
    .ToArray();
var diagnosticFiles = allDiagnosticFiles
    .Where(file => suite != TestSuite.Llvm && suite != TestSuite.SelfHost && suite != TestSuite.Semantic)
    .Where(file => MatchesFilters("diagnostic/" + Path.GetFileNameWithoutExtension(file), filters, exactFilters))
    .Where(file => MatchesAffectedDiagnostic(file, diagnosticDir, repoRoot, affectedPaths))
    .ToArray();

if (allExpectedFiles.Length == 0)
{
    Console.Error.WriteLine("No expected stdout files found.");
    return 1;
}
if (expectedFiles.Length + diagnosticFiles.Length == 0)
{
    Console.Error.WriteLine($"No example or diagnostic test matched suite={suite}, filters=[{string.Join(", ", filters)}], exact=[{string.Join(", ", exactFilters)}], affected=[{string.Join(", ", affectedFiles)}].");
    return 2;
}

var selfHostDriverPath = Path.Combine(baseArtifactsDir, "selfhost-sollangc-driver.exe");
var selfHostDriverSourcesPath = Path.Combine(
    repoRoot,
    "tests",
    "Sollang.ExampleTests",
    "Fixtures",
    "selfhost-sollangc-driver.sources.txt");
if (expectedFiles.Any(IsReusableSelfHostCompilerTest))
{
    var selfHostDriverSources = File.ReadLines(selfHostDriverSourcesPath)
        .Where(static line => !string.IsNullOrWhiteSpace(line))
        .Select(line => Path.GetFullPath(line.Trim(), repoRoot))
        .ToArray();
    var selfHostDriverInputs = selfHostDriverSources
        .Concat(Directory.EnumerateFiles(
            Path.Combine(repoRoot, "stdlib"),
            "*.slg",
            SearchOption.AllDirectories))
        .Append(selfHostDriverSourcesPath)
        .Append(Path.Combine(repoRoot, "tests", "Sollang.ExampleTests", "Program.cs"))
        .Append(compilerDll)
        .ToArray();
    if (!IsOutputCurrent(selfHostDriverPath, selfHostDriverInputs))
    {
        Console.WriteLine("[selfhost bootstrap] Building the reusable native sollangc...");
        Console.Out.Flush();
        var driverArguments = new List<string> { compilerDll, "build" };
        driverArguments.AddRange(selfHostDriverSources);
        driverArguments.AddRange([
            "-o", selfHostDriverPath,
            "--target", "windows-x64",
            "--llvm", llvmDir,
            "-O1"
        ]);
        var driverBuild = Run("dotnet", driverArguments, input: null, repoRoot, relayOutput: true);
        if (driverBuild.ExitCode != 0)
        {
            Console.Error.WriteLine("FAIL reusable native sollangc bootstrap");
            Console.Error.WriteLine(driverBuild.Stdout);
            Console.Error.WriteLine(driverBuild.Stderr);
            return 1;
        }
        Console.WriteLine("[selfhost bootstrap] PASS reusable native sollangc");
    }
    else
    {
        Console.WriteLine("[selfhost bootstrap] REUSE current native sollangc");
    }
    Console.Out.Flush();
}

if (expectedFiles.Any(file => string.Equals(
        Path.GetFileName(file),
        "442-git-dependency.stdout.txt",
        StringComparison.Ordinal)))
{
    PrepareGitDependencyFixture(baseArtifactsDir);
}

var failures = 0;
var started = 0;
var completed = 0;
var totalTests = expectedFiles.Length + diagnosticFiles.Length;
var progressLock = new object();
Console.WriteLine($"[0/{totalTests}] Running {suite.ToString().ToLowerInvariant()} suite for {TestTargetName(testTarget)} with {jobs} worker(s).");
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
        Console.WriteLine($"[{current}/{totalTests}] {(passed ? "PASS" : "FAIL")} {name} ({elapsed.TotalSeconds:F2}s)");
        Console.Out.Flush();
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
    var defaultSourcePath = Path.Combine(repoRoot, "examples", name + ".slg");
    var targetSourcePath = Path.Combine(repoRoot, "examples", name + "." + TestTargetName(testTarget) + ".slg");
    var sourcePath = File.Exists(targetSourcePath) ? targetSourcePath : defaultSourcePath;
    var projectPath = Path.Combine(expectedDir, name + ".project.txt");
    var stdinPath = Path.Combine(expectedDir, name + ".stdin.txt");
    var argumentsPath = Path.Combine(expectedDir, name + ".args.txt");
    var environmentPath = Path.Combine(expectedDir, name + ".env.txt");
    var outputPath = Path.Combine(
        artifactsDir,
        name + "." + TestTargetName(testTarget) + TestExecutableSuffix(testTarget));
    var commonLlvmContainsPath = Path.Combine(expectedDir, name + ".llvm.contains.txt");
    var commonLlvmNotContainsPath = Path.Combine(expectedDir, name + ".llvm.not-contains.txt");
    var targetLlvmContainsPath = Path.Combine(expectedDir, name + ".linux-x64.llvm.contains.txt");
    var targetLlvmNotContainsPath = Path.Combine(expectedDir, name + ".linux-x64.llvm.not-contains.txt");
    var llvmContainsPath = testTarget == TestTarget.LinuxX64 && File.Exists(targetLlvmContainsPath)
        ? targetLlvmContainsPath
        : commonLlvmContainsPath;
    var llvmNotContainsPath = testTarget == TestTarget.LinuxX64 && File.Exists(targetLlvmNotContainsPath)
        ? targetLlvmNotContainsPath
        : commonLlvmNotContainsPath;
    var wasmLlvmContainsPath = Path.Combine(expectedDir, name + ".wasm32.llvm.contains.txt");
    var sourcesPath = Path.Combine(expectedDir, name + ".sources.txt");
    var selfHostRootPath = Path.Combine(expectedDir, name + ".root.txt");
    var stdoutLlvmValidationPath = Path.Combine(expectedDir, name + ".stdout.llvm.validate.txt");
    var stdoutLlvmExecutionPath = Path.Combine(expectedDir, name + ".stdout.llvm.execute.txt");
    var verifyLlvm = File.Exists(llvmContainsPath) || File.Exists(llvmNotContainsPath);

    if (!File.Exists(sourcePath) && !File.Exists(projectPath))
    {
        Console.Error.WriteLine($"FAIL {name}: source or project manifest reference not found");
        Interlocked.Increment(ref failures);
        return;
    }

    ProcessResult run;
    string[]? selfHostSourcePaths = null;
    var reusableSelfHostTest = IsReusableSelfHostCompilerTest(expectedFile);
    var selfHostMode = "";
    if (reusableSelfHostTest)
    {
        var outerSource = File.ReadAllText(sourcePath, Encoding.UTF8);
        selfHostMode = SelfHostTargetMode(outerSource, testTarget);
        var driverArguments = new List<string>
        {
            File.Exists(selfHostRootPath) ? selfHostMode + "-root" : selfHostMode
        };
        if (File.Exists(selfHostRootPath))
        {
            var sourceRoot = File.ReadLines(selfHostRootPath)
                .First(static line => !string.IsNullOrWhiteSpace(line))
                .Trim();
            driverArguments.Add(Path.GetFullPath(sourceRoot, repoRoot));
        }
        else
        {
            selfHostSourcePaths = MaterializeSelfHostSources(
                name,
                ExtractRawMultilineStrings(outerSource),
                artifactsDir);
            driverArguments.AddRange(selfHostSourcePaths);
        }
        run = Run(selfHostDriverPath, driverArguments, input: null, repoRoot);
    }
    else
    {
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
            string.Equals(Path.GetExtension(projectReference), ".workspace", StringComparison.OrdinalIgnoreCase)
                ? "--workspace"
                : "--project",
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
        "--target", TestTargetName(testTarget),
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
        var wasmOutputPath = Path.Combine(artifactsDir, name + ".wasm32.wasm");
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
    run = RunTargetExecutable(
        testTarget,
        outputPath,
        runArguments,
        stdin,
        repoRoot,
        runEnvironment,
        wslDistribution);
    }
    var actual = Normalize(run.Stdout);
    var compareRawStdout = testTarget == TestTarget.WindowsX64
        || !reusableSelfHostTest
        || selfHostMode.StartsWith("wasm", StringComparison.Ordinal);
    var expected = compareRawStdout
        ? Normalize(File.ReadAllText(expectedFile, Encoding.UTF8))
        : actual;

    if (run.ExitCode != 0)
    {
        Console.Error.WriteLine($"FAIL {name}: executable exited {run.ExitCode}");
        Console.Error.WriteLine(run.Stderr);
        Interlocked.Increment(ref failures);
        return;
    }

    if (!StringComparer.Ordinal.Equals(expected, actual) && updateExpected)
    {
        if (File.Exists(stdoutLlvmValidationPath))
        {
            var updateLlvmPath = Path.Combine(artifactsDir, name + ".update.stdout.ll");
            var updateBitcodePath = Path.Combine(artifactsDir, name + ".update.stdout.bc");
            File.WriteAllText(updateLlvmPath, actual, new UTF8Encoding(false));
            var updateLlvmAs = Run(llvmAsPath, [updateLlvmPath, "-o", updateBitcodePath], input: null, repoRoot);
            if (updateLlvmAs.ExitCode != 0)
            {
                Console.Error.WriteLine($"FAIL {name}: refusing to update expected stdout because LLVM verification failed");
                Console.Error.WriteLine(updateLlvmAs.Stdout);
                Console.Error.WriteLine(updateLlvmAs.Stderr);
                Interlocked.Increment(ref failures);
                return;
            }
        }

        File.WriteAllText(expectedFile, actual, new UTF8Encoding(false));
        expected = actual;
        Console.WriteLine($"UPDATE {name}: expected stdout refreshed");
        Console.Out.Flush();
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

    if (File.Exists(stdoutLlvmValidationPath)
        || (testTarget == TestTarget.LinuxX64
            && reusableSelfHostTest
            && !selfHostMode.StartsWith("wasm", StringComparison.Ordinal)))
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

        var linuxExecutionPath = Path.Combine(expectedDir, name + ".stdout.llvm.linux.execute.txt");
        var executionPath = testTarget == TestTarget.LinuxX64 && File.Exists(linuxExecutionPath)
            ? linuxExecutionPath
            : stdoutLlvmExecutionPath;
        if (File.Exists(executionPath))
        {
            var linkedPath = Path.Combine(artifactsDir, name + ".stdout" + TestExecutableSuffix(testTarget));
            var link = testTarget == TestTarget.LinuxX64
                ? LinkLinuxLlvm(clangPath, stdoutLlvmPath, linkedPath, repoRoot, wslDistribution)
                : Run(clangPath, ["-Wno-override-module", stdoutLlvmPath, "-o", linkedPath], input: null, repoRoot);
            if (link.ExitCode != 0)
            {
                Console.Error.WriteLine($"FAIL {name}: stdout LLVM could not be linked");
                Console.Error.WriteLine(link.Stdout);
                Console.Error.WriteLine(link.Stderr);
                Interlocked.Increment(ref failures);
                return;
            }

            var linkedRun = RunTargetExecutable(
                testTarget,
                linkedPath,
                [],
                input: null,
                repoRoot,
                environment: null,
                wslDistribution);
            var executionExpectation = Normalize(File.ReadAllText(executionPath, Encoding.UTF8));
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

            if (compareCompilers && selfHostSourcePaths is not null)
            {
                var referencePath = Path.Combine(artifactsDir, name + ".reference" + TestExecutableSuffix(testTarget));
                var referenceArguments = new List<string> { compilerDll, "build" };
                referenceArguments.AddRange(selfHostSourcePaths);
                referenceArguments.AddRange([
                    "-o", referencePath,
                    "--target", TestTargetName(testTarget),
                    "--llvm", llvmDir,
                    "-O0",
                    "--keep-temps"
                ]);
                var referenceBuild = Run("dotnet", referenceArguments, input: null, repoRoot);
                if (referenceBuild.ExitCode != 0)
                {
                    Console.Error.WriteLine($"FAIL {name}: C# reference compiler failed during differential verification");
                    Console.Error.WriteLine(referenceBuild.Stdout);
                    Console.Error.WriteLine(referenceBuild.Stderr);
                    Interlocked.Increment(ref failures);
                    return;
                }

                var referenceRun = RunTargetExecutable(
                    testTarget,
                    referencePath,
                    [],
                    input: null,
                    repoRoot,
                    environment: null,
                    wslDistribution);
                var referenceStdout = Normalize(referenceRun.Stdout);
                if (referenceRun.ExitCode != linkedRun.ExitCode
                    || !StringComparer.Ordinal.Equals(referenceStdout, actualLinkedStdout))
                {
                    Console.Error.WriteLine($"FAIL {name}: C# and Sollang generated LLVM differ at runtime");
                    Console.Error.WriteLine("C# LLVM OUTPUT:");
                    Console.Error.WriteLine(referenceRun.Stdout);
                    Console.Error.WriteLine("Sollang LLVM OUTPUT:");
                    Console.Error.WriteLine(linkedRun.Stdout);
                    Interlocked.Increment(ref failures);
                    return;
                }
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
        : TestTargetName(testTarget);
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
        else if (string.Equals(Path.GetExtension(sourcePath), ".workspace", StringComparison.OrdinalIgnoreCase))
        {
            diagnosticArguments.AddRange(["--workspace", sourcePath]);
            if (name.Contains("lock-stale", StringComparison.Ordinal))
            {
                diagnosticArguments.Add("--locked");
            }
        }
        else
        {
            diagnosticArguments.Add(sourcePath);
        }
    }
    diagnosticArguments.AddRange([
        "-o", Path.Combine(artifactsDir, "diagnostic-" + name + TestExecutableSuffix(testTarget)),
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
        if (File.Exists(Path.Combine(current.FullName, "Sollang.slnx")))
        {
            return current.FullName;
        }
    }

    throw new InvalidOperationException("Could not find repository root.");
}

static bool MatchesFilters(
    string name,
    IReadOnlyList<string> filters,
    IReadOnlyList<string> exactFilters) => (filters.Count == 0 && exactFilters.Count == 0)
    || filters.Any(filter => name.Contains(filter, StringComparison.OrdinalIgnoreCase))
    || exactFilters.Any(filter => name.Equals(filter, StringComparison.OrdinalIgnoreCase));

static bool MatchesSuite(string path, TestSuite suite) => suite switch
{
    TestSuite.Fast => !IsExpensiveSelfHostLlvmTest(path),
    TestSuite.Reference => !IsSelfHostTest(path),
    TestSuite.Semantic => IsSelfHostTest(path) && !IsExpensiveSelfHostLlvmTest(path),
    TestSuite.SelfHost => IsSelfHostTest(path),
    TestSuite.Llvm => IsExpensiveSelfHostLlvmTest(path),
    _ => true
};

static bool MatchesAffectedExpected(
    string expectedFile,
    string repoRoot,
    string expectedDir,
    IReadOnlySet<string> affectedPaths)
{
    if (affectedPaths.Count == 0)
    {
        return true;
    }

    var name = Path.GetFileName(expectedFile)[..^".stdout.txt".Length];
    if (affectedPaths.Contains(Path.GetFullPath(expectedFile))
        || affectedPaths.Contains(Path.Combine(repoRoot, "examples", name + ".slg"))
        || affectedPaths.Contains(Path.Combine(expectedDir, name + ".project.txt")))
    {
        return true;
    }

    var sourcesPath = Path.Combine(expectedDir, name + ".sources.txt");
    var rootPath = Path.Combine(expectedDir, name + ".root.txt");
    if (File.Exists(rootPath))
    {
        if (affectedPaths.Contains(Path.GetFullPath(rootPath)))
        {
            return true;
        }

        var rootReference = File.ReadLines(rootPath)
            .First(static line => !string.IsNullOrWhiteSpace(line))
            .Trim();
        var sourceRoot = Path.GetFullPath(rootReference, repoRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (affectedPaths.Any(path => path.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
    }

    return MatchesAffectedSources(sourcesPath, repoRoot, affectedPaths);
}

static bool MatchesAffectedDiagnostic(
    string sourceFile,
    string diagnosticDir,
    string repoRoot,
    IReadOnlySet<string> affectedPaths)
{
    if (affectedPaths.Count == 0)
    {
        return true;
    }

    var name = Path.GetFileNameWithoutExtension(sourceFile);
    if (affectedPaths.Contains(Path.GetFullPath(sourceFile))
        || affectedPaths.Contains(Path.Combine(diagnosticDir, name + ".stderr.contains.txt")))
    {
        return true;
    }

    return MatchesAffectedSources(
        Path.Combine(diagnosticDir, name + ".sources.txt"),
        repoRoot,
        affectedPaths);
}

static bool MatchesAffectedSources(
    string sourcesPath,
    string repoRoot,
    IReadOnlySet<string> affectedPaths)
{
    if (!File.Exists(sourcesPath))
    {
        return false;
    }

    if (affectedPaths.Contains(Path.GetFullPath(sourcesPath)))
    {
        return true;
    }

    return File.ReadLines(sourcesPath)
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .Select(line => Path.GetFullPath(line.Trim(), repoRoot))
        .Any(affectedPaths.Contains);
}

static bool IsExpensiveSelfHostLlvmTest(string path) => Path
    .GetFileName(path)
    .Contains("selfhost-llvm-", StringComparison.Ordinal);

static bool IsReusableSelfHostCompilerTest(string path)
{
    var name = Path.GetFileName(path);
    return name.Contains("selfhost-llvm-", StringComparison.Ordinal)
        && !name.StartsWith("195-selfhost-llvm-target-descriptor", StringComparison.Ordinal)
        && !name.StartsWith("291-selfhost-llvm-canonical-type-selection", StringComparison.Ordinal);
}

static string SelfHostTargetMode(string source, TestTarget testTarget) =>
    source.Contains("llvm.emitWasm", StringComparison.Ordinal)
        ? "wasm"
        : testTarget == TestTarget.LinuxX64
            || source.Contains("llvm.emitLinux", StringComparison.Ordinal)
                ? "linux"
                : "windows";

static IEnumerable<string> ExtractRawMultilineStrings(string source)
{
    var matches = Regex.Matches(
        source,
        "\"\"\"\\r?\\n(?<body>.*?)\\r?\\n[ \\t]*\"\"\"",
        RegexOptions.Singleline);
    if (matches.Count == 0)
    {
        throw new InvalidOperationException("Reusable self-host compiler test requires at least one raw multiline source string.");
    }

    foreach (Match match in matches)
    {
        var lines = Normalize(match.Groups["body"].Value).Split('\n');
        var indentation = lines
            .Where(static line => line.Length != 0)
            .Select(static line => line.TakeWhile(static character => character is ' ' or '\t').Count())
            .DefaultIfEmpty(0)
            .Min();
        yield return string.Join('\n', lines.Select(line =>
            line.Length >= indentation ? line[indentation..] : string.Empty));
    }
}

static string[] MaterializeSelfHostSources(
    string testName,
    IEnumerable<string> sources,
    string artifactsDir)
{
    var sourceDirectory = Path.Combine(artifactsDir, "selfhost-inputs", testName);
    Directory.CreateDirectory(sourceDirectory);
    return sources.Select((source, index) =>
    {
        var sourcePath = Path.Combine(sourceDirectory, $"source-{index}.slg");
        File.WriteAllText(sourcePath, source, new UTF8Encoding(false));
        return sourcePath;
    }).ToArray();
}

static bool IsOutputCurrent(string outputPath, IEnumerable<string> inputPaths)
{
    if (!File.Exists(outputPath))
    {
        return false;
    }

    var outputTime = File.GetLastWriteTimeUtc(outputPath);
    return inputPaths.All(path => File.Exists(path) && File.GetLastWriteTimeUtc(path) <= outputTime);
}

static bool IsSelfHostTest(string path) => Path
    .GetFileName(path)
    .Contains("selfhost-", StringComparison.Ordinal);

static bool TryParseTestTarget(string value, out TestTarget target)
{
    switch (value.ToLowerInvariant())
    {
        case "windows-x64":
            target = TestTarget.WindowsX64;
            return true;
        case "linux-x64":
            target = TestTarget.LinuxX64;
            return true;
        default:
            target = default;
            return false;
    }
}

static string TestTargetName(TestTarget target) => target switch
{
    TestTarget.WindowsX64 => "windows-x64",
    TestTarget.LinuxX64 => "linux-x64",
    _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
};

static string TestExecutableSuffix(TestTarget target) => target switch
{
    TestTarget.WindowsX64 => ".exe",
    TestTarget.LinuxX64 => ".linux",
    _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
};

static ProcessResult RunTargetExecutable(
    TestTarget target,
    string executablePath,
    IReadOnlyList<string> args,
    string? input,
    string workingDirectory,
    IReadOnlyDictionary<string, string>? environment,
    string wslDistribution) => target switch
{
    TestTarget.WindowsX64 => Run(executablePath, args, input, workingDirectory, environment),
    TestTarget.LinuxX64 => RunInWsl(
        executablePath,
        args,
        input,
        workingDirectory,
        environment,
        wslDistribution),
    _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
};

static ProcessResult RunInWsl(
    string executablePath,
    IReadOnlyList<string> args,
    string? input,
    string workingDirectory,
    IReadOnlyDictionary<string, string>? environment,
    string wslDistribution)
{
    var wslArguments = new List<string>
    {
        "-d", wslDistribution,
        "--cd", ConvertToWslPath(workingDirectory),
        "--"
    };
    if (environment is not null && environment.Count != 0)
    {
        wslArguments.Add("env");
        wslArguments.AddRange(environment.Select(static pair => $"{pair.Key}={pair.Value}"));
    }
    wslArguments.Add(ConvertToWslPath(executablePath));
    wslArguments.AddRange(args);
    return Run("wsl.exe", wslArguments, input, workingDirectory);
}

static ProcessResult LinkLinuxLlvm(
    string clangPath,
    string llvmPath,
    string executablePath,
    string workingDirectory,
    string wslDistribution)
{
    var objectPath = Path.ChangeExtension(executablePath, ".o");
    var compile = Run(
        clangPath,
        ["--target=x86_64-unknown-linux-gnu", "-Wno-override-module", "-c", llvmPath, "-O0", "-o", objectPath],
        input: null,
        workingDirectory);
    if (compile.ExitCode != 0)
    {
        return compile;
    }

    return Run(
        "wsl.exe",
        [
            "-d", wslDistribution,
            "--cd", ConvertToWslPath(workingDirectory),
            "--", "gcc", ConvertToWslPath(objectPath), "-pthread", "-o", ConvertToWslPath(executablePath)
        ],
        input: null,
        workingDirectory);
}

static string ConvertToWslPath(string path)
{
    var absolute = Path.GetFullPath(path);
    if (absolute.Length < 3 || absolute[1] != ':' || absolute[2] != Path.DirectorySeparatorChar)
    {
        throw new InvalidOperationException($"WSL test paths must be drive-qualified Windows paths: {absolute}");
    }

    var drive = char.ToLowerInvariant(absolute[0]);
    return $"/mnt/{drive}/{absolute[3..].Replace('\\', '/')}";
}

static void PrepareGitDependencyFixture(string artifactsDir)
{
    var root = Path.Combine(artifactsDir, "442-git-dependency");
    if (Directory.Exists(root))
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
        Directory.Delete(root, recursive: true);
    }
    var remote = Path.Combine(root, "remote");
    var app = Path.Combine(root, "app");
    Directory.CreateDirectory(Path.Combine(remote, "src"));
    Directory.CreateDirectory(Path.Combine(remote, "base", "src"));
    Directory.CreateDirectory(Path.Combine(app, "src"));
    File.WriteAllText(
        Path.Combine(remote, "sollang.project"),
        "project {\n    name: \"remote\"\n    version: \"1.2.3\"\n    root: \"src/remote.slg\"\n    dependencies: { base: { path: \"base\", version: \"^1.0.0\" } }\n}\n",
        new UTF8Encoding(false));
    File.WriteAllText(
        Path.Combine(remote, "src", "remote.slg"),
        "namespace remote\n\nimport base\n\npublic addTwo value: Int -> Int {\n    value -> base.addOne => incremented\n    incremented + 1\n}\n",
        new UTF8Encoding(false));
    File.WriteAllText(
        Path.Combine(remote, "base", "sollang.project"),
        "project {\n    name: \"base\"\n    version: \"1.0.0\"\n    root: \"src/base.slg\"\n}\n",
        new UTF8Encoding(false));
    File.WriteAllText(
        Path.Combine(remote, "base", "src", "base.slg"),
        "namespace base\n\npublic addOne value: Int -> Int { value + 1 }\n",
        new UTF8Encoding(false));
    foreach (var command in new[]
    {
        new[] { "init", "--quiet" },
        new[] { "config", "user.name", "Sollang Tests" },
        new[] { "config", "user.email", "tests@sollang.invalid" },
        new[] { "add", "." },
        new[] { "commit", "--quiet", "-m", "fixture" }
    })
    {
        var result = Run("git", command, input: null, remote);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"failed to prepare Git dependency fixture: {result.Stderr}");
        }
    }
    var revisionResult = Run("git", ["rev-parse", "HEAD"], input: null, remote);
    if (revisionResult.ExitCode != 0)
    {
        throw new InvalidOperationException($"failed to read Git dependency fixture revision: {revisionResult.Stderr}");
    }
    var revision = revisionResult.Stdout.Trim();
    var portableRemote = remote.Replace('\\', '/');
    File.WriteAllText(
        Path.Combine(app, "sollang.project"),
        $"project {{\n    name: \"git_app\"\n    version: \"0.1.0\"\n    root: \"src/main.slg\"\n    dependencies: {{\n        remote: {{ git: \"{portableRemote}\", rev: \"{revision}\", version: \"^1.2.0\" }}\n    }}\n}}\n",
        new UTF8Encoding(false));
    File.WriteAllText(
        Path.Combine(app, "src", "main.slg"),
        "import remote\n\nmain { 40 -> remote.addTwo => value\n    \"$value\" -> println }\n",
        new UTF8Encoding(false));
}

static ProcessResult Run(
    string fileName,
    IReadOnlyList<string> args,
    string? input,
    string workingDirectory,
    IReadOnlyDictionary<string, string>? environment = null,
    bool relayOutput = false)
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

    var stdoutTask = relayOutput
        ? ReadAndRelayAsync(process.StandardOutput, Console.Out)
        : process.StandardOutput.ReadToEndAsync();
    var stderrTask = relayOutput
        ? ReadAndRelayAsync(process.StandardError, Console.Error)
        : process.StandardError.ReadToEndAsync();
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

static async Task<string> ReadAndRelayAsync(StreamReader reader, TextWriter writer)
{
    var output = new StringBuilder();
    while (await reader.ReadLineAsync() is { } line)
    {
        output.AppendLine(line);
        await writer.WriteLineAsync(line);
        await writer.FlushAsync();
    }
    return output.ToString();
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

internal enum TestSuite
{
    Fast,
    Reference,
    Semantic,
    SelfHost,
    Full,
    Llvm
}

internal enum TestTarget
{
    WindowsX64,
    LinuxX64
}
