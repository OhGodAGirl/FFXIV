param([ValidateSet('Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = @(Get-Command dotnet -CommandType Application -ErrorAction Stop)[0].Source
Push-Location $root
try {
    & ./tests/test-repository.ps1 -Configuration $Configuration
    & ./tests/test-mutation.ps1 -Configuration $Configuration
    # Restore normal artifacts after mutation instrumentation and verify the final payload.
    & $dotnet build BetterGlamorPlates.slnx -c $Configuration --no-restore -t:Rebuild
    if ($LASTEXITCODE -ne 0) { throw 'Post-mutation rebuild failed.' }
    & $dotnet test BetterGlamorPlates.slnx -c $Configuration --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Post-mutation tests failed.' }
    & ./tests/test-smoke.ps1 -BundlePath (Join-Path $root 'bin/Release') -PackagePath (Join-Path $root 'bin/Release/BetterGlamorPlates/latest.zip')
    Write-Output 'Portable offline quality checks passed. Artwork, in-game acceptance and publication remain separate.'
}
finally { Pop-Location }
