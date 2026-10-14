param([string]$OutputDirectory = "$PSScriptRoot/../project/bin/Release")
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
[xml]$project = Get-Content -LiteralPath "$projectRoot/project/Exportaramus.csproj"
$version = [string]$project.Project.PropertyGroup.Version
$assembly = [Reflection.AssemblyName]::GetAssemblyName("$output/Exportaramus.dll")
$manifest = Get-Content -LiteralPath "$output/Exportaramus.json" -Raw | ConvertFrom-Json
if (@($manifest.Tags | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -eq 0) { throw 'Plugin manifest must include search tags.' }
if ($assembly.Name -ne 'Exportaramus' -or $assembly.Version.ToString() -ne $version -or $manifest.InternalName -ne 'Exportaramus' -or $manifest.Name -ne 'Exportaramus' -or $manifest.AssemblyVersion -ne $version -or $manifest.DalamudApiLevel -ne 15) { throw 'Assembly/manifest/project identity mismatch.' }
if ($manifest.IconUrl -ne "https://raw.githubusercontent.com/OhGodAGirl/FFXIV/main/Exportaramus/images/icon.png") { throw "Packaged manifest must include the public plugin icon URL." }
$assemblyStream = [IO.File]::OpenRead("$output/Exportaramus.dll")
$pe = [System.Reflection.PortableExecutable.PEReader]::new($assemblyStream)
try {
    $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    foreach ($handle in $metadata.TypeDefinitions) {
        $type = $metadata.GetTypeDefinition($handle)
        $name = $metadata.GetString($type.Name); $ns = $metadata.GetString($type.Namespace)
        if ($name -in @('CollectionDataIpcClient','CollectionDataGateBindings') -or $ns -like '*Reference*' -or $ns -like '*Tests*' -or $ns -like '*Wardrobe*') { throw "Reference consumer or test type ships: $ns.$name" }
    }
}
finally { $pe.Dispose(); $assemblyStream.Dispose() }
$zipPath = Join-Path $output 'Exportaramus/latest.zip'
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $expected = @('Exportaramus.ConsumerKit.V1_1.dll','Exportaramus.ConsumerKit.V1_2.dll','Exportaramus.deps.json','Exportaramus.dll','Exportaramus.json','images/icon.png')
    $names = @($zip.Entries.FullName | Sort-Object)
    if (@(Compare-Object $expected $names).Count) { throw "Package contents differ from the exact runtime allowlist: $($names -join ', ')" }
    $files = foreach ($entry in $zip.Entries) {
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        finally { $stream.Dispose() }
        $local = Join-Path $output $entry.FullName
        if ($hash -ne (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash.ToLowerInvariant()) { throw "Package byte mismatch: $($entry.FullName)" }
        [ordered]@{ path=$entry.FullName; bytes=$entry.Length; sha256=$hash }
    }
}
finally { $zip.Dispose() }
$evidenceDirectory = Join-Path $projectRoot 'development/docs'
[void][IO.Directory]::CreateDirectory($evidenceDirectory)
$result = [ordered]@{ status='Passed'; product='Exportaramus'; version=$version; apiLevel=15; package=$zipPath; packageSha256=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant(); files=@($files); runtimeReferencesExcluded=$true; sourceAndPrivateEvidenceExcluded=$true }
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$evidenceDirectory/package-inspection.json" -Encoding utf8
Write-Output "Package verified: $version, $($files.Count) runtime files, $($result.packageSha256)"
