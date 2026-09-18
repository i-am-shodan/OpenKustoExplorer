[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PublishRoot,

    [Parameter(Mandatory)]
    [string] $BinaryStagingRoot,

    [Parameter(Mandatory)]
    [string] $SymbolStagingRoot,

    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string] $Version,

    [string] $IconSource = (Join-Path $PSScriptRoot '../src/OpenKustoExplorer.Desktop/Assets/OpenKustoExplorer.png')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MacOSApplicationBundle.Common.ps1')

function Test-PathContains {
    param(
        [Parameter(Mandatory)]
        [string] $Parent,

        [Parameter(Mandatory)]
        [string] $Child
    )

    if ($Parent.Equals($Child, [StringComparison]::Ordinal)) {
        return $true
    }

    $prefix = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $Child.StartsWith($prefix, [StringComparison]::Ordinal)
}

function Assert-SeparatePaths {
    param(
        [Parameter(Mandatory)]
        [string] $FirstName,

        [Parameter(Mandatory)]
        [string] $FirstPath,

        [Parameter(Mandatory)]
        [string] $SecondName,

        [Parameter(Mandatory)]
        [string] $SecondPath
    )

    if ((Test-PathContains -Parent $FirstPath -Child $SecondPath) -or
        (Test-PathContains -Parent $SecondPath -Child $FirstPath)) {
        throw "$FirstName '$FirstPath' and $SecondName '$SecondPath' must not overlap."
    }
}

function New-ApplicationIcon {
    param(
        [Parameter(Mandatory)]
        [string] $SourcePath,

        [Parameter(Mandatory)]
        [string] $DestinationPath
    )

    $sips = Get-Command sips -CommandType Application -ErrorAction Stop
    $iconutil = Get-Command iconutil -CommandType Application -ErrorAction Stop
    $iconsetPath = Join-Path ([IO.Path]::GetTempPath()) "OpenKustoExplorer-$([Guid]::NewGuid().ToString('N')).iconset"
    $null = New-Item -ItemType Directory -Path $iconsetPath

    $sizes = @(
        @{ Name = 'icon_16x16.png'; Size = 16 }
        @{ Name = 'icon_16x16@2x.png'; Size = 32 }
        @{ Name = 'icon_32x32.png'; Size = 32 }
        @{ Name = 'icon_32x32@2x.png'; Size = 64 }
        @{ Name = 'icon_128x128.png'; Size = 128 }
        @{ Name = 'icon_128x128@2x.png'; Size = 256 }
        @{ Name = 'icon_256x256.png'; Size = 256 }
        @{ Name = 'icon_256x256@2x.png'; Size = 512 }
        @{ Name = 'icon_512x512.png'; Size = 512 }
        @{ Name = 'icon_512x512@2x.png'; Size = 1024 }
    )

    try {
        foreach ($size in $sizes) {
            $output = & $sips.Source `
                --resampleHeightWidth $size.Size $size.Size `
                $SourcePath `
                --out (Join-Path $iconsetPath $size.Name) 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "sips failed to create $($size.Name): $($output -join "`n")"
            }
        }

        $output = & $iconutil.Source --convert icns $iconsetPath --output $DestinationPath 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "iconutil failed to create the application icon: $($output -join "`n")"
        }
    }
    finally {
        Remove-Item -LiteralPath $iconsetPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not $IsMacOS) {
    throw 'macOS application bundles must be staged on macOS.'
}

$publishPath = (Resolve-Path -LiteralPath $PublishRoot).Path
$iconPath = (Resolve-Path -LiteralPath $IconSource).Path
$binaryPath = [IO.Path]::GetFullPath($BinaryStagingRoot)
$symbolPath = [IO.Path]::GetFullPath($SymbolStagingRoot)

Assert-SeparatePaths 'PublishRoot' $publishPath 'BinaryStagingRoot' $binaryPath
Assert-SeparatePaths 'PublishRoot' $publishPath 'SymbolStagingRoot' $symbolPath
Assert-SeparatePaths 'BinaryStagingRoot' $binaryPath 'SymbolStagingRoot' $symbolPath

$publishedFiles = @(Get-ChildItem -LiteralPath $publishPath -Recurse -File)
$applicationSource = Join-Path $publishPath 'OpenKustoExplorer'
if (-not (Test-Path -LiteralPath $applicationSource -PathType Leaf) -or
    (Get-Item -LiteralPath $applicationSource).Length -eq 0) {
    throw "PublishRoot '$publishPath' does not contain a non-empty OpenKustoExplorer executable."
}

$symbolFiles = @(
    $publishedFiles | Where-Object {
        Test-MacOSSymbolFile -PublishRoot $publishPath -File $_
    }
)
$binaryFiles = @(
    $publishedFiles | Where-Object {
        $isSymbol = Test-MacOSSymbolFile -PublishRoot $publishPath -File $_
        -not $isSymbol
    }
)

if ($binaryFiles.Count -eq 0 -or $symbolFiles.Count -eq 0) {
    throw "macOS release staging requires binary and symbol files; found $($binaryFiles.Count) binaries and $($symbolFiles.Count) symbols."
}

Remove-Item -LiteralPath $binaryPath -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $symbolPath -Recurse -Force -ErrorAction SilentlyContinue
$macOSPath = Join-Path $binaryPath 'OpenKustoExplorer.app/Contents/MacOS'
$resourcesPath = Join-Path $binaryPath 'OpenKustoExplorer.app/Contents/Resources'
$null = New-Item -ItemType Directory -Path $macOSPath, $resourcesPath, $symbolPath

foreach ($file in $binaryFiles) {
    $relativePath = [IO.Path]::GetRelativePath($publishPath, $file.FullName)
    $targetPath = Join-Path $macOSPath $relativePath
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force
    Copy-Item -LiteralPath $file.FullName -Destination $targetPath
}

foreach ($file in $symbolFiles) {
    $relativePath = [IO.Path]::GetRelativePath($publishPath, $file.FullName)
    $targetPath = Join-Path $symbolPath $relativePath
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force
    Copy-Item -LiteralPath $file.FullName -Destination $targetPath
}

$applicationPath = Join-Path $macOSPath 'OpenKustoExplorer'
$executableMode = [IO.UnixFileMode]::UserRead `
    -bor [IO.UnixFileMode]::UserWrite `
    -bor [IO.UnixFileMode]::UserExecute `
    -bor [IO.UnixFileMode]::GroupRead `
    -bor [IO.UnixFileMode]::GroupExecute `
    -bor [IO.UnixFileMode]::OtherRead `
    -bor [IO.UnixFileMode]::OtherExecute
[IO.File]::SetUnixFileMode($applicationPath, $executableMode)

$contentsPath = Split-Path -Parent $macOSPath
Write-InfoPlist -Path (Join-Path $contentsPath 'Info.plist') -ApplicationVersion $Version
New-ApplicationIcon `
    -SourcePath $iconPath `
    -DestinationPath (Join-Path $resourcesPath 'OpenKustoExplorer.icns')

[pscustomobject]@{
    ApplicationBundlePath = Join-Path $binaryPath 'OpenKustoExplorer.app'
    BinaryFileCount = $binaryFiles.Count
    SymbolFileCount = $symbolFiles.Count
}