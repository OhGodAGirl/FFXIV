#Requires -Version 7.0
[CmdletBinding()]
param([string]$Dotnet = 'dotnet')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Verify-ConsumerKit.ps1') -Root $root
Push-Location $root
try {
    & $Dotnet restore EZDelivery.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked plugin restore failed.' }
    & $Dotnet build EZDelivery.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    Write-Output 'Build complete. Output: bin/Release. No staging or game action was performed.'
}
finally { Pop-Location }
