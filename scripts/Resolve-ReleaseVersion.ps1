[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InputVersion,

    [string]$GitHubOutput
)

$ErrorActionPreference = 'Stop'
$candidate = $InputVersion.Trim()
if ($candidate.StartsWith('v', [StringComparison]::OrdinalIgnoreCase)) {
    $candidate = $candidate.Substring(1)
}

$semanticVersionPattern = '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<prerelease>(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?$'
$match = [regex]::Match($candidate, $semanticVersionPattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)
if (-not $match.Success) {
    throw "'$InputVersion' is not an accepted release version. Use vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-prerelease."
}

$fileVersion = '{0}.{1}.{2}.0' -f $match.Groups['major'].Value, $match.Groups['minor'].Value, $match.Groups['patch'].Value
$tag = "v$candidate"

if ($GitHubOutput) {
    @(
        "version=$candidate"
        "file_version=$fileVersion"
        "tag=$tag"
        "prerelease=$($match.Groups['prerelease'].Success.ToString().ToLowerInvariant())"
    ) | Add-Content -LiteralPath $GitHubOutput -Encoding utf8
}

[pscustomobject]@{
    Version = $candidate
    FileVersion = $fileVersion
    Tag = $tag
    IsPrerelease = $match.Groups['prerelease'].Success
}
