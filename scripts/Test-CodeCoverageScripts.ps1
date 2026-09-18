[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$coverageScript = Join-Path $PSScriptRoot 'Get-CodeCoverage.ps1'
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

function Escape-XmlText {
    param(
        [Parameter(Mandatory)]
        [string] $Text
    )

    return [Security.SecurityElement]::Escape($Text)
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "OpenKustoExplorer-coverage-$([Guid]::NewGuid().ToString('N'))"
$repositoryRoot = Join-Path $testRoot 'repository'
$projectRoot = Join-Path $repositoryRoot 'src/OpenKustoExplorer.Sample'
$outsideRoot = Join-Path $repositoryRoot 'outside'
$generatedRoot = Join-Path $testRoot 'generated'
$reportRoot = Join-Path $testRoot 'reports'
$outputRoot = Join-Path $testRoot 'output'

try {
    Set-TestFile -Path (Join-Path $projectRoot 'First.cs') -Content "first`nsecond`nthird`n"
    Set-TestFile -Path (Join-Path $projectRoot 'Nested/Second.cs') -Content "only`n"
    Set-TestFile -Path (Join-Path $projectRoot 'View.axaml') -Content "<View />`n"
    Set-TestFile -Path (Join-Path $outsideRoot 'External.cs') -Content "outside`n"
    Set-TestFile -Path (Join-Path $generatedRoot 'Generated.g.cs') -Content "generated`n"

    $firstSource = Escape-XmlText $projectRoot
    $generatedSource = Escape-XmlText $generatedRoot
    $firstReport = @"
<?xml version="1.0" encoding="utf-8"?>
<coverage>
  <sources>
    <source>$firstSource</source>
    <source>$generatedSource</source>
  </sources>
  <packages>
    <package name="OpenKustoExplorer.Sample">
      <classes>
        <class name="First" filename="First.cs">
          <lines>
            <line number="1" hits="0" />
            <line number="2" hits="1" />
            <line number="3" hits="0" />
          </lines>
        </class>
        <class name="Generated" filename="Generated.g.cs">
          <lines><line number="1" hits="1" /></lines>
        </class>
        <class name="View" filename="View.axaml">
          <lines><line number="1" hits="0" /></lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
"@
    Set-TestFile -Path (Join-Path $reportRoot 'first/coverage.cobertura.xml') -Content $firstReport

    $absoluteFirstFile = Escape-XmlText (
        (Join-Path $projectRoot 'First.cs').Replace('\', '/'))
    $absoluteSecondFile = Escape-XmlText (
        (Join-Path $projectRoot 'Nested/Second.cs').Replace('/', '\'))
    $absoluteExternalFile = Escape-XmlText (
        (Join-Path $outsideRoot 'External.cs').Replace('\', '/'))
    $secondReport = @"
<?xml version="1.0" encoding="utf-8"?>
<coverage>
  <sources />
  <packages>
    <package name="OpenKustoExplorer.Sample">
      <classes>
        <class name="First" filename="$absoluteFirstFile">
          <lines>
            <line number="1" hits="2" />
            <line number="2" hits="0" />
          </lines>
        </class>
        <class name="First.Nested" filename="$absoluteFirstFile">
          <lines><line number="3" hits="0" /></lines>
        </class>
        <class name="Second" filename="$absoluteSecondFile">
          <lines><line number="1" hits="3" /></lines>
        </class>
        <class name="External" filename="$absoluteExternalFile">
          <lines><line number="1" hits="1" /></lines>
        </class>
        <class name="NoLines" filename="$absoluteFirstFile" />
      </classes>
    </package>
  </packages>
</coverage>
"@
    Set-TestFile -Path (Join-Path $reportRoot 'second/coverage.cobertura.xml') -Content $secondReport

    $reports = @(
        Join-Path $reportRoot 'first/coverage.cobertura.xml'
        Join-Path $reportRoot 'second/coverage.cobertura.xml'
    )
    $summary = & $coverageScript `
        -ReportPath $reports `
        -RepositoryRoot $repositoryRoot `
        -OutputDirectory $outputRoot `
        -ExpectedProject 'OpenKustoExplorer.Sample' `
        -MinimumLineRate 75

    Assert-Equal 3 $summary.CoveredLines 'OR-merged covered lines'
    Assert-Equal 4 $summary.ValidLines 'Deduplicated executable lines'
    Assert-Equal 2 $summary.ReportCount 'Report count'
    Assert-Equal 2 $summary.FileCount 'Physical C# source filtering'
    Assert-Equal 75 $summary.LineRatePercent 'Exact line rate'
    Assert-Equal 1 $summary.Projects.Count 'Project count'
    Assert-Equal 'OpenKustoExplorer.Sample' $summary.Projects[0].Project 'Project path derivation'

    $jsonPath = Join-Path $outputRoot 'summary.json'
    $markdownPath = Join-Path $outputRoot 'summary.md'
    Assert-Equal $true (Test-Path -LiteralPath $jsonPath -PathType Leaf) 'JSON summary output'
    Assert-Equal $true (Test-Path -LiteralPath $markdownPath -PathType Leaf) 'Markdown summary output'
    $json = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
    Assert-Equal 3 $json.CoveredLines 'JSON covered lines'
    $markdown = Get-Content -LiteralPath $markdownPath -Raw
    Assert-Equal $true $markdown.Contains('**75.00%**') 'Markdown line rate'

    Assert-Throws -Scenario 'Threshold above exact rate' -Action {
        & $coverageScript `
            -ReportPath $reports `
            -RepositoryRoot $repositoryRoot `
            -MinimumLineRate 75.01
    }

        Assert-Throws -Scenario 'Missing expected project' -Action {
          & $coverageScript `
            -ReportPath $reports `
            -RepositoryRoot $repositoryRoot `
            -ExpectedProject 'OpenKustoExplorer.Missing'
        }

    $emptyReport = @"
<?xml version="1.0" encoding="utf-8"?>
<coverage>
  <sources><source>$firstSource</source></sources>
  <packages>
    <package name="OpenKustoExplorer.Sample">
      <classes><class name="First" filename="First.cs" /></classes>
    </package>
  </packages>
</coverage>
"@
    $emptyReportPath = Join-Path $reportRoot 'empty/coverage.cobertura.xml'
    Set-TestFile -Path $emptyReportPath -Content $emptyReport
    Assert-Throws -Scenario 'Report without executable source lines' -Action {
        & $coverageScript -ReportPath $emptyReportPath -RepositoryRoot $repositoryRoot
    }

    Write-Host 'Code coverage script tests passed.'
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}