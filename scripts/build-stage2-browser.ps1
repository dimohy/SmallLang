[CmdletBinding()]
param(
    [string]$Stage2Compiler = "artifacts\example-tests\selfhost-stage2.exe",
    [switch]$ReuseCompilerArtifact
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$stage2Path = (Resolve-Path (Join-Path $repoRoot $Stage2Compiler)).Path
$manifestPath = Join-Path $repoRoot "selfhost\browser_driver.sources.txt"
$llvmRoot = Join-Path $repoRoot ".tools\llvm-22.1.8"
$llvmAs = Join-Path $llvmRoot "bin\llvm-as.exe"
$clang = Join-Path $llvmRoot "bin\clang.exe"
$wasmLd = Join-Path $llvmRoot "bin\wasm-ld.exe"
$compilerLlvm = Join-Path $repoRoot "artifacts\sollangc-browser-stage2.ll"
$compilerError = Join-Path $repoRoot "artifacts\sollangc-browser-stage2.err"
$compilerBitcode = Join-Path $repoRoot "artifacts\sollangc-browser-stage2.bc"
$compilerObject = Join-Path $repoRoot "artifacts\sollangc-browser-stage2.o"
$compilerArtifact = Join-Path $repoRoot "artifacts\sollangc-browser.wasm"
$publicCompiler = Join-Path $repoRoot "public\sollangc-stage2-0.2.260725.wasm"

$browserSources = Get-Content -LiteralPath $manifestPath |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { (Resolve-Path (Join-Path $repoRoot $_.Trim())).Path }

if ($ReuseCompilerArtifact) {
    if (-not (Test-Path -LiteralPath $compilerArtifact)) {
        throw "verified browser compiler artifact is missing: $compilerArtifact"
    }
    Write-Host "[browser 1/4] Reuse the explicitly selected browser compiler artifact."
    Write-Host "[browser 2/4] Reused artifact: $compilerArtifact"
} else {
    Write-Host "[browser 1/4] Emit the browser compiler with the verified Stage2 compiler."
    $process = Start-Process `
        -FilePath $stage2Path `
        -ArgumentList (@("wasm") + $browserSources) `
        -RedirectStandardOutput $compilerLlvm `
        -RedirectStandardError $compilerError `
        -PassThru `
        -WindowStyle Hidden `
        -Wait
    if ($process.ExitCode -ne 0) {
        throw "Stage2 browser emission failed.`n$([System.IO.File]::ReadAllText($compilerError))"
    }

    Write-Host "[browser 2/4] Verify and link the Stage2-emitted LLVM."
    & $llvmAs $compilerLlvm -o $compilerBitcode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $clang `
        -target wasm32-unknown-unknown-wasm `
        -O2 `
        -g `
        -fno-addrsig `
        -c $compilerLlvm `
        -o $compilerObject
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $wasmLd `
        --no-entry `
        --export=sollang_start `
        --export=sollang_alloc `
        --export-memory `
        --allow-undefined `
        --gc-sections `
        $compilerObject `
        -o $compilerArtifact
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "[browser 3/4] Execute browser compiler regressions."
foreach ($case in @(
    @("examples\576-linq-multiplication-table.slg", "browser-stage2-table.ll", "576-linq-multiplication-table.stdout.txt"),
    @("examples\580-deferred-text-evaluation.slg", "browser-stage2-deferred-text.ll", "580-deferred-text-evaluation.stdout.txt"),
    @("examples\582-billion-sensor-alerts.slg", "browser-stage2-sensor.ll", "582-billion-sensor-alerts.stdout.txt"),
    @("examples\583-stream-state-take-skip.slg", "browser-stage2-state.ll", "583-stream-state-take-skip.stdout.txt"),
    @("examples\585-stream-transaction-risk-scan.slg", "browser-stage2-risk.ll", "585-stream-transaction-risk-scan.stdout.txt")
)) {
    $programLlvm = Join-Path $repoRoot "artifacts\$($case[1])"
    $programBitcode = [System.IO.Path]::ChangeExtension($programLlvm, ".bc")
    $programObject = [System.IO.Path]::ChangeExtension($programLlvm, ".o")
    $programWasm = [System.IO.Path]::ChangeExtension($programLlvm, ".wasm")
    & node (Join-Path $PSScriptRoot "verify-browser-stage2.mjs") `
        $compilerArtifact `
        (Join-Path $repoRoot $case[0]) `
        $programLlvm
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $llvmAs $programLlvm -o $programBitcode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $clang -target wasm32-unknown-unknown-wasm -O2 -fno-addrsig -c $programLlvm -o $programObject
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $wasmLd `
        --no-entry `
        --export=sollang_start `
        --export-memory `
        --allow-undefined `
        --gc-sections `
        $programObject `
        -o $programWasm
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & node (Join-Path $PSScriptRoot "verify-browser-program.mjs") `
        $programWasm `
        (Join-Path $repoRoot "examples\expected\$($case[2])")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& node (Join-Path $PSScriptRoot "verify-browser-stage2.mjs") `
    $compilerArtifact `
    (Join-Path $repoRoot "examples\diagnostics\browser-interpolation-boundary.slg") `
    (Join-Path $repoRoot "artifacts\browser-stage2-interpolation-diagnostic.txt") `
    "unknown interpolation binding 'dimohy는'"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& node (Join-Path $PSScriptRoot "verify-browser-stage2.mjs") `
    $compilerArtifact `
    (Join-Path $repoRoot "tests\Sollang.ExampleTests\Fixtures\browser-stage2-unknown-flow-target.slg") `
    (Join-Path $repoRoot "artifacts\browser-stage2-unknown-flow-target-diagnostic.txt") `
    "unresolved call target 'println2'"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& node (Join-Path $PSScriptRoot "verify-browser-stage2.mjs") `
    $compilerArtifact `
    (Join-Path $repoRoot "examples\588-mouse-event-stream.slg") `
    (Join-Path $repoRoot "artifacts\browser-stage2-mouse-event-diagnostic.txt") `
    "mouse event streams are unavailable on wasm32-browser; browser events require host-driven callback lowering"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "[browser 4/4] Publish only the verified compiler artifact."
Copy-Item -LiteralPath $compilerArtifact -Destination $publicCompiler -Force
$artifactHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $compilerArtifact).Hash
$publicHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $publicCompiler).Hash
if ($artifactHash -ne $publicHash) {
    throw "published browser compiler hash differs from the verified artifact"
}

Write-Host "[browser stage2] PASS $publicHash"
