param(
    [switch]$RunMutation,
    [switch]$CheckFormatting,
    [string]$Configuration = 'Release',
    [string]$Dotnet = 'dotnet',
    [string]$Python = 'python',
    [string]$OutputDirectory,
    [string]$CollectorPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. "$projectRoot/tools/VerificationEnvironment.ps1"
if ($RunMutation) { throw 'Mutation qualification is a separate manual campaign. This portable runner does not execute or certify mutation tests; see README.md.' }
$dotnetTool = Resolve-VerificationTool -Command $Dotnet
$pythonTool = Resolve-VerificationTool -Command $Python
$run = New-VerificationRun -ProjectRoot $projectRoot -OutputDirectory $OutputDirectory -Name 'offline'
$steps = [Collections.Generic.List[object]]::new()
$evidence = [Collections.Generic.List[object]]::new()
$sourceSha256 = $null
$finalSourceSha256 = $null
function Invoke-Gate([string]$Name, [scriptblock]$Action) {
    $start = [DateTime]::UtcNow
    try { & $Action; $steps.Add([ordered]@{name=$Name;status='Passed';startedUtc=$start.ToString('o');completedUtc=[DateTime]::UtcNow.ToString('o')}) }
    catch { $steps.Add([ordered]@{name=$Name;status='Failed';startedUtc=$start.ToString('o');error=$_.Exception.Message}); throw }
}
function Add-Evidence([string]$Path) {
    $evidence.Add([ordered]@{path=[IO.Path]::GetFullPath($Path);sha256=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()})
}
Push-Location $projectRoot
try {
    $sourceSha256 = & ./tools/Get-SourceInventory.ps1 -Destination "$run/inputs.json"
    Add-Evidence "$run/inputs.json"
    Invoke-Gate 'LockedRestore' { & $dotnetTool restore project/Exportaramus.slnx --locked-mode; Assert-VerificationExit 'Locked restore' }
    if ($CheckFormatting) { Invoke-Gate 'FormattingAndAnalyzers' { & $dotnetTool format project/Exportaramus.slnx --no-restore --verify-no-changes; Assert-VerificationExit 'Formatting' } }
    else { $steps.Add([ordered]@{name='FormattingAndAnalyzers';status='NotRun';reason='Optional formatting verification was not requested.'}) }
    Invoke-Gate 'Build' { & $dotnetTool build project/Exportaramus.slnx -c $Configuration --no-restore -p:ShouldUnsetParentConfigurationAndPlatform=false; Assert-VerificationExit 'Build' }
    Invoke-Gate 'PackageInspection' { & ./tools/Inspect-Package.ps1 -OutputDirectory "project/bin/$Configuration" -EvidenceDirectory "$run/package"; Add-Evidence "$run/package/package-inspection.json" }
    Invoke-Gate 'ProviderAndConsumerTestsAndStrictCoverage' {
        $result = @(& ./tests/test-all.ps1 -Configuration $Configuration -Dotnet $dotnetTool -OutputDirectory "$run/coverage" -CollectorPath $CollectorPath)
        $summaryPath = [string]$result[-1]
        $coverage = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        Add-Evidence $summaryPath
        foreach ($component in @($coverage, $coverage.consumerClient, $coverage.inventoryReader)) { Add-Evidence $component.report; Add-Evidence $component.trx }
    }
    Invoke-Gate 'SchemasFixturesMappingsAndKit' {
        & $pythonTool tools/contract-verify.py; Assert-VerificationExit 'Schemas and fixtures'
        & $pythonTool tools/contract-checksums.py --verify; Assert-VerificationExit 'Contract checksums'
    }
    Invoke-Gate 'DecisionScopeInventory' { & $pythonTool tools/contract-quality-scope.py --output-dir "$run/scope"; Assert-VerificationExit 'Decision scope'; Add-Evidence "$run/scope/coverage-manifest.json" }
    Invoke-Gate 'PortableToolingTests' {
        $previousPath = $env:PATH
        try {
            $env:PATH = $PSHOME + [IO.Path]::PathSeparator + $previousPath
            foreach ($test in @('test-contract-verification.py','test-consumer-kit-verification.py','test-contract-quality-scope.py','test-source-inventory.py','test-reproducibility.py','test-portable-verification.py')) {
                & $pythonTool (Join-Path tools $test)
                Assert-VerificationExit $test
            }
        }
        finally { $env:PATH = $previousPath }
    }
}
finally {
    try {
        $finalSourceSha256 = & ./tools/Get-SourceInventory.ps1 -Destination "$run/inputs-after.json"
        Add-Evidence "$run/inputs-after.json"
        $unchanged = $null -ne $sourceSha256 -and $sourceSha256 -eq $finalSourceSha256
        $required = @('LockedRestore','Build','PackageInspection','ProviderAndConsumerTestsAndStrictCoverage','SchemasFixturesMappingsAndKit','DecisionScopeInventory','PortableToolingTests')
        $passed = $unchanged -and @($steps | Where-Object status -EQ 'Failed').Count -eq 0
        foreach ($name in $required) { $matching = @($steps | Where-Object name -EQ $name); if ($matching.Count -ne 1 -or $matching[0].status -ne 'Passed') { $passed = $false } }
        [ordered]@{
            format='exportaramus-portable-verification';status=$(if ($passed) {'Passed'} else {'IncompleteOrFailed'})
            sourceSha256=$sourceSha256;inputsUnchanged=$unchanged;configuration=$Configuration
            steps=@($steps.ToArray());evidence=@($evidence.ToArray())
            mutation=[ordered]@{status='NotRun';reason='Separate manual mutation campaign required.'}
            releaseQualification='Incomplete';loaded=$false;liveTested=$false;releaseApproved=$false;published=$false
            completedUtc=[DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$run/gate-execution.json" -Encoding utf8
    }
    finally { Pop-Location }
}
Assert-VerificationOutcome -Receipt "$run/gate-execution.json"
Write-Output "$run/gate-execution.json"
