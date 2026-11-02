param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
Push-Location $root
try {
    & $PSScriptRoot/Build-Package.ps1
    & $PSScriptRoot/Inspect-Package.ps1
    $package = Join-Path $root 'project/bin/Release/SpringCleaning/latest.zip'
    $before = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    & $dotnet build project/SpringCleaning.slnx -c Release --no-restore -t:Rebuild
    if ($LASTEXITCODE -ne 0) { throw 'Reproduction rebuild failed.' }
    & $PSScriptRoot/Build-Package.ps1
    & $PSScriptRoot/Inspect-Package.ps1
    if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $before) { throw 'Runtime package changed after an identical rebuild.' }
    Write-Host "Package reproduction verified: $before"
}
finally { Pop-Location }
