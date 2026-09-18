[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),

    [string] $OutputDirectory = 'artifacts/coverage',

    [ValidateRange(-1, 100)]
    [decimal] $MinimumLineRate = -1,

    [ValidateRange(0, 1000)]
    [int] $TopUncoveredCount = 25,

    [switch] $UseExistingBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryPath = [IO.Path]::GetFullPath($RepositoryRoot)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory, $repositoryPath)
$sourcePath = [IO.Path]::GetFullPath((Join-Path $repositoryPath 'src'))
$replacesRepository = $outputPath.Equals($repositoryPath, [StringComparison]::OrdinalIgnoreCase)
$replacesSource = $outputPath.Equals($sourcePath, [StringComparison]::OrdinalIgnoreCase)
if ($replacesRepository -or $replacesSource) {
    throw "Coverage output must not replace the repository or source directory: $outputPath"
}

$settingsPath = Join-Path $repositoryPath 'coverage.runsettings'
$coverageScript = Join-Path $PSScriptRoot 'Get-CodeCoverage.ps1'
if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw "Coverage settings not found: $settingsPath"
}

$testProjects = @(
    'tests/OpenKustoExplorer.Application.Tests/OpenKustoExplorer.Application.Tests.csproj'
    'tests/OpenKustoExplorer.Domain.Tests/OpenKustoExplorer.Domain.Tests.csproj'
    'tests/OpenKustoExplorer.Graph.Tests/OpenKustoExplorer.Graph.Tests.csproj'
    'tests/OpenKustoExplorer.Infrastructure.Tests/OpenKustoExplorer.Infrastructure.Tests.csproj'
    'tests/OpenKustoExplorer.Presentation.Tests/OpenKustoExplorer.Presentation.Tests.csproj'
)
$expectedProjects = @(
    'OpenKustoExplorer.Application'
    'OpenKustoExplorer.Desktop'
    'OpenKustoExplorer.Domain'
    'OpenKustoExplorer.Graph'
    'OpenKustoExplorer.Infrastructure'
    'OpenKustoExplorer.Presentation'
)

Remove-Item -LiteralPath $outputPath -Recurse -Force -ErrorAction SilentlyContinue
$rawReportPath = Join-Path $outputPath 'raw'
$buildPath = Join-Path $outputPath 'build'
$null = New-Item -ItemType Directory -Path $rawReportPath -Force

Push-Location $repositoryPath
try {
    foreach ($testProject in $testProjects) {
        $arguments = @(
            'test'
            $testProject
            '--configuration'
            $Configuration
            '--collect'
            'XPlat Code Coverage'
            '--settings'
            $settingsPath
            '--results-directory'
            $rawReportPath
            '--logger'
            'console;verbosity=minimal'
        )
        if ($UseExistingBuild) {
            $arguments += @('--no-build', '--no-restore')
        }
        else {
            $projectName = [IO.Path]::GetFileNameWithoutExtension($testProject)
            $projectBuildPath = Join-Path $buildPath $projectName
            $baseOutputPath = $projectBuildPath + [IO.Path]::DirectorySeparatorChar
            $arguments += @('--no-restore', "-p:BaseOutputPath=$baseOutputPath")
        }

        Write-Host "Collecting coverage: $testProject"
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Coverage test run failed: $testProject"
        }
    }
}
finally {
    Pop-Location
}

$reports = @(Get-ChildItem -LiteralPath $rawReportPath -Recurse -Filter 'coverage.cobertura.xml')
if ($reports.Count -ne $testProjects.Count) {
    throw "Expected $($testProjects.Count) coverage reports but found $($reports.Count) under $rawReportPath."
}

return & $coverageScript `
    -ReportPath @($reports.FullName) `
    -RepositoryRoot $repositoryPath `
    -OutputDirectory $outputPath `
    -TopUncoveredCount $TopUncoveredCount `
    -ExpectedProject $expectedProjects `
    -MinimumLineRate $MinimumLineRate