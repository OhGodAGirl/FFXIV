function Resolve-VerificationTool {
    param([Parameter(Mandatory)][string]$Command)
    $tool = Get-Command -Name $Command -CommandType Application,ExternalScript -ErrorAction Stop | Select-Object -First 1
    return $tool.Source
}

function New-VerificationRun {
    param([Parameter(Mandatory)][string]$ProjectRoot, [string]$OutputDirectory, [string]$Name = 'verification')
    if (!$OutputDirectory) { $OutputDirectory = 'development/verification' }
    if (![IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $ProjectRoot $OutputDirectory }
    $root = [IO.Path]::GetFullPath($OutputDirectory)
    $run = Join-Path $root ($Name + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($run)
    return $run
}

function Assert-VerificationExit {
    param([string]$Name = 'Tool')
    if ($LASTEXITCODE -ne 0) { throw "$Name exited with code $LASTEXITCODE." }
}

function Assert-VerificationOutcome {
    param([Parameter(Mandatory)][string]$Receipt)
    $outcome = Get-Content -LiteralPath $Receipt -Raw | ConvertFrom-Json
    if ($outcome.status -ne 'Passed' -or !$outcome.inputsUnchanged) {
        throw "Offline verification did not pass; inspect the preserved receipt: $Receipt"
    }
}

function Read-VerificationTests {
    param([Parameter(Mandatory)][string]$Trx)
    [xml]$result = Get-Content -LiteralPath $Trx -Raw
    if ($result.DocumentElement.LocalName -ne 'TestRun') { throw "TRX root must be TestRun: $Trx" }
    $summaries = @($result.DocumentElement.SelectNodes("./*[local-name()='ResultSummary']"))
    $results = @($result.DocumentElement.SelectNodes("./*[local-name()='Results']"))
    if ($summaries.Count -ne 1 -or $results.Count -ne 1) { throw "TRX requires exactly one direct summary and results collection: $Trx" }
    $summary = $summaries[0]
    $counterSets = @($summary.SelectNodes("./*[local-name()='Counters']"))
    if ($counterSets.Count -ne 1 -or $summary.GetAttribute('outcome') -ne 'Completed') { throw "TRX requires one completed summary with one counter set: $Trx" }
    $counts = $counterSets[0]
    $incompleteCounters = @('failed','error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','notExecuted','disconnected','warning','inProgress','pending')
    foreach ($name in (@('total','executed','passed') + $incompleteCounters)) {
        if (!$counts.HasAttribute($name) -or $counts.GetAttribute($name) -notmatch '^\d+$') { throw "Missing or malformed TRX counter ${name}: $Trx" }
    }
    $rows = @($results[0].SelectNodes("./*[local-name()='UnitTestResult']"))
    $incomplete = @($rows | Where-Object { $_.GetAttribute('outcome') -ne 'Passed' }).Count -ne 0
    foreach ($name in $incompleteCounters) {
        if ([long]$counts.GetAttribute($name) -ne 0) { $incomplete = $true }
    }
    if ($incomplete -or [long]$counts.total -le 0 -or [long]$counts.total -ne [long]$counts.executed -or [long]$counts.total -ne [long]$counts.passed -or [long]$counts.total -ne $rows.Count) {
        throw "TRX totals, executed/passed counts and result rows must agree without failures or skips: $Trx"
    }
    return [ordered]@{total=[long]$counts.total;passed=[long]$counts.passed;failed=[long]$counts.failed;skipped=[long]$counts.notExecuted}
}

function Read-VerificationCoverage {
    param([Parameter(Mandatory)][string]$Run, [Parameter(Mandatory)][string]$Trx, [string]$TypePrefix)
    $tests = Read-VerificationTests -Trx $Trx
    [xml]$testResult = Get-Content -LiteralPath $Trx -Raw
    $attachment = @($testResult.SelectNodes("//*[local-name()='UriAttachment']/*[local-name()='A']") | Where-Object { $_.href -like '*coverage.cobertura.xml' })
    if ($attachment.Count -ne 1) { throw 'Exactly one coverage attachment is required in the fresh TRX result.' }
    $deployment = $testResult.SelectSingleNode("//*[local-name()='TestSettings']/*[local-name()='Deployment']")
    if ($null -eq $deployment -or !$deployment.GetAttribute('runDeploymentRoot')) { throw 'The fresh TRX has no deployment root for its coverage attachment.' }
    $href = $attachment[0].GetAttribute('href').Replace('\', [IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::IsPathRooted($href)) { throw 'Coverage attachment must be relative to the fresh deployment.' }
    $runRoot = [IO.Path]::GetFullPath($Run).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $deploymentDirectory = Join-Path $runRoot ($deployment.GetAttribute('runDeploymentRoot') + '/In')
    $report = [IO.Path]::GetFullPath((Join-Path $deploymentDirectory $href))
    if (!$report.StartsWith($runRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Coverage attachment escaped its fresh run directory.' }
    if (!(Test-Path -LiteralPath $report -PathType Leaf)) { throw 'The exact TRX coverage attachment is missing.' }
    $reports = @(Get-ChildItem -LiteralPath $Run -Recurse -Filter 'coverage.cobertura.xml' -File)
    if ($reports.Count -eq 0) { throw 'The fresh run has no coverage report.' }
    $hashes = @($reports | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } | Select-Object -Unique)
    if ($hashes.Count -ne 1) { throw 'The collector and TRX attachment copies disagree.' }
    [xml]$coverage = Get-Content -LiteralPath $report -Raw
    if ($TypePrefix) {
        $classes = @($coverage.SelectNodes("//*[local-name()='class']") | Where-Object { $_.name -eq $TypePrefix -or $_.name.StartsWith($TypePrefix + '/') })
        if (@($classes | Where-Object name -CEQ $TypePrefix).Count -ne 1) { throw "Coverage must represent the exact required component once: $TypePrefix" }
        $lines = 0L; $branches = 0L; $coveredLines = 0L; $coveredBranches = 0L
        foreach ($type in $classes) {
            foreach ($line in $type.SelectNodes("./*[local-name()='lines']/*[local-name()='line']")) {
                $lines++; if ([long]$line.hits -gt 0) { $coveredLines++ }
                if ($line.branch -eq 'true') {
                    if ($line.'condition-coverage' -notmatch '^\d+(?:\.\d+)?% \((\d+)/(\d+)\)$' -or [long]$Matches[2] -le 0 -or [long]$Matches[1] -gt [long]$Matches[2]) { throw "Malformed branch counts for component: $TypePrefix" }
                    $coveredBranches += [long]$Matches[1]; $branches += [long]$Matches[2]
                }
            }
        }
    }
    else {
        $lines = [long]$coverage.coverage.'lines-valid'; $coveredLines = [long]$coverage.coverage.'lines-covered'
        $branches = [long]$coverage.coverage.'branches-valid'; $coveredBranches = [long]$coverage.coverage.'branches-covered'
    }
    if ($lines -le 0 -or $branches -le 0 -or $lines -ne $coveredLines -or $branches -ne $coveredBranches) {
        throw "Strict coverage requires positive, complete line and branch counts: $TypePrefix ($coveredLines/$lines lines, $coveredBranches/$branches branches)."
    }
    return [ordered]@{linesCovered=$coveredLines;linesValid=$lines;branchesCovered=$coveredBranches;branchesValid=$branches;report=$report;reportSha256=$hashes[0];trx=[IO.Path]::GetFullPath($Trx);trxSha256=(Get-FileHash -LiteralPath $Trx -Algorithm SHA256).Hash.ToLowerInvariant();tests=$tests;component=$TypePrefix}
}
