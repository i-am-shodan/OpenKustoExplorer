[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int]$Cycles = 5,

    [ValidateRange(0, 10)]
    [int]$WarmupCycles = 1,

    [ValidateRange(0, 100)]
    [int]$RecordEvery = 5,

    [ValidateRange(1, 20)]
    [double]$CpuThrottle = 1,

    [ValidateRange(0.1, 20)]
    [double]$BudgetMultiplier = 1,

    [switch]$EnforceBudgets,

    [switch]$Trace,

    [switch]$Headful,

    [switch]$NoBuild,

    [switch]$InstallDependencies,

    [string]$OutputPath = "artifacts\performance\browser-power-user.json"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "src\OpenKustoExplorer.Web\OpenKustoExplorer.Web.csproj"
$hostPath = Join-Path $repoRoot "src\OpenKustoExplorer.Web\bin\Release\net10.0\OpenKustoExplorer.Web.exe"
$outputFullPath = if ([IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $repoRoot $OutputPath
}
$artifactDirectory = Split-Path $outputFullPath -Parent
$hostOutputPath = Join-Path $artifactDirectory "power-user-host.stdout.log"
$hostErrorPath = Join-Path $artifactDirectory "power-user-host.stderr.log"
$tracePath = Join-Path $artifactDirectory "browser-power-user-trace.zip"
$baseUrl = "http://127.0.0.1:5216"
$environmentNames = @(
    "ASPNETCORE_ENVIRONMENT",
    "OPEN_KUSTO_EXPLORER_WEB_URL",
    "OPEN_KUSTO_EXPLORER_POWER_USER_OUTPUT",
    "OPEN_KUSTO_EXPLORER_POWER_USER_CYCLES",
    "OPEN_KUSTO_EXPLORER_POWER_USER_WARMUP_CYCLES",
    "OPEN_KUSTO_EXPLORER_POWER_USER_RECORD_EVERY",
    "OPEN_KUSTO_EXPLORER_POWER_USER_CPU_THROTTLE",
    "OPEN_KUSTO_EXPLORER_POWER_USER_TRACE",
    "OPEN_KUSTO_EXPLORER_ENFORCE_PERFORMANCE_BUDGETS",
    "OPEN_KUSTO_EXPLORER_PERFORMANCE_BUDGET_MULTIPLIER",
    "OPEN_KUSTO_EXPLORER_HEADLESS"
)
$originalEnvironment = @{}
foreach ($name in $environmentNames) {
    $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}

$hostProcess = $null
try {
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    if (Get-NetTCPConnection -LocalPort 5216 -State Listen -ErrorAction SilentlyContinue) {
        throw "Port 5216 is already in use. Stop the existing host or choose a clean test machine."
    }

    Push-Location $PSScriptRoot
    try {
        if ($InstallDependencies) {
            & npm.cmd ci
            if ($LASTEXITCODE -ne 0) {
                throw "npm ci failed with exit code $LASTEXITCODE."
            }

            & npx.cmd playwright install chromium
            if ($LASTEXITCODE -ne 0) {
                throw "Playwright browser installation failed with exit code $LASTEXITCODE."
            }
        }
    } finally {
        Pop-Location
    }

    if (-not $NoBuild) {
        & dotnet build $projectPath --configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw "Release Web build failed with exit code $LASTEXITCODE."
        }
    }

    if (-not (Test-Path $hostPath)) {
        throw "The Release host does not exist at '$hostPath'. Run without -NoBuild first."
    }

    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:OPEN_KUSTO_EXPLORER_WEB_URL = $baseUrl
    $env:OPEN_KUSTO_EXPLORER_POWER_USER_OUTPUT = $outputFullPath
    $env:OPEN_KUSTO_EXPLORER_POWER_USER_CYCLES = $Cycles.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:OPEN_KUSTO_EXPLORER_POWER_USER_WARMUP_CYCLES = $WarmupCycles.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:OPEN_KUSTO_EXPLORER_POWER_USER_RECORD_EVERY = $RecordEvery.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:OPEN_KUSTO_EXPLORER_POWER_USER_CPU_THROTTLE = $CpuThrottle.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:OPEN_KUSTO_EXPLORER_ENFORCE_PERFORMANCE_BUDGETS = if ($EnforceBudgets) { "1" } else { $null }
    $env:OPEN_KUSTO_EXPLORER_POWER_USER_TRACE = if ($Trace) { $tracePath } else { $null }
    $env:OPEN_KUSTO_EXPLORER_PERFORMANCE_BUDGET_MULTIPLIER = $BudgetMultiplier.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:OPEN_KUSTO_EXPLORER_HEADLESS = if ($Headful) { "0" } else { "1" }

    $hostStartParameters = @{
        FilePath = $hostPath
        ArgumentList = @("--urls", $baseUrl)
        WorkingDirectory = Split-Path $projectPath -Parent
        RedirectStandardOutput = $hostOutputPath
        RedirectStandardError = $hostErrorPath
        PassThru = $true
    }
    $hostProcess = Start-Process @hostStartParameters

    $deadline = [DateTime]::UtcNow.AddMinutes(2)
    $hostReady = $false
    while ([DateTime]::UtcNow -lt $deadline -and -not $hostReady) {
        if ($hostProcess.HasExited) {
            throw "The Web host exited with code $($hostProcess.ExitCode). See '$hostErrorPath'."
        }

        try {
            $response = Invoke-WebRequest -Uri "$baseUrl/healthz" -UseBasicParsing -TimeoutSec 2
            $hostReady = $response.StatusCode -eq 200
        } catch {
            [Threading.Tasks.Task]::Delay(250).GetAwaiter().GetResult()
        }
    }

    if (-not $hostReady) {
        throw "The Web host did not become ready at $baseUrl."
    }

    Push-Location $PSScriptRoot
    try {
        & npm.cmd run power-user
        if ($LASTEXITCODE -ne 0) {
            throw "Power-user profiling failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }

    Write-Host "Power-user report: $outputFullPath"
    if ($Trace) {
        Write-Host "Playwright trace: $tracePath"
    }
} finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
        Wait-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue
    }

    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name])
    }
}