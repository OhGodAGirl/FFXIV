#Requires -Version 7.0
[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [switch]$IncludeHost)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Verify-ConsumerKit.ps1') -Root $root
$projects = @('tests/EZDelivery.Tests/EZDelivery.Tests.csproj')
if ($IncludeHost) { $projects += 'tests/EZDelivery.HostTests/EZDelivery.HostTests.csproj' }
$run = Join-Path $root ('TestResults/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N'))
Push-Location $root
try {
    foreach ($project in $projects) {
        & $Dotnet restore $project --locked-mode
        if ($LASTEXITCODE -ne 0) { throw "Locked restore failed: $project" }
        $name = [IO.Path]::GetFileNameWithoutExtension($project)
        $results = Join-Path $run $name
        & $Dotnet test $project -c Release --no-restore --logger 'trx;LogFileName=tests.trx' --results-directory $results
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
        [xml]$trx = Get-Content -LiteralPath (Join-Path $results 'tests.trx') -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        if ([int]$counters.total -le 0 -or [int]$counters.passed -ne [int]$counters.total) {
            throw "Every test must execute and pass: $project"
        }
    }
    Write-Output "Tests passed. Results: $run. No live game tests were performed."
}
finally { Pop-Location }
