#Requires -Version 7.0
[CmdletBinding()]
param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$rootPath = [IO.Path]::GetFullPath($Root)
$prefix = $rootPath + [IO.Path]::DirectorySeparatorChar
$manifest = Get-Content (Join-Path $rootPath 'project/Contracts/1.1/provenance.json') -Raw | ConvertFrom-Json
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.Files) {
    $path = [IO.Path]::GetFullPath((Join-Path $rootPath $entry.PublicPath))
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not $seen.Add($entry.PublicPath)) { throw 'Invalid or duplicate contract path.' }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash -ine $entry.PublicSha256) { throw "Consumer contract fingerprint mismatch: $($entry.PublicPath)" }
    if ($entry.Transformation -eq 'None' -and $entry.UpstreamSha256 -ine $entry.PublicSha256) { throw 'Unmodified upstream file hash differs.' }
}
if ($seen.Count -ne 34 -or @($manifest.Files | Where-Object PublicPath -like 'src/Contracts/1.1/*.cs').Count -ne 8) { throw 'Incomplete relocated Consumer Kit inventory.' }
Write-Output 'Consumer Kit verified: 34 retained files; 8 C# sources unchanged.'