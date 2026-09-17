[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolverPath = Join-Path $PSScriptRoot 'Resolve-ReleaseVersion.ps1'
$majorBumpPath = Join-Path $PSScriptRoot 'Bump-MajorVersion.ps1'
$utf8 = [Text.UTF8Encoding]::new($false, $true)

function Assert-Equal {
    param(
        [Parameter(Mandatory)]
        [object] $Expected,
        [Parameter(Mandatory)]
        [object] $Actual,
        [Parameter(Mandatory)]
        [string] $Scenario
    )

    if ($Expected -ne $Actual) {
        throw "$Scenario expected '$Expected' but found '$Actual'."
    }
}

function Invoke-TestGit {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & git -C $RepositoryRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Test git $($Arguments -join ' ') failed: $($output -join "`n")"
    }

    return ($output -join "`n").Trim()
}

function Set-TestVersionFloor {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,
        [Parameter(Mandatory)]
        [string] $Version
    )

    $content = @"
<Project>
  <PropertyGroup>
    <VersionPrefix>$Version</VersionPrefix>
  </PropertyGroup>
</Project>
"@
    [IO.File]::WriteAllText(
        (Join-Path $RepositoryRoot 'Directory.Build.props'),
        $content.Replace("`r`n", "`n"),
        $utf8)
}

function Add-TestCommit {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,
        [Parameter(Mandatory)]
        [string] $Message
    )

    $null = Invoke-TestGit -RepositoryRoot $RepositoryRoot -Arguments @('add', '--all')
    $null = Invoke-TestGit -RepositoryRoot $RepositoryRoot -Arguments @('commit', '--quiet', '--message', $Message)
    return (Invoke-TestGit -RepositoryRoot $RepositoryRoot -Arguments @('rev-parse', 'HEAD'))
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "OpenKustoExplorer-release-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $testRoot

try {
    $null = Invoke-TestGit -RepositoryRoot $testRoot -Arguments @('init', '--quiet')
    $null = Invoke-TestGit -RepositoryRoot $testRoot -Arguments @('config', 'user.name', 'Release Test')
    $null = Invoke-TestGit -RepositoryRoot $testRoot -Arguments @('config', 'user.email', 'release-test@example.com')

    $preFloorContent = "<Project>`n  <PropertyGroup />`n</Project>`n"
    [IO.File]::WriteAllText((Join-Path $testRoot 'Directory.Build.props'), $preFloorContent, $utf8)
    $null = Add-TestCommit -RepositoryRoot $testRoot -Message 'Before automatic release floor'

    Set-TestVersionFloor -RepositoryRoot $testRoot -Version '1.0.0'
    $oldFloorCommit = Add-TestCommit -RepositoryRoot $testRoot -Message 'Old release floor'
    Assert-Equal '1.0.0' (& $resolverPath -RepositoryRoot $testRoot) 'First release floor'
    $null = Invoke-TestGit -RepositoryRoot $testRoot -Arguments @('tag', 'v1.0.0', $oldFloorCommit)

    Set-TestVersionFloor -RepositoryRoot $testRoot -Version '1.0.1'
    $floorCommit = Add-TestCommit -RepositoryRoot $testRoot -Message 'Automatic release floor'
    Assert-Equal '1.0.1' (& $resolverPath -RepositoryRoot $testRoot) 'Bumped release floor'

    [IO.File]::WriteAllText((Join-Path $testRoot 'change.txt'), 'first', $utf8)
    $firstPatchCommit = Add-TestCommit -RepositoryRoot $testRoot -Message 'First patch'
    Assert-Equal '1.0.2' (& $resolverPath -RepositoryRoot $testRoot) 'First patch'
    $null = Invoke-TestGit -RepositoryRoot $testRoot -Arguments @('tag', 'v1.0.2', $firstPatchCommit)
    Assert-Equal '1.0.2' (& $resolverPath -RepositoryRoot $testRoot) 'Same-commit rerun'

    [IO.File]::WriteAllText((Join-Path $testRoot 'change.txt'), 'second', $utf8)
    $secondPatchCommit = Add-TestCommit -RepositoryRoot $testRoot -Message 'Second patch'
    Assert-Equal '1.0.3' (& $resolverPath -RepositoryRoot $testRoot) 'Second patch'
    $null = Invoke-TestGit -RepositoryRoot $testRoot -Arguments @('tag', 'v1.0.3', $secondPatchCommit)
    Assert-Equal '1.0.2' (
        & $resolverPath -RepositoryRoot $testRoot -Commit $firstPatchCommit
    ) 'Older overlapping workflow'

    $beforeWhatIf = [IO.File]::ReadAllBytes((Join-Path $testRoot 'Directory.Build.props'))
    $majorPreview = @(& $majorBumpPath -RepositoryRoot $testRoot -WhatIf)[-1]
    $afterWhatIf = [IO.File]::ReadAllBytes((Join-Path $testRoot 'Directory.Build.props'))
    Assert-Equal '2.0.0' $majorPreview 'Major bump preview'
    if (-not [Linq.Enumerable]::SequenceEqual([byte[]] $beforeWhatIf, [byte[]] $afterWhatIf)) {
        throw 'Major bump preview modified Directory.Build.props.'
    }

    $beforeMajor = [IO.File]::ReadAllText((Join-Path $testRoot 'Directory.Build.props'))
    Assert-Equal '2.0.0' (& $majorBumpPath -RepositoryRoot $testRoot) 'Major bump'
    $afterMajor = [IO.File]::ReadAllText((Join-Path $testRoot 'Directory.Build.props'))
    Assert-Equal (
        $beforeMajor.Replace('<VersionPrefix>1.0.1</VersionPrefix>', '<VersionPrefix>2.0.0</VersionPrefix>')
    ) $afterMajor 'Major bump byte scope'
    $majorCommit = Add-TestCommit -RepositoryRoot $testRoot -Message 'Major release floor'
    Assert-Equal '2.0.0' (& $resolverPath -RepositoryRoot $testRoot) 'Major release'

    $null = Invoke-TestGit -RepositoryRoot $testRoot -Arguments @('tag', 'v2.0.1', $floorCommit)
    $conflictThrown = $false
    try {
        $null = & $resolverPath -RepositoryRoot $testRoot -Commit $majorCommit
    }
    catch {
        $conflictThrown = $true
    }

    if (-not $conflictThrown) {
        throw 'A higher tag outside the later first-parent history was not rejected.'
    }

    Write-Host 'Release version script tests passed.'
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}