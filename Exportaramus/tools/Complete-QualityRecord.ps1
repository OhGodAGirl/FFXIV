param(
    [Parameter(Mandatory)][string]$MutationSummary,
    [Parameter(Mandatory)][string]$GateExecution,
    [Parameter(Mandatory)][string]$Reproduction,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/VerificationEnvironment.ps1"
$evidence = New-VerificationRun -ProjectRoot $projectRoot -OutputDirectory $OutputDirectory -Name 'qualification'
$execution = Get-Content -LiteralPath $GateExecution -Raw | ConvertFrom-Json
$mutation = Get-Content -LiteralPath $MutationSummary -Raw | ConvertFrom-Json
$reproductionRecord = Get-Content -LiteralPath $Reproduction -Raw | ConvertFrom-Json
$sourceHash = & "$PSScriptRoot/Get-SourceInventory.ps1" -Destination "$evidence/source-before.json"
$verified = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-VerifiedEvidence([string]$Path, [string]$Expected = '') {
    $full = [IO.Path]::GetFullPath($Path)
    $actual = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Expected -and ($Expected -cnotmatch '^[0-9a-f]{64}$' -or $actual -ne $Expected)) { throw "Verification evidence changed: $full" }
    if ($verified.ContainsKey($full) -and $verified[$full] -ne $actual) { throw "Conflicting evidence binding: $full" }
    $verified[$full] = $actual
}
function Assert-BoundEvidence([string]$Path, [string]$Expected) {
    $full = [IO.Path]::GetFullPath($Path)
    if (!$verified.ContainsKey($full) -or $verified[$full] -ne $Expected) { throw "Evidence is not bound by this gate execution: $full" }
    Add-VerifiedEvidence $full $Expected
}
function Add-RequiredEvidence([string]$Path, [string]$Expected) {
    if (!$Path -or $Expected -cnotmatch '^[0-9a-f]{64}$') { throw 'A required mutation proof needs an exact artifact hash.' }
    Add-VerifiedEvidence $Path $Expected
}
Add-VerifiedEvidence $GateExecution
if ($execution.format -ne 'exportaramus-portable-verification' -or $execution.status -ne 'Passed' -or
    $execution.configuration -ne 'Release' -or !$execution.inputsUnchanged -or $execution.sourceSha256 -ne $sourceHash) {
    throw 'A passing explicit portable Release receipt matching the current source is required.'
}
if (@($execution.evidence).Count -lt 4) { throw 'Gate execution has no complete artifact bindings.' }
foreach ($artifact in $execution.evidence) {
    if ($artifact.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'An exact evidence hash is required.' }
    Add-VerifiedEvidence $artifact.path $artifact.sha256
}
$required = @('LockedRestore','FormattingAndAnalyzers','Build','PackageInspection','ProviderAndConsumerTestsAndStrictCoverage','SchemasFixturesMappingsAndKit','DecisionScopeInventory','PortableToolingTests')
foreach ($name in $required) {
    $step = @($execution.steps | Where-Object name -EQ $name)
    if ($step.Count -ne 1 -or $step[0].status -ne 'Passed') { throw "Required gate is not passed: $name" }
}
function Get-UniqueBoundArtifact([string]$Name) {
    $matches = @($execution.evidence | Where-Object { [IO.Path]::GetFileName($_.path) -eq $Name })
    if ($matches.Count -ne 1) { throw "Exactly one bound $Name is required." }
    return $matches[0].path
}
$coveragePath = Get-UniqueBoundArtifact 'summary.json'
$coverage = Get-Content -LiteralPath $coveragePath -Raw | ConvertFrom-Json
$packagePath = Get-UniqueBoundArtifact 'package-inspection.json'
$package = Get-Content -LiteralPath $packagePath -Raw | ConvertFrom-Json
$scopePath = Get-UniqueBoundArtifact 'coverage-manifest.json'
$scope = Get-Content -LiteralPath $scopePath -Raw | ConvertFrom-Json
$components = @(
    @{value=$coverage;prefix=''},
    @{value=$coverage.consumerClient;prefix='Exportaramus.ConsumerKit.V1_2.CollectionDataIpcClient'},
    @{value=$coverage.inventoryReader;prefix='CollectionExample.InventoryItemReader'}
)
foreach ($component in $components) {
    $value = $component.value
    if ($null -eq $value) { throw 'Provider, consumer client and example reader coverage are all required.' }
    Assert-BoundEvidence $value.report $value.reportSha256
    Assert-BoundEvidence $value.trx $value.trxSha256
    $actual = Read-VerificationCoverage -Run (Split-Path $value.trx -Parent) -Trx $value.trx -TypePrefix $component.prefix
    foreach ($field in @('linesCovered','linesValid','branchesCovered','branchesValid','reportSha256','trxSha256')) {
        if ($actual[$field] -ne $value.$field) { throw "Coverage summary differs from its actual report: $field" }
    }
    if (![IO.Path]::GetFullPath($actual.report).Equals([IO.Path]::GetFullPath($value.report), [StringComparison]::OrdinalIgnoreCase)) { throw 'Coverage summary does not name its exact TRX attachment.' }
}
if ($coverage.consumerClient.trx -ne $coverage.inventoryReader.trx) { throw 'Client and reader coverage must come from the same complete ConsumerKit suite.' }
$expectedSources = @{}
foreach ($map in @($scope.handwrittenSourceHashes, $scope.separateConsumerSuite.sourceHashes)) {
    foreach ($property in $map.PSObject.Properties) {
        if ($expectedSources.ContainsKey($property.Name) -and $expectedSources[$property.Name] -ne $property.Value) { throw 'Conflicting source scopes.' }
        $expectedSources[$property.Name] = $property.Value
    }
}
if ($expectedSources.Count -eq 0 -or !$mutation.gatePassed -or !$mutation.inputsUnchanged -or
    $mutation.sourceSha256 -ne $sourceHash -or $mutation.totalMutants -le 0 -or
    @($mutation.unresolvedMutants).Count -gt 0 -or @($mutation.reviewRequired).Count -gt 0) { throw 'Final mutation evidence is stale or has unresolved cases.' }
$instrumented = @{}
foreach ($property in $mutation.instrumentationAudit.instrumentedFiles.PSObject.Properties) {
    if (!$expectedSources.ContainsKey($property.Name) -or $expectedSources[$property.Name] -ne $property.Value.sourceSha256 -or !$property.Value.matchesCurrentSource) { throw 'Mutation instrumentation differs from the complete current provider/consumer/reader scope.' }
    $instrumented[$property.Name] = $true
}
foreach ($name in $mutation.reviewedSources) {
    if (!$expectedSources.ContainsKey($name)) { throw 'Reviewed mutation source is outside the current scope.' }
    $instrumented[$name] = $true
}
if ($instrumented.Count -ne $expectedSources.Count) { throw 'Complete current provider, consumer client and reader mutation instrumentation is required.' }
if ($mutation.format -notin @('exportaramus-mutation-campaign','exportaramus-repaired-mutation-campaign','exportaramus-multi-stage-mutation-campaign')) { throw 'Unsupported final mutation evidence format.' }
if ($mutation.format -eq 'exportaramus-repaired-mutation-campaign') {
    if (!$mutation.repairScopeVerified -or $mutation.evidenceStrategy -ne 'complete-original-campaign-plus-exact-strengthened-test-execution' -or !$mutation.originalCampaignSourceSha256 -or !$mutation.repairBridgeSha256) { throw 'Repair campaign lacks explicit original/final input reconciliation.' }
    Add-RequiredEvidence $mutation.repairBridge $mutation.repairBridgeSha256
}
if ($mutation.format -eq 'exportaramus-multi-stage-mutation-campaign') {
    if (!$mutation.stageScopeVerified -or $mutation.evidenceStrategy -ne 'complete-original-campaign-plus-exact-reviewed-stage-executions' -or !$mutation.originalCampaignSourceSha256 -or !$mutation.stageCompositionSha256) { throw 'Multi-stage campaign lacks exact original/final stage reconciliation.' }
    Add-RequiredEvidence $mutation.stageComposition $mutation.stageCompositionSha256
}
Add-VerifiedEvidence $MutationSummary
Add-RequiredEvidence $mutation.report $mutation.reportSha256
if ($mutation.reviewLedger) { Add-RequiredEvidence $mutation.reviewLedger $mutation.reviewLedgerSha256 }
foreach ($artifact in $mutation.reviewEvidence) { Add-RequiredEvidence $artifact.path $artifact.sha256 }
if ($reproductionRecord.status -ne 'Passed' -or $reproductionRecord.scope -ne 'SameEnvironmentPackageReproduction' -or
    $reproductionRecord.configuration -ne 'Release' -or !$reproductionRecord.dependencyInputsUnchanged -or
    $reproductionRecord.sourceSha256 -ne $sourceHash -or $reproductionRecord.firstPackageSha256 -ne $package.packageSha256 -or
    $reproductionRecord.rebuiltPackageSha256 -ne $package.packageSha256) { throw 'Explicit package reproduction evidence does not match final source and package bytes.' }
Add-VerifiedEvidence $Reproduction
$packageHash = (Get-FileHash -LiteralPath $package.package -Algorithm SHA256).Hash.ToLowerInvariant()
if ($package.status -ne 'Passed' -or $packageHash -ne $package.packageSha256) { throw 'Package inspection is stale.' }
if ((& "$PSScriptRoot/Get-SourceInventory.ps1" -Destination "$evidence/source-after.json") -ne $sourceHash) { throw 'Source changed during final qualification verification.' }
$record = Join-Path $evidence 'quality-gates.json'
[ordered]@{
    format='exportaramus-final-quality-record';schemaVersion='2.0';status='Passed';version=$package.version;configuration='Release';sourceSha256=$sourceHash;packageSha256=$packageHash
    packageInspection=[ordered]@{path=[IO.Path]::GetFullPath($packagePath);sha256=$verified[[IO.Path]::GetFullPath($packagePath)]}
    gateExecution=[IO.Path]::GetFullPath($GateExecution);reproduction=[IO.Path]::GetFullPath($Reproduction)
    tests=$coverage.tests;consumerKitTests=$coverage.consumerClient.tests
    lineCoverage=100;branchCoverage=100;coverage=$coverage
    survived=0;uncovered=0;timedOut=0;mutationSummary=[IO.Path]::GetFullPath($MutationSummary)
    mutationSummarySha256=(Get-FileHash -LiteralPath $MutationSummary -Algorithm SHA256).Hash.ToLowerInvariant()
    mutationTotal=$mutation.totalMutants;mutationStatusCounts=$mutation.statusCounts
    evidence=@($verified.GetEnumerator() | Sort-Object Key | ForEach-Object { [ordered]@{path=$_.Key;sha256=$_.Value} })
    completedUtc=[DateTime]::UtcNow.ToString('o');loaded=$false;liveTested=$false;releaseApproved=$false;published=$false
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $record -Encoding utf8
Write-Output $record
