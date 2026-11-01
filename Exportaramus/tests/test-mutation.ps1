param([string]$RunDirectory, [string]$Dotnet = 'dotnet', [string]$Python = 'python', [string]$Runner, [switch]$SetupOnly, [ValidateRange(1, 4)][int]$Concurrency = 4)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. "$repository/tools/VerificationEnvironment.ps1"
$dotnet = Resolve-VerificationTool -Command $Dotnet
$python = Resolve-VerificationTool -Command $Python
if (!$Runner) { throw 'Pass -Runner with the path to Stryker.CLI.dll from the pinned Stryker 4.16.0 installation.' }
$runner = [IO.Path]::GetFullPath($Runner)
if (!(Test-Path -LiteralPath $runner)) { throw 'Pinned Stryker 4.16.0 is unavailable at the supplied -Runner path.' }
$runnerVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($runner).ProductVersion
if ($runnerVersion -notmatch '^4\.16\.0(?:\+|$)') { throw "Expected Stryker 4.16.0; supplied runner reports $runnerVersion." }

if (!$RunDirectory) {
    $RunDirectory = Join-Path $repository ('development/mutation/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N'))
}
$RunDirectory = [IO.Path]::GetFullPath($RunDirectory)
if (Test-Path -LiteralPath $RunDirectory) { throw 'Use a new, non-existing mutation run directory to avoid stale-report acceptance.' }
$helper = Join-Path $repository 'tools/mutation-campaign.py'
& (Join-Path $repository 'tools/Get-SourceInventory.ps1') -Destination (Join-Path $RunDirectory 'source-before.json')
& $python $helper prepare --run-dir $RunDirectory --runner $runner
if ($LASTEXITCODE -ne 0) { throw 'Mutation input and scope preparation failed.' }
$campaigns = @(& $python $helper campaigns | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or $campaigns.Count -ne 4) { throw 'The complete current mutation campaign definition is unavailable.' }
& $dotnet $runner --help > (Join-Path $RunDirectory 'runner-help.log')
Assert-VerificationExit 'Stryker startup'
$tested = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$baselines = @()
foreach ($campaign in $campaigns) {
    $project = [IO.Path]::GetFullPath((Join-Path $campaign.workingDirectory $campaign.testProject))
    if (!$tested.Add($project)) { continue }
    $results = Join-Path $RunDirectory ('baseline-' + $campaign.name)
    & $dotnet test $project -c Release -p:RestoreLockedMode=true --logger 'trx;LogFileName=baseline.trx' --results-directory $results -- xUnit.MaxParallelThreads=2
    Assert-VerificationExit 'Mutation baseline'
    $counts = Read-VerificationTests -Trx (Join-Path $results 'baseline.trx')
    $baselines += @{ project=$project; tests=$counts }
}
@{ baselines=$baselines; mutationExecuted=$false } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $RunDirectory 'baseline-verification.json') -Encoding utf8NoBOM
if ($SetupOnly) {
    & (Join-Path $repository 'tools/Get-SourceInventory.ps1') -Destination (Join-Path $RunDirectory 'source-after.json')
    $before = Get-Content -LiteralPath (Join-Path $RunDirectory 'source-before.json') -Raw | ConvertFrom-Json
    $after = Get-Content -LiteralPath (Join-Path $RunDirectory 'source-after.json') -Raw | ConvertFrom-Json
    if ($before.sha256 -ne $after.sha256) { throw 'Source inputs changed during mutation setup verification.' }
    @{
        format='exportaramus-mutation-setup'; status='Ready'; mutationExecuted=$false; gatePassed=$false
        runnerVersion=$runnerVersion; sourceSha256=$before.sha256; inputsUnchanged=$true
        campaignCount=$campaigns.Count; concurrency=$Concurrency; baselines=$baselines
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $RunDirectory 'setup-verification.json') -Encoding utf8NoBOM
    Write-Output 'Mutation setup verified. No mutants were executed; this is not a passing mutation campaign.'
    return
}

@{
    format='exportaramus-mutation-process'; schemaVersion='1.1'; wrapperProcessId=$PID
    startedAtUtc=[DateTime]::UtcNow.ToString('O'); executable=$dotnet; runner=$runner
    campaigns=$campaigns
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $RunDirectory 'process.json') -Encoding utf8NoBOM
$executions = @()
$strykerExit = 0
$auditExit = -1
try {
    foreach ($campaign in $campaigns) {
        $outputDirectory = Join-Path $RunDirectory $campaign.name
        [void][IO.Directory]::CreateDirectory($outputDirectory)
        $arguments = @('--config-file', $campaign.config, '--concurrency', [string]$Concurrency, '--output', $outputDirectory, '--skip-version-check', '--verbosity', 'info', '--log-to-file', '--break-on-initial-test-failure')
        @{
            format = 'exportaramus-mutation-process'; schemaVersion = '1.0'; wrapperProcessId = $PID
            startedAtUtc = [DateTime]::UtcNow.ToString('O'); executable = $dotnet; runner = $runner
            arguments = $arguments; workingDirectory = $campaign.workingDirectory
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputDirectory 'process.json') -Encoding utf8NoBOM
        Write-Output "Mutation campaign: $($campaign.name), $outputDirectory"
        Push-Location $campaign.workingDirectory
        try {
            & $dotnet $runner @arguments 2>&1 | Tee-Object -FilePath (Join-Path $outputDirectory 'console.log')
            $campaignExit = $LASTEXITCODE
            $executions += @{ name=$campaign.name; exitCode=$campaignExit }
            if ($campaignExit -ne 0) { $strykerExit = $campaignExit }
        }
        finally { Pop-Location }
    }
    ConvertTo-Json -InputObject @($executions) -Depth 5 | Set-Content -LiteralPath (Join-Path $RunDirectory 'executions.json') -Encoding utf8NoBOM
    & $python $helper combine --run-dir $RunDirectory
    if ($LASTEXITCODE -ne 0) { $strykerExit = 1 }
}
finally {
    & (Join-Path $repository 'tools/Get-SourceInventory.ps1') -Destination (Join-Path $RunDirectory 'source-after.json')
    & $python $helper finish --run-dir $RunDirectory --exit-code $strykerExit
    $auditExit = $LASTEXITCODE
}
if ($strykerExit -ne 0 -or $auditExit -ne 0) {
    throw "Mutation gate failed. Review $RunDirectory/mutation-campaign-summary.json and unresolved-mutants.json."
}
