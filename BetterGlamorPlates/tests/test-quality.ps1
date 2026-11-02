param([ValidateSet('Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'run-quality.ps1') -Configuration $Configuration
