param(
    [switch]$Rebuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsDir = Join-Path $repoRoot "artifacts\example-tests"
$runnerProject = Join-Path $repoRoot "tests\SmallLang.ExampleTests\SmallLang.ExampleTests.csproj"
$manifestPath = Join-Path $repoRoot "tests\SmallLang.ExampleTests\Fixtures\selfhost-slc-driver.sources.txt"
$stage1Path = Join-Path $artifactsDir "selfhost-slc-driver.exe"
$stage2LlvmPath = Join-Path $artifactsDir "selfhost-stage2.ll"
$stage2BitcodePath = Join-Path $artifactsDir "selfhost-stage2.bc"
$stage2Path = Join-Path $artifactsDir "selfhost-stage2.exe"
$llvmDir = Join-Path $repoRoot ".tools\llvm-22.1.8"
$llvmAsPath = Join-Path $llvmDir "bin\llvm-as.exe"
$clangPath = Join-Path $llvmDir "bin\clang.exe"
$singleSource = Join-Path $repoRoot "tests\SmallLang.ExampleTests\Fixtures\selfhost-stage2-single-smoke.sl"
$multiLibrarySource = Join-Path $repoRoot "tests\SmallLang.ExampleTests\Fixtures\selfhost-stage2-library-smoke.sl"
$multiMainSource = Join-Path $repoRoot "tests\SmallLang.ExampleTests\Fixtures\selfhost-stage2-main-smoke.sl"
$groupedNotSource = Join-Path $repoRoot "tests\SmallLang.ExampleTests\Fixtures\selfhost-stage2-grouped-not-smoke.sl"
$expectedStage2Bytes = 6730900L

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

function Invoke-ProcessToFile {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$OutputPath,
        [string]$ErrorPath
    )

    Remove-Item -LiteralPath $OutputPath, $ErrorPath -ErrorAction SilentlyContinue
    $process = Start-Process `
        -FilePath $FilePath `
        -ArgumentList $ArgumentList `
        -RedirectStandardOutput $OutputPath `
        -RedirectStandardError $ErrorPath `
        -PassThru `
        -WindowStyle Hidden
    return $process
}

function Assert-ProcessSucceeded {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$ErrorPath,
        [string]$Description
    )

    $Process.WaitForExit()
    if ($Process.ExitCode -ne 0) {
        $details = if (Test-Path $ErrorPath) { Get-Content $ErrorPath -Raw } else { "" }
        throw "$Description failed with exit code $($Process.ExitCode).`n$details"
    }
}

function Get-NormalizedHash {
    param([string]$Path)

    $content = [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($content)
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes))
}

function Test-Stage2IsCurrent {
    if ($Rebuild -or -not (Test-Path $stage2Path) -or -not (Test-Path $stage2LlvmPath)) {
        return $false
    }

    $stage2Time = (Get-Item $stage2Path).LastWriteTimeUtc
    $inputs = @($stage1Path, $manifestPath)
    $inputs += Get-Content $manifestPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { Join-Path $repoRoot $_.Trim() }
    return -not ($inputs | Where-Object { (Get-Item $_).LastWriteTimeUtc -gt $stage2Time })
}

Write-Host "[stage2 1/6] Bootstrap or reuse the native stage-1 compiler."
& dotnet run --project $runnerProject -c Release -- `
    --exact 365-selfhost-llvm-stage2-single-smoke `
    --exact 366-selfhost-llvm-stage2-multi-file-smoke `
    --jobs 2
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not (Test-Path $stage1Path)) {
    throw "stage-1 compiler was not produced: $stage1Path"
}

Write-Host "[stage2 2/6] Build or reuse the complete stage-2 compiler."
if (Test-Stage2IsCurrent) {
    Write-Host "[stage2 2/6] REUSE current stage-2 compiler."
} else {
    $sourcePaths = Get-Content $manifestPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { (Resolve-Path (Join-Path $repoRoot $_.Trim())).Path }
    $stage2ErrorPath = Join-Path $artifactsDir "selfhost-stage2.err.log"
    $stage2Process = Invoke-ProcessToFile `
        -FilePath $stage1Path `
        -ArgumentList (@("windows") + $sourcePaths) `
        -OutputPath $stage2LlvmPath `
        -ErrorPath $stage2ErrorPath

    while (-not $stage2Process.HasExited) {
        Start-Sleep -Seconds 2
        $bytes = if (Test-Path $stage2LlvmPath) { (Get-Item $stage2LlvmPath).Length } else { 0L }
        $percent = [Math]::Min(99.9, 100.0 * $bytes / $expectedStage2Bytes)
        Write-Host ("[stage2 2/6] LLVM {0:N0} bytes ({1:N1}% heuristic)" -f $bytes, $percent)
        $stage2Process.Refresh()
    }
    Assert-ProcessSucceeded $stage2Process $stage2ErrorPath "stage-2 LLVM emission"

    & $llvmAsPath $stage2LlvmPath -o $stage2BitcodePath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $clangPath -Wno-override-module $stage2LlvmPath -O1 -o $stage2Path
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "[stage2 3/6] Compare stage-1 and stage-2 LLVM for one source file."
$singleStage1Llvm = Join-Path $artifactsDir "stage2-check-single-stage1.ll"
$singleStage2Llvm = Join-Path $artifactsDir "stage2-check-single-stage2.ll"
$singleStage1Error = Join-Path $artifactsDir "stage2-check-single-stage1.err"
$singleStage2Error = Join-Path $artifactsDir "stage2-check-single-stage2.err"
$singleStage1Process = Invoke-ProcessToFile $stage1Path @("windows", $singleSource) $singleStage1Llvm $singleStage1Error
$singleStage2Process = Invoke-ProcessToFile $stage2Path @("windows", $singleSource) $singleStage2Llvm $singleStage2Error
Assert-ProcessSucceeded $singleStage1Process $singleStage1Error "stage-1 single-file emission"
Assert-ProcessSucceeded $singleStage2Process $singleStage2Error "stage-2 single-file emission"
$singleStage1Hash = Get-NormalizedHash $singleStage1Llvm
$singleStage2Hash = Get-NormalizedHash $singleStage2Llvm
if ($singleStage1Hash -ne $singleStage2Hash) {
    throw "single-file normalized LLVM differs: stage1=$singleStage1Hash stage2=$singleStage2Hash"
}
Write-Host "[stage2 3/6] PASS $singleStage2Hash"

$groupedStage1Llvm = Join-Path $artifactsDir "stage2-check-grouped-not-stage1.ll"
$groupedStage2Llvm = Join-Path $artifactsDir "stage2-check-grouped-not-stage2.ll"
$groupedStage1Error = Join-Path $artifactsDir "stage2-check-grouped-not-stage1.err"
$groupedStage2Error = Join-Path $artifactsDir "stage2-check-grouped-not-stage2.err"
$groupedStage1Process = Invoke-ProcessToFile $stage1Path @("windows", $groupedNotSource) $groupedStage1Llvm $groupedStage1Error
$groupedStage2Process = Invoke-ProcessToFile $stage2Path @("windows", $groupedNotSource) $groupedStage2Llvm $groupedStage2Error
Assert-ProcessSucceeded $groupedStage1Process $groupedStage1Error "stage-1 grouped-not emission"
Assert-ProcessSucceeded $groupedStage2Process $groupedStage2Error "stage-2 grouped-not emission"
$groupedStage1Hash = Get-NormalizedHash $groupedStage1Llvm
$groupedStage2Hash = Get-NormalizedHash $groupedStage2Llvm
if ($groupedStage1Hash -ne $groupedStage2Hash) {
    throw "grouped-not normalized LLVM differs: stage1=$groupedStage1Hash stage2=$groupedStage2Hash"
}
if (-not ([System.IO.File]::ReadAllText($groupedStage2Llvm).Contains("xor i1"))) {
    throw "grouped-not stage-2 LLVM does not contain unary Boolean lowering"
}
Write-Host "[stage2 3/6] PASS grouped-not $groupedStage2Hash"

Write-Host "[stage2 4/6] Compare stage-1 and stage-2 LLVM for imported source files."
$multiStage1Llvm = Join-Path $artifactsDir "stage2-check-multi-stage1.ll"
$multiStage2Llvm = Join-Path $artifactsDir "stage2-check-multi-stage2.ll"
$multiStage1Error = Join-Path $artifactsDir "stage2-check-multi-stage1.err"
$multiStage2Error = Join-Path $artifactsDir "stage2-check-multi-stage2.err"
$multiArguments = @("windows", $multiLibrarySource, $multiMainSource)
$multiStage1Process = Invoke-ProcessToFile $stage1Path $multiArguments $multiStage1Llvm $multiStage1Error
$multiStage2Process = Invoke-ProcessToFile $stage2Path $multiArguments $multiStage2Llvm $multiStage2Error
Assert-ProcessSucceeded $multiStage1Process $multiStage1Error "stage-1 multi-file emission"
Assert-ProcessSucceeded $multiStage2Process $multiStage2Error "stage-2 multi-file emission"
$multiStage1Hash = Get-NormalizedHash $multiStage1Llvm
$multiStage2Hash = Get-NormalizedHash $multiStage2Llvm
if ($multiStage1Hash -ne $multiStage2Hash) {
    throw "multi-file normalized LLVM differs: stage1=$multiStage1Hash stage2=$multiStage2Hash"
}
Write-Host "[stage2 4/6] PASS $multiStage2Hash"

Write-Host "[stage2 5/6] Assemble, link, and execute both stage-2 smoke modules."
foreach ($case in @(
    @($singleStage2Llvm, "stage2-check-single.exe", "stage2-single-ok"),
    @($multiStage2Llvm, "stage2-check-multi.exe", "stage2-multi-ok"),
    @($groupedStage2Llvm, "stage2-check-grouped-not.exe", "grouped-not-ok")
)) {
    $executablePath = Join-Path $artifactsDir $case[1]
    & $llvmAsPath $case[0] -o ([System.IO.Path]::ChangeExtension($executablePath, ".bc"))
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $clangPath -Wno-override-module $case[0] -o $executablePath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $actual = (& $executablePath | Out-String).TrimEnd("`r", "`n")
    if ($LASTEXITCODE -ne 0 -or $actual -ne $case[2]) {
        throw "stage-2 smoke execution failed: expected '$($case[2])', actual '$actual'"
    }
}
Write-Host "[stage2 5/6] PASS single, grouped-not, and multi-file execution."

Write-Host "[stage2 6/6] Compare C# reference and native SL compiler runtime behavior."
& dotnet run --project $runnerProject -c Release --no-build -- `
    --skip-bootstrap `
    --compare-compilers `
    --exact 365-selfhost-llvm-stage2-single-smoke `
    --exact 366-selfhost-llvm-stage2-multi-file-smoke `
    --jobs 2
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "[stage2 6/6] PASS complete stage-2 differential verification."
