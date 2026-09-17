[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $Commit = 'HEAD',
    [switch] $VersionFloorOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-StrictVersion {
    param(
        [Parameter(Mandatory)]
        [string] $Value,
        [Parameter(Mandatory)]
        [string] $Source
    )

    $match = [regex]::Match(
        $Value,
        '^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)$')
    if (-not $match.Success) {
        throw "$Source must be a strict MAJOR.MINOR.PATCH version without leading zeroes; found '$Value'."
    }

    try {
        $major = [int]::Parse($match.Groups['major'].Value, [Globalization.CultureInfo]::InvariantCulture)
        $minor = [int]::Parse($match.Groups['minor'].Value, [Globalization.CultureInfo]::InvariantCulture)
        $patch = [int]::Parse($match.Groups['patch'].Value, [Globalization.CultureInfo]::InvariantCulture)
        $numeric = [Version]::new($major, $minor, $patch)
    }
    catch [OverflowException] {
        throw "$Source contains a version component larger than a 32-bit signed integer."
    }

    return [pscustomobject]@{
        Major = $major
        Minor = $minor
        Patch = $patch
        Numeric = $numeric
        Text = "$major.$minor.$patch"
    }
}

function Get-VersionPrefixFromText {
    param(
        [Parameter(Mandatory)]
        [string] $Text,
        [Parameter(Mandatory)]
        [string] $Source
    )

    try {
        [xml] $document = $Text
    }
    catch {
        throw "$Source is not valid XML: $($_.Exception.Message)"
    }

    $nodes = @($document.SelectNodes('/Project/PropertyGroup/VersionPrefix'))
    if ($nodes.Count -ne 1) {
        throw "$Source must contain exactly one Project/PropertyGroup/VersionPrefix element."
    }

    return (Get-StrictVersion -Value $nodes[0].InnerText.Trim() -Source "$Source VersionPrefix")
}

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,
        [switch] $AllowFailure
    )

    $output = & git -C $script:ResolvedRepositoryRoot @Arguments 2>$null
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        if ($AllowFailure) {
            return $null
        }

        throw "git $($Arguments -join ' ') failed with exit code $exitCode."
    }

    return ($output -join "`n").Trim()
}

function Get-VersionPrefixAtCommit {
    param(
        [Parameter(Mandatory)]
        [string] $CommitId
    )

    $content = Invoke-Git -Arguments @('show', "${CommitId}:Directory.Build.props") -AllowFailure
    if ($null -eq $content) {
        return $null
    }

    return (Get-VersionPrefixFromText -Text $content -Source "Directory.Build.props at commit $CommitId")
}

function Get-FirstParent {
    param(
        [Parameter(Mandatory)]
        [string] $CommitId
    )

    $line = Invoke-Git -Arguments @('rev-list', '--parents', '-n', '1', $CommitId)
    $parts = @($line -split '\s+')
    return $parts.Count -gt 1 ? $parts[1] : $null
}

function Test-FirstParentAncestor {
    param(
        [Parameter(Mandatory)]
        [string] $Ancestor,
        [Parameter(Mandatory)]
        [string] $Descendant
    )

    if ($Ancestor -eq $Descendant) {
        return $false
    }

    $historyText = Invoke-Git -Arguments @('rev-list', '--first-parent', $Descendant)
    $history = @($historyText -split "`n")
    return $history -contains $Ancestor
}

$script:ResolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$propsPath = Join-Path $script:ResolvedRepositoryRoot 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
    throw "Directory.Build.props was not found beneath '$script:ResolvedRepositoryRoot'."
}

$floorContent = [IO.File]::ReadAllText($propsPath)
$floor = Get-VersionPrefixFromText -Text $floorContent -Source $propsPath
if ($VersionFloorOnly) {
    $floor.Text
    exit 0
}

$head = Invoke-Git -Arguments @('rev-parse', '--verify', "${Commit}^{commit}")
$headFloor = Get-VersionPrefixAtCommit -CommitId $head
if ($null -eq $headFloor -or $headFloor.Text -ne $floor.Text) {
    throw "Commit $head must contain VersionPrefix $($floor.Text). Commit the version floor before resolving a release."
}

$baseline = $head
while ($true) {
    $parent = Get-FirstParent -CommitId $baseline
    if ($null -eq $parent) {
        break
    }

    $parentFloor = Get-VersionPrefixAtCommit -CommitId $parent
    if ($null -eq $parentFloor -or $parentFloor.Text -ne $floor.Text) {
        break
    }

    $baseline = $parent
}

$distanceText = Invoke-Git -Arguments @('rev-list', '--count', '--first-parent', "$baseline..$head")
$distance = [long]::Parse($distanceText, [Globalization.CultureInfo]::InvariantCulture)
$resolvedPatch = [long] $floor.Patch + $distance
if ($resolvedPatch -gt [int]::MaxValue) {
    throw 'The resolved patch component exceeds a 32-bit signed integer.'
}

$candidate = Get-StrictVersion -Value "$($floor.Major).$($floor.Minor).$resolvedPatch" -Source 'Resolved release version'
$candidateTag = "v$($candidate.Text)"
$candidateTagCommit = Invoke-Git -Arguments @('rev-parse', '--verify', "refs/tags/${candidateTag}^{commit}") -AllowFailure
if ($null -ne $candidateTagCommit -and $candidateTagCommit -ne $head) {
    throw "Tag $candidateTag already points to $candidateTagCommit instead of release commit $head."
}

$latest = $null
$tagList = Invoke-Git -Arguments @('tag', '--list', 'v*')
foreach ($tag in @($tagList -split "`n")) {
    if ([string]::IsNullOrWhiteSpace($tag)) {
        continue
    }

    $tagMatch = [regex]::Match($tag, '^v(?<version>(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*))$')
    if (-not $tagMatch.Success) {
        continue
    }

    $tagVersion = Get-StrictVersion -Value $tagMatch.Groups['version'].Value -Source "Tag $tag"
    if ($null -eq $latest -or $tagVersion.Numeric -gt $latest.Version.Numeric) {
        $latest = [pscustomobject]@{
            Commit = Invoke-Git -Arguments @('rev-parse', '--verify', "refs/tags/${tag}^{commit}")
            Name = $tag
            Version = $tagVersion
        }
    }
}

if ($null -ne $latest -and $latest.Version.Numeric -gt $candidate.Numeric) {
    $isLegitimateOlderRun = Test-FirstParentAncestor -Ancestor $head -Descendant $latest.Commit
    if (-not $isLegitimateOlderRun) {
        throw "Resolved version $($candidate.Text) is older than $($latest.Name), which is not a later first-parent commit. Bump VersionPrefix before releasing."
    }
}

$candidate.Text