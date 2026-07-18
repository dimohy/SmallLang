param(
    [string]$Distribution = "Ubuntu",
    [ValidateRange(1, 4)]
    [int]$Jobs = 4
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "SmallLang.slnx"
$runnerProject = Join-Path $repoRoot "tests\SmallLang.ExampleTests\SmallLang.ExampleTests.csproj"

Write-Host "[linux-full 1/2] Build the Release solution."
& dotnet build $solution -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Release solution build failed"
}
Write-Host "[linux-full 1/2] PASS Release solution"

Write-Host "[linux-full 2/2] Run all examples and diagnostics as Linux x64."
& dotnet run --project $runnerProject -c Release --no-build -- `
    --target linux-x64 `
    --wsl-distribution $Distribution `
    --suite full `
    --skip-bootstrap `
    --jobs $Jobs
if ($LASTEXITCODE -ne 0) {
    throw "Linux x64 full suite failed"
}
Write-Host "[linux-full 2/2] PASS Linux x64 full suite"
