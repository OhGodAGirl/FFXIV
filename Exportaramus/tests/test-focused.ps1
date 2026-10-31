param([string]$Filter = 'FullyQualifiedName~Snapshots', [string]$Configuration = 'Release', [string]$Dotnet = 'dotnet', [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. "$projectRoot/tools/VerificationEnvironment.ps1"
$dotnetTool = Resolve-VerificationTool -Command $Dotnet
$run = New-VerificationRun -ProjectRoot $projectRoot -OutputDirectory $OutputDirectory -Name 'focused'
& $dotnetTool test "$PSScriptRoot/Exportaramus.Tests/Exportaramus.Tests.csproj" -c $Configuration --no-restore --filter $Filter --results-directory $run --logger 'trx;LogFileName=focused.trx'
Assert-VerificationExit 'Focused tests'
$null = Read-VerificationTests -Trx (Join-Path $run 'focused.trx')
