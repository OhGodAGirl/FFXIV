param([string]$Destination)
$ErrorActionPreference = 'Stop'
$version = '15.0.3.9'
$revision = '261006903b8fd45513910a5d8a072bafe15e7f28'
$expected = '3e11e262426747b939bd8e1164a0f7d18bfda2dcc35b3f3ee7ee9fc7df78d2d9'
if (!$Destination) { $Destination = Join-Path (Split-Path $PSScriptRoot -Parent) ".tools/dalamud/$version" }
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) { throw 'Use a new destination directory for the verified build dependencies, or set DALAMUD_HOME to an existing installation.' }
$archive = Join-Path ([IO.Path]::GetTempPath()) ('better-glamour-plates-dalamud-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/goatcorp/dalamud-distrib/$revision/latest.zip" -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Dalamud archive checksum does not match the pinned build dependencies.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $destinationPath
    if (!(Test-Path -LiteralPath (Join-Path $destinationPath 'Dalamud.dll'))) { throw 'The downloaded archive is missing Dalamud.dll.' }
    $env:DALAMUD_HOME = $destinationPath
    Write-Output "DALAMUD_HOME=$destinationPath"
}
finally {
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
}
