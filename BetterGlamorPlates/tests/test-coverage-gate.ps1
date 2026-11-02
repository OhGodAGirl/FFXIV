$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'coverage-gate.ps1')

$fixtureRoot = Join-Path $PSScriptRoot ('TestResults/coverage-gate-contract-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'src') -Force
$null = New-Item -ItemType File -Path (Join-Path $fixtureRoot 'src/Included.cs')
$null = New-Item -ItemType File -Path (Join-Path $fixtureRoot 'src/Excluded.cs')
$manifestPath = Join-Path $fixtureRoot 'manifest.json'
$reportPath = Join-Path $fixtureRoot 'coverage.xml'
$script:passed = 0

function New-Manifest {
    return [pscustomobject]@{
        minimumLineCoverage = 100
        minimumBranchCoverage = 100
        includedProductionFiles = @('src/Included.cs')
        excludedProductionFiles = @([pscustomobject]@{ path = 'src/Excluded.cs'; reason = 'Synthetic adapter.' })
    }
}

function Assert-Rejected([scriptblock]$Action, [string]$Expected) {
    $caught = $false
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        $caught = $true
    }
    if (-not $caught) { throw "Coverage gate incorrectly accepted: $Expected" }
    $script:passed++
}

function Save-Manifest($Manifest) {
    [IO.File]::WriteAllText($manifestPath, ($Manifest | ConvertTo-Json -Depth 8))
}

function Save-Report([string]$Lines, [string]$Filename = 'src/Included.cs', [int]$Valid = 1) {
    [IO.File]::WriteAllText($reportPath,
        "<coverage lines-valid='$Valid'><packages><package><classes><class filename='$Filename'><lines>$Lines</lines></class></classes></package></packages></coverage>")
}

try {
    $manifest = New-Manifest
    Save-Manifest $manifest
    $validated = Test-CoverageInventory -Root $fixtureRoot -ManifestPath $manifestPath
    Save-Report "<line number='1' hits='1' branch='true' condition-coverage='100% (2/2)'/>"
    Test-CoverageReports -CoverageManifest $validated -ReportPaths @($reportPath)
    $script:passed++

    Assert-Rejected { Test-CoverageReports $manifest @() } 'No coverage reports'
    [IO.File]::WriteAllText($reportPath, '<coverage lines-valid="0"/>')
    Assert-Rejected { Test-CoverageReports $manifest @($reportPath) } 'no valid lines'
    Save-Report "<line number='1' hits='1'/>" 'src/Other.cs'
    Assert-Rejected { Test-CoverageReports $manifest @($reportPath) } 'missing included production file'
    Save-Report ''
    Assert-Rejected { Test-CoverageReports $manifest @($reportPath) } 'no valid lines'
    Save-Report "<line number='1' hits='0'/>"
    Assert-Rejected { Test-CoverageReports $manifest @($reportPath) } 'Line coverage'
    Save-Report "<line number='1' hits='1' branch='true' condition-coverage='50% (1/2)'/>"
    Assert-Rejected { Test-CoverageReports $manifest @($reportPath) } 'Branch coverage'
    Save-Report "<line number='1' hits='1' branch='true'/>"
    Assert-Rejected { Test-CoverageReports $manifest @($reportPath) } 'branch counts are invalid'

    # Both display as 100.00% when rounded. They must still fail exact-count checks.
    Save-Report "<line number='1' hits='1' branch='true' condition-coverage='99.995% (20000/20001)'/>"
    Assert-Rejected { Test-CoverageReports $manifest @($reportPath) } 'Branch coverage'
    $lines = [Text.StringBuilder]::new()
    for ($index = 1; $index -le 20001; $index++) {
        $hits = if ($index -eq 20001) { 0 } else { 1 }
        $null = $lines.Append("<line number='$index' hits='$hits'/>")
    }
    Save-Report $lines.ToString() -Valid 20001
    Assert-Rejected { Test-CoverageReports $manifest @($reportPath) } 'Line coverage'

    foreach ($case in 'unclassified', 'duplicateIncluded', 'duplicateExcluded', 'overlap', 'missing', 'reason', 'threshold') {
        $changed = New-Manifest
        $expected = switch ($case) {
            'unclassified' { $changed.excludedProductionFiles = @(); 'missing from coverage inventory' }
            'duplicateIncluded' { $changed.includedProductionFiles += 'src/Included.cs'; 'duplicate paths' }
            'duplicateExcluded' { $changed.excludedProductionFiles += $changed.excludedProductionFiles[0]; 'duplicate paths' }
            'overlap' { $changed.includedProductionFiles += 'src/Excluded.cs'; 'both included and excluded' }
            'missing' { $changed.includedProductionFiles += 'src/Missing.cs'; 'missing production file' }
            'reason' { $changed.excludedProductionFiles[0].reason = ' '; 'narrow reason' }
            'threshold' { $changed.minimumLineCoverage = 0; 'explicit 100 percent' }
        }
        Save-Manifest $changed
        Assert-Rejected { Test-CoverageInventory -Root $fixtureRoot -ManifestPath $manifestPath } $expected
    }
    # Historical kit and provider test/example sources do not ship in the plugin.
    foreach ($relative in @('github/OtherPlugin/src/Runtime.cs',
        'reference/ConsumerKit/1.1/OldClient.cs',
        'reference/ConsumerKit/1.2/tests/ProviderTests.cs',
        'reference/ConsumerKit/1.2/examples/CollectionExample/Plugin.cs')) {
        $path = Join-Path $fixtureRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        New-Item -ItemType File -Path $path | Out-Null
    }
    Save-Manifest (New-Manifest)
    Test-CoverageInventory -Root $fixtureRoot -ManifestPath $manifestPath | Out-Null
    $script:passed++

    # A new current contract, plugin source, or unknown reference cannot disappear.
    foreach ($relative in @('src/Contracts/1.2/NewContract.cs',
        'src/NewDecision.cs', 'reference/Unexpected/Runtime.cs')) {
        $path = Join-Path $fixtureRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        New-Item -ItemType File -Path $path | Out-Null
        Assert-Rejected { Test-CoverageInventory -Root $fixtureRoot -ManifestPath $manifestPath } 'missing from coverage inventory'
        Remove-Item -LiteralPath $path
    }
    Write-Host "Coverage gate contracts passed: $script:passed fixtures."
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $expectedParent = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'TestResults'))
    if ([IO.Path]::GetDirectoryName($resolved) -ne $expectedParent -or
        -not [IO.Path]::GetFileName($resolved).StartsWith('coverage-gate-contract-')) {
        throw 'Unsafe coverage contract cleanup path.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

