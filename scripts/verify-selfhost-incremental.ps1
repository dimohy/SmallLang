[CmdletBinding()]
param(
    [string[]]$Fixture = @("examples/582-billion-sensor-alerts.slg"),
    [ValidateSet("windows", "linux")]
    [string]$Target = "windows",
    [bool]$CompareStage2 = $true,
    [switch]$BootstrapStage2,
    [switch]$NoExecute
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-sollangc-driver.sources.txt"
$runtimeManifestPath = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-compiler-runtime.sources.txt"
$compilerProject = Join-Path $repoRoot "src\Sollang.Compiler\Sollang.Compiler.csproj"
$llvmRoot = Join-Path $repoRoot ".tools\llvm-22.1.8"
$llvmAs = Join-Path $llvmRoot "bin\llvm-as.exe"
$clang = Join-Path $llvmRoot "bin\clang.exe"
$cacheRoot = Join-Path $repoRoot "artifacts\incremental-selfhost"
$stage1Compiler = Join-Path $cacheRoot "selfhost-stage1-host.exe"
$stage1Fingerprint = Join-Path $cacheRoot "selfhost-stage1-host.sha256"
$stage2Compiler = Join-Path $cacheRoot "selfhost-stage2.exe"
$stage2Llvm = Join-Path $cacheRoot "selfhost-stage2.ll"
$stage2Fingerprint = Join-Path $cacheRoot "selfhost-stage2.sha256"

New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null

function Resolve-Manifest {
    param([string]$Path)
    Get-Content -LiteralPath $Path |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { (Resolve-Path (Join-Path $repoRoot $_.Trim())).Path }
}

function Get-ContentFingerprint {
    param([string[]]$Paths)
    $hash = [System.Security.Cryptography.IncrementalHash]::CreateHash(
        [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    foreach ($path in $Paths) {
        $relative = [System.IO.Path]::GetRelativePath($repoRoot, $path).Replace("\", "/")
        $nameBytes = [System.Text.Encoding]::UTF8.GetBytes("$relative`0")
        $hash.AppendData($nameBytes)
        $hash.AppendData([System.IO.File]::ReadAllBytes($path))
    }
    [Convert]::ToHexString($hash.GetHashAndReset())
}

function Get-NormalizedTextHash {
    param([string]$Path)
    $text = [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
    [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes))
}

function Test-Fingerprint {
    param([string]$Path, [string]$Expected)
    (Test-Path -LiteralPath $Path) -and
        ([System.IO.File]::ReadAllText($Path).Trim() -eq $Expected)
}

function Invoke-ToFile {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$OutputPath,
        [string]$ErrorPath
    )
    $outputDirectory = Split-Path -Parent $OutputPath
    $temporaryOutput = Join-Path $outputDirectory ([System.IO.Path]::GetRandomFileName())
    $temporaryError = Join-Path $outputDirectory ([System.IO.Path]::GetRandomFileName())
    try {
        $process = Start-Process `
            -FilePath $FilePath `
            -ArgumentList $Arguments `
            -RedirectStandardOutput $temporaryOutput `
            -RedirectStandardError $temporaryError `
            -PassThru `
            -WindowStyle Hidden `
            -Wait
        if ($process.ExitCode -ne 0) {
            $standardOutput = if (Test-Path -LiteralPath $temporaryOutput) {
                [System.IO.File]::ReadAllText($temporaryOutput)
            } else { "" }
            $standardError = if (Test-Path -LiteralPath $temporaryError) {
                [System.IO.File]::ReadAllText($temporaryError)
            } else { "" }
            $details = ($standardError + $standardOutput).Trim()
            if (Test-Path -LiteralPath $temporaryError) {
                Move-Item -LiteralPath $temporaryError -Destination $ErrorPath -Force
            }
            throw "$FilePath failed with exit code $($process.ExitCode).`n$details"
        }
        Move-Item -LiteralPath $temporaryOutput -Destination $OutputPath -Force
        Move-Item -LiteralPath $temporaryError -Destination $ErrorPath -Force
    } finally {
        if (Test-Path -LiteralPath $temporaryOutput) {
            Remove-Item -LiteralPath $temporaryOutput -Force
        }
        if (Test-Path -LiteralPath $temporaryError) {
            Remove-Item -LiteralPath $temporaryError -Force
        }
    }
}

$compilerSources = @(Resolve-Manifest $manifestPath)
$runtimeSources = @(Resolve-Manifest $runtimeManifestPath)
$compilerInputSources = @($compilerSources + $runtimeSources | Select-Object -Unique)
$compilerHash = Get-ContentFingerprint $compilerInputSources
$started = Get-Date

if (-not (Test-Path -LiteralPath $stage1Compiler) -or
    -not (Test-Fingerprint $stage1Fingerprint $compilerHash)) {
    Write-Host "[fast 1/5] Stage1-hosted selfhost compiler cache MISS."
    $arguments = @(
        "run", "--project", $compilerProject, "-c", "Release", "--",
        "build"
    ) + $compilerSources + @(
        "-o", $stage1Compiler, "--target", "windows-x64", "-O1"
    )
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    [System.IO.File]::WriteAllText($stage1Fingerprint, $compilerHash)
} else {
    Write-Host "[fast 1/5] Stage1-hosted selfhost compiler cache HIT."
}

$fixturePaths = @($Fixture | ForEach-Object {
    (Resolve-Path (Join-Path $repoRoot $_)).Path
})
$needsSequence = $fixturePaths | Where-Object {
    [System.IO.File]::ReadAllText($_).Contains("import std.sequence")
}
if ($needsSequence) {
    $sequencePath = (Resolve-Path (Join-Path $repoRoot "stdlib\std\sequence.slg")).Path
    if ($fixturePaths -notcontains $sequencePath) {
        $fixturePaths += $sequencePath
    }
}
$needsEvent = $fixturePaths | Where-Object {
    [System.IO.File]::ReadAllText($_).Contains("import sys.event")
}
if ($needsEvent) {
    $eventPath = (Resolve-Path (Join-Path $repoRoot "stdlib\sys\event.slg")).Path
    if ($fixturePaths -notcontains $eventPath) {
        $fixturePaths += $eventPath
    }
}

$fixtureHash = Get-ContentFingerprint $fixturePaths
$actionText = "$compilerHash|$fixtureHash|$Target"
$actionHash = [Convert]::ToHexString(
    [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes($actionText))).Substring(0, 20)
$stage1Llvm = Join-Path $cacheRoot "$actionHash-stage1.ll"
$stage1Error = Join-Path $cacheRoot "$actionHash-stage1.err"

if (-not (Test-Path -LiteralPath $stage1Llvm) -or
    (Get-Item -LiteralPath $stage1Llvm).Length -eq 0) {
    Write-Host "[fast 2/5] Focused Stage1 LLVM cache MISS."
    Invoke-ToFile $stage1Compiler (@($Target) + $fixturePaths) $stage1Llvm $stage1Error
} else {
    Write-Host "[fast 2/5] Focused Stage1 LLVM cache HIT."
}
& $llvmAs $stage1Llvm -o ([System.IO.Path]::ChangeExtension($stage1Llvm, ".bc"))
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "[fast 3/5] Focused LLVM verifier PASS."

if (-not $NoExecute) {
    $fixtureName = [System.IO.Path]::GetFileNameWithoutExtension($fixturePaths[0])
    $expectedPath = Join-Path $repoRoot "examples\expected\$fixtureName.stdout.txt"
    if (Test-Path -LiteralPath $expectedPath) {
        $executable = Join-Path $cacheRoot "$actionHash-stage1.exe"
        & $clang -Wno-override-module $stage1Llvm -O1 -o $executable
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        $actual = (& $executable | Out-String).Replace("`r`n", "`n").TrimEnd("`n")
        $expected = [System.IO.File]::ReadAllText($expectedPath).Replace("`r`n", "`n").TrimEnd("`n")
        if ($LASTEXITCODE -ne 0 -or $actual -ne $expected) {
            throw "focused execution differs from $expectedPath"
        }
        Write-Host "[fast 4/5] Focused execution PASS."
    } else {
        Write-Host "[fast 4/5] No expected stdout; execution skipped."
    }
} else {
    Write-Host "[fast 4/5] Execution skipped by request."
}

if ($BootstrapStage2) {
    Write-Host "[final gate] Stage2 bootstrap cache MISS; full self-build starts once."
    $stage2Error = Join-Path $cacheRoot "selfhost-stage2.err"
    Invoke-ToFile $stage1Compiler (@($Target) + $compilerSources + $runtimeSources) $stage2Llvm $stage2Error
    & $llvmAs $stage2Llvm -o ([System.IO.Path]::ChangeExtension($stage2Llvm, ".bc"))
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $clang -Wno-override-module $stage2Llvm -O1 -o $stage2Compiler
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    [System.IO.File]::WriteAllText($stage2Fingerprint, $compilerHash)
}

if ($CompareStage2 -and
    (Test-Path -LiteralPath $stage2Compiler) -and
    (Test-Fingerprint $stage2Fingerprint $compilerHash)) {
    $stage2FocusedLlvm = Join-Path $cacheRoot "$actionHash-stage2.ll"
    $stage2FocusedError = Join-Path $cacheRoot "$actionHash-stage2.err"
    Invoke-ToFile $stage2Compiler (@($Target) + $fixturePaths) $stage2FocusedLlvm $stage2FocusedError
    & $llvmAs $stage2FocusedLlvm -o ([System.IO.Path]::ChangeExtension($stage2FocusedLlvm, ".bc"))
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $stage1Hash = Get-NormalizedTextHash $stage1Llvm
    $stage2Hash = Get-NormalizedTextHash $stage2FocusedLlvm
    if ($stage1Hash -ne $stage2Hash) {
        throw "Stage1/Stage2 focused LLVM differs: stage1=$stage1Hash stage2=$stage2Hash"
    }
    Write-Host "[fast 5/5] Stage1/Stage2 LLVM parity PASS $stage2Hash"
} elseif ($CompareStage2) {
    Write-Host "[fast 5/5] Stage2 is stale; parity deferred to the explicit final bootstrap gate."
} else {
    Write-Host "[fast 5/5] Stage2 comparison disabled."
}

$elapsed = [int]((Get-Date) - $started).TotalMilliseconds
Write-Host "[fast complete] ${elapsed}ms compiler=$($compilerHash.Substring(0, 12)) fixture=$($fixtureHash.Substring(0, 12))"
