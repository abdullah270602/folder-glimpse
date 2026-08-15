[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishedExecutable,

    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$source = (Get-Item -LiteralPath $PublishedExecutable).FullName
$resolved = & (Join-Path $PSScriptRoot 'Resolve-ReleaseVersion.ps1') -InputVersion $Version
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("FolderGlimpseReleaseTooling-" + [guid]::NewGuid().ToString('N'))
$bundle = Join-Path $temporaryRoot 'bundle'
$repeatBundle = Join-Path $temporaryRoot 'repeat-bundle'
$sbom = Join-Path $temporaryRoot 'test.spdx.json'
$releaseRepository = Join-Path $temporaryRoot 'release-repository'

function Assert-Throws([scriptblock]$Action, [string]$Description) {
    try {
        & $Action
    }
    catch {
        Write-Verbose "$Description rejected as expected: $($_.Exception.Message)"
        return
    }
    throw "$Description was unexpectedly accepted."
}

try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    [IO.Directory]::CreateDirectory($releaseRepository) | Out-Null
    $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash
    [ordered]@{
        spdxVersion = 'SPDX-2.2'
        dataLicense = 'CC0-1.0'
        SPDXID = 'SPDXRef-DOCUMENT'
        name = "FolderGlimpse $($resolved.Version) release-tooling test"
        documentNamespace = "https://github.com/abdullah270602/folder-glimpse/test/$([guid]::NewGuid())"
        creationInfo = [ordered]@{ created = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'); creators = @('Tool: FolderGlimpse release-tooling test') }
        packages = @([ordered]@{ name = 'FolderGlimpse'; SPDXID = 'SPDXRef-Package-FolderGlimpse'; versionInfo = $resolved.Version; downloadLocation = 'NOASSERTION'; filesAnalyzed = $false })
        files = @([ordered]@{ fileName = './FolderGlimpse.exe'; SPDXID = 'SPDXRef-File-FolderGlimpse'; checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $sourceHash }) })
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $sbom -Encoding utf8

    foreach ($name in @('LICENSE', 'CONTRIBUTING.md', 'SECURITY.md', 'PRIVACY.md', 'CODE_OF_CONDUCT.md', 'SUPPORT.md')) {
        Set-Content -LiteralPath (Join-Path $releaseRepository $name) -Value 'release fixture' -Encoding utf8
    }
    Set-Content -LiteralPath (Join-Path $releaseRepository 'README.md') -Value 'Verified signed FolderGlimpse release.' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $releaseRepository 'CHANGELOG.md') `
        -Value "## [$($resolved.Version)]`n`n## [0.1.0-beta.1]" -Encoding utf8
    & (Join-Path $PSScriptRoot 'Test-ProductionRepository.ps1') `
        -RepositoryRoot $releaseRepository -Version $resolved.Version | Out-Null

    Set-Content -LiteralPath (Join-Path $releaseRepository 'README.md') `
        -Value 'No official FolderGlimpse binary has been published yet.' -Encoding utf8
    Assert-Throws {
        & (Join-Path $PSScriptRoot 'Test-ProductionRepository.ps1') `
            -RepositoryRoot $releaseRepository -Version $resolved.Version | Out-Null
    } 'Pre-release README production gate'
    Set-Content -LiteralPath (Join-Path $releaseRepository 'README.md') -Value 'Verified signed FolderGlimpse release.' -Encoding utf8
    Remove-Item -LiteralPath (Join-Path $releaseRepository 'LICENSE') -Force
    Assert-Throws {
        & (Join-Path $PSScriptRoot 'Test-ProductionRepository.ps1') `
            -RepositoryRoot $releaseRepository -Version $resolved.Version | Out-Null
    } 'Missing-license production gate'
    Set-Content -LiteralPath (Join-Path $releaseRepository 'LICENSE') -Value 'release fixture' -Encoding utf8

    Set-Content -LiteralPath (Join-Path $releaseRepository 'README.md') `
        -Value 'FolderGlimpse unsigned beta downloads are published only through https://github.com/abdullah270602/folder-glimpse/releases.' -Encoding utf8
    & (Join-Path $PSScriptRoot 'Test-UnsignedPrereleaseRepository.ps1') `
        -RepositoryRoot $releaseRepository -Version '0.1.0-beta.1' | Out-Null
    Assert-Throws {
        & (Join-Path $PSScriptRoot 'Test-UnsignedPrereleaseRepository.ps1') `
            -RepositoryRoot $releaseRepository -Version '1.0.0' | Out-Null
    } 'Unsigned stable release gate'
    Set-Content -LiteralPath (Join-Path $releaseRepository 'README.md') `
        -Value 'FolderGlimpse unsigned beta: users should disable SmartScreen and use https://github.com/abdullah270602/folder-glimpse/releases.' -Encoding utf8
    Assert-Throws {
        & (Join-Path $PSScriptRoot 'Test-UnsignedPrereleaseRepository.ps1') `
            -RepositoryRoot $releaseRepository -Version '0.1.0-beta.1' | Out-Null
    } 'Unsafe SmartScreen bypass guidance'
    Set-Content -LiteralPath (Join-Path $releaseRepository 'README.md') -Value 'Verified signed FolderGlimpse release.' -Encoding utf8

    & (Join-Path $PSScriptRoot 'New-ReleaseBundle.ps1') `
        -SourceExecutable $source -SbomPath $sbom -OutputDirectory $bundle -Version $resolved.Version | Out-Null
    & (Join-Path $PSScriptRoot 'Test-ReleaseBundle.ps1') `
        -Directory $bundle -Version $resolved.Version | Out-Null

    & (Join-Path $PSScriptRoot 'New-ReleaseBundle.ps1') `
        -SourceExecutable $source -SbomPath $sbom -OutputDirectory $repeatBundle -Version $resolved.Version | Out-Null
    foreach ($name in @('FolderGlimpse.exe', 'FolderGlimpse-win-x64.zip', 'FolderGlimpse.spdx.json', 'SHA256SUMS.txt')) {
        $firstHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $bundle $name)).Hash
        $repeatHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repeatBundle $name)).Hash
        if ($firstHash -ne $repeatHash) { throw "Release output '$name' is not deterministic for identical inputs." }
    }

    Assert-Throws {
        & (Join-Path $PSScriptRoot 'New-ReleaseBundle.ps1') `
            -SourceExecutable $source -SbomPath $sbom -OutputDirectory $bundle -Version $resolved.Version | Out-Null
    } 'Nonempty release output directory'

    $unexpectedFile = Join-Path $bundle 'unexpected.txt'
    Set-Content -LiteralPath $unexpectedFile -Value 'not a release asset' -Encoding utf8
    Assert-Throws { & (Join-Path $PSScriptRoot 'Test-ReleaseBundle.ps1') -Directory $bundle -Version $resolved.Version | Out-Null } 'Unexpected release file'
    Remove-Item -LiteralPath $unexpectedFile -Force

    Assert-Throws { & (Join-Path $PSScriptRoot 'Resolve-ReleaseVersion.ps1') -InputVersion 'v01.0.0' | Out-Null } 'Leading-zero SemVer'
    Assert-Throws { & (Join-Path $PSScriptRoot 'Resolve-ReleaseVersion.ps1') -InputVersion '1.0' | Out-Null } 'Incomplete SemVer'
    Assert-Throws { & (Join-Path $PSScriptRoot 'Test-ReleaseBundle.ps1') -Directory $bundle -Version $resolved.Version -RequireSignature | Out-Null } 'Unsigned production bundle'

    $bundleSbom = Join-Path $bundle 'FolderGlimpse.spdx.json'
    $checksum = Join-Path $bundle 'SHA256SUMS.txt'
    $originalSbom = Get-Content -LiteralPath $bundleSbom -Raw
    $tamperedSbom = $originalSbom | ConvertFrom-Json
    $tamperedSbom.files[0].checksums[0].checksumValue = ('0' * 64)
    [IO.File]::WriteAllText($bundleSbom, ($tamperedSbom | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    $lines = Get-Content -LiteralPath $checksum
    $sbomLine = [Array]::FindIndex($lines, [Predicate[string]]{ param($line) $line.EndsWith('  FolderGlimpse.spdx.json', [StringComparison]::Ordinal) })
    if ($sbomLine -lt 0) { throw 'Test fixture checksum list does not contain the SBOM.' }
    $lines[$sbomLine] = '{0}  FolderGlimpse.spdx.json' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $bundleSbom).Hash.ToLowerInvariant()
    [IO.File]::WriteAllLines($checksum, $lines, [Text.UTF8Encoding]::new($false))
    Assert-Throws { & (Join-Path $PSScriptRoot 'Test-ReleaseBundle.ps1') -Directory $bundle -Version $resolved.Version | Out-Null } 'SBOM executable-hash mismatch'

    [IO.File]::WriteAllText($bundleSbom, $originalSbom, [Text.UTF8Encoding]::new($false))
    $lines[$sbomLine] = '{0}  FolderGlimpse.spdx.json' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $bundleSbom).Hash.ToLowerInvariant()
    $replacement = if ($lines[0][0] -eq '0') { '1' } else { '0' }
    $lines[0] = ($replacement + $lines[0].Substring(1))
    [IO.File]::WriteAllLines($checksum, $lines, [Text.UTF8Encoding]::new($false))
    Assert-Throws { & (Join-Path $PSScriptRoot 'Test-ReleaseBundle.ps1') -Directory $bundle -Version $resolved.Version | Out-Null } 'Tampered checksum'

    Write-Output "PASS: release tooling positive and fail-closed tests for $($resolved.Version)."
}
finally {
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $systemTemporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemporaryRoot.StartsWith($systemTemporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedTemporaryRoot).StartsWith('FolderGlimpseReleaseTooling-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
