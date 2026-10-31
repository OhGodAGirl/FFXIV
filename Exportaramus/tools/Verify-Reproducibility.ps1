#Requires -Version 7.2
param(
    [string]$DotNet = 'dotnet',
    [Alias('EvidenceDirectory')][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/VerificationEnvironment.ps1"
$dotnet = Resolve-VerificationTool -Command $DotNet
$evidence = New-VerificationRun -ProjectRoot $projectRoot -OutputDirectory $OutputDirectory -Name 'reproducibility'
$beforeSource = & "$PSScriptRoot/Get-SourceInventory.ps1" -Destination "$evidence/reproduction-inputs.json"
$packageOutput = Join-Path $projectRoot 'project/bin/Release'
$expectedPackage = [IO.Path]::GetFullPath((Join-Path $packageOutput 'Exportaramus/latest.zip'))
& "$PSScriptRoot/Inspect-Package.ps1" -OutputDirectory $packageOutput -EvidenceDirectory "$evidence/baseline"
$before = Get-Content -LiteralPath "$evidence/baseline/package-inspection.json" -Raw | ConvertFrom-Json
if ($before.status -ne 'Passed' -or (Get-FileHash -LiteralPath $before.package -Algorithm SHA256).Hash.ToLowerInvariant() -ne $before.packageSha256) { throw 'Fresh baseline package inspection failed.' }
if (![IO.Path]::GetFullPath($before.package).Equals($expectedPackage, [StringComparison]::OrdinalIgnoreCase)) { throw 'The baseline must be the selected Release package.' }
function Get-ReferenceEvidence {
    $referenceOutput = & $dotnet msbuild project/Exportaramus.csproj -target:ResolveReferences -getItem:ReferencePath -property:Configuration=Release
    if ($LASTEXITCODE -ne 0) { throw 'Reference resolution failed.' }
    $referenceText = $referenceOutput -join "`n"
    $referenceJson = $referenceText.Substring($referenceText.IndexOf('{')) | ConvertFrom-Json
    $resolved = @($referenceJson.Items.ReferencePath)
    if ($resolved.Count -eq 0) { throw 'No resolved dependency references were recorded.' }
    @($resolved | ForEach-Object {
        [pscustomobject]@{path=$_.Identity;sha256=(Get-FileHash -LiteralPath $_.Identity -Algorithm SHA256).Hash.ToLowerInvariant()}
    } | Sort-Object path)
}
Push-Location $projectRoot
try {
    $sdkVersion = & $dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'SDK identity check failed.' }
    $references = @(Get-ReferenceEvidence)
    & $dotnet build project/Exportaramus.csproj -c Release --no-restore -target:Rebuild
    if ($LASTEXITCODE -ne 0) { throw 'Clean product rebuild failed.' }
    & "$PSScriptRoot/Inspect-Package.ps1" -OutputDirectory $packageOutput -EvidenceDirectory "$evidence/rebuilt"
    $after = Get-Content -LiteralPath "$evidence/rebuilt/package-inspection.json" -Raw | ConvertFrom-Json
    if ($after.status -ne 'Passed' -or ![IO.Path]::GetFullPath($after.package).Equals($expectedPackage, [StringComparison]::OrdinalIgnoreCase)) { throw 'Rebuilt Release package inspection did not pass.' }
    $rebuiltPackageHash = (Get-FileHash -LiteralPath $after.package -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($rebuiltPackageHash -ne $after.packageSha256) { throw 'Rebuilt ZIP bytes do not match the refreshed inspection.' }
    $referencesAfter = @(Get-ReferenceEvidence)
    if ($references.Count -ne $referencesAfter.Count) { throw 'Resolved dependency reference set changed during reproduction.' }
    for ($index = 0; $index -lt $references.Count; $index++) {
        if ($references[$index].path -cne $referencesAfter[$index].path -or $references[$index].sha256 -ne $referencesAfter[$index].sha256) { throw 'Resolved dependency paths or bytes changed during reproduction.' }
    }
    $afterSource = & "$PSScriptRoot/Get-SourceInventory.ps1" -Destination "$evidence/reproduction-inputs-after.json"
    if ($beforeSource -ne $afterSource) { throw 'Source inputs changed during reproduction.' }
    if ($before.packageSha256 -ne $rebuiltPackageHash) { throw 'Clean rebuild did not reproduce the exact package bytes.' }
    if (@($before.files).Count -ne @($after.files).Count) { throw 'Rebuilt runtime file set differs.' }
    foreach ($file in $before.files) {
        $rebuilt = @($after.files | Where-Object path -EQ $file.path)
        if ($rebuilt.Count -ne 1 -or $rebuilt[0].sha256 -ne $file.sha256) { throw "Rebuilt runtime file differs: $($file.path)" }
    }
    [ordered]@{
        status='Passed';scope='SameEnvironmentPackageReproduction';configuration='Release';sdkVersion=[string]$sdkVersion;sourceSha256=$beforeSource
        firstPackageSha256=$before.packageSha256;rebuiltPackageSha256=$rebuiltPackageHash
        method='Exact same-environment package reproduction: clean Rebuild with previously restored dependencies, unchanged recorded source inputs and resolved dependency paths/hashes, per-file and whole-ZIP byte comparison'
        dependencyInputsUnchanged=$true;references=$references;referencesAfter=$referencesAfter
        completedUtc=[DateTime]::UtcNow.ToString('o');files=$after.files
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$evidence/reproducibility.json" -Encoding utf8
    Write-Output "Reproduced exact Release package: $($after.packageSha256)"
}
finally { Pop-Location }
