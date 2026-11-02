param(
    [Parameter(Mandatory = $true)][string]$Filter,
    [string]$Project = '.\BetterGlamorPlates.slnx',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = @(Get-Command dotnet -CommandType Application -ErrorAction Stop)[0].Source

Push-Location $root
try {
    & $dotnet test $Project -c $Configuration --no-restore --filter $Filter
    if ($LASTEXITCODE -ne 0) { throw "Focused tests failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
