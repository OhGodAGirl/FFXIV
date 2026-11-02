[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReportPath,
    [string]$EquivalencePath = (Join-Path $PSScriptRoot 'mutation-equivalences.json'),
    [string]$SourceRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
$review = Get-Content -LiteralPath $EquivalencePath -Raw | ConvertFrom-Json
if ($review.schemaVersion -ne 1) { throw 'Invalid equivalence schema.' }
$rows = @()
foreach ($file in $report.files.PSObject.Properties) {
    foreach ($mutant in $file.Value.mutants) {
        $rows += [pscustomobject]@{ File = $file.Name; Source = $file.Value.source; Mutant = $mutant }
    }
}
if ($rows.Count -eq 0) { throw 'Empty mutation report.' }
$known = @('Killed', 'Survived', 'NoCoverage', 'Timeout', 'CompileError', 'Ignored')
if (@($rows | Where-Object { $_.Mutant.status -notin $known }).Count) { throw 'Unfinished or unknown mutant status.' }
$approved = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $review.entries) {
    if ([string]::IsNullOrWhiteSpace($entry.rationale) -or [string]::IsNullOrWhiteSpace($entry.reviewer)) {
        throw 'Equivalence requires a proof and independent reviewer.'
    }
    $root = [IO.Path]::GetFullPath($SourceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $source = [IO.Path]::GetFullPath((Join-Path $root $entry.path))
    if (-not $source.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Equivalence source escaped project.'
    }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.sourceSha256) {
        throw "Stale equivalence source: $($entry.path)"
    }
    $matches = @($rows | Where-Object {
        (Split-Path -Leaf $_.File) -eq (Split-Path -Leaf $entry.path) -and
        $_.Mutant.mutatorName -ceq $entry.mutator -and $_.Mutant.replacement -ceq $entry.replacement -and
        $_.Mutant.location.start.line -eq $entry.startLine -and $_.Mutant.location.start.column -eq $entry.startColumn -and
        $_.Mutant.location.end.line -eq $entry.endLine -and $_.Mutant.location.end.column -eq $entry.endColumn
    })
    if ($matches.Count -ne 1) { throw "Equivalence must match exactly one mutant: $($entry.path)" }
    $match = $matches[0]
    $localText = ([IO.File]::ReadAllText($source)).Replace("`r`n", "`n").TrimStart([char]0xFEFF)
    $reportText = ([string]$match.Source).Replace("`r`n", "`n").TrimStart([char]0xFEFF)
    if ($localText -cne $reportText) { throw 'Mutation source does not match reviewed source.' }
    if ($match.Mutant.status -notin @('Survived', 'Killed')) { throw 'Equivalence cannot excuse untested or timed-out mutants.' }
    if (-not $approved.Add($match.File + ':' + $match.Mutant.id)) { throw 'Duplicate equivalence approval.' }
}
$killed = @($rows | Where-Object { $_.Mutant.status -eq 'Killed' }).Count
$survived = @($rows | Where-Object { $_.Mutant.status -eq 'Survived' }).Count
$equivalent = @($rows | Where-Object { $_.Mutant.status -eq 'Survived' -and $approved.Contains($_.File + ':' + $_.Mutant.id) }).Count
$uncovered = @($rows | Where-Object { $_.Mutant.status -eq 'NoCoverage' }).Count
$timeout = @($rows | Where-Object { $_.Mutant.status -eq 'Timeout' }).Count
$scored = $killed + $survived + $uncovered + $timeout
if ($scored -eq 0 -or $killed -eq 0) { throw 'No killed/scored mutants.' }
$rawScore = [math]::Round(100 * $killed / $scored, 2)
Write-Output "Raw mutation score: $rawScore%; killed=$killed, survived=$survived, reviewed-equivalent=$equivalent, timeout=$timeout, uncovered=$uncovered."
if ($survived -ne $equivalent -or $uncovered -ne 0 -or $timeout -ne 0) {
    throw 'Valid mutation gate failed: unapproved survivors, uncovered or timed-out mutants remain.'
}
Write-Output 'Valid mutation gate passed: 100%; zero behavior-changing survivors, uncovered or timed-out mutants.'
