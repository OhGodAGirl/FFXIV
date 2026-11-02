$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Initialize-CoverageSourceMap.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Coverage source-map script parse failure.' }
$function = @($ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'ConvertTo-CoverageSourceMap'
}, $true))
if ($function.Count -ne 1) { throw 'Expected one coverage source-map conversion.' }
. ([scriptblock]::Create($function[0].Extent.Text))
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$owned = "test.csproj|$root=$root"
$correct = "test.csproj|$root=/_/CollectionShopper/"
$other = 'other.csproj|D:\external\=/_/external/'
$mapped = @(ConvertTo-CoverageSourceMap @($owned, $other) $root)
if ($mapped.Count -ne 2 -or $mapped[0] -cne $correct -or $mapped[1] -cne $other) { throw 'Owned mapping correction changed an unrelated mapping.' }
if ((ConvertTo-CoverageSourceMap @($correct) $root) -cne $correct) { throw 'Coverage map correction is not idempotent.' }
$passed = 2
foreach ($invalid in @(@($other), @('malformed'), @("test.csproj|$root=/_/Unexpected/"))) {
    $rejected = $false
    try { ConvertTo-CoverageSourceMap $invalid $root | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Unsafe coverage source map was accepted.' }
    $passed++
}
Write-Host "Coverage source-map contracts passed: $passed cases."
