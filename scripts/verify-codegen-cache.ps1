[CmdletBinding()]
param(
    [string]$Distribution = "Ubuntu"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$caseRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "codegen-cache-verification"))
$artifactsPrefix = $artifactsRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $caseRoot.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "verification root escaped the artifacts directory: $caseRoot"
}
if (Test-Path -LiteralPath $caseRoot) {
    Remove-Item -LiteralPath $caseRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $caseRoot "cache") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $caseRoot "build") | Out-Null

$compiler = Join-Path $repoRoot "src\Sollang.Compiler\bin\Release\net11.0\Sollang.Compiler.dll"
$llvm = Join-Path $repoRoot ".tools\llvm-22.1.8"
$mainSource = Join-Path $caseRoot "main.slg"
$providerSource = Join-Path $caseRoot "cache\provider.slg"
$consumerSource = Join-Path $caseRoot "cache\consumer.slg"
$outputPath = Join-Path $caseRoot "build\app.exe"

@'
import cache.consumer as consumer
import sys.file as file

rootValue value: Int -> Int {
    value
}

rootIdentity<T> value: T -> T {
    value
}

rootRepeat<N: Int> value: Int -> Int {
    [value; N] => values
    values -> fold 0 total, item {
        total + item
    }
}

cachedRead reader: file.File -> Result<Option<UInt16>, Text> uses File {
    reader -> readAt<UInt16>(0)
}

main {
    21 -> consumer.compute -> rootIdentity -> rootRepeat<1> -> rootValue => result
    "$result" -> println
}
'@ | Set-Content -LiteralPath $mainSource -Encoding utf8NoBOM

@'
namespace cache.consumer

import cache.provider as provider

public compute value: Int -> Int {
    localIdentity item: Int -> Int {
        item
    }

    value -> provider.scale -> genericIdentity -> localIdentity
}

genericIdentity<T> value: T -> T {
    value -> nestedIdentity
}

nestedIdentity<T> value: T -> T {
    value
}
'@ | Set-Content -LiteralPath $consumerSource -Encoding utf8NoBOM

function Write-Provider {
    param(
        [Parameter(Mandatory)] [int]$Factor,
        [Parameter(Mandatory)] [int]$InterfaceRevision,
        [int]$PrivateRevision = 0
    )

    $extra = switch ($InterfaceRevision) {
        0 { "" }
        1 { "`npublic identity value: Int -> Int {`n    value`n}`n" }
        2 { "`npublic decrement value: Int -> Int {`n    value - 1`n}`n" }
        default { throw "unsupported interface revision $InterfaceRevision" }
    }
    $private = switch ($PrivateRevision) {
        0 { "" }
        1 { "`nstruct HiddenState { value: Int }`n" }
        default { throw "unsupported private revision $PrivateRevision" }
    }
    @"
namespace cache.provider

$private

public scale value: Int -> Int {
    value * $Factor
}
$extra
"@ | Set-Content -LiteralPath $providerSource -Encoding utf8NoBOM
}

function Invoke-Build {
    param(
        [Parameter(Mandatory)] [string]$Target
    )

    $text = & dotnet $compiler build $mainSource `
        -o $outputPath `
        --target $Target `
        --llvm $llvm `
        --keep-temps 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "build failed for ${Target}:`n$text"
    }
    $match = [regex]::Match($text, '\[codegen-cache\] (?<status>.+?); reused (?<reused>\d+)/(?<total>\d+) units;')
    if (-not $match.Success) {
        throw "codegen cache status was not reported for ${Target}:`n$text"
    }
    $frontendMatch = [regex]::Match($text, '\[frontend-cache\] (?<status>.+?); (?:skipped|rebuilt)')
    if (-not $frontendMatch.Success) {
        throw "frontend cache status was not reported for ${Target}:`n$text"
    }
    $productMatch = [regex]::Match($text, '\[product-cache\] (?<status>.+?); (?:skipped|linked)')
    if (-not $productMatch.Success) {
        throw "product cache status was not reported for ${Target}:`n$text"
    }
    $semanticMatch = [regex]::Match($text, '\[semantic-cache\] (?<status>.+?);')
    if (-not $semanticMatch.Success) {
        throw "semantic cache status was not reported for ${Target}:`n$text"
    }
    $semanticCounts = [regex]::Match($text, '\[semantic-cache\].+?; reused (?<reused>\d+)/(?<reuseTotal>\d+) functions; main (?<main>reused|rebuilt); mapped (?<functions>\d+)/(?<functionTotal>\d+) functions, (?<calls>\d+)/(?<callTotal>\d+) call sites;')
    [pscustomobject]@{
        Text = $text.Trim()
        Status = $match.Groups['status'].Value
        FrontendStatus = $frontendMatch.Groups['status'].Value
        ProductStatus = $productMatch.Groups['status'].Value
        SemanticStatus = $semanticMatch.Groups['status'].Value
        ReusedSemanticFunctions = $(if ($semanticCounts.Success) { [int]$semanticCounts.Groups['reused'].Value } else { -1 })
        SemanticFunctionTotal = $(if ($semanticCounts.Success) { [int]$semanticCounts.Groups['reuseTotal'].Value } else { -1 })
        ReusedMainSemantics = $semanticCounts.Success -and $semanticCounts.Groups['main'].Value -eq 'reused'
        MappedFunctions = $(if ($semanticCounts.Success) { [int]$semanticCounts.Groups['functions'].Value } else { -1 })
        FunctionTotal = $(if ($semanticCounts.Success) { [int]$semanticCounts.Groups['functionTotal'].Value } else { -1 })
        MappedCalls = $(if ($semanticCounts.Success) { [int]$semanticCounts.Groups['calls'].Value } else { -1 })
        CallTotal = $(if ($semanticCounts.Success) { [int]$semanticCounts.Groups['callTotal'].Value } else { -1 })
        Reused = [int]$match.Groups['reused'].Value
        Total = [int]$match.Groups['total'].Value
        LlvmHash = (Get-FileHash -Algorithm SHA256 ([System.IO.Path]::ChangeExtension($outputPath, ".ll"))).Hash
    }
}

function Assert-SemanticStatus {
    param(
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)] [string]$ExpectedPrefix,
        [Parameter(Mandatory)] [string]$Description
    )

    if (-not $Result.SemanticStatus.StartsWith($ExpectedPrefix, [System.StringComparison]::Ordinal)) {
        throw "$Description expected semantic status '$ExpectedPrefix...', actual '$($Result.SemanticStatus)':`n$($Result.Text)"
    }
}

function Assert-ProductStatus {
    param(
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)] [string]$ExpectedPrefix,
        [Parameter(Mandatory)] [string]$Description
    )

    if (-not $Result.ProductStatus.StartsWith($ExpectedPrefix, [System.StringComparison]::Ordinal)) {
        throw "$Description expected product status '$ExpectedPrefix...', actual '$($Result.ProductStatus)':`n$($Result.Text)"
    }
}

function Assert-FrontendStatus {
    param(
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)] [string]$ExpectedPrefix,
        [Parameter(Mandatory)] [string]$Description
    )

    if (-not $Result.FrontendStatus.StartsWith($ExpectedPrefix, [System.StringComparison]::Ordinal)) {
        throw "$Description expected frontend status '$ExpectedPrefix...', actual '$($Result.FrontendStatus)':`n$($Result.Text)"
    }
}

function Invoke-Product {
    param(
        [Parameter(Mandatory)] [string]$Target
    )

    if ($Target -eq "windows-x64") {
        $value = & $outputPath
    } else {
        $wslPath = "/mnt/p/" + $outputPath.Substring(3).Replace('\', '/')
        $value = wsl.exe -d $Distribution -- $wslPath
    }
    if ($LASTEXITCODE -ne 0) {
        throw "generated product failed for $Target"
    }
    $value.Trim()
}

function Assert-Reused {
    param(
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)] [int]$Expected,
        [Parameter(Mandatory)] [string]$Description
    )

    if ($Result.Total -ne 5 -or $Result.Reused -ne $Expected) {
        throw "$Description expected $Expected/5 reused units, actual $($Result.Reused)/$($Result.Total):`n$($Result.Text)"
    }
}

function Assert-Product {
    param(
        [Parameter(Mandatory)] [string]$Target,
        [Parameter(Mandatory)] [string]$Expected
    )

    $actual = Invoke-Product $Target
    if ($actual -ne $Expected) {
        throw "$Target product expected '$Expected', actual '$actual'"
    }
}

function Corrupt-Cache {
    param(
        [Parameter(Mandatory)] [string]$Target
    )

    $cachePath = Join-Path $caseRoot "build\.sollang-cache\app.$Target.o0.cgu"
    $stream = [System.IO.File]::Open($cachePath, 'Open', 'ReadWrite', 'None')
    try {
        $stream.Position = $stream.Length - 1
        $value = $stream.ReadByte()
        $stream.Position = $stream.Length - 1
        $stream.WriteByte($value -bxor 0xff)
        $stream.Flush($true)
    } finally {
        $stream.Dispose()
    }
}

function Corrupt-FrontendCache {
    param(
        [Parameter(Mandatory)] [string]$Target
    )

    $cachePath = Join-Path $caseRoot "build\.sollang-cache\app.$Target.o0.sources"
    $stream = [System.IO.File]::Open($cachePath, 'Open', 'ReadWrite', 'None')
    try {
        $stream.Position = $stream.Length - 1
        $value = $stream.ReadByte()
        $stream.Position = $stream.Length - 1
        $stream.WriteByte($value -bxor 0xff)
        $stream.Flush($true)
    } finally {
        $stream.Dispose()
    }
}

function Corrupt-SemanticCache {
    param(
        [Parameter(Mandatory)] [string]$Target
    )

    $cachePath = Join-Path $caseRoot "build\.sollang-cache\app.$Target.o0.semantic"
    $stream = [System.IO.File]::Open($cachePath, 'Open', 'ReadWrite', 'None')
    try {
        $stream.Position = $stream.Length - 1
        $value = $stream.ReadByte()
        $stream.Position = $stream.Length - 1
        $stream.WriteByte($value -bxor 0xff)
        $stream.Flush($true)
    } finally {
        $stream.Dispose()
    }
}

function Corrupt-Product {
    param(
        [Parameter(Mandatory)] [string]$Target
    )

    $stream = [System.IO.File]::Open($outputPath, 'Open', 'ReadWrite', 'None')
    try {
        $stream.Position = $stream.Length - 1
        $value = $stream.ReadByte()
        $stream.Position = $stream.Length - 1
        $stream.WriteByte($value -bxor 0xff)
        $stream.Flush($true)
    } finally {
        $stream.Dispose()
    }
}

function Verify-Target {
    param(
        [Parameter(Mandatory)] [string]$Target,
        [Parameter(Mandatory)] [int]$InitialFactor,
        [Parameter(Mandatory)] [int]$BodyFactor,
        [Parameter(Mandatory)] [int]$InitialInterfaceRevision,
        [Parameter(Mandatory)] [int]$ChangedInterfaceRevision
    )

    Write-Host "[$Target 1/10] Cold build."
    Write-Provider $InitialFactor $InitialInterfaceRevision
    $cold = Invoke-Build $Target
    Assert-Reused $cold 0 "$Target cold build"
    Assert-FrontendStatus $cold "cold" "$Target cold build"
    Assert-ProductStatus $cold "rebuilt" "$Target cold build"
    Assert-SemanticStatus $cold "cold" "$Target cold build"
    Assert-Product $Target ([string](21 * $InitialFactor))

    Write-Host "[$Target 2/10] Exact warm build skips the frontend and linker."
    $warm = Invoke-Build $Target
    Assert-Reused $warm 5 "$Target warm build"
    Assert-FrontendStatus $warm "exact hit" "$Target warm build"
    Assert-ProductStatus $warm "exact hit" "$Target warm build"
    Assert-SemanticStatus $warm "exact via frontend" "$Target warm build"
    if ($warm.LlvmHash -ne $cold.LlvmHash) {
        throw "$Target clean and cached LLVM differed"
    }

    Write-Host "[$Target 3/10] Body-only dependency edit reuses unchanged semantic bodies."
    Write-Provider $BodyFactor $InitialInterfaceRevision
    $body = Invoke-Build $Target
    Assert-Reused $body 2 "$Target body-only build"
    Assert-FrontendStatus $body "miss: source changed:" "$Target body-only build"
    Assert-ProductStatus $body "rebuilt" "$Target body-only build"
    Assert-SemanticStatus $body "loaded" "$Target body-only build"
    if ($body.ReusedSemanticFunctions -le 0 -or $body.SemanticFunctionTotal -le 0) {
        throw "$Target body-only build did not reuse unchanged semantic function bodies"
    }
    if (-not $body.ReusedMainSemantics) {
        throw "$Target body-only build did not reuse main semantics with a generic call"
    }
    if ($body.CallTotal -lt 5 -or $body.MappedCalls -ne $body.CallTotal) {
        throw "$Target body-only build did not restore all stable generic call sites"
    }
    Write-Host "[$Target 3/10] Semantic reuse $($body.ReusedSemanticFunctions)/$($body.SemanticFunctionTotal) functions, main reused, generic calls $($body.MappedCalls)/$($body.CallTotal)."
    Assert-Product $Target ([string](21 * $BodyFactor))
    $bodyWarm = Invoke-Build $Target
    Assert-Reused $bodyWarm 5 "$Target body-only warm build"
    Assert-FrontendStatus $bodyWarm "exact hit" "$Target body-only warm build"
    Assert-ProductStatus $bodyWarm "exact hit" "$Target body-only warm build"
    Assert-SemanticStatus $bodyWarm "exact via frontend" "$Target body-only warm build"
    if ($bodyWarm.LlvmHash -ne $body.LlvmHash) {
        throw "$Target body-only clean and cached LLVM differed"
    }

    Write-Host "[$Target 4/10] Semantic corruption is rejected during a private declaration edit."
    Corrupt-SemanticCache $Target
    Write-Provider $BodyFactor $InitialInterfaceRevision 1
    $private = Invoke-Build $Target
    Assert-Reused $private 2 "$Target private-declaration build"
    Assert-FrontendStatus $private "miss: source changed:" "$Target private-declaration build"
    Assert-ProductStatus $private "rebuilt" "$Target private-declaration build"
    Assert-SemanticStatus $private "rejected:" "$Target private-declaration build"
    Assert-Product $Target ([string](21 * $BodyFactor))

    Write-Host "[$Target 5/10] Removing a private declaration reuses unchanged semantic bodies."
    Write-Provider $BodyFactor $InitialInterfaceRevision
    $privateRemoval = Invoke-Build $Target
    Assert-Reused $privateRemoval 2 "$Target private-removal build"
    Assert-FrontendStatus $privateRemoval "miss: source changed:" "$Target private-removal build"
    Assert-ProductStatus $privateRemoval "rebuilt" "$Target private-removal build"
    Assert-SemanticStatus $privateRemoval "loaded" "$Target private-removal build"
    if ($privateRemoval.ReusedSemanticFunctions -le 0 -or $privateRemoval.SemanticFunctionTotal -le 0) {
        throw "$Target private-removal build did not reuse semantic function bodies"
    }
    if (-not $privateRemoval.ReusedMainSemantics) {
        throw "$Target private-removal build did not reuse main semantics"
    }
    if ($privateRemoval.MappedFunctions -le 0 -or $privateRemoval.FunctionTotal -le 0) {
        throw "$Target private-removal build did not map stable semantic functions"
    }
    if ($privateRemoval.CallTotal -lt 5 -or $privateRemoval.MappedCalls -ne $privateRemoval.CallTotal) {
        throw "$Target private-removal build did not preserve generic call mappings"
    }

    Write-Host "[$Target 6/10] Public-interface edit invalidates transitive consumers."
    Write-Provider $BodyFactor $ChangedInterfaceRevision
    $interface = Invoke-Build $Target
    Assert-Reused $interface 0 "$Target interface-change build"
    Assert-FrontendStatus $interface "miss: source changed:" "$Target interface-change build"
    Assert-ProductStatus $interface "rebuilt" "$Target interface-change build"
    Assert-SemanticStatus $interface "loaded" "$Target interface-change build"
    if ($interface.ReusedSemanticFunctions -ne 0) {
        throw "$Target public-interface build unexpectedly reused semantic function bodies"
    }
    if ($interface.ReusedMainSemantics) {
        throw "$Target public-interface build unexpectedly reused main semantics"
    }
    Assert-Product $Target ([string](21 * $BodyFactor))

    Write-Host "[$Target 7/10] Frontend snapshot corruption falls back to validated codegen units."
    Corrupt-FrontendCache $Target
    $frontendCorrupt = Invoke-Build $Target
    Assert-Reused $frontendCorrupt 5 "$Target frontend-corruption build"
    Assert-FrontendStatus $frontendCorrupt "rejected:" "$Target frontend-corruption build"
    Assert-ProductStatus $frontendCorrupt "rebuilt" "$Target frontend-corruption build"
    Assert-SemanticStatus $frontendCorrupt "loaded" "$Target frontend-corruption build"

    Write-Host "[$Target 8/10] Codegen corruption is rejected and rebuilt."
    Corrupt-Cache $Target
    $corrupt = Invoke-Build $Target
    Assert-Reused $corrupt 0 "$Target corruption build"
    if (-not $corrupt.Status.StartsWith("rejected:", [System.StringComparison]::Ordinal)) {
        throw "$Target corrupt cache was not explicitly rejected: $($corrupt.Status)"
    }
    Assert-FrontendStatus $corrupt "rejected:" "$Target codegen-corruption build"
    Assert-ProductStatus $corrupt "rebuilt" "$Target codegen-corruption build"
    Assert-SemanticStatus $corrupt "loaded" "$Target codegen-corruption build"

    Write-Host "[$Target 9/10] Output corruption relinks without rebuilding the frontend."
    Corrupt-Product $Target
    $productCorrupt = Invoke-Build $Target
    Assert-Reused $productCorrupt 5 "$Target product-corruption build"
    Assert-FrontendStatus $productCorrupt "exact hit" "$Target product-corruption build"
    Assert-ProductStatus $productCorrupt "miss: output changed" "$Target product-corruption build"
    Assert-SemanticStatus $productCorrupt "exact via frontend" "$Target product-corruption build"

    Write-Host "[$Target 10/10] Rebuilt generation is exact-warm and byte-identical."
    $repaired = Invoke-Build $Target
    Assert-Reused $repaired 5 "$Target repaired warm build"
    Assert-FrontendStatus $repaired "exact hit" "$Target repaired warm build"
    Assert-ProductStatus $repaired "exact hit" "$Target repaired warm build"
    Assert-SemanticStatus $repaired "exact via frontend" "$Target repaired warm build"
    if ($repaired.LlvmHash -ne $corrupt.LlvmHash) {
        throw "$Target rebuilt and cached LLVM differed"
    }
    Write-Host "[$Target 10/10] PASS LLVM $($repaired.LlvmHash)"
}

Write-Host "[cache 1/3] Build the Release compiler once."
& dotnet build (Join-Path $repoRoot "Sollang.slnx") -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Release build failed"
}
Write-Host "[cache 1/3] PASS Release compiler."

Write-Host "[cache 2/3] Verify Windows codegen generations."
Verify-Target "windows-x64" 2 3 0 1
Write-Host "[cache 2/3] PASS Windows codegen generations."

Write-Host "[cache 3/3] Verify target isolation and Linux codegen generations."
Verify-Target "linux-x64" 3 4 1 2
Write-Host "[cache 3/3] PASS Linux target isolation and codegen generations."
