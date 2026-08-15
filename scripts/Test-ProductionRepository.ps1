[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RepositoryRoot,

    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$resolvedVersion = & (Join-Path $PSScriptRoot 'Resolve-ReleaseVersion.ps1') -InputVersion $Version
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$requiredRepositoryFiles = @(
    'LICENSE', 'README.md', 'CHANGELOG.md', 'CONTRIBUTING.md',
    'SECURITY.md', 'PRIVACY.md', 'CODE_OF_CONDUCT.md'
)

foreach ($relativePath in $requiredRepositoryFiles) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Production release prerequisite '$relativePath' is missing."
    }
}

$readme = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw
if ($readme -match 'No official FolderGlimpse binary has been published yet' -or
    $readme -match 'FolderGlimpse is not code-signed yet' -or
    $readme -match '(?is)(?:unsigned.{0,80}(?:public\s+)?beta|(?:public\s+)?beta.{0,80}unsigned)') {
    throw 'README release/signing notices must be updated with verified production facts before the first release.'
}

$changelog = Get-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') -Raw
$headingPattern = '(?m)^## \[?' + [regex]::Escape($resolvedVersion.Version) + '\]?(?:\s|$)'
if ($changelog -notmatch $headingPattern) {
    throw "CHANGELOG.md does not contain a heading for production release $($resolvedVersion.Version)."
}

[pscustomobject]@{
    Version = $resolvedVersion.Version
    RequiredFiles = $requiredRepositoryFiles.Count
    ReadmeStatus = 'Production-ready'
    ChangelogStatus = 'Version present'
}
