param([string]$Configuration = 'Release', [switch]$EnforceTiming)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source

$budgetPath = Join-Path $PSScriptRoot 'performance-budget.json'
$resultPath = Join-Path $PSScriptRoot 'TestResults\performance\quality-latest.json'
$projectPath = Join-Path $PSScriptRoot 'SpringCleaning.Performance\SpringCleaning.Performance.csproj'

Push-Location $root
try {
    & $dotnet run --project $projectPath -c $Configuration --no-build --no-restore -- $resultPath
    if ($LASTEXITCODE -ne 0) {
        throw "Performance harness failed with exit code $LASTEXITCODE."
    }

    $budget = Get-Content -LiteralPath $budgetPath -Raw | ConvertFrom-Json
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ([string]$result.sourceState -ne [string]$budget.sourceState) {
        throw "Performance source state $($result.sourceState) does not match budget $($budget.sourceState)."
    }

    foreach ($scenario in @($budget.scenarios)) {
        $measured = @($result.scenarios | Where-Object Name -eq $scenario.name)
        if ($measured.Count -ne 1) {
            throw "Performance result must contain exactly one '$($scenario.name)' scenario."
        }

        $actual = $measured[0]
        if ($EnforceTiming -and [double]$actual.MeanMilliseconds -gt [double]$scenario.budget.maxMeanMilliseconds) {
            throw "Performance mean budget failed for '$($scenario.name)': $($actual.MeanMilliseconds) ms."
        }
        if ($EnforceTiming -and [double]$actual.P95Milliseconds -gt [double]$scenario.budget.maxP95Milliseconds) {
            throw "Performance p95 budget failed for '$($scenario.name)': $($actual.P95Milliseconds) ms."
        }
        if ([double]$actual.MeanAllocatedBytesPerOperation -gt
            [double]$scenario.budget.maxAllocatedBytesPerOperation) {
            throw "Performance allocation budget failed for '$($scenario.name)': $($actual.MeanAllocatedBytesPerOperation) B/op."
        }

        Write-Host ("Performance {0}: mean {1:N6} ms, p95 {2:N6} ms, {3:N0} B/op" -f
            $scenario.name,
            [double]$actual.MeanMilliseconds,
            [double]$actual.P95Milliseconds,
            [double]$actual.MeanAllocatedBytesPerOperation)
    }
}
finally {
    Pop-Location
}
