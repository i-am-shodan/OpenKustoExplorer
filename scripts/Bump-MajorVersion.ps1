[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$propsPath = Join-Path $resolvedRepositoryRoot 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
    throw "Directory.Build.props was not found beneath '$resolvedRepositoryRoot'."
}

$utf8 = [Text.UTF8Encoding]::new($false, $true)
$content = $utf8.GetString([IO.File]::ReadAllBytes($propsPath))
$pattern = '<VersionPrefix>(?<version>[^<]+)</VersionPrefix>'
$matches = [regex]::Matches($content, $pattern)
if ($matches.Count -ne 1) {
    throw 'Directory.Build.props must contain exactly one VersionPrefix element.'
}

$versionText = $matches[0].Groups['version'].Value.Trim()
$versionMatch = [regex]::Match(
    $versionText,
    '^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)$')
if (-not $versionMatch.Success) {
    throw "VersionPrefix must be a strict MAJOR.MINOR.PATCH version; found '$versionText'."
}

try {
    $major = [int]::Parse(
        $versionMatch.Groups['major'].Value,
        [Globalization.CultureInfo]::InvariantCulture)
}
catch [OverflowException] {
    throw 'The major version cannot be incremented beyond a 32-bit signed integer.'
}

if ($major -eq [int]::MaxValue) {
    throw 'The major version cannot be incremented beyond a 32-bit signed integer.'
}

$nextMajor = $major + 1

$nextVersion = "$nextMajor.0.0"
$replacement = "<VersionPrefix>$nextVersion</VersionPrefix>"
$match = $matches[0]
$updatedContent = $content.Substring(0, $match.Index) + $replacement +
    $content.Substring($match.Index + $match.Length)

if ($PSCmdlet.ShouldProcess($propsPath, "Set VersionPrefix to $nextVersion")) {
    [IO.File]::WriteAllText($propsPath, $updatedContent, $utf8)
}

$nextVersion