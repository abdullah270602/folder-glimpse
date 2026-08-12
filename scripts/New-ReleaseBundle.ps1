[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceExecutable,

    [Parameter(Mandatory)]
    [string]$SbomPath,

    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [Parameter(Mandatory)]
    [string]$Version,

    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'
$resolvedVersion = & (Join-Path $PSScriptRoot 'Resolve-ReleaseVersion.ps1') -InputVersion $Version
$source = Get-Item -LiteralPath $SourceExecutable
$sbom = Get-Item -LiteralPath $SbomPath
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Directory]::Exists($output) -and [IO.Directory]::EnumerateFileSystemEntries($output).GetEnumerator().MoveNext()) {
    throw "Release output directory '$output' must be empty to prevent stale files entering a bundle."
}
[IO.Directory]::CreateDirectory($output) | Out-Null

$versionInfo = $source.VersionInfo
if ($versionInfo.ProductName -ne 'FolderGlimpse') {
    throw "Unexpected product metadata '$($versionInfo.ProductName)' in $($source.FullName)."
}
if (-not $versionInfo.ProductVersion.StartsWith($resolvedVersion.Version, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Executable product version '$($versionInfo.ProductVersion)' does not match '$($resolvedVersion.Version)'."
}
if ($versionInfo.FileVersion -ne $resolvedVersion.FileVersion) {
    throw "Executable file version '$($versionInfo.FileVersion)' does not match '$($resolvedVersion.FileVersion)'."
}

$signature = Get-AuthenticodeSignature -LiteralPath $source.FullName
if ($RequireSignature) {
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Production release requires a valid Authenticode signature; status is '$($signature.Status)'."
    }
    if (-not $signature.SignerCertificate -or -not $signature.TimeStamperCertificate) {
        throw 'Production release requires both a signer certificate and an RFC 3161 timestamp.'
    }
}

$exePath = Join-Path $output 'FolderGlimpse.exe'
$sbomOutput = Join-Path $output 'FolderGlimpse.spdx.json'
$zipPath = Join-Path $output 'FolderGlimpse-win-x64.zip'
$checksumsPath = Join-Path $output 'SHA256SUMS.txt'

Copy-Item -LiteralPath $source.FullName -Destination $exePath -Force
Copy-Item -LiteralPath $sbom.FullName -Destination $sbomOutput -Force

Add-Type -AssemblyName System.IO.Compression
$zipStream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        $entry = $archive.CreateEntry('FolderGlimpse.exe', [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $entryStream = $entry.Open()
        try {
            $sourceStream = [IO.File]::OpenRead($exePath)
            try { $sourceStream.CopyTo($entryStream) }
            finally { $sourceStream.Dispose() }
        }
        finally { $entryStream.Dispose() }
    }
    finally { $archive.Dispose() }
}
finally { $zipStream.Dispose() }

$hashTargets = @($exePath, $zipPath, $sbomOutput)
$checksumLines = foreach ($target in $hashTargets) {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $target
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), [IO.Path]::GetFileName($target)
}
[IO.File]::WriteAllLines($checksumsPath, $checksumLines, [Text.UTF8Encoding]::new($false))

& (Join-Path $PSScriptRoot 'Test-ReleaseBundle.ps1') -Directory $output -Version $resolvedVersion.Version -RequireSignature:$RequireSignature
