param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Exportaramus.ConsumerKit.Tests.csproj'
& dotnet test $project -c $Configuration -p:RestoreLockedMode=true --logger 'trx;LogFileName=consumer-kit.trx'
if ($LASTEXITCODE -ne 0) { throw 'Consumer tests failed.' }
