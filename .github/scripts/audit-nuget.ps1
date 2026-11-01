param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'Exportaramus'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$projects = @(git -C $root ls-files -- '*.csproj')
if ($LASTEXITCODE -ne 0 -or $projects.Count -eq 0) { throw 'No tracked projects found for audit.' }
$failed = @()
foreach ($relative in $projects) {
    $project = Join-Path $root $relative
    $log = Join-Path $OutputDirectory (($relative -replace '[/\\]', '_') + '.log')
    # All vulnerability levels and advisory-source errors fail, including transitive packages.
    & dotnet restore $project --locked-mode --force '-p:NuGetAudit=true' '-p:NuGetAuditMode=all' '-p:WarningsAsErrors=NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904%3BNU1905' *> $log
    $code = $LASTEXITCODE
    Get-Content -LiteralPath $log
    if ($code -ne 0) { $failed += $relative }
}
[ordered]@{ projects=$projects; failed=$failed; status=$(if ($failed.Count) { 'Failed' } else { 'Passed' }) } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'nuget-audit.json') -Encoding utf8
if ($failed.Count) { throw "NuGet audit failed for $($failed.Count) project(s); inspect the saved logs." }
