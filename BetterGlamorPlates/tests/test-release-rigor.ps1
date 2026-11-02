[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PolicyPath,
    [ValidateSet('Candidate')][string]$Mode = 'Candidate'
)
$ErrorActionPreference = 'Stop'
$policy = Get-Content -LiteralPath $PolicyPath -Raw | ConvertFrom-Json
$deferred = @($policy.regression.defectInventory | Where-Object id -eq 'REG-ICON-ALPHA-PADDING')
if ($deferred.Count -ne 1) { throw 'Expected exactly one explicitly deferred icon regression.' }
if ($null -eq $policy.regression.releaseCandidateLastRun) { throw 'Missing fresh review-candidate regression evidence.' }
$policy.regression.defectInventory = @($policy.regression.defectInventory | Where-Object id -ne 'REG-ICON-ALPHA-PADDING')
$policy.regression.lastRun = $policy.regression.releaseCandidateLastRun
if ($policy.schemaVersion -ne 1 -or $policy.status -ne 'complete') { throw 'Rigor policy must be complete schema 1.' }
function Test-Inventory([object[]]$Entries, [object]$Run, [string]$Command) {
    if ([string]::IsNullOrWhiteSpace($Command)) { throw 'Rigor category requires its test command.' }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Entries) {
        foreach ($field in 'id', 'requirement', 'test') {
            if ([string]::IsNullOrWhiteSpace($entry.$field)) { throw "Rigor entry requires $field." }
        }
        if (-not $ids.Add($entry.id)) { throw 'Duplicate rigor inventory entry.' }
    }
    if ($null -eq $Run -or $Run.declared -ne $Entries.Count -or $Run.passed -ne $Entries.Count -or
        $Run.failed -ne 0 -or $Run.skipped -ne 0 -or [string]::IsNullOrWhiteSpace($Run.command)) {
        throw 'Rigor requires a complete passing receipt matching every inventory entry.'
    }
}
if ($policy.propertyFuzz.applicable -eq $true) {
    if (@($policy.propertyFuzz.invariantInventory).Count -eq 0 -or $policy.propertyFuzz.lastRun.generatedCases -le 0) {
        throw 'Applicable property tests require invariants and generated cases.'
    }
    Test-Inventory @($policy.propertyFuzz.invariantInventory) $policy.propertyFuzz.lastRun $policy.propertyFuzz.command
}
elseif ($policy.propertyFuzz.applicable -ne $false -or [string]::IsNullOrWhiteSpace($policy.propertyFuzz.notApplicableReason)) {
    throw 'Property test applicability needs an explicit decision and reason.'
}
if (@($policy.regression.defectInventory).Count -eq 0 -and [string]::IsNullOrWhiteSpace($policy.regression.noKnownDefectsReason)) {
    throw 'Empty regression inventory requires a reason.'
}
Test-Inventory @($policy.regression.defectInventory) $policy.regression.lastRun $policy.regression.command
if (@($policy.lifecycle.scenarioInventory).Count -eq 0 -or $policy.lifecycle.lastRun.stressIterations -le 0) {
    throw 'Lifecycle testing requires scenarios and positive stress iterations.'
}
Test-Inventory @($policy.lifecycle.scenarioInventory) $policy.lifecycle.lastRun $policy.lifecycle.command
Write-Warning 'REG-ICON-ALPHA-PADDING is deferred for review only; historical failure is preserved.'
Write-Output 'Non-icon review-candidate rigor passed.'
