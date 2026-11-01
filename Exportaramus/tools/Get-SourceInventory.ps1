#Requires -Version 7.2
param([string]$Destination = "$PSScriptRoot/../development/docs/source-inventory.json")
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$destinationPath = [IO.Path]::GetFullPath($Destination)
$roots = @('src','tests','data','images','tools','project','examples','docs','.config','.github')
$excludedDirectories = @('bin','obj','TestResults','__pycache__','.git','.vs','node_modules','development')
$entries = [Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal)
function Add-SourceFile([IO.FileInfo]$File) {
    if (($File.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $File.FullName -eq $destinationPath -or
        $File.Name -eq 'AGENTS.md' -or $File.Extension -in @('.tmp','.bak','.pyc','.pyo','.user','.suo','.trx','.binlog','.cache')) { return }
    $relative = [IO.Path]::GetRelativePath($projectRoot, $File.FullName).Replace('\','/')
    $entries.Add($relative, (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash.ToLowerInvariant())
}
foreach ($file in Get-ChildItem -LiteralPath $projectRoot -File -Force) {
    if ($file.Extension -notin @('.dll','.pdb','.zip','.nupkg')) { Add-SourceFile $file }
}
foreach ($root in $roots) {
    $directory = Join-Path $projectRoot $root
    if (!(Test-Path -LiteralPath $directory -PathType Container)) { continue }
    if (((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($directory)
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            # Do not follow links or junctions.
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            if ($entry.PSIsContainer) {
                if ($entry.Name -notin $excludedDirectories) { $pending.Push($entry.FullName) }
            } else { Add-SourceFile $entry }
        }
    }
}
$inventory = @($entries.GetEnumerator() | ForEach-Object { [ordered]@{ path=$_.Key; sha256=$_.Value } })
$canonical = ($inventory | ForEach-Object { $_.sha256 + '  ' + $_.path }) -join "`n"
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
$result = [ordered]@{ format='exportaramus-source-inventory'; version=1; sha256=$hash; files=$inventory }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Destination)))
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Destination -Encoding utf8
Write-Output $hash
