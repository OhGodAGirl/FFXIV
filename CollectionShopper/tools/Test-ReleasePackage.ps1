[CmdletBinding()]
param(
    [string]$ZipPath = '.\bin\Release\CollectionShopper\latest.zip',
    [string]$ProjectPath = '.\CollectionShopper.csproj',
    [string]$ExpectedTag,
    [string]$BuildOutputPath,
    [string]$IconPath
)

$ErrorActionPreference = 'Stop'

function Assert-PublicDebugPath([string]$Path, [switch]$AllowFileName) {
    $normalized = $Path.Replace('\', '/')
    if ($AllowFileName -and $normalized -cmatch '^[A-Za-z0-9_.-]+[.]pdb$' -and !$normalized.Contains('..')) { return }
    if (!$normalized.StartsWith('/_/CollectionShopper/', [StringComparison]::Ordinal) -or
        $normalized.Contains(':') -or $normalized -match '//|(?:^|/)[.]{1,2}(?:/|$)') {
        throw 'Debug metadata contains a path outside the deterministic public source root.'
    }
}

function Assert-PublicPdbDocuments([System.Reflection.Metadata.MetadataReaderProvider]$Provider) {
    # Invoke the optional-parameter overload explicitly for PowerShell.
    $reader = [System.Reflection.Metadata.MetadataReaderProvider].GetMethod('GetMetadataReader').Invoke(
        $Provider, @([System.Reflection.Metadata.MetadataReaderOptions]::Default, $null))
    if ($reader.Documents.Count -eq 0) { throw 'Portable PDB contains no source document evidence.' }
    foreach ($handle in $reader.Documents) {
        $document = $reader.GetDocument($handle)
        Assert-PublicDebugPath $reader.GetString($document.Name)
    }
}

function Assert-PublicDebugMetadata([string]$OutputPath) {
    foreach ($identity in 'CollectionShopper', 'CollectionShopper.Exportaramus') {
        $stream = [IO.File]::OpenRead((Join-Path $OutputPath "$identity.dll"))
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $codeViews = 0
            foreach ($entry in $pe.ReadDebugDirectory()) {
                if ($entry.Type -eq [System.Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView) {
                    $codeViews++
                    Assert-PublicDebugPath $pe.ReadCodeViewDebugDirectoryData($entry).Path -AllowFileName
                }
                elseif ($entry.Type -eq [System.Reflection.PortableExecutable.DebugDirectoryEntryType]::EmbeddedPortablePdb) {
                    $provider = $pe.ReadEmbeddedPortablePdbDebugDirectoryData($entry)
                    try { Assert-PublicPdbDocuments $provider }
                    finally { $provider.Dispose() }
                }
            }
            if ($codeViews -ne 1) { throw "Expected exactly one CodeView record for $identity." }
        }
        finally { $pe.Dispose(); $stream.Dispose() }
    }
    $stream = [IO.File]::OpenRead((Join-Path $OutputPath 'CollectionShopper.Exportaramus.pdb'))
    $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
    try { Assert-PublicPdbDocuments $provider }
    finally { $provider.Dispose(); $stream.Dispose() }
}

$resolvedZip = (Resolve-Path -LiteralPath $ZipPath).Path
$resolvedProject = (Resolve-Path -LiteralPath $ProjectPath).Path
$projectRoot = Split-Path -Parent $resolvedProject
if (!$BuildOutputPath) { $BuildOutputPath = Join-Path $projectRoot 'bin/Release' }
if (!$IconPath) { $IconPath = Join-Path $projectRoot 'images/icon.png' }
$resolvedOutput = (Resolve-Path -LiteralPath $BuildOutputPath).Path
$resolvedIcon = (Resolve-Path -LiteralPath $IconPath).Path

[xml]$project = Get-Content -LiteralPath $resolvedProject -Raw
$projectVersion = [string]($project.Project.PropertyGroup | Select-Object -First 1).Version
if ($projectVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Project version must have four numeric parts; found '$projectVersion'."
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedTag)) {
    $tagPrefix = 'collectionshopper-v'
    if (-not $ExpectedTag.StartsWith($tagPrefix, [StringComparison]::Ordinal)) {
        throw "Release tag must begin with '$tagPrefix'; found '$ExpectedTag'."
    }

    $tagVersion = $ExpectedTag.Substring($tagPrefix.Length)
    $parts = $tagVersion.Split('.')
    if ($tagVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
        throw "Release tag has an invalid numeric version: '$tagVersion'."
    }
    if ($parts.Count -eq 3) {
        $tagVersion += '.0'
    }
    elseif ($parts.Count -ne 4) {
        throw "Release tag version must have three or four numeric parts; found '$tagVersion'."
    }

    if ($tagVersion -ne $projectVersion) {
        throw "Release tag '$ExpectedTag' does not match project version '$projectVersion'."
    }
}

Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedZip)
try {
    $entryNames = @($archive.Entries | ForEach-Object FullName | Sort-Object)
    $expectedEntries = @(
        'CollectionShopper.deps.json',
        'CollectionShopper.dll',
        'CollectionShopper.Exportaramus.dll',
        'CollectionShopper.Exportaramus.pdb',
        'CollectionShopper.json',
        'CollectionShopper.xml',
        'images/icon.png'
    )

    if ([string]::Join("`n", $entryNames) -cne [string]::Join("`n", $expectedEntries)) {
        throw "Unexpected ZIP contents:`n$($entryNames -join "`n")"
    }

    # Comparing every entry to the tested output rejects stale or corrupted bytes,
    # even when an archive has plausible names and manifest metadata.
    foreach ($entry in $archive.Entries) {
        if ($entry.Length -le 0) { throw "Empty packaged file: $($entry.FullName)" }
        $sourcePath = Join-Path $resolvedOutput $entry.FullName
        $expectedHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        $stream = $entry.Open()
        try { $actualHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($actualHash -cne $expectedHash) { throw "Packaged bytes differ from build output: $($entry.FullName)" }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $resolvedOutput 'images/icon.png')).Hash -cne
        (Get-FileHash -LiteralPath $resolvedIcon).Hash) { throw 'Packaged icon differs from the approved source icon.' }
    Assert-PublicDebugMetadata $resolvedOutput
    $assemblies = @{}
    foreach ($identity in 'CollectionShopper', 'CollectionShopper.Exportaramus') {
        $assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $resolvedOutput "$identity.dll"))
        if ($assembly.Name -cne $identity) { throw "Packaged assembly identity does not match $identity." }
        if ($identity -eq 'CollectionShopper' -and $assembly.Version.ToString() -cne $projectVersion) {
            throw 'Packaged assembly version differs from the project.'
        }
        $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $resolvedOutput "$identity.dll")).FileVersion
        $assemblies[$identity] = @{ assemblyVersion = $assembly.Version.ToString(); fileVersion = $fileVersion }
        if ($identity -eq 'CollectionShopper' -and $fileVersion -cne $projectVersion) {
            throw 'Packaged DLL file version differs from the project.'
        }
    }
    $deps = Get-Content -LiteralPath (Join-Path $resolvedOutput 'CollectionShopper.deps.json') -Raw | ConvertFrom-Json -AsHashtable
    $target = $deps.targets[$deps.runtimeTarget.name]
    $mainKey = "CollectionShopper/$projectVersion"
    $main = $target[$mainKey]
    $companionVersion = $main.dependencies.'CollectionShopper.Exportaramus'
    $companionKey = "CollectionShopper.Exportaramus/$companionVersion"
    if (!$companionVersion -or !$main.runtime.Contains('CollectionShopper.dll') -or
        !$target[$companionKey].runtime.Contains('CollectionShopper.Exportaramus.dll')) {
        throw 'Packaged dependency graph does not include both runtime assemblies.'
    }
    if ($deps.targets.Count -ne 1 -or $target.Count -ne 2 -or $deps.libraries.Count -ne 2 -or
        $main.dependencies.Count -ne 1 -or $target[$companionKey].dependencies.Count -gt 0) {
        throw 'Packaged dependency graph contains unexpected runtime dependencies.'
    }
    foreach ($key in @($mainKey, $companionKey)) {
        $node = $target[$key]
        if ($deps.libraries[$key].type -cne 'project' -or $node.runtime.Count -ne 1 -or
            $node.native.Count -gt 0 -or $node.runtimeTargets.Count -gt 0 -or $node.resources.Count -gt 0) {
            throw "Packaged dependency graph contains unshipped assets: $key"
        }
    }
    $runtime = $target[$companionKey].runtime['CollectionShopper.Exportaramus.dll']
    foreach ($property in 'assemblyVersion', 'fileVersion') {
        if ($runtime[$property] -cne $assemblies['CollectionShopper.Exportaramus'][$property]) {
            throw "Companion runtime $property does not match its DLL."
        }
    }

    $manifestEntry = $archive.GetEntry('CollectionShopper.json')
    if ($null -eq $manifestEntry) {
        throw 'CollectionShopper.json is missing from the ZIP.'
    }

    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try {
        $manifest = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
    }

    if ($manifest.InternalName -cne 'CollectionShopper') {
        throw "Packaged InternalName is '$($manifest.InternalName)'."
    }
    if ($manifest.AssemblyVersion -ne $projectVersion) {
        throw "Packaged version '$($manifest.AssemblyVersion)' does not match '$projectVersion'."
    }
    if (($manifest.DalamudApiLevel -isnot [int] -and $manifest.DalamudApiLevel -isnot [long]) -or
        $manifest.DalamudApiLevel -ne 15) {
        throw "Packaged Dalamud API level is '$($manifest.DalamudApiLevel)'."
    }
}
finally {
    $archive.Dispose()
}

$hash = (Get-FileHash -LiteralPath $resolvedZip -Algorithm SHA256).Hash
Write-Host "Release package passed: $resolvedZip"
Write-Host "Version: $projectVersion"
Write-Host "SHA256: $hash"
