$ErrorActionPreference = 'Stop'
$pluginRoot = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $pluginRoot 'project/Contracts/1.2/SHA256SUMS'
$expected = 'b2b845fea10693f5a046057e5ab41fab5dfda97d9a6765f8c8a6932105caeb17'
if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -ne $expected) {
    throw 'ConsumerKit 1.2 build/test subset manifest identity changed.'
}
$files = @()
foreach ($line in Get-Content -LiteralPath $manifest) {
    if ($line -notmatch '^([a-fA-F0-9]{64})\s+\*?(.+)$') { throw 'Malformed ConsumerKit manifest line.' }
    $hash = $Matches[1]
    $relative = $Matches[2].Replace('\', '/')
    if ($relative -notmatch '^(src/Contracts/1\.2/|data/Contracts/1\.2/|tests/fixtures/Contracts/1\.2/fixtures/|project/Contracts/1\.2/)') {
        throw 'ConsumerKit entry is outside the versioned contract directories.'
    }
    $target = [IO.Path]::GetFullPath((Join-Path $pluginRoot $relative))
    if (-not $target.StartsWith([IO.Path]::GetFullPath($pluginRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'ConsumerKit entry escapes the source tree.'
    }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $hash) { throw "ConsumerKit source differs: $relative" }
    $files += $relative
}
$actual = @(foreach ($directory in @('src/Contracts/1.2', 'data/Contracts/1.2', 'tests/fixtures/Contracts/1.2','project/Contracts/1.2')) {
    Get-ChildItem -LiteralPath (Join-Path $pluginRoot $directory) -Recurse -File | Where-Object Name -ne 'SHA256SUMS' | ForEach-Object {
        [IO.Path]::GetRelativePath($pluginRoot, $_.FullName).Replace('\', '/')
    }
})
if ($files.Count -ne 107 -or @($files | Sort-Object -Unique).Count -ne 107 -or
    @(Compare-Object ($files | Sort-Object) ($actual | Sort-Object)).Count -ne 0) {
    throw 'ConsumerKit contains missing, duplicate, or unexpected files.'
}
Write-Host "ConsumerKit 1.2 build/test subset: all $($files.Count) pinned files match."
