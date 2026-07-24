[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d{6}$')]
    [string]$Version = "0.2.260725",
    [string]$OutputRoot,
    [string]$WindowsStage3Path,
    [string]$LinuxStage3Path
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\Sollang.Compiler\Sollang.Compiler.csproj"
$packageVersion = $Version
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\release\$Version"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$stagingRoot = Join-Path $OutputRoot "staging"
if ([string]::IsNullOrWhiteSpace($WindowsStage3Path)) {
    $WindowsStage3Path = Join-Path $repoRoot "artifacts\example-tests\selfhost-stage3.exe"
}
if ([string]::IsNullOrWhiteSpace($LinuxStage3Path)) {
    $LinuxStage3Path = Join-Path $repoRoot "artifacts\example-tests\selfhost-stage3-linux"
}
$WindowsStage3Path = [System.IO.Path]::GetFullPath($WindowsStage3Path)
$LinuxStage3Path = [System.IO.Path]::GetFullPath($LinuxStage3Path)

function New-ReleasePackage {
    param(
        [Parameter(Mandatory)] [string]$Runtime,
        [Parameter(Mandatory)] [string]$PlatformName
    )

    $packageName = "sollang-$Version-$PlatformName"
    $packageRoot = Join-Path $stagingRoot $packageName
    Write-Host "[release $PlatformName 1/3] Publish self-contained compiler."
    dotnet publish $project -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:AssemblyName=sollang `
        -p:PackageVersion=$packageVersion `
        -p:InformationalVersion=$packageVersion `
        -o $packageRoot | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Runtime" }

    Copy-Item (Join-Path $repoRoot "stdlib") (Join-Path $packageRoot "stdlib") -Recurse
    Copy-Item (Join-Path $repoRoot "README.md") $packageRoot
    Copy-Item (Join-Path $repoRoot "LICENSE") $packageRoot
    Copy-Item (Join-Path $repoRoot "Sollang.slnx") $packageRoot
    $packageDocs = Join-Path $packageRoot "docs"
    New-Item -ItemType Directory -Path $packageDocs -Force | Out-Null
    Copy-Item (Join-Path $repoRoot "docs\STAGE3_COMPILER.md") $packageDocs
    $stage3Source = if ($Runtime -eq "win-x64") { $WindowsStage3Path } else { $LinuxStage3Path }
    $stage3Name = if ($Runtime -eq "win-x64") { "sollangc-stage3.exe" } else { "sollangc-stage3" }
    if (-not (Test-Path -LiteralPath $stage3Source -PathType Leaf)) {
        throw "verified Stage 3 compiler is missing: $stage3Source"
    }
    Copy-Item -LiteralPath $stage3Source -Destination (Join-Path $packageRoot $stage3Name)

    Write-Host "[release $PlatformName 2/3] Verify package contents."
    $executableName = if ($Runtime -eq "win-x64") { "sollang.exe" } else { "sollang" }
    $required = @(
        (Join-Path $packageRoot $executableName),
        (Join-Path $packageRoot "stdlib\sys\io.slg"),
        (Join-Path $packageRoot "README.md"),
        (Join-Path $packageRoot "LICENSE"),
        (Join-Path $packageRoot "Sollang.slnx"),
        (Join-Path $packageRoot "docs\STAGE3_COMPILER.md"),
        (Join-Path $packageRoot $stage3Name)
    )
    foreach ($path in $required) {
        if (-not (Test-Path -LiteralPath $path)) { throw "release package is missing $path" }
    }

    Write-Host "[release $PlatformName 3/3] Archive package."
    if ($Runtime -eq "win-x64") {
        $archive = Join-Path $OutputRoot "$packageName.zip"
        Compress-Archive -Path $packageRoot -DestinationPath $archive -CompressionLevel Optimal
    } else {
        $archive = Join-Path $OutputRoot "$packageName.tar.gz"
        tar -C $stagingRoot -czf $archive $packageName
        if ($LASTEXITCODE -ne 0) { throw "tar failed for $packageName" }
    }
    $archive
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$windowsArchive = New-ReleasePackage -Runtime "win-x64" -PlatformName "windows-x64"
$linuxArchive = New-ReleasePackage -Runtime "linux-x64" -PlatformName "linux-x64"
$checksumPath = Join-Path $OutputRoot "SHA256SUMS.txt"
@($windowsArchive, $linuxArchive) | ForEach-Object {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($_))"
} | Set-Content -LiteralPath $checksumPath -Encoding utf8NoBOM

Write-Host "[release complete] $windowsArchive"
Write-Host "[release complete] $linuxArchive"
Write-Host "[release complete] $checksumPath"
