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
$processSource = Join-Path $repoRoot "stdlib\sys\process.slg"
$compilerRuntimeSources = @(
    $processSource
    (Join-Path $repoRoot "selfhost\runtime\path.slg")
    (Join-Path $repoRoot "selfhost\runtime\file.slg")
    (Join-Path $repoRoot "stdlib\sys\directory.slg")
    (Join-Path $repoRoot "stdlib\sys\directory\kind.slg")
)
$singleSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-single-smoke.slg"
$multiLibrarySource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-library-smoke.slg"
$multiMainSource = Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\selfhost-stage2-main-smoke.slg"
$expectedStage2Bytes = 9544344L

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
    $inputs = @($stage1Path, $manifestPath) + $compilerRuntimeSources
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

Write-Host "[linux-stage2 1/5] Bootstrap or reuse the native stage-1 compiler."
& dotnet run --project $runnerProject -c Release -- `
    --exact 365-selfhost-llvm-stage2-single-smoke `
    --exact 366-selfhost-llvm-stage2-multi-file-smoke `
    --jobs 2
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not (Test-Path $stage1Path)) { throw "stage-1 compiler was not produced: $stage1Path" }
Write-Host "[linux-stage2 1/5] PASS native stage 1."

Write-Host "[linux-stage2 2/5] Build or reuse the complete Linux stage-2 compiler."
if (Test-Stage2IsCurrent) {
    Write-Host "[linux-stage2 2/5] REUSE current Linux stage 2."
} else {
    $sourcePaths = Get-Content $manifestPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { (Resolve-Path (Join-Path $repoRoot $_.Trim())).Path }
    $sourcePaths += $compilerRuntimeSources | ForEach-Object { (Resolve-Path $_).Path }
    $stage2ErrorPath = Join-Path $artifactsDir "selfhost-stage2-linux.err.log"
    $stage2Process = Invoke-ProcessToFile `
        -FilePath $stage1Path `
        -ArgumentList (@("linux", "--jobs", "4") + $sourcePaths) `
        -OutputPath $stage2LlvmPath `
        -ErrorPath $stage2ErrorPath
    while (-not $stage2Process.HasExited) {
        Start-Sleep -Seconds 2
        $bytes = if (Test-Path $stage2LlvmPath) { (Get-Item $stage2LlvmPath).Length } else { 0L }
        $percent = [Math]::Min(99.9, 100.0 * $bytes / $expectedStage2Bytes)
        Write-Host ("[linux-stage2 2/5] LLVM {0:N0} bytes ({1:N1}% heuristic)" -f $bytes, $percent)
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
Write-Host "[linux-stage2 2/5] PASS $((Get-Item $stage2LlvmPath).Length) LLVM bytes."

Write-Host "[linux-stage2 3/5] Compare stage-1 and stage-2 single-file LLVM."
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
Write-Host "[linux-stage2 3/5] PASS $singleStage2Hash"

Write-Host "[linux-stage2 4/5] Compare stage-1 and stage-2 imported multi-file LLVM."
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
Write-Host "[linux-stage2 4/5] PASS $multiStage2Hash"

Write-Host "[linux-stage2 5/5] Assemble, link, and execute both Linux stage-2 products."
Build-And-ExecuteLinuxLlvm $singleStage2Llvm "linux-stage2-check-single" "stage2-single-ok"
Build-And-ExecuteLinuxLlvm $multiStage2Llvm "linux-stage2-check-multi" "stage2-multi-ok"
Write-Host "[linux-stage2 5/5] PASS complete Linux stage-2 verification."
