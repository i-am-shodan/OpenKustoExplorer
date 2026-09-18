[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]] $ReportPath,

    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),

    [string] $OutputDirectory,

    [ValidateRange(0, 1000)]
    [int] $TopUncoveredCount = 25,

    [string[]] $ExpectedProject = @(),

    [ValidateRange(-1, 100)]
    [decimal] $MinimumLineRate = -1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$pathComparison = if ([OperatingSystem]::IsWindows()) {
    [StringComparison]::OrdinalIgnoreCase
}
else {
    [StringComparison]::Ordinal
}
$pathComparer = if ([OperatingSystem]::IsWindows()) {
    [StringComparer]::OrdinalIgnoreCase
}
else {
    [StringComparer]::Ordinal
}

function Get-NormalizedPath {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    return [IO.Path]::GetFullPath($Path).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
}

function Test-ChildPath {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $ParentPath
    )

    if ($Path.Equals($ParentPath, $pathComparison)) {
        return $true
    }

    $prefix = $ParentPath + [IO.Path]::DirectorySeparatorChar
    return $Path.StartsWith($prefix, $pathComparison)
}

function Resolve-SourceFile {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]] $SourceRoots,

        [Parameter(Mandatory)]
        [string] $FileName,

        [Parameter(Mandatory)]
        [string] $SourceDirectory
    )

    $normalizedFileName = $FileName.Replace(
        '\',
        [IO.Path]::DirectorySeparatorChar).Replace(
            '/',
            [IO.Path]::DirectorySeparatorChar)
    $candidates = [Collections.Generic.List[string]]::new()
    if ([IO.Path]::IsPathRooted($normalizedFileName)) {
        $candidates.Add($normalizedFileName)
    }

    foreach ($sourceRoot in $SourceRoots) {
        $normalizedSourceRoot = $sourceRoot.Replace(
            '\',
            [IO.Path]::DirectorySeparatorChar).Replace(
                '/',
                [IO.Path]::DirectorySeparatorChar)
        if (-not [IO.Path]::IsPathRooted($normalizedSourceRoot)) {
            $normalizedSourceRoot = Join-Path $SourceDirectory $normalizedSourceRoot
        }

        $candidates.Add((Join-Path $normalizedSourceRoot $normalizedFileName))
    }

    foreach ($candidate in $candidates) {
        try {
            $fullPath = Get-NormalizedPath -Path $candidate
        }
        catch {
            continue
        }

        $isProductionSource = Test-ChildPath -Path $fullPath -ParentPath $SourceDirectory
        $isCSharp = [IO.Path]::GetExtension($fullPath).Equals(
            '.cs',
            [StringComparison]::OrdinalIgnoreCase)
        $isPhysicalFile = Test-Path -LiteralPath $fullPath -PathType Leaf
        if ($isProductionSource -and $isCSharp -and $isPhysicalFile) {
            return $fullPath
        }
    }

    return $null
}

$repositoryPath = Get-NormalizedPath -Path $RepositoryRoot
$sourceDirectory = Get-NormalizedPath -Path (Join-Path $repositoryPath 'src')
if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
    throw "Production source directory not found: $sourceDirectory"
}

$reportFiles = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($path in $ReportPath) {
    $resolvedPaths = @(Resolve-Path -Path $path -ErrorAction SilentlyContinue)
    if ($resolvedPaths.Count -eq 0) {
        throw "Coverage report path did not match any files: $path"
    }

    foreach ($resolvedPath in $resolvedPaths) {
        $item = Get-Item -LiteralPath $resolvedPath.Path
        if (-not $item.PSIsContainer) {
            $reportFiles.Add($item)
        }
    }
}

$reports = @($reportFiles | Sort-Object FullName -Unique)
if ($reports.Count -eq 0) {
    throw 'No coverage reports were provided.'
}

$lineMap = [Collections.Generic.Dictionary[string, object]]::new($pathComparer)
foreach ($report in $reports) {
    [xml] $coverage = Get-Content -LiteralPath $report.FullName -Raw
    $sourceRoots = @(
        $coverage.SelectNodes('/coverage/sources/source') |
            ForEach-Object { $_.InnerText.Trim() } |
            Where-Object { $_ }
    )

    foreach ($package in @($coverage.coverage.packages.package)) {
        foreach ($class in @($package.classes.class)) {
            $sourceFile = Resolve-SourceFile `
                -SourceRoots $sourceRoots `
                -FileName ([string] $class.filename) `
                -SourceDirectory $sourceDirectory
            if ($null -eq $sourceFile) {
                continue
            }

            $relativePath = [IO.Path]::GetRelativePath($sourceDirectory, $sourceFile).Replace('\', '/')
            $project = $relativePath.Split('/')[0]
            foreach ($line in @($class.SelectNodes('./lines/line'))) {
                $lineNumber = [int] $line.number
                $key = "$sourceFile|$lineNumber"
                $covered = [int] $line.hits -gt 0
                if ($lineMap.ContainsKey($key)) {
                    if ($covered) {
                        $lineMap[$key].Covered = $true
                    }
                }
                else {
                    $lineMap.Add($key, [pscustomobject] @{
                        Project = $project
                        RelativePath = $relativePath
                        Line = $lineNumber
                        Covered = $covered
                    })
                }
            }
        }
    }
}

$lines = @($lineMap.Values)
if ($lines.Count -eq 0) {
    throw 'Coverage reports contained no executable lines for physical source files under src.'
}

$coveredLines = @($lines | Where-Object Covered).Count
$validLines = $lines.Count
$uncoveredLines = $validLines - $coveredLines
$lineRate = [decimal] $coveredLines / $validLines
$lineRatePercent = $lineRate * 100

$projectSummaries = @(
    $lines |
        Group-Object Project |
        ForEach-Object {
            $projectLines = @($_.Group)
            $projectCovered = @($projectLines | Where-Object Covered).Count
            [pscustomobject] @{
                Project = $_.Name
                CoveredLines = $projectCovered
                ValidLines = $projectLines.Count
                UncoveredLines = $projectLines.Count - $projectCovered
                LineRatePercent = [decimal] $projectCovered / $projectLines.Count * 100
            }
        } |
        Sort-Object Project
)
$actualProjects = @($projectSummaries.Project)
$missingProjects = @(
    $ExpectedProject |
        Where-Object { $_ -notin $actualProjects } |
        Sort-Object -Unique
)
if ($missingProjects.Count -gt 0) {
    throw "Coverage reports are missing expected projects: $($missingProjects -join ', ')"
}

$fileSummaries = @(
    $lines |
        Group-Object RelativePath |
        ForEach-Object {
            $fileLines = @($_.Group)
            $fileCovered = @($fileLines | Where-Object Covered).Count
            [pscustomobject] @{
                Project = $fileLines[0].Project
                RelativePath = $_.Name
                CoveredLines = $fileCovered
                ValidLines = $fileLines.Count
                UncoveredLines = $fileLines.Count - $fileCovered
                LineRatePercent = [decimal] $fileCovered / $fileLines.Count * 100
            }
        } |
        Sort-Object `
            @{ Expression = 'UncoveredLines'; Descending = $true },
            @{ Expression = 'ValidLines'; Descending = $true },
            RelativePath
)
$topUncoveredFiles = @($fileSummaries | Select-Object -First $TopUncoveredCount)

$summary = [pscustomobject] @{
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    RepositoryRoot = $repositoryPath
    ReportCount = $reports.Count
    ReportPaths = @($reports.FullName)
    FileCount = $fileSummaries.Count
    CoveredLines = $coveredLines
    ValidLines = $validLines
    UncoveredLines = $uncoveredLines
    LineRate = $lineRate
    LineRatePercent = $lineRatePercent
    Projects = $projectSummaries
    TopUncoveredFiles = $topUncoveredFiles
}

$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
$rateText = $lineRatePercent.ToString('F2', $invariantCulture)
Write-Host "Production line coverage: $rateText% ($coveredLines/$validLines)"
Write-Host "Reports: $($reports.Count); files: $($fileSummaries.Count); uncovered lines: $uncoveredLines"
$projectSummaries |
    Select-Object Project, CoveredLines, ValidLines, UncoveredLines,
        @{ Name = 'LineRatePercent'; Expression = { $_.LineRatePercent.ToString('F2', $invariantCulture) } } |
    Format-Table -AutoSize |
    Out-Host

if ($OutputDirectory) {
    $outputPath = [IO.Path]::GetFullPath($OutputDirectory, $repositoryPath)
    $null = New-Item -ItemType Directory -Path $outputPath -Force
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'summary.json') -Encoding utf8NoBOM

    $markdown = [Collections.Generic.List[string]]::new()
    $markdown.Add('## Code coverage')
    $markdown.Add('')
    $markdown.Add("**$rateText%** production line coverage ($coveredLines of $validLines executable lines).")
    $markdown.Add('')
    $markdown.Add('| Project | Covered | Executable | Uncovered | Line coverage |')
    $markdown.Add('| --- | ---: | ---: | ---: | ---: |')
    foreach ($projectSummary in $projectSummaries) {
        $projectRate = $projectSummary.LineRatePercent.ToString('F2', $invariantCulture)
        $markdown.Add("| $($projectSummary.Project) | $($projectSummary.CoveredLines) | $($projectSummary.ValidLines) | $($projectSummary.UncoveredLines) | $projectRate% |")
    }

    $markdown.Add('')
    $markdown.Add('### Largest uncovered files')
    $markdown.Add('')
    $markdown.Add('| File | Covered | Executable | Uncovered | Line coverage |')
    $markdown.Add('| --- | ---: | ---: | ---: | ---: |')
    foreach ($fileSummary in $topUncoveredFiles) {
        $fileRate = $fileSummary.LineRatePercent.ToString('F2', $invariantCulture)
        $markdown.Add("| ``$($fileSummary.RelativePath)`` | $($fileSummary.CoveredLines) | $($fileSummary.ValidLines) | $($fileSummary.UncoveredLines) | $fileRate% |")
    }

    $markdown | Set-Content -LiteralPath (Join-Path $outputPath 'summary.md') -Encoding utf8NoBOM
}

$isBelowMinimum = ([decimal] $coveredLines * 100) -lt ($MinimumLineRate * $validLines)
if ($MinimumLineRate -ge 0 -and $isBelowMinimum) {
    throw "Production line coverage $rateText% is below the required $MinimumLineRate%."
}

return $summary