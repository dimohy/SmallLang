param(
    [switch]$Rebuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsDir = Join-Path $repoRoot "artifacts\example-tests"
$runnerProject = Join-Path $repoRoot "tests\Sollang.ExampleTests\Sollang.ExampleTests.csproj"
$manifestPath = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-sollangc-driver.sources.txt"
$stage1Path = Join-Path $artifactsDir "selfhost-sollangc-driver.exe"
$stage2LlvmPath = Join-Path $artifactsDir "selfhost-stage2.ll"
$stage2BitcodePath = Join-Path $artifactsDir "selfhost-stage2.bc"
$stage2Path = Join-Path $artifactsDir "selfhost-stage2.exe"
$llvmDir = Join-Path $repoRoot ".tools\llvm-22.1.8"
$llvmAsPath = Join-Path $llvmDir "bin\llvm-as.exe"
$clangPath = Join-Path $llvmDir "bin\clang.exe"
$singleSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-single-smoke.slg"
$multiLibrarySource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-library-smoke.slg"
$multiMainSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-main-smoke.slg"
$groupedNotSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-grouped-not-smoke.slg"
$borrowConflictSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-borrow-conflict.slg"
$borrowUnionConflictSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-borrow-union-conflict.slg"
$borrowAliasConflictSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-borrow-alias-conflict.slg"
$borrowAggregateConflictSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-borrow-aggregate-conflict.slg"
$borrowProjectionConflictSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-borrow-projection-conflict.slg"
$partialMoveConflictSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-partial-move-conflict.slg"
$branchPartialMoveConflictSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-branch-partial-move-conflict.slg"
$parallelMutableCaptureSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-parallel-mutable-capture.slg"
$parallelNonSendableCaptureSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-parallel-nonsendable-capture.slg"
$referenceTemporarySource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-reference-temporary.slg"
$borrowSourceRuntime = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-borrow-source.slg"
$runtimeManifestPath = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-compiler-runtime.sources.txt"
$fingerprintSources = @(
    (Join-Path $repoRoot "examples\fixtures\429-selfhost-root\Alpha.slg")
    (Join-Path $repoRoot "examples\fixtures\429-selfhost-root\Zeta.slg")
    (Join-Path $repoRoot "examples\fixtures\429-selfhost-root\nested\Beta.slg")
)
$semanticContextSource = Join-Path $repoRoot "selfhost\semantic\context.slg"
$compilerRuntimeSources = Get-Content $runtimeManifestPath |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { Join-Path $repoRoot $_.Trim() }
$expectedStage2Bytes = 11963482L

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

$semanticContextText = [System.IO.File]::ReadAllText($semanticContextSource)
if (-not $semanticContextText.Contains("public struct SemanticSnapshot") -or
    $semanticContextText.Contains("public struct CompilationContext")) {
    throw "semantic workers are not separated from construction by SemanticSnapshot"
}

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
    $null = $process.Handle
    return $process
}

function Assert-ProcessSucceeded {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$ErrorPath,
        [string]$Description
    )

    $Process.WaitForExit()
    $Process.Refresh()
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
    $inputs = @($stage1Path, $manifestPath, $runtimeManifestPath) + $compilerRuntimeSources
    $inputs += Get-Content $manifestPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { Join-Path $repoRoot $_.Trim() }
    return -not ($inputs | Where-Object { (Get-Item $_).LastWriteTimeUtc -gt $stage2Time })
}

Write-Host "[stage2 1/7] Bootstrap or reuse the native stage-1 compiler."
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

Write-Host "[stage2 2/7] Build or reuse the complete stage-2 compiler."
if (Test-Stage2IsCurrent) {
    Write-Host "[stage2 2/7] REUSE current stage-2 compiler."
} else {
    $sourcePaths = Get-Content $manifestPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { (Resolve-Path (Join-Path $repoRoot $_.Trim())).Path }
    $sourcePaths += $compilerRuntimeSources | ForEach-Object { (Resolve-Path $_).Path }
    $sourceLineCount = ($sourcePaths | ForEach-Object { [System.IO.File]::ReadAllLines($_).LongLength } | Measure-Object -Sum).Sum
    $stage2ErrorPath = Join-Path $artifactsDir "selfhost-stage2.err.log"
    $stage2Started = Get-Date
    $lastAnalysisHeartbeat = -1
    Write-Host ("[stage2 2/7] phase 1/2 analyze {0:N0} source files / {1:N0} lines" -f $sourcePaths.Count, $sourceLineCount)
    $stage2Process = Invoke-ProcessToFile `
        -FilePath $stage1Path `
        -ArgumentList (@("windows") + $sourcePaths) `
        -OutputPath $stage2LlvmPath `
        -ErrorPath $stage2ErrorPath

    while (-not $stage2Process.HasExited) {
        Start-Sleep -Seconds 2
        $bytes = if (Test-Path $stage2LlvmPath) { (Get-Item $stage2LlvmPath).Length } else { 0L }
        if ($bytes -eq 0L) {
            $elapsed = [int]((Get-Date) - $stage2Started).TotalSeconds
            $heartbeat = [Math]::Floor($elapsed / 10)
            if ($heartbeat -gt $lastAnalysisHeartbeat) {
                Write-Host ("[stage2 2/7] phase 1/2 analyze active ({0:N0}s elapsed)" -f $elapsed)
                $lastAnalysisHeartbeat = $heartbeat
            }
        } else {
            $percent = [Math]::Min(100.0, 100.0 * $bytes / $expectedStage2Bytes)
            Write-Host ("[stage2 2/7] phase 2/2 LLVM {0:N0} bytes ({1:N1}%)" -f $bytes, $percent)
        }
        $stage2Process.Refresh()
    }
    Assert-ProcessSucceeded $stage2Process $stage2ErrorPath "stage-2 LLVM emission"

    & $llvmAsPath $stage2LlvmPath -o $stage2BitcodePath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $clangPath -Wno-override-module $stage2LlvmPath -O1 -o $stage2Path
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$stage2Llvm = [System.IO.File]::ReadAllText($stage2LlvmPath)
if ($stage2Llvm -notmatch '(?s)define internal void @sollang_parallel_callback_\d+\(ptr %group, i64 %index\) \{.*?call %sollang\.struct\.m(?<typedIrModule>\d+)_s19 @sollang_m\k<typedIrModule>_s\d+\(.*?\r?\n\}') {
    throw "stage-2 LLVM does not contain the function-local typed IR worker callback"
}

Write-Host "[stage2 3/7] Compare stage-1 and stage-2 LLVM with an explicit worker limit."
$singleStage1Llvm = Join-Path $artifactsDir "stage2-check-single-stage1.ll"
$singleStage2Llvm = Join-Path $artifactsDir "stage2-check-single-stage2.ll"
$singleStage1Error = Join-Path $artifactsDir "stage2-check-single-stage1.err"
$singleStage2Error = Join-Path $artifactsDir "stage2-check-single-stage2.err"
$singleArguments = @("windows", "--jobs", "2", $singleSource)
$singleStage1Process = Invoke-ProcessToFile $stage1Path $singleArguments $singleStage1Llvm $singleStage1Error
$singleStage2Process = Invoke-ProcessToFile $stage2Path $singleArguments $singleStage2Llvm $singleStage2Error
Assert-ProcessSucceeded $singleStage1Process $singleStage1Error "stage-1 single-file emission"
Assert-ProcessSucceeded $singleStage2Process $singleStage2Error "stage-2 single-file emission"
$singleStage1Hash = Get-NormalizedHash $singleStage1Llvm
$singleStage2Hash = Get-NormalizedHash $singleStage2Llvm
if ($singleStage1Hash -ne $singleStage2Hash) {
    throw "single-file normalized LLVM differs: stage1=$singleStage1Hash stage2=$singleStage2Hash"
}
if (-not ([System.IO.File]::ReadAllText($singleStage2Llvm).StartsWith("; sollang workers = 2"))) {
    throw "stage-2 compiler did not report the effective --jobs worker count"
}
Write-Host "[stage2 3/7] PASS $singleStage2Hash"

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
Write-Host "[stage2 3/7] PASS grouped-not $groupedStage2Hash"

Write-Host "[stage2 4/7] Compare stage-1 and stage-2 LLVM for imported source files."
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
Write-Host "[stage2 4/7] PASS $multiStage2Hash"

Write-Host "[stage2 5/7] Assemble, link, execute, and exercise the native build path."
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

$nativeBuildLlvm = Join-Path $artifactsDir "stage2-check-native-build.ll"
$nativeBuildExecutable = Join-Path $artifactsDir "stage2-check-native-build.exe"
$nativeBuildOutput = Join-Path $artifactsDir "stage2-check-native-build.stdout.txt"
$nativeBuildError = Join-Path $artifactsDir "stage2-check-native-build.stderr.txt"
Remove-Item -LiteralPath $nativeBuildLlvm, $nativeBuildExecutable -ErrorAction SilentlyContinue
$nativeBuildProcess = Invoke-ProcessToFile `
    -FilePath $stage2Path `
    -ArgumentList @("build-windows", $nativeBuildLlvm, $nativeBuildExecutable, $clangPath, $singleSource) `
    -OutputPath $nativeBuildOutput `
    -ErrorPath $nativeBuildError
Assert-ProcessSucceeded $nativeBuildProcess $nativeBuildError "stage-2 native build"

$nativeBuildMessage = ([System.IO.File]::ReadAllText($nativeBuildOutput)).TrimEnd("`r", "`n")
if ($nativeBuildMessage -ne "native build = 0") {
    throw "stage-2 native build did not report success: '$nativeBuildMessage'"
}
if (-not (Test-Path $nativeBuildLlvm) -or -not (Test-Path $nativeBuildExecutable)) {
    throw "stage-2 native build did not produce both LLVM and executable artifacts"
}

$nativeBuildActual = (& $nativeBuildExecutable | Out-String).TrimEnd("`r", "`n")
if ($LASTEXITCODE -ne 0 -or $nativeBuildActual -ne "stage2-single-ok") {
    throw "stage-2 native build execution failed: expected 'stage2-single-ok', actual '$nativeBuildActual'"
}

$stage1FingerprintOutput = Join-Path $artifactsDir "stage2-check-fingerprint-stage1.txt"
$stage2FingerprintOutput = Join-Path $artifactsDir "stage2-check-fingerprint-stage2.txt"
$stage1FingerprintError = Join-Path $artifactsDir "stage2-check-fingerprint-stage1.err"
$stage2FingerprintError = Join-Path $artifactsDir "stage2-check-fingerprint-stage2.err"
$fingerprintArguments = @("fingerprint") + $fingerprintSources
$stage1FingerprintProcess = Invoke-ProcessToFile $stage1Path $fingerprintArguments $stage1FingerprintOutput $stage1FingerprintError
$stage2FingerprintProcess = Invoke-ProcessToFile $stage2Path $fingerprintArguments $stage2FingerprintOutput $stage2FingerprintError
Assert-ProcessSucceeded $stage1FingerprintProcess $stage1FingerprintError "stage-1 module fingerprint emission"
Assert-ProcessSucceeded $stage2FingerprintProcess $stage2FingerprintError "stage-2 module fingerprint emission"
$stage1Fingerprints = [System.IO.File]::ReadAllText($stage1FingerprintOutput).Replace("`r`n", "`n")
$stage2Fingerprints = [System.IO.File]::ReadAllText($stage2FingerprintOutput).Replace("`r`n", "`n")
if ($stage1Fingerprints -ne $stage2Fingerprints) {
    throw "stage-1 and stage-2 module fingerprints differ"
}
$fingerprintLineCount = ([regex]::Matches($stage2Fingerprints, '(?m)^module fingerprint = \d+,\d+,\d+,\d+,\d+,\d+,\d+$')).Count
if ($fingerprintLineCount -ne 3) {
    throw "stage-2 module fingerprint mode emitted $fingerprintLineCount records instead of 3"
}
$stage1CacheOutput = Join-Path $artifactsDir "stage2-check-module-cache-stage1.txt"
$stage2CacheOutput = Join-Path $artifactsDir "stage2-check-module-cache-stage2.txt"
$stage1CacheError = Join-Path $artifactsDir "stage2-check-module-cache-stage1.err"
$stage2CacheError = Join-Path $artifactsDir "stage2-check-module-cache-stage2.err"
$stage1CachePath = Join-Path $artifactsDir "stage2-check-module-cache-stage1.bin"
$stage1CacheTemporary = Join-Path $artifactsDir "stage2-check-module-cache-stage1.tmp"
$stage2CachePath = Join-Path $artifactsDir "stage2-check-module-cache-stage2.bin"
$stage2CacheTemporary = Join-Path $artifactsDir "stage2-check-module-cache-stage2.tmp"
$stage1CacheArguments = @("interface-cache", $stage1CachePath, $stage1CacheTemporary) + $fingerprintSources
$stage2CacheArguments = @("interface-cache", $stage2CachePath, $stage2CacheTemporary) + $fingerprintSources
$stage1CacheProcess = Invoke-ProcessToFile $stage1Path $stage1CacheArguments $stage1CacheOutput $stage1CacheError
$stage2CacheProcess = Invoke-ProcessToFile $stage2Path $stage2CacheArguments $stage2CacheOutput $stage2CacheError
Assert-ProcessSucceeded $stage1CacheProcess $stage1CacheError "stage-1 module-cache planner"
Assert-ProcessSucceeded $stage2CacheProcess $stage2CacheError "stage-2 module-cache planner"
$stage1CacheText = ([System.IO.File]::ReadAllText($stage1CacheOutput)).Trim()
$stage2CacheText = ([System.IO.File]::ReadAllText($stage2CacheOutput)).Trim()
if ($stage1CacheText -ne "module cache = 0,3,0,0,1") {
    throw "stage-1 module-cache planner result differed: $stage1CacheText"
}
if ($stage2CacheText -ne "module cache = 0,3,0,0,1") {
    throw "stage-2 module-cache planner result differed: $stage2CacheText"
}
$stage1ArtifactOutput = Join-Path $artifactsDir "stage2-check-module-artifacts-stage1.txt"
$stage2ArtifactOutput = Join-Path $artifactsDir "stage2-check-module-artifacts-stage2.txt"
$stage1ArtifactError = Join-Path $artifactsDir "stage2-check-module-artifacts-stage1.err"
$stage2ArtifactError = Join-Path $artifactsDir "stage2-check-module-artifacts-stage2.err"
$artifactArguments = @("module-artifacts") + $fingerprintSources
$stage1ArtifactProcess = Invoke-ProcessToFile $stage1Path $artifactArguments $stage1ArtifactOutput $stage1ArtifactError
$stage2ArtifactProcess = Invoke-ProcessToFile $stage2Path $artifactArguments $stage2ArtifactOutput $stage2ArtifactError
Assert-ProcessSucceeded $stage1ArtifactProcess $stage1ArtifactError "stage-1 canonical module artifacts"
Assert-ProcessSucceeded $stage2ArtifactProcess $stage2ArtifactError "stage-2 canonical module artifacts"
$stage1ArtifactText = ([System.IO.File]::ReadAllText($stage1ArtifactOutput)).Trim()
$stage2ArtifactText = ([System.IO.File]::ReadAllText($stage2ArtifactOutput)).Trim()
if ($stage1ArtifactText -ne "module artifacts = 0,3,1") {
    throw "stage-1 canonical module artifacts differed: $stage1ArtifactText"
}
if ($stage2ArtifactText -ne $stage1ArtifactText) {
    throw "stage-2 canonical module artifacts differed: $stage2ArtifactText"
}
$stage1CodegenOutput = Join-Path $artifactsDir "stage2-check-codegen-units-stage1.txt"
$stage2CodegenOutput = Join-Path $artifactsDir "stage2-check-codegen-units-stage2.txt"
$stage1CodegenError = Join-Path $artifactsDir "stage2-check-codegen-units-stage1.err"
$stage2CodegenError = Join-Path $artifactsDir "stage2-check-codegen-units-stage2.err"
$stage1CodegenProcess = Invoke-ProcessToFile $stage1Path @("llvm-codegen-units") $stage1CodegenOutput $stage1CodegenError
$stage2CodegenProcess = Invoke-ProcessToFile $stage2Path @("llvm-codegen-units") $stage2CodegenOutput $stage2CodegenError
Assert-ProcessSucceeded $stage1CodegenProcess $stage1CodegenError "stage-1 canonical codegen units"
Assert-ProcessSucceeded $stage2CodegenProcess $stage2CodegenError "stage-2 canonical codegen units"
$stage1CodegenText = ([System.IO.File]::ReadAllText($stage1CodegenOutput)).Trim()
$stage2CodegenText = ([System.IO.File]::ReadAllText($stage2CodegenOutput)).Trim()
if ($stage1CodegenText -ne "codegen units = 0,2,6") {
    throw "stage-1 canonical codegen units differed: $stage1CodegenText"
}
if ($stage2CodegenText -ne $stage1CodegenText) {
    throw "stage-2 canonical codegen units differed: $stage2CodegenText"
}
Write-Host "[stage2 5/7] PASS execution, native build, fingerprints, module cache, typed-IR artifacts, and codegen-unit parity."

Write-Host "[stage2 6/7] Enforce production ownership diagnostics E17 through E22."
foreach ($conflict in @(
    @($borrowConflictSource, "single"),
    @($borrowUnionConflictSource, "union"),
    @($borrowAliasConflictSource, "alias"),
    @($borrowAggregateConflictSource, "aggregate"),
    @($borrowProjectionConflictSource, "projection")
)) {
    foreach ($compiler in @(
        @($stage1Path, "stage1"),
        @($stage2Path, "stage2")
    )) {
        $diagnosticOutput = Join-Path $artifactsDir "stage2-check-borrow-$($conflict[1])-$($compiler[1]).txt"
        $diagnosticError = Join-Path $artifactsDir "stage2-check-borrow-$($conflict[1])-$($compiler[1]).err"
        $diagnosticProcess = Invoke-ProcessToFile `
            -FilePath $compiler[0] `
            -ArgumentList @("windows", $conflict[0], $borrowSourceRuntime) `
            -OutputPath $diagnosticOutput `
            -ErrorPath $diagnosticError
        $diagnosticProcess.WaitForExit()
        $diagnosticProcess.Refresh()
        if ($diagnosticProcess.ExitCode -eq 0) {
            throw "$($compiler[1]) accepted a $($conflict[1])-origin move with a live borrowed Text view"
        }
        $diagnosticText = [System.IO.File]::ReadAllText($diagnosticOutput)
        if ($diagnosticText -notmatch 'error\[E21\].*origin moved while a borrowed Text view is still live') {
            throw "$($compiler[1]) did not emit ownership diagnostic E21 for $($conflict[1]) origin: '$diagnosticText'"
        }
        if ($diagnosticText -match '^target (datalayout|triple)') {
            throw "$($compiler[1]) began LLVM emission before rejecting $($conflict[1])-origin diagnostic E21"
        }
    }
}
foreach ($compiler in @(
    @($stage1Path, "stage1"),
    @($stage2Path, "stage2")
)) {
    $diagnosticOutput = Join-Path $artifactsDir "stage2-check-partial-move-$($compiler[1]).txt"
    $diagnosticError = Join-Path $artifactsDir "stage2-check-partial-move-$($compiler[1]).err"
    $diagnosticProcess = Invoke-ProcessToFile `
        -FilePath $compiler[0] `
        -ArgumentList @("windows", $partialMoveConflictSource) `
        -OutputPath $diagnosticOutput `
        -ErrorPath $diagnosticError
    $diagnosticProcess.WaitForExit()
    $diagnosticProcess.Refresh()
    if ($diagnosticProcess.ExitCode -eq 0) {
        throw "$($compiler[1]) accepted a reachable whole-owner use after a partial move"
    }
    $diagnosticText = [System.IO.File]::ReadAllText($diagnosticOutput)
    if ($diagnosticText -notmatch 'error\[E17\].*use of a partially moved value') {
        throw "$($compiler[1]) did not emit ownership diagnostic E17: '$diagnosticText'"
    }
    if ($diagnosticText -match '^target (datalayout|triple)') {
        throw "$($compiler[1]) began LLVM emission before rejecting partial-move diagnostic E17"
    }
}
foreach ($compiler in @(
    @($stage1Path, "stage1"),
    @($stage2Path, "stage2")
)) {
    $diagnosticOutput = Join-Path $artifactsDir "stage2-check-branch-partial-move-$($compiler[1]).txt"
    $diagnosticError = Join-Path $artifactsDir "stage2-check-branch-partial-move-$($compiler[1]).err"
    $diagnosticProcess = Invoke-ProcessToFile `
        -FilePath $compiler[0] `
        -ArgumentList @("windows", $branchPartialMoveConflictSource) `
        -OutputPath $diagnosticOutput `
        -ErrorPath $diagnosticError
    $diagnosticProcess.WaitForExit()
    $diagnosticProcess.Refresh()
    if ($diagnosticProcess.ExitCode -eq 0) {
        throw "$($compiler[1]) accepted a branch that exits with a partial move"
    }
    $diagnosticText = [System.IO.File]::ReadAllText($diagnosticOutput)
    if ($diagnosticText -notmatch 'error\[E20\].*partial move exits a branch or loop without reinitialization') {
        throw "$($compiler[1]) did not emit ownership diagnostic E20: '$diagnosticText'"
    }
    if ($diagnosticText -match '^target (datalayout|triple)') {
        throw "$($compiler[1]) began LLVM emission before rejecting branch-partial-move diagnostic E20"
    }
}
foreach ($compiler in @(
    @($stage1Path, "stage1"),
    @($stage2Path, "stage2")
)) {
    $diagnosticOutput = Join-Path $artifactsDir "stage2-check-parallel-mutable-capture-$($compiler[1]).txt"
    $diagnosticError = Join-Path $artifactsDir "stage2-check-parallel-mutable-capture-$($compiler[1]).err"
    $diagnosticProcess = Invoke-ProcessToFile `
        -FilePath $compiler[0] `
        -ArgumentList @("windows", $parallelMutableCaptureSource) `
        -OutputPath $diagnosticOutput `
        -ErrorPath $diagnosticError
    $diagnosticProcess.WaitForExit()
    $diagnosticProcess.Refresh()
    if ($diagnosticProcess.ExitCode -eq 0) {
        throw "$($compiler[1]) accepted a transitive mutable parallel capture"
    }
    $diagnosticText = [System.IO.File]::ReadAllText($diagnosticOutput)
    if ($diagnosticText -notmatch 'error\[E18\].*mutable binding captured by a parallel callback') {
        throw "$($compiler[1]) did not emit ownership diagnostic E18: '$diagnosticText'"
    }
    if ($diagnosticText -match '^target (datalayout|triple)') {
        throw "$($compiler[1]) began LLVM emission before rejecting parallel-capture diagnostic E18"
    }
}
foreach ($compiler in @(
    @($stage1Path, "stage1"),
    @($stage2Path, "stage2")
)) {
    $diagnosticOutput = Join-Path $artifactsDir "stage2-check-parallel-nonsendable-capture-$($compiler[1]).txt"
    $diagnosticError = Join-Path $artifactsDir "stage2-check-parallel-nonsendable-capture-$($compiler[1]).err"
    $diagnosticProcess = Invoke-ProcessToFile `
        -FilePath $compiler[0] `
        -ArgumentList @("windows", $parallelNonSendableCaptureSource) `
        -OutputPath $diagnosticOutput `
        -ErrorPath $diagnosticError
    $diagnosticProcess.WaitForExit()
    $diagnosticProcess.Refresh()
    if ($diagnosticProcess.ExitCode -eq 0) {
        throw "$($compiler[1]) accepted a transitive non-sendable parallel capture"
    }
    $diagnosticText = [System.IO.File]::ReadAllText($diagnosticOutput)
    if ($diagnosticText -notmatch 'error\[E19\].*non-sendable binding captured by a parallel callback') {
        throw "$($compiler[1]) did not emit ownership diagnostic E19: '$diagnosticText'"
    }
    if ($diagnosticText -match '^target (datalayout|triple)') {
        throw "$($compiler[1]) began LLVM emission before rejecting parallel-capture diagnostic E19"
    }
}
foreach ($compiler in @(
    @($stage1Path, "stage1"),
    @($stage2Path, "stage2")
)) {
    $diagnosticOutput = Join-Path $artifactsDir "stage2-check-reference-temporary-$($compiler[1]).txt"
    $diagnosticError = Join-Path $artifactsDir "stage2-check-reference-temporary-$($compiler[1]).err"
    $diagnosticProcess = Invoke-ProcessToFile $compiler[0] @("windows", $referenceTemporarySource) $diagnosticOutput $diagnosticError
    $diagnosticProcess.WaitForExit()
    $diagnosticProcess.Refresh()
    if ($diagnosticProcess.ExitCode -eq 0) { throw "$($compiler[1]) accepted a temporary readonly-reference argument" }
    $diagnosticText = [System.IO.File]::ReadAllText($diagnosticOutput)
    if ($diagnosticText -notmatch 'error\[E22\].*readonly reference argument requires stable immutable storage') {
        throw "$($compiler[1]) did not emit ownership diagnostic E22: '$diagnosticText'"
    }
    if ($diagnosticText -match '^target (datalayout|triple)') {
        throw "$($compiler[1]) began LLVM emission before rejecting readonly-reference diagnostic E22"
    }
}
Write-Host "[stage2 6/7] PASS E17-E22 ownership violations block LLVM emission in stage-1 and stage-2."

Write-Host "[stage2 7/7] Compare C# reference and native Sollang compiler runtime behavior."
& dotnet run --project $runnerProject -c Release --no-build -- `
    --skip-bootstrap `
    --compare-compilers `
    --exact 365-selfhost-llvm-stage2-single-smoke `
    --exact 366-selfhost-llvm-stage2-multi-file-smoke `
    --jobs 2
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "[stage2 7/7] PASS complete stage-2 differential verification."
