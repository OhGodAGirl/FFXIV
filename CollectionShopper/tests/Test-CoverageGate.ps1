$ErrorActionPreference = 'Stop'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path (Split-Path -Parent $PSScriptRoot) 'tools/Test-Repository.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'Quality script has parse errors.' }
foreach ($name in 'Normalize-RelativePath', 'Get-FileCoverage') {
    $definitions = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($definitions.Count -ne 1) { throw "Expected one coverage function: $name" }
    . ([scriptblock]::Create($definitions[0].Extent.Text))
}

function Get-FixtureCoverage([string]$Lines, [string]$Path = 'src/Fixture.cs') {
    [xml]$fixture = "<class filename='src/Fixture.cs'><lines>$Lines</lines></class>"
    Get-FileCoverage -Classes @($fixture.class) -ExpectedPath $Path
}

$full = Get-FixtureCoverage '<line number="1" hits="1" branch="True" condition-coverage="100% (2/2)"><conditions><condition coverage="100%" /></conditions></line>'
if ($full.LineCoverage -ne 100 -or $full.BranchCoverage -ne 100) { throw 'Full coverage was rejected.' }
$partial = Get-FixtureCoverage '<line number="1" hits="1" branch="True" condition-coverage="50% (1/2)"><conditions><condition coverage="50%" /></conditions></line>'
if ($partial.BranchCoverage -ne 50) { throw 'Bare condition percentages hid an uncovered branch.' }
$rounded = Get-FixtureCoverage '<line number="1" hits="1" branch="True" condition-coverage="99.99% (99999/100000)" />'
if ($rounded.BranchCoverage -ge 100) { throw 'Partial branch coverage rounded to full coverage.' }
$noBranch = Get-FixtureCoverage '<line number="1" hits="0" branch="False" />'
if ($noBranch.LineCoverage -ne 0 -or $noBranch.BranchCoverage -ne 100) { throw 'Line-only coverage was misread.' }

$invalid = @(
    '<line number="1" hits="1" branch="True" />',
    '<line number="1" hits="1" branch="True" condition-coverage="100%" />',
    '<line number="1" hits="1" branch="True" condition-coverage="100% (0/0)" />',
    '<line number="1" hits="1" branch="True" condition-coverage="100% (3/2)" />',
    '<line number="1" hits="1" branch="True" condition-coverage="100% (2/2)" /><line number="1" hits="1" branch="True" condition-coverage="50% (1/2)" />',
    ''
)
foreach ($fixture in $invalid) {
    $rejected = $false
    try { Get-FixtureCoverage $fixture | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Malformed coverage was admitted: $fixture" }
}
$missingRejected = $false
try { Get-FixtureCoverage '<line number="1" hits="1" />' 'src/Missing.cs' | Out-Null } catch { $missingRejected = $true }
if (-not $missingRejected) { throw 'Missing source coverage was admitted.' }
Write-Host 'Coverage gate contracts passed: 11 cases.'
