[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsMacOS) {
    Write-Host 'macOS application bundle tests skipped because the current platform is not macOS.'
    return
}

$bundleScript = Join-Path $PSScriptRoot 'New-MacOSApplicationBundle.ps1'
$iconSource = Join-Path $PSScriptRoot '../src/OpenKustoExplorer.Desktop/Assets/OpenKustoExplorer.png'
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

function Assert-PathExists {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Scenario
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Scenario expected '$Path' to exist."
    }
}

function Assert-PathMissing {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Scenario
    )

    if (Test-Path -LiteralPath $Path) {
        throw "$Scenario expected '$Path' not to exist."
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [Parameter(Mandatory)]
        [string] $Scenario
    )

    try {
        $null = & $Action
    }
    catch {
        return
    }

    throw "$Scenario did not throw."
}

function Set-TestFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Content
    )

    $directory = Split-Path -Parent $Path
    $null = New-Item -ItemType Directory -Path $directory -Force
    [IO.File]::WriteAllText($Path, $Content.Replace("`r`n", "`n"), $utf8)
}

function Get-PlistString {
    param(
        [Parameter(Mandatory)]
        [xml] $Document,

        [Parameter(Mandatory)]
        [string] $Key
    )

    $keyNode = $Document.SelectSingleNode("/plist/dict/key[text()='$Key']")
    if ($null -eq $keyNode) {
        throw "Info.plist does not contain '$Key'."
    }

    $valueNode = $keyNode.SelectSingleNode('following-sibling::*[1]')
    if ($null -eq $valueNode -or $valueNode.Name -ne 'string') {
        throw "Info.plist '$Key' is not a string."
    }

    return $valueNode.InnerText
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "OpenKustoExplorer-macos-bundle-$([Guid]::NewGuid().ToString('N'))"
$publishRoot = Join-Path $testRoot 'publish'
$binaryRoot = Join-Path $testRoot 'binaries'
$symbolRoot = Join-Path $testRoot 'symbols'

try {
    Set-TestFile -Path (Join-Path $publishRoot 'OpenKustoExplorer') -Content "#!/bin/sh`nexit 0`n"
    Set-TestFile -Path (Join-Path $publishRoot 'runtimes/osx/native/libExample.dylib') -Content 'native'
    Set-TestFile -Path (Join-Path $publishRoot 'OpenKustoExplorer.xml') -Content '<doc />'
    Set-TestFile -Path (Join-Path $publishRoot 'OpenKustoExplorer.pdb') -Content 'pdb'
    Set-TestFile -Path (Join-Path $publishRoot 'native.dbg') -Content 'dbg'
    Set-TestFile `
        -Path (Join-Path $publishRoot 'OpenKustoExplorer.dSYM/Contents/Resources/DWARF/OpenKustoExplorer') `
        -Content 'dwarf'
    Set-TestFile -Path (Join-Path $binaryRoot 'stale-binary.txt') -Content 'stale'
    Set-TestFile -Path (Join-Path $symbolRoot 'stale-symbol.txt') -Content 'stale'

    $result = & $bundleScript `
        -PublishRoot $publishRoot `
        -BinaryStagingRoot $binaryRoot `
        -SymbolStagingRoot $symbolRoot `
        -Version '1.2.3' `
        -IconSource $iconSource

    $applicationRoot = Join-Path $binaryRoot 'OpenKustoExplorer.app'
    $contentsRoot = Join-Path $applicationRoot 'Contents'
    $applicationPath = Join-Path $contentsRoot 'MacOS/OpenKustoExplorer'
    $plistPath = Join-Path $contentsRoot 'Info.plist'
    $applicationIcon = Join-Path $contentsRoot 'Resources/OpenKustoExplorer.icns'

    Assert-Equal $applicationRoot $result.ApplicationBundlePath 'Returned application path'
    Assert-Equal 2 $result.BinaryFileCount 'Binary file count'
    Assert-Equal 4 $result.SymbolFileCount 'Symbol file count'
    Assert-PathExists $applicationPath 'Application executable'
    Assert-PathExists `
        (Join-Path $contentsRoot 'MacOS/runtimes/osx/native/libExample.dylib') `
        'Nested runtime file'
    Assert-PathMissing (Join-Path $contentsRoot 'MacOS/OpenKustoExplorer.xml') 'XML separation'
    Assert-PathMissing (Join-Path $contentsRoot 'MacOS/OpenKustoExplorer.dSYM') 'dSYM separation'
    Assert-PathExists (Join-Path $symbolRoot 'OpenKustoExplorer.xml') 'XML symbol'
    Assert-PathExists (Join-Path $symbolRoot 'OpenKustoExplorer.pdb') 'PDB symbol'
    Assert-PathExists (Join-Path $symbolRoot 'native.dbg') 'DBG symbol'
    Assert-PathExists `
        (Join-Path $symbolRoot 'OpenKustoExplorer.dSYM/Contents/Resources/DWARF/OpenKustoExplorer') `
        'dSYM symbol'
    Assert-PathMissing (Join-Path $binaryRoot 'stale-binary.txt') 'Stale binary cleanup'
    Assert-PathMissing (Join-Path $symbolRoot 'stale-symbol.txt') 'Stale symbol cleanup'

    $applicationMode = [IO.File]::GetUnixFileMode($applicationPath)
    Assert-Equal `
        [IO.UnixFileMode]::UserExecute `
        ($applicationMode -band [IO.UnixFileMode]::UserExecute) `
        'Application executable permission'

    Assert-PathExists $applicationIcon 'Application icon'
    if ((Get-Item -LiteralPath $applicationIcon).Length -eq 0) {
        throw 'Application icon is empty.'
    }

    $plutilOutput = & plutil -lint $plistPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "plutil rejected Info.plist: $($plutilOutput -join "`n")"
    }

    [xml] $plist = Get-Content -LiteralPath $plistPath -Raw
    Assert-Equal 'OpenKustoExplorer' (Get-PlistString $plist 'CFBundleExecutable') 'Bundle executable'
    Assert-Equal 'Kusto Explorer' (Get-PlistString $plist 'CFBundleName') 'Bundle name'
    Assert-Equal 'Open Kusto Explorer' (Get-PlistString $plist 'CFBundleDisplayName') 'Bundle display name'
    Assert-Equal 'io.github.i-am-shodan.OpenKustoExplorer' `
        (Get-PlistString $plist 'CFBundleIdentifier') `
        'Bundle identifier'
    Assert-Equal '1.2.3' (Get-PlistString $plist 'CFBundleShortVersionString') 'Display version'
    Assert-Equal '1.2.3' (Get-PlistString $plist 'CFBundleVersion') 'Bundle version'
    Assert-Equal '14.0' (Get-PlistString $plist 'LSMinimumSystemVersion') 'Minimum macOS version'
    Assert-Equal 'true' `
        $plist.SelectSingleNode(
            "/plist/dict/key[text()='NSHighResolutionCapable']/following-sibling::*[1]").Name `
        'High-resolution capability'

    Assert-Throws -Scenario 'Invalid semantic version' -Action {
        & $bundleScript `
            -PublishRoot $publishRoot `
            -BinaryStagingRoot (Join-Path $testRoot 'invalid-version-binaries') `
            -SymbolStagingRoot (Join-Path $testRoot 'invalid-version-symbols') `
            -Version '1.2' `
            -IconSource $iconSource
    }

    $missingExecutableRoot = Join-Path $testRoot 'missing-executable'
    Set-TestFile -Path (Join-Path $missingExecutableRoot 'OpenKustoExplorer.xml') -Content '<doc />'
    Assert-Throws -Scenario 'Missing executable' -Action {
        & $bundleScript `
            -PublishRoot $missingExecutableRoot `
            -BinaryStagingRoot (Join-Path $testRoot 'missing-executable-binaries') `
            -SymbolStagingRoot (Join-Path $testRoot 'missing-executable-symbols') `
            -Version '1.2.3' `
            -IconSource $iconSource
    }

    $missingSymbolsRoot = Join-Path $testRoot 'missing-symbols'
    Set-TestFile -Path (Join-Path $missingSymbolsRoot 'OpenKustoExplorer') -Content 'application'
    Assert-Throws -Scenario 'Missing symbols' -Action {
        & $bundleScript `
            -PublishRoot $missingSymbolsRoot `
            -BinaryStagingRoot (Join-Path $testRoot 'missing-symbol-binaries') `
            -SymbolStagingRoot (Join-Path $testRoot 'missing-symbol-symbols') `
            -Version '1.2.3' `
            -IconSource $iconSource
    }

    Write-Host 'macOS application bundle tests passed.'
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}