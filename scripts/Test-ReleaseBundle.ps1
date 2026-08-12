[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Directory,

    [Parameter(Mandatory)]
    [string]$Version,

    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'
$resolvedVersion = & (Join-Path $PSScriptRoot 'Resolve-ReleaseVersion.ps1') -InputVersion $Version
$root = [IO.Path]::GetFullPath($Directory)
$required = @('FolderGlimpse.exe', 'FolderGlimpse-win-x64.zip', 'FolderGlimpse.spdx.json', 'SHA256SUMS.txt')
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)) {
        throw "Release bundle is missing $name."
    }
}
$actualFiles = @(Get-ChildItem -LiteralPath $root -File | Select-Object -ExpandProperty Name)
$unexpectedFiles = @($actualFiles | Where-Object { $_ -notin $required })
if ($unexpectedFiles.Count -gt 0) {
    throw "Release bundle contains unexpected files: $($unexpectedFiles -join ', ')."
}
if ($actualFiles.Count -ne $required.Count) {
    throw "Release bundle must contain exactly $($required.Count) files; found $($actualFiles.Count)."
}

$exe = Get-Item -LiteralPath (Join-Path $root 'FolderGlimpse.exe')
if ($exe.VersionInfo.ProductName -ne 'FolderGlimpse' -or
    -not $exe.VersionInfo.ProductVersion.StartsWith($resolvedVersion.Version, [StringComparison]::OrdinalIgnoreCase) -or
    $exe.VersionInfo.FileVersion -ne $resolvedVersion.FileVersion) {
    throw 'Release executable version or product metadata is inconsistent.'
}

$signature = Get-AuthenticodeSignature -LiteralPath $exe.FullName
if ($RequireSignature -and ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
    -not $signature.SignerCertificate -or -not $signature.TimeStamperCertificate)) {
    throw 'Release executable is not validly Authenticode-signed and timestamped.'
}

$checksumLines = Get-Content -LiteralPath (Join-Path $root 'SHA256SUMS.txt')
if ($checksumLines.Count -ne 3) { throw 'SHA256SUMS.txt must contain exactly three release artifact hashes.' }
foreach ($line in $checksumLines) {
    if ($line -notmatch '^([0-9a-f]{64})  ([^\\/]+)$') { throw "Invalid checksum line: $line" }
    $target = Join-Path $root $Matches[2]
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Checksum target '$($Matches[2])' is missing." }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash.ToLowerInvariant()
    if ($actual -ne $Matches[1]) { throw "Checksum mismatch for '$($Matches[2])'." }
}

$sbom = Get-Content -LiteralPath (Join-Path $root 'FolderGlimpse.spdx.json') -Raw | ConvertFrom-Json
if (-not $sbom.spdxVersion -or -not $sbom.documentNamespace -or -not $sbom.packages -or -not $sbom.files) {
    throw 'The SPDX SBOM is missing required document metadata.'
}
$sbomExecutable = @($sbom.files | Where-Object { [IO.Path]::GetFileName($_.fileName) -eq 'FolderGlimpse.exe' })
if ($sbomExecutable.Count -ne 1) {
    throw "The SPDX SBOM must describe exactly one FolderGlimpse.exe; found $($sbomExecutable.Count)."
}
$sbomSha256 = @($sbomExecutable[0].checksums | Where-Object algorithm -eq 'SHA256')
if ($sbomSha256.Count -ne 1 -or $sbomSha256[0].checksumValue -ne (Get-FileHash -Algorithm SHA256 -LiteralPath $exe.FullName).Hash) {
    throw 'The SPDX SBOM executable hash does not match the release executable.'
}

$extractRoot = Join-Path ([IO.Path]::GetTempPath()) ("FolderGlimpseReleaseCheck-" + [guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -LiteralPath (Join-Path $root 'FolderGlimpse-win-x64.zip') -DestinationPath $extractRoot
    $zippedExe = Join-Path $extractRoot 'FolderGlimpse.exe'
    if (-not (Test-Path -LiteralPath $zippedExe -PathType Leaf)) { throw 'Release ZIP does not contain FolderGlimpse.exe at its root.' }
    $directHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exe.FullName).Hash
    $zippedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zippedExe).Hash
    if ($directHash -ne $zippedHash) { throw 'The ZIP contains a different executable than the standalone release asset.' }
}
finally {
    if (Test-Path -LiteralPath $extractRoot) { Remove-Item -LiteralPath $extractRoot -Recurse -Force }
}

[pscustomobject]@{
    Version = $resolvedVersion.Version
    SignatureStatus = $signature.Status
    Signer = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { 'None' }
    Timestamped = $null -ne $signature.TimeStamperCertificate
    Files = $required.Count
}
