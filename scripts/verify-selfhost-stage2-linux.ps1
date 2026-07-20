param(
    [string]$Distribution = "Ubuntu",
    [switch]$Rebuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsDir = Join-Path $repoRoot "artifacts\example-tests"
$runnerProject = Join-Path $repoRoot "tests\Sollang.ExampleTests\Sollang.ExampleTests.csproj"
$manifestPath = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-sollangc-driver.sources.txt"
$stage1Path = Join-Path $artifactsDir "selfhost-sollangc-driver.exe"
$stage2LlvmPath = Join-Path $artifactsDir "selfhost-stage2-linux.ll"
$stage2BitcodePath = Join-Path $artifactsDir "selfhost-stage2-linux.bc"
$stage2ObjectPath = Join-Path $artifactsDir "selfhost-stage2-linux.o"
$stage2Path = Join-Path $artifactsDir "selfhost-stage2-linux"
$llvmDir = Join-Path $repoRoot ".tools\llvm-22.1.8"
$llvmAsPath = Join-Path $llvmDir "bin\llvm-as.exe"
$clangPath = Join-Path $llvmDir "bin\clang.exe"
$runtimeManifestPath = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-compiler-runtime.sources.txt"
$compilerRuntimeSources = Get-Content $runtimeManifestPath |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { Join-Path $repoRoot $_.Trim() }
$singleSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-single-smoke.slg"
$multiLibrarySource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-library-smoke.slg"
$multiMainSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-main-smoke.slg"
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
$referenceLivenessSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-reference-liveness.slg"
$referenceOwnerMoveSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-reference-owner-move.slg"
$referenceLoopContinueSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-reference-loop-continue.slg"
$referenceStoredStructSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-reference-stored-struct.slg"
$referenceAggregateEscapeSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-reference-aggregate-escape.slg"
$referenceStoredEnumSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-reference-stored-enum.slg"
$referenceStoredArraySource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-reference-stored-array.slg"
$referenceEnumEscapeSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-reference-enum-escape.slg"
$referenceArrayEscapeSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-reference-array-escape.slg"
$borrowSourceRuntime = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-borrow-source.slg"
$expectedStage2Bytes = 11987197L

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

function Convert-ToWslPath {
    param([string]$Path)

    $absolute = [System.IO.Path]::GetFullPath($Path)
    $drive = $absolute.Substring(0, 1).ToLowerInvariant()
    $tail = $absolute.Substring(3).Replace('\', '/')
    return "/mnt/$drive/$tail"
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

function Build-And-ExecuteLinuxLlvm {
    param(
        [string]$LlvmPath,
        [string]$Name,
        [string]$Expected
    )

    $bitcodePath = Join-Path $artifactsDir "$Name.bc"
    $objectPath = Join-Path $artifactsDir "$Name.o"
    $executablePath = Join-Path $artifactsDir $Name
    & $llvmAsPath $LlvmPath -o $bitcodePath
    if ($LASTEXITCODE -ne 0) { throw "llvm-as failed for $Name" }
    & $clangPath --target=x86_64-unknown-linux-gnu -c $LlvmPath -O0 -o $objectPath
    if ($LASTEXITCODE -ne 0) { throw "Linux object generation failed for $Name" }
    & wsl.exe -d $Distribution -- gcc (Convert-ToWslPath $objectPath) -o (Convert-ToWslPath $executablePath)
    if ($LASTEXITCODE -ne 0) { throw "Linux link failed for $Name" }
    $actual = (& wsl.exe -d $Distribution -- (Convert-ToWslPath $executablePath) | Out-String).TrimEnd("`r", "`n")
    if ($LASTEXITCODE -ne 0 -or $actual -ne $Expected) {
        throw "Linux execution failed for $Name`: expected '$Expected', actual '$actual'"
    }
}

Write-Host "[linux-stage2 1/6] Bootstrap or reuse the native stage-1 compiler."
& dotnet run --project $runnerProject -c Release -- `
    --exact 365-selfhost-llvm-stage2-single-smoke `
    --exact 366-selfhost-llvm-stage2-multi-file-smoke `
    --jobs 2
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not (Test-Path $stage1Path)) { throw "stage-1 compiler was not produced: $stage1Path" }
Write-Host "[linux-stage2 1/6] PASS native stage 1."

Write-Host "[linux-stage2 2/6] Build or reuse the complete Linux stage-2 compiler."
if (Test-Stage2IsCurrent) {
    Write-Host "[linux-stage2 2/6] REUSE current Linux stage 2."
} else {
    $sourcePaths = Get-Content $manifestPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { (Resolve-Path (Join-Path $repoRoot $_.Trim())).Path }
    $sourcePaths += $compilerRuntimeSources | ForEach-Object { (Resolve-Path $_).Path }
    $sourceLineCount = ($sourcePaths | ForEach-Object { [System.IO.File]::ReadAllLines($_).LongLength } | Measure-Object -Sum).Sum
    $stage2ErrorPath = Join-Path $artifactsDir "selfhost-stage2-linux.err.log"
    $stage2Started = Get-Date
    $lastAnalysisHeartbeat = -1
    Write-Host ("[linux-stage2 2/6] phase 1/2 analyze {0:N0} source files / {1:N0} lines" -f $sourcePaths.Count, $sourceLineCount)
    $stage2Process = Invoke-ProcessToFile `
        -FilePath $stage1Path `
        -ArgumentList (@("linux", "--jobs", "4") + $sourcePaths) `
        -OutputPath $stage2LlvmPath `
        -ErrorPath $stage2ErrorPath
    while (-not $stage2Process.HasExited) {
        Start-Sleep -Seconds 2
        $bytes = if (Test-Path $stage2LlvmPath) { (Get-Item $stage2LlvmPath).Length } else { 0L }
        if ($bytes -eq 0L) {
            $elapsed = [int]((Get-Date) - $stage2Started).TotalSeconds
            $heartbeat = [Math]::Floor($elapsed / 10)
            if ($heartbeat -gt $lastAnalysisHeartbeat) {
                Write-Host ("[linux-stage2 2/6] phase 1/2 analyze active ({0:N0}s elapsed)" -f $elapsed)
                $lastAnalysisHeartbeat = $heartbeat
            }
        } else {
            $percent = [Math]::Min(100.0, 100.0 * $bytes / $expectedStage2Bytes)
            Write-Host ("[linux-stage2 2/6] phase 2/2 LLVM {0:N0} bytes ({1:N1}%)" -f $bytes, $percent)
        }
        $stage2Process.Refresh()
    }
    Assert-ProcessSucceeded $stage2Process $stage2ErrorPath "Linux stage-2 LLVM emission"
    & $llvmAsPath $stage2LlvmPath -o $stage2BitcodePath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $clangPath --target=x86_64-unknown-linux-gnu -c $stage2LlvmPath -O0 -o $stage2ObjectPath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & wsl.exe -d $Distribution -- gcc (Convert-ToWslPath $stage2ObjectPath) -pthread -o (Convert-ToWslPath $stage2Path)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Write-Host "[linux-stage2 2/6] PASS $((Get-Item $stage2LlvmPath).Length) LLVM bytes."

Write-Host "[linux-stage2 3/6] Compare stage-1 and stage-2 single-file LLVM."
$singleStage1Llvm = Join-Path $artifactsDir "linux-stage2-check-single-stage1.ll"
$singleStage2Llvm = Join-Path $artifactsDir "linux-stage2-check-single-stage2.ll"
$singleStage1Error = Join-Path $artifactsDir "linux-stage2-check-single-stage1.err"
$singleStage2Error = Join-Path $artifactsDir "linux-stage2-check-single-stage2.err"
$singleStage1 = Invoke-ProcessToFile $stage1Path @("linux", "--jobs", "2", $singleSource) $singleStage1Llvm $singleStage1Error
$singleStage2 = Invoke-ProcessToFile "wsl.exe" @("-d", $Distribution, "--", (Convert-ToWslPath $stage2Path), "linux", "--jobs", "2", (Convert-ToWslPath $singleSource)) $singleStage2Llvm $singleStage2Error
Assert-ProcessSucceeded $singleStage1 $singleStage1Error "Linux stage-1 single-file emission"
Assert-ProcessSucceeded $singleStage2 $singleStage2Error "Linux stage-2 single-file emission"
$singleStage1Hash = Get-NormalizedHash $singleStage1Llvm
$singleStage2Hash = Get-NormalizedHash $singleStage2Llvm
if ($singleStage1Hash -ne $singleStage2Hash) { throw "Linux single-file LLVM differs: stage1=$singleStage1Hash stage2=$singleStage2Hash" }
Write-Host "[linux-stage2 3/6] PASS $singleStage2Hash"

Write-Host "[linux-stage2 4/6] Compare stage-1 and stage-2 imported multi-file LLVM."
$multiStage1Llvm = Join-Path $artifactsDir "linux-stage2-check-multi-stage1.ll"
$multiStage2Llvm = Join-Path $artifactsDir "linux-stage2-check-multi-stage2.ll"
$multiStage1Error = Join-Path $artifactsDir "linux-stage2-check-multi-stage1.err"
$multiStage2Error = Join-Path $artifactsDir "linux-stage2-check-multi-stage2.err"
$multiStage1 = Invoke-ProcessToFile $stage1Path @("linux", $multiLibrarySource, $multiMainSource) $multiStage1Llvm $multiStage1Error
$multiStage2 = Invoke-ProcessToFile "wsl.exe" @("-d", $Distribution, "--", (Convert-ToWslPath $stage2Path), "linux", (Convert-ToWslPath $multiLibrarySource), (Convert-ToWslPath $multiMainSource)) $multiStage2Llvm $multiStage2Error
Assert-ProcessSucceeded $multiStage1 $multiStage1Error "Linux stage-1 multi-file emission"
Assert-ProcessSucceeded $multiStage2 $multiStage2Error "Linux stage-2 multi-file emission"
$multiStage1Hash = Get-NormalizedHash $multiStage1Llvm
$multiStage2Hash = Get-NormalizedHash $multiStage2Llvm
if ($multiStage1Hash -ne $multiStage2Hash) { throw "Linux multi-file LLVM differs: stage1=$multiStage1Hash stage2=$multiStage2Hash" }
$codegenStage1Output = Join-Path $artifactsDir "linux-stage2-check-codegen-units-stage1.txt"
$codegenStage2Output = Join-Path $artifactsDir "linux-stage2-check-codegen-units-stage2.txt"
$codegenStage1Error = Join-Path $artifactsDir "linux-stage2-check-codegen-units-stage1.err"
$codegenStage2Error = Join-Path $artifactsDir "linux-stage2-check-codegen-units-stage2.err"
$codegenStage1 = Invoke-ProcessToFile $stage1Path @("llvm-codegen-units") $codegenStage1Output $codegenStage1Error
$codegenStage2 = Invoke-ProcessToFile "wsl.exe" @("-d", $Distribution, "--", (Convert-ToWslPath $stage2Path), "llvm-codegen-units") $codegenStage2Output $codegenStage2Error
Assert-ProcessSucceeded $codegenStage1 $codegenStage1Error "Linux stage-1 canonical codegen units"
Assert-ProcessSucceeded $codegenStage2 $codegenStage2Error "Linux stage-2 canonical codegen units"
$codegenStage1Text = ([System.IO.File]::ReadAllText($codegenStage1Output)).Trim()
$codegenStage2Text = ([System.IO.File]::ReadAllText($codegenStage2Output)).Trim()
if ($codegenStage1Text -ne "codegen units = 0,2,6") { throw "Linux stage-1 canonical codegen units differed: $codegenStage1Text" }
if ($codegenStage2Text -ne $codegenStage1Text) { throw "Linux stage-2 canonical codegen units differed: $codegenStage2Text" }
Write-Host "[linux-stage2 4/6] PASS $multiStage2Hash"

Write-Host "[linux-stage2 5/6] Assemble, link, and execute both Linux stage-2 products."
Build-And-ExecuteLinuxLlvm $singleStage2Llvm "linux-stage2-check-single" "stage2-single-ok"
Build-And-ExecuteLinuxLlvm $multiStage2Llvm "linux-stage2-check-multi" "stage2-multi-ok"
Write-Host "[linux-stage2 5/6] PASS Linux stage-2 products execute."

Write-Host "[linux-stage2 6/6] Enforce production ownership diagnostics E17 through E23."
foreach ($conflict in @(
    @($borrowConflictSource, "single"),
    @($borrowUnionConflictSource, "union"),
    @($borrowAliasConflictSource, "alias"),
    @($borrowAggregateConflictSource, "aggregate"),
    @($borrowProjectionConflictSource, "projection")
)) {
    $stage1DiagnosticOutput = Join-Path $artifactsDir "linux-stage2-check-borrow-$($conflict[1])-stage1.txt"
    $stage1DiagnosticError = Join-Path $artifactsDir "linux-stage2-check-borrow-$($conflict[1])-stage1.err"
    $stage1Diagnostic = Invoke-ProcessToFile $stage1Path @("linux", $conflict[0], $borrowSourceRuntime) $stage1DiagnosticOutput $stage1DiagnosticError
    $stage2DiagnosticOutput = Join-Path $artifactsDir "linux-stage2-check-borrow-$($conflict[1])-stage2.txt"
    $stage2DiagnosticError = Join-Path $artifactsDir "linux-stage2-check-borrow-$($conflict[1])-stage2.err"
    $stage2Diagnostic = Invoke-ProcessToFile "wsl.exe" @(
        "-d", $Distribution, "--", (Convert-ToWslPath $stage2Path), "linux",
        (Convert-ToWslPath $conflict[0]), (Convert-ToWslPath $borrowSourceRuntime)
    ) $stage2DiagnosticOutput $stage2DiagnosticError
    foreach ($candidate in @(
        @($stage1Diagnostic, $stage1DiagnosticOutput, "stage1"),
        @($stage2Diagnostic, $stage2DiagnosticOutput, "stage2")
    )) {
        $candidate[0].WaitForExit()
        $candidate[0].Refresh()
        if ($candidate[0].ExitCode -eq 0) { throw "$($candidate[2]) accepted a $($conflict[1])-origin move with a live borrowed Text view" }
        $diagnosticText = [System.IO.File]::ReadAllText($candidate[1])
        if ($diagnosticText -notmatch 'error\[E21\].*origin moved while a borrowed Text view is still live') {
            throw "$($candidate[2]) did not emit ownership diagnostic E21 for $($conflict[1]) origin: '$diagnosticText'"
        }
        if ($diagnosticText -match '^target (datalayout|triple)') {
            throw "$($candidate[2]) began LLVM emission before rejecting $($conflict[1])-origin diagnostic E21"
        }
    }
}
$stage1PartialMoveOutput = Join-Path $artifactsDir "linux-stage2-check-partial-move-stage1.txt"
$stage1PartialMoveError = Join-Path $artifactsDir "linux-stage2-check-partial-move-stage1.err"
$stage1PartialMove = Invoke-ProcessToFile $stage1Path @("linux", $partialMoveConflictSource) $stage1PartialMoveOutput $stage1PartialMoveError
$stage2PartialMoveOutput = Join-Path $artifactsDir "linux-stage2-check-partial-move-stage2.txt"
$stage2PartialMoveError = Join-Path $artifactsDir "linux-stage2-check-partial-move-stage2.err"
$stage2PartialMove = Invoke-ProcessToFile "wsl.exe" @(
    "-d", $Distribution, "--", (Convert-ToWslPath $stage2Path), "linux",
    (Convert-ToWslPath $partialMoveConflictSource)
) $stage2PartialMoveOutput $stage2PartialMoveError
foreach ($candidate in @(
    @($stage1PartialMove, $stage1PartialMoveOutput, "stage1"),
    @($stage2PartialMove, $stage2PartialMoveOutput, "stage2")
)) {
    $candidate[0].WaitForExit()
    $candidate[0].Refresh()
    if ($candidate[0].ExitCode -eq 0) { throw "$($candidate[2]) accepted a reachable whole-owner use after a partial move" }
    $diagnosticText = [System.IO.File]::ReadAllText($candidate[1])
    if ($diagnosticText -notmatch 'error\[E17\].*use of a partially moved value') {
        throw "$($candidate[2]) did not emit ownership diagnostic E17: '$diagnosticText'"
    }
    if ($diagnosticText -match '^target (datalayout|triple)') {
        throw "$($candidate[2]) began LLVM emission before rejecting partial-move diagnostic E17"
    }
}
$stage1BranchPartialMoveOutput = Join-Path $artifactsDir "linux-stage2-check-branch-partial-move-stage1.txt"
$stage1BranchPartialMoveError = Join-Path $artifactsDir "linux-stage2-check-branch-partial-move-stage1.err"
$stage1BranchPartialMove = Invoke-ProcessToFile $stage1Path @("linux", $branchPartialMoveConflictSource) $stage1BranchPartialMoveOutput $stage1BranchPartialMoveError
$stage2BranchPartialMoveOutput = Join-Path $artifactsDir "linux-stage2-check-branch-partial-move-stage2.txt"
$stage2BranchPartialMoveError = Join-Path $artifactsDir "linux-stage2-check-branch-partial-move-stage2.err"
$stage2BranchPartialMove = Invoke-ProcessToFile "wsl.exe" @(
    "-d", $Distribution, "--", (Convert-ToWslPath $stage2Path), "linux",
    (Convert-ToWslPath $branchPartialMoveConflictSource)
) $stage2BranchPartialMoveOutput $stage2BranchPartialMoveError
foreach ($candidate in @(
    @($stage1BranchPartialMove, $stage1BranchPartialMoveOutput, "stage1"),
    @($stage2BranchPartialMove, $stage2BranchPartialMoveOutput, "stage2")
)) {
    $candidate[0].WaitForExit()
    $candidate[0].Refresh()
    if ($candidate[0].ExitCode -eq 0) { throw "$($candidate[2]) accepted a branch that exits with a partial move" }
    $diagnosticText = [System.IO.File]::ReadAllText($candidate[1])
    if ($diagnosticText -notmatch 'error\[E20\].*partial move exits a branch or loop without reinitialization') {
        throw "$($candidate[2]) did not emit ownership diagnostic E20: '$diagnosticText'"
    }
    if ($diagnosticText -match '^target (datalayout|triple)') {
        throw "$($candidate[2]) began LLVM emission before rejecting branch-partial-move diagnostic E20"
    }
}
$stage1ParallelCaptureOutput = Join-Path $artifactsDir "linux-stage2-check-parallel-mutable-capture-stage1.txt"
$stage1ParallelCaptureError = Join-Path $artifactsDir "linux-stage2-check-parallel-mutable-capture-stage1.err"
$stage1ParallelCapture = Invoke-ProcessToFile $stage1Path @("linux", $parallelMutableCaptureSource) $stage1ParallelCaptureOutput $stage1ParallelCaptureError
$stage2ParallelCaptureOutput = Join-Path $artifactsDir "linux-stage2-check-parallel-mutable-capture-stage2.txt"
$stage2ParallelCaptureError = Join-Path $artifactsDir "linux-stage2-check-parallel-mutable-capture-stage2.err"
$stage2ParallelCapture = Invoke-ProcessToFile "wsl.exe" @(
    "-d", $Distribution, "--", (Convert-ToWslPath $stage2Path), "linux",
    (Convert-ToWslPath $parallelMutableCaptureSource)
) $stage2ParallelCaptureOutput $stage2ParallelCaptureError
foreach ($candidate in @(
    @($stage1ParallelCapture, $stage1ParallelCaptureOutput, "stage1"),
    @($stage2ParallelCapture, $stage2ParallelCaptureOutput, "stage2")
)) {
    $candidate[0].WaitForExit()
    $candidate[0].Refresh()
    if ($candidate[0].ExitCode -eq 0) { throw "$($candidate[2]) accepted a transitive mutable parallel capture" }
    $diagnosticText = [System.IO.File]::ReadAllText($candidate[1])
    if ($diagnosticText -notmatch 'error\[E18\].*mutable binding captured by a parallel callback') {
        throw "$($candidate[2]) did not emit ownership diagnostic E18: '$diagnosticText'"
    }
    if ($diagnosticText -match '^target (datalayout|triple)') {
        throw "$($candidate[2]) began LLVM emission before rejecting parallel-capture diagnostic E18"
    }
}
$stage1NonSendableCaptureOutput = Join-Path $artifactsDir "linux-stage2-check-parallel-nonsendable-capture-stage1.txt"
$stage1NonSendableCaptureError = Join-Path $artifactsDir "linux-stage2-check-parallel-nonsendable-capture-stage1.err"
$stage1NonSendableCapture = Invoke-ProcessToFile $stage1Path @("linux", $parallelNonSendableCaptureSource) $stage1NonSendableCaptureOutput $stage1NonSendableCaptureError
$stage2NonSendableCaptureOutput = Join-Path $artifactsDir "linux-stage2-check-parallel-nonsendable-capture-stage2.txt"
$stage2NonSendableCaptureError = Join-Path $artifactsDir "linux-stage2-check-parallel-nonsendable-capture-stage2.err"
$stage2NonSendableCapture = Invoke-ProcessToFile "wsl.exe" @(
    "-d", $Distribution, "--", (Convert-ToWslPath $stage2Path), "linux",
    (Convert-ToWslPath $parallelNonSendableCaptureSource)
) $stage2NonSendableCaptureOutput $stage2NonSendableCaptureError
foreach ($candidate in @(
    @($stage1NonSendableCapture, $stage1NonSendableCaptureOutput, "stage1"),
    @($stage2NonSendableCapture, $stage2NonSendableCaptureOutput, "stage2")
)) {
    $candidate[0].WaitForExit()
    $candidate[0].Refresh()
    if ($candidate[0].ExitCode -eq 0) { throw "$($candidate[2]) accepted a transitive non-sendable parallel capture" }
    $diagnosticText = [System.IO.File]::ReadAllText($candidate[1])
    if ($diagnosticText -notmatch 'error\[E19\].*non-sendable binding captured by a parallel callback') {
        throw "$($candidate[2]) did not emit ownership diagnostic E19: '$diagnosticText'"
    }
    if ($diagnosticText -match '^target (datalayout|triple)') {
        throw "$($candidate[2]) began LLVM emission before rejecting parallel-capture diagnostic E19"
    }
}
$stage1ReferenceOutput = Join-Path $artifactsDir "linux-stage2-check-reference-temporary-stage1.txt"
$stage1ReferenceError = Join-Path $artifactsDir "linux-stage2-check-reference-temporary-stage1.err"
$stage1Reference = Invoke-ProcessToFile $stage1Path @("linux", $referenceTemporarySource) $stage1ReferenceOutput $stage1ReferenceError
$stage2ReferenceOutput = Join-Path $artifactsDir "linux-stage2-check-reference-temporary-stage2.txt"
$stage2ReferenceError = Join-Path $artifactsDir "linux-stage2-check-reference-temporary-stage2.err"
$stage2Reference = Invoke-ProcessToFile "wsl.exe" @(
    "-d", $Distribution, "--", (Convert-ToWslPath $stage2Path), "linux", (Convert-ToWslPath $referenceTemporarySource)
) $stage2ReferenceOutput $stage2ReferenceError
foreach ($candidate in @(
    @($stage1Reference, $stage1ReferenceOutput, "stage1"),
    @($stage2Reference, $stage2ReferenceOutput, "stage2")
)) {
    $candidate[0].WaitForExit()
    $candidate[0].Refresh()
    if ($candidate[0].ExitCode -eq 0) { throw "$($candidate[2]) accepted a temporary readonly-reference argument" }
    $diagnosticText = [System.IO.File]::ReadAllText($candidate[1])
    if ($diagnosticText -notmatch 'error\[E22\].*readonly reference argument requires stable immutable storage') {
        throw "$($candidate[2]) did not emit ownership diagnostic E22: '$diagnosticText'"
    }
    if ($diagnosticText -match '^target (datalayout|triple)') {
        throw "$($candidate[2]) began LLVM emission before rejecting readonly-reference diagnostic E22"
    }
}
foreach ($referenceConflict in @(
    @($referenceLivenessSource, "mutation"),
    @($referenceOwnerMoveSource, "move"),
    @($referenceLoopContinueSource, "loop-continue"),
    @($referenceStoredStructSource, "stored-struct"),
    @($referenceStoredEnumSource, "stored-enum"),
    @($referenceStoredArraySource, "stored-array")
)) {
    $stage1ReferenceOutput = Join-Path $artifactsDir "linux-stage2-check-reference-$($referenceConflict[1])-stage1.txt"
    $stage1ReferenceError = Join-Path $artifactsDir "linux-stage2-check-reference-$($referenceConflict[1])-stage1.err"
    $stage1Reference = Invoke-ProcessToFile $stage1Path @("linux", $referenceConflict[0]) $stage1ReferenceOutput $stage1ReferenceError
    $stage2ReferenceOutput = Join-Path $artifactsDir "linux-stage2-check-reference-$($referenceConflict[1])-stage2.txt"
    $stage2ReferenceError = Join-Path $artifactsDir "linux-stage2-check-reference-$($referenceConflict[1])-stage2.err"
    $stage2Reference = Invoke-ProcessToFile "wsl.exe" @(
        "-d", $Distribution, "--", (Convert-ToWslPath $stage2Path), "linux", (Convert-ToWslPath $referenceConflict[0])
    ) $stage2ReferenceOutput $stage2ReferenceError
    foreach ($candidate in @(
        @($stage1Reference, $stage1ReferenceOutput, "stage1"),
        @($stage2Reference, $stage2ReferenceOutput, "stage2")
    )) {
        $candidate[0].WaitForExit()
        $candidate[0].Refresh()
        if ($candidate[0].ExitCode -eq 0) { throw "$($candidate[2]) accepted owner $($referenceConflict[1]) with a live readonly reference" }
        $diagnosticText = [System.IO.File]::ReadAllText($candidate[1])
        if ($diagnosticText -notmatch 'error\[E23\].*owner mutation conflicts with a live readonly reference') {
            throw "$($candidate[2]) did not emit ownership diagnostic E23 for owner $($referenceConflict[1]): '$diagnosticText'"
        }
        if ($diagnosticText -match '^target (datalayout|triple)') {
            throw "$($candidate[2]) began LLVM emission before rejecting readonly-reference owner $($referenceConflict[1]) diagnostic E23"
        }
    }
}

foreach ($referenceEscape in @(
    @($referenceAggregateEscapeSource, "aggregate-escape"),
    @($referenceEnumEscapeSource, "enum-escape"),
    @($referenceArrayEscapeSource, "array-escape")
)) {
    $stage1AggregateEscapeOutput = Join-Path $artifactsDir "linux-stage2-check-reference-$($referenceEscape[1])-stage1.txt"
    $stage1AggregateEscapeError = Join-Path $artifactsDir "linux-stage2-check-reference-$($referenceEscape[1])-stage1.err"
    $stage1AggregateEscape = Invoke-ProcessToFile $stage1Path @("linux", $referenceEscape[0]) $stage1AggregateEscapeOutput $stage1AggregateEscapeError
    $stage2AggregateEscapeOutput = Join-Path $artifactsDir "linux-stage2-check-reference-$($referenceEscape[1])-stage2.txt"
    $stage2AggregateEscapeError = Join-Path $artifactsDir "linux-stage2-check-reference-$($referenceEscape[1])-stage2.err"
    $stage2AggregateEscape = Invoke-ProcessToFile "wsl.exe" @(
        "-d", $Distribution, "--", (Convert-ToWslPath $stage2Path), "linux", (Convert-ToWslPath $referenceEscape[0])
    ) $stage2AggregateEscapeOutput $stage2AggregateEscapeError
    foreach ($candidate in @(
        @($stage1AggregateEscape, $stage1AggregateEscapeOutput, $stage1AggregateEscapeError, "stage-1"),
        @($stage2AggregateEscape, $stage2AggregateEscapeOutput, $stage2AggregateEscapeError, "stage-2")
    )) {
        $candidate[0].WaitForExit()
        $candidate[0].Refresh()
        $diagnosticText = (Get-Content $candidate[1] -Raw) + (Get-Content $candidate[2] -Raw)
        if ($candidate[0].ExitCode -eq 0) { throw "$($candidate[3]) accepted returned $($referenceEscape[1]) containing a callee-owned readonly reference" }
        if ($diagnosticText -notmatch 'error\[E22\]') {
            throw "$($candidate[3]) did not emit ownership diagnostic E22 for $($referenceEscape[1]): '$diagnosticText'"
        }
        if ($diagnosticText -match 'target triple') {
            throw "$($candidate[3]) began LLVM emission before rejecting $($referenceEscape[1]) diagnostic E22"
        }
    }
}
Write-Host "[linux-stage2 6/6] PASS E17-E23 ownership violations block LLVM emission in stage-1 and stage-2."
