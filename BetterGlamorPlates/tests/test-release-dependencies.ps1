[CmdletBinding()]
param([string]$PluginPath = (Split-Path -Parent $PSScriptRoot), [string]$ProviderRoot)
$ErrorActionPreference = 'Stop'
$provenance = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'release-dependencies.json') -Raw | ConvertFrom-Json
$contractProject = Join-Path $PluginPath 'project/Contracts/1.2'
$manifest = Join-Path $contractProject 'SHA256SUMS.original'
if ((Get-FileHash -LiteralPath $manifest).Hash -ne $provenance.kitManifestSha256) { throw 'Provider manifest differs from pinned provenance.' }
$originals = @{}
foreach ($line in Get-Content -LiteralPath $manifest) {
    if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') { throw 'Invalid provider manifest entry.' }
    if ($originals.ContainsKey($Matches[2])) { throw 'Duplicate provider manifest entry.' }
    $originals[$Matches[2]] = $Matches[1]
}
if ($originals.Count -ne $provenance.kitFileCount -or $provenance.providerFiles.Count -ne $originals.Count) { throw 'Provider inventory count differs.' }
$retained = @(Get-Content -LiteralPath (Join-Path $contractProject 'retained-files.json') -Raw | ConvertFrom-Json)
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$root = [IO.Path]::GetFullPath($PluginPath).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($entry in $retained) {
    if (-not $seen.Add($entry.originalPath) -or -not $paths.Add($entry.path)) { throw 'Duplicate retained mapping.' }
    $path = [IO.Path]::GetFullPath((Join-Path $PluginPath $entry.path))
    if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Retained path escapes source root.' }
    if ($originals[$entry.originalPath] -ne $entry.sha256 -or (Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256) { throw "Retained pin mismatch: $($entry.path)" }
}
$expected = @($originals.Keys | Where-Object { $_ -match '^(src/Contracts/1[.]2/|data/Contracts/1[.]2/|tests/Exportaramus[.]ConsumerKit[.]Tests/fixtures/)' } | Sort-Object)
if (@(Compare-Object $expected @($seen | Sort-Object)).Count) { throw 'Retained inventory is incomplete.' }
$providerSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $provenance.providerFiles) {
    if (-not $providerSeen.Add($entry.file) -or $originals[$entry.file] -ne $entry.sha256) { throw 'Provider provenance differs from original inventory.' }
    if ($ProviderRoot -and (Get-FileHash -LiteralPath (Join-Path $ProviderRoot $entry.file)).Hash -ne $entry.sha256) { throw "Provider checkout differs: $($entry.file)" }
}
Write-Output "ConsumerKit 1.2: $($retained.Count) retained files verified against $($originals.Count) original manifest entries."
Write-Output 'Live IPC and native game behavior require in-game acceptance.'
