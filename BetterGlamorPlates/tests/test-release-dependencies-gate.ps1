$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('bgp-retained-gate-' + [Guid]::NewGuid().ToString('N'))
$expectedRoot = [IO.Path]::GetFullPath($fixtureRoot)
New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'tests'), (Join-Path $fixtureRoot 'project/Contracts/1.2'), (Join-Path $fixtureRoot 'src/Contracts/1.2') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test-release-dependencies.ps1') -Destination (Join-Path $fixtureRoot 'tests/test-release-dependencies.ps1')
function Reset-Fixture {
    $script:relative = 'src/Contracts/1.2/Contract.cs'
    Set-Content -LiteralPath (Join-Path $fixtureRoot $relative) -Value '// Fixture' -Encoding utf8
    $script:hash = (Get-FileHash -LiteralPath (Join-Path $fixtureRoot $relative)).Hash
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'project/Contracts/1.2/SHA256SUMS.original') -Value "$hash  $relative" -Encoding utf8
    $script:provenance = [ordered]@{kitManifestSha256=(Get-FileHash (Join-Path $fixtureRoot 'project/Contracts/1.2/SHA256SUMS.original')).Hash; kitFileCount=1; providerFiles=@([ordered]@{file=$relative;sha256=$hash})}
    $script:mappings = @([ordered]@{originalPath=$relative;path=$relative;sha256=$hash})
}
function Write-Fixture {
    $provenance | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $fixtureRoot 'tests/release-dependencies.json') -Encoding utf8
    ConvertTo-Json -InputObject @($mappings) -Depth 5 | Set-Content -LiteralPath (Join-Path $fixtureRoot 'project/Contracts/1.2/retained-files.json') -Encoding utf8
}
function Invoke-Fixture([string]$Name, [scriptblock]$Change, [bool]$MustFail) {
    Reset-Fixture
    & $Change
    Write-Fixture
    $failed = $false
    try { & (Join-Path $fixtureRoot 'tests/test-release-dependencies.ps1') -PluginPath $fixtureRoot | Out-Null } catch { $failed = $true }
    if ($failed -ne $MustFail) { throw "Unexpected retained-file gate result: $Name" }
}
try {
    Invoke-Fixture 'valid retained mapping' {} $false
    Invoke-Fixture 'wrong manifest pin' { $provenance.kitManifestSha256 = '0' * 64 } $true
    Invoke-Fixture 'wrong file count' { $provenance.kitFileCount = 2 } $true
    Invoke-Fixture 'changed payload' { Add-Content -LiteralPath (Join-Path $fixtureRoot $relative) -Value 'changed' } $true
    Invoke-Fixture 'missing retained file' { $script:mappings = @() } $true
    Invoke-Fixture 'duplicate retained file' { $script:mappings = @($mappings[0], $mappings[0]) } $true
    Invoke-Fixture 'wrong mapping hash' { $mappings[0].sha256 = '0' * 64 } $true
    Invoke-Fixture 'path escapes root' { $mappings[0].path = '../outside.cs' } $true
    Invoke-Fixture 'missing provider provenance' { $provenance.providerFiles = @() } $true
    Invoke-Fixture 'wrong provider hash' { $provenance.providerFiles[0].sha256 = '0' * 64 } $true
    Write-Output 'Retained dependency gate fixtures passed: 10.'
}
finally {
    if ([IO.Path]::GetFullPath($fixtureRoot) -ne $expectedRoot -or -not (Split-Path -Leaf $fixtureRoot).StartsWith('bgp-retained-gate-', [StringComparison]::Ordinal)) { throw 'Unexpected cleanup path.' }
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}
