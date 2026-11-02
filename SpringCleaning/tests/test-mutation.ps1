param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$toolManifest = Join-Path $root '.config\dotnet-tools.json'
$policyPath = Join-Path $PSScriptRoot 'mutation-policy.json'
$testProjectRoot = Join-Path $PSScriptRoot 'SpringCleaning.Tests'


$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
if ($policy.status -eq 'not-applicable') {
    if ([string]::IsNullOrWhiteSpace([string]$policy.notApplicableReason)) {
        throw 'Mutation testing marked not-applicable without a reason.'
    }
    Write-Host "Mutation testing not applicable: $($policy.notApplicableReason)"
    exit 0
}
if ($policy.status -ne 'required' -or @($policy.targets).Count -eq 0) {
    throw 'Mutation policy must be required and name at least one decision target.'
}

# Keep the declared inventory and executable mutation selection synchronized.
$config = Get-Content -LiteralPath (Join-Path $testProjectRoot 'stryker-config.json') -Raw | ConvertFrom-Json
$selected = @($config.'stryker-config'.mutate | ForEach-Object { $_.Replace('\', '/') -replace '^\.\./\.\./', '' })
$declared = @($policy.targets | ForEach-Object { $_.Replace('\', '/') })
if (@(Compare-Object $declared $selected).Count -ne 0) {
    throw 'Mutation config and policy target inventories differ.'
}
$coverage = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'coverage-manifest.json') -Raw | ConvertFrom-Json
$missing = @($coverage.includedProductionFiles | Where-Object {
    $includedPath = $_.Replace('\', '/')
    $selected -notcontains $includedPath -and
    @($policy.excludedDecisionFiles | Where-Object { $_.path -eq $includedPath -and -not [string]::IsNullOrWhiteSpace($_.reason) }).Count -eq 0
})
if ($missing.Count -ne 0) {
    throw "Included production files lack mutation targets or reviewed exclusions: $($missing -join ', ')."
}

& $dotnet tool restore --tool-manifest $toolManifest
if ($LASTEXITCODE -ne 0) {
    throw "Pinned dotnet-stryker restore failed with exit code $LASTEXITCODE."
}

Push-Location $testProjectRoot
try {
    & $dotnet tool run dotnet-stryker -- `
        --config-file 'stryker-config.json' `
        --configuration $Configuration `
        --break-at ([double]$policy.minimumMutationScore)
    if ($LASTEXITCODE -ne 0) {
        throw "Mutation testing failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
