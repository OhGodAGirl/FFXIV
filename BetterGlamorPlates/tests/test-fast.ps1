param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = @(Get-Command dotnet -CommandType Application -ErrorAction Stop)[0].Source

Push-Location $root
try {
    & $dotnet build '.\BetterGlamorPlates.slnx' -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

    & $dotnet test '.\BetterGlamorPlates.slnx' -c $Configuration --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
