[CmdletBinding()]
param([string]$PluginPath = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PluginPath)
$pinHash = 'BEFC9209298BFD552E6361C8BDC5030DC2FBBF93464ACC3F9908DE0B05E16172'
$privateKit = Join-Path $root 'reference/ConsumerKit/1.2'
$privateLayout = Test-Path -LiteralPath $privateKit
$manifest = if ($privateLayout) { Join-Path $privateKit 'SHA256SUMS' } else { Join-Path $root 'data/Contracts/1.2/UPSTREAM_SHA256SUMS' }
if ((Get-FileHash -LiteralPath $manifest).Hash -cne $pinHash) { throw 'ConsumerKit manifest pin differs.' }
$entries = @{}
foreach ($line in Get-Content -LiteralPath $manifest) {
    if ($line -cnotmatch '^([0-9a-f]{64})  ([A-Za-z0-9_./-]+)$') { throw 'Malformed manifest entry.' }
    $hash = $Matches[1]; $path = $Matches[2]
    if ($path.Contains('..') -or [IO.Path]::IsPathRooted($path) -or $entries.ContainsKey($path)) { throw 'Unsafe or duplicate entry.' }
    $entries.Add($path, $hash)
}
if ($entries.Count -ne 147) { throw 'Unexpected upstream inventory size.' }
$verified = 0
if ($privateLayout) {
    foreach ($path in $entries.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $privateKit $path)).Hash.ToLowerInvariant() -cne $entries[$path]) { throw "ConsumerKit file differs: $path" }
        $verified++
    }
    if (@(Get-ChildItem $privateKit -Recurse -File).Count -ne 148) { throw 'Unexpected files in pinned kit.' }
}
else {
    $provenance = Get-Content (Join-Path $root 'data/Contracts/1.2/provenance.json') -Raw | ConvertFrom-Json
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $provenance.files) {
        if (!$seen.Add($entry.originalPath) -or !$entries.ContainsKey($entry.originalPath) -or $entries[$entry.originalPath] -cne $entry.sha256) { throw 'Provenance does not match upstream inventory.' }
        if ($entry.destination) {
            $path = [IO.Path]::GetFullPath((Join-Path $root $entry.destination))
            if (!$path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe provenance destination.' }
            if ((Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -cne $entry.sha256) { throw "Exported file differs: $($entry.destination)" }
            $verified++
        }
        elseif ([string]::IsNullOrWhiteSpace($entry.omission)) { throw 'Missing omission explanation.' }
    }
    if ($seen.Count -ne 147) { throw 'Provenance is incomplete.' }
}
Write-Host "ConsumerKit 1.2 pin verified: $verified retained upstream files."