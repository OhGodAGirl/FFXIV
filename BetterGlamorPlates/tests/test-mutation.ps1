param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = @(Get-Command dotnet -CommandType Application -ErrorAction Stop)[0].Source
$policyPath = Join-Path $PSScriptRoot 'mutation-policy.json'
$coverageManifestPath = Join-Path $PSScriptRoot 'coverage-manifest.json'
$manifestPath = Join-Path $root '.config\dotnet-tools.json'
$testProjectRoot = Join-Path $PSScriptRoot 'BetterGlamorPlates.Mutation.Tests'

$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
if ([int]$policy.schemaVersion -ne 2) {
    throw 'Mutation policy schemaVersion must be 2.'
}
if ($policy.status -ne 'required') {
    throw 'Mutation testing must remain required for every deterministic decision target.'
}
if ([double]$policy.minimumMutationScore -ne 100) {
    throw 'Mutation testing must require an exact 100 percent score.'
}
if (@($policy.reviewedSurvivors).Count -ne 0 -or
    @($policy.excludedDecisionFiles).Count -ne 0) {
    throw 'Strict mutation policy cannot retain survivors or excluded decision files.'
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$pinnedVersion = [string]$manifest.tools.'dotnet-stryker'.version
if ($pinnedVersion -ne [string]$policy.runnerVersion) {
    throw "Pinned Stryker version $pinnedVersion does not match mutation policy version $($policy.runnerVersion)."
}

foreach ($target in $policy.targets) {
    $targetPath = Join-Path $root ([string]$target)
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
        throw "Mutation target is missing: $target"
    }
}

$coverageManifest = Get-Content -LiteralPath $coverageManifestPath -Raw | ConvertFrom-Json
$expectedTargets = @($coverageManifest.includedProductionFiles | Sort-Object -Unique)
$actualTargets = @($policy.targets | ForEach-Object { [string]$_ } | Sort-Object -Unique)
if ($actualTargets.Count -ne $expectedTargets.Count -or
    @(Compare-Object -ReferenceObject $expectedTargets -DifferenceObject $actualTargets).Count -ne 0) {
    throw 'Mutation targets must exactly match the complete deterministic coverage inventory.'
}
if ([string]::IsNullOrWhiteSpace([string]$policy.targetInventoryProof)) {
    throw 'Mutation policy requires targetInventoryProof.'
}

function Get-MutationRows([string]$ReportPath) {
    $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
    foreach ($fileProperty in $report.files.PSObject.Properties) {
        foreach ($mutant in $fileProperty.Value.mutants) {
            [pscustomobject]@{
                File = [string]$fileProperty.Name
                Id = [int]$mutant.id
                Status = [string]$mutant.status
                Mutator = [string]$mutant.mutatorName
                Replacement = [string]$mutant.replacement
                Line = [int]$mutant.location.start.line
            }
        }
    }
}

function Invoke-StrykerRun(
    [string]$ConfigFile,
    [string]$OutputPath,
    [string]$ReportName) {
    $script:safeModeDetected = $false
    & $dotnet stryker `
        --config-file $ConfigFile `
        --configuration $Configuration `
        --output $OutputPath `
        --skip-version-check |
        ForEach-Object {
            if ([string]$_ -match "Safe Mode!.*remove all mutations") { $script:safeModeDetected = $true }
            Write-Host $_
        }
    $strykerExitCode = $LASTEXITCODE

    $reportPath = Join-Path $OutputPath "reports\$ReportName.json"
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "Stryker exited $strykerExitCode and did not produce a JSON report: $reportPath"
    }

    if ($strykerExitCode -notin @(0, 2)) {
        throw "Stryker exited $strykerExitCode. Report retained for diagnosis: $reportPath"
    }

    if ($ReportName -eq "mutation-report" -and $script:safeModeDetected) {
        throw "Mutation instrumentation dropped complete methods. Report retained: $reportPath"
    }
    return $reportPath
}

Push-Location $root
try {
    & $dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned local tool restore failed with exit code $LASTEXITCODE."
    }

    $runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $sourceHashes = @{}
    foreach ($target in $policy.targets) { $sourceHashes[[string]$target] = (Get-FileHash -LiteralPath (Join-Path $root $target) -Algorithm SHA256).Hash }
    $resultRoot = Join-Path $PSScriptRoot "TestResults\mutation\$runId"
    $probeOutput = Join-Path $resultRoot 'probe'
    $mainOutput = Join-Path $resultRoot 'main'

    Push-Location $testProjectRoot
    try {
        $probeReport = Invoke-StrykerRun `
            'stryker-probe-config.json' `
            $probeOutput `
            'probe-report'
        $probeRows = @(Get-MutationRows $probeReport)
        $probeKilled = @($probeRows | Where-Object Status -eq 'Killed').Count
        $probeUndetected = @($probeRows | Where-Object { $_.Status -notin @('Killed', 'CompileError', 'Ignored') }).Count
        if ($probeKilled -lt 1 -or $probeUndetected -ne 0) {
            throw "Mutation instrumentation proof failed: killed=$probeKilled, undetected=$probeUndetected."
        }

        $mainReport = Invoke-StrykerRun `
            'stryker-config.json' `
            $mainOutput `
            'mutation-report'
        $mainRows = @(Get-MutationRows $mainReport)
    }
    finally {
        Pop-Location
    }

    $completeReport = Get-Content -LiteralPath $mainReport -Raw | ConvertFrom-Json
    foreach ($target in $policy.targets) {
        $localPath = Join-Path $root $target
        if ((Get-FileHash -LiteralPath $localPath -Algorithm SHA256).Hash -ne $sourceHashes[[string]$target]) {
            throw "Mutation source changed during run: $target"
        }
        $reported = @($completeReport.files.PSObject.Properties | Where-Object {
            [IO.Path]::GetFullPath($_.Name) -eq [IO.Path]::GetFullPath($localPath)
        })
        if ($reported.Count -ne 1) { throw "Mutation report missing exact source path: $target" }
        $localText = [IO.File]::ReadAllText($localPath).Replace("`r`n", "`n").TrimStart([char]0xFEFF)
        $reportText = ([string]$reported[0].Value.source).Replace("`r`n", "`n").TrimStart([char]0xFEFF)
        if ($localText -cne $reportText) { throw "Mutation report source differs from target: $target" }
    }

    foreach ($target in $policy.targets) {
        $leaf = Split-Path -Leaf ([string]$target)
        if (-not ($mainRows.File | Where-Object { (Split-Path -Leaf $_) -eq $leaf })) {
            throw "Mutation report does not contain configured target: $target"
        }
    }

    $killed = @($mainRows | Where-Object Status -eq 'Killed').Count
    $survived = @($mainRows | Where-Object Status -eq 'Survived').Count
    $timeout = @($mainRows | Where-Object Status -eq 'Timeout').Count
    $noCoverage = @($mainRows | Where-Object Status -eq 'NoCoverage').Count
    $denominator = $killed + $survived + $timeout + $noCoverage
    if ($denominator -eq 0) {
        throw 'Mutation run produced no scored mutants.'
    }

    $score = [math]::Round(100.0 * $killed / $denominator, 2)
    & (Join-Path $PSScriptRoot 'test-mutation-report.ps1') -ReportPath $mainReport

    # Persist the exact successful campaign; never replace a failed run with a green receipt.
    $previousRun = $policy.lastRun
    if ($null -ne $previousRun) {
        if ($null -eq $policy.historicalEvidence) {
            $policy | Add-Member -NotePropertyName historicalEvidence -NotePropertyValue ([pscustomobject]@{})
        }
        $policy.historicalEvidence | Add-Member -NotePropertyName "before-$runId" -NotePropertyValue $previousRun
    }
    $policy.lastRun = [pscustomobject]@{
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        runnerVersion = $pinnedVersion
        instrumentationProbeKilled = $probeKilled
        instrumentationProbeUndetected = $probeUndetected
        score = 100.0 * $killed / $denominator
        validMutationScore = 100
        killed = $killed
        survived = $survived
        reviewedEquivalent = $survived
        timeout = $timeout
        noCoverage = $noCoverage
        compileError = @($mainRows | Where-Object Status -eq 'CompileError').Count
        ignored = @($mainRows | Where-Object Status -eq 'Ignored').Count
        report = [IO.Path]::GetRelativePath($root, $mainReport).Replace('\', '/')
        reportSha256 = (Get-FileHash -LiteralPath $mainReport -Algorithm SHA256).Hash
        equivalenceManifest = 'tests/mutation-equivalences.json'
        qualification = 'Exact-source report validated; raw score preserved; zero behavior-changing survivors, uncovered mutants or timeouts.'
    }
    $policy | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $policyPath -Encoding utf8

    Write-Host "Instrumentation proof: $probeKilled killed, 0 undetected."
    Write-Host "Mutation score: $score% ($killed killed, $survived survived, $timeout timed out, $noCoverage uncovered)."
    Write-Host "Report: $mainReport"
}
finally {
    Pop-Location
}
