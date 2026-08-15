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

if (-not $resolvedVersion.IsPrerelease -or
    $resolvedVersion.Version -notmatch '^\d+\.\d+\.\d+-(?:beta|rc)\.[1-9]\d*$') {
    throw "Unsigned public releases are restricted to beta.N or rc.N versions; received '$Version'."
}

$requiredRepositoryFiles = @(
    'LICENSE', 'README.md', 'CHANGELOG.md', 'CONTRIBUTING.md',
    'SECURITY.md', 'PRIVACY.md', 'CODE_OF_CONDUCT.md', 'SUPPORT.md'
)

foreach ($relativePath in $requiredRepositoryFiles) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Unsigned prerelease prerequisite '$relativePath' is missing."
    }
}

$readme = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw
if ($readme -notmatch '(?is)(?:unsigned.{0,80}(?:public\s+)?beta|(?:public\s+)?beta.{0,80}unsigned)' -or
    $readme -notmatch 'github\.com/abdullah270602/folder-glimpse/releases') {
    throw 'README must clearly identify the public beta as unsigned and link to canonical GitHub Releases.'
}
if ($readme -match '(?i)(?:please|should|must|need\s+to|to)\s+disable\s+(?:Windows\s+)?SmartScreen|(?:please|should|must|need\s+to|to)\s+install.+trusted\s+root|(?:please|should|must|need\s+to|to)\s+ignore.+security\s+warning') {
    throw 'README contains unsafe security-warning bypass guidance.'
}

$changelog = Get-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') -Raw
$headingPattern = '(?m)^## \[?' + [regex]::Escape($resolvedVersion.Version) + '\]?(?:\s|$)'
if ($changelog -notmatch $headingPattern) {
    throw "CHANGELOG.md does not contain a heading for unsigned prerelease $($resolvedVersion.Version)."
}

[pscustomobject]@{
    Version = $resolvedVersion.Version
    RequiredFiles = $requiredRepositoryFiles.Count
    ReadmeStatus = 'Unsigned beta disclosed'
    ChangelogStatus = 'Version present'
}
