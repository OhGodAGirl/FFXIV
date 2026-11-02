[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BundlePath,
    [Parameter(Mandatory)][string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$bundle = [IO.Path]::GetFullPath($BundlePath)
$package = [IO.Path]::GetFullPath($PackagePath)
$required = @('BetterGlamorPlates.dll', 'BetterGlamorPlates.Exportaramus.dll',
    'BetterGlamorPlates.deps.json', 'BetterGlamorPlates.json', 'images/icon.png')
$allowed = $required + @('BetterGlamorPlates.pdb', 'BetterGlamorPlates.xml', 'BetterGlamorPlates.Exportaramus.pdb')
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $bundle $name) -PathType Leaf)) {
        throw "Bundle is missing $name."
    }
}
$manifest = Get-Content -LiteralPath (Join-Path $bundle 'BetterGlamorPlates.json') -Raw | ConvertFrom-Json
$assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $bundle 'BetterGlamorPlates.dll'))
if ($manifest.InternalName -ne 'BetterGlamorPlates' -or $manifest.Name -ne 'Better Glamour Plates' -or
    $manifest.DalamudApiLevel -ne 15 -or $manifest.AssemblyVersion -ne $assembly.Version.ToString()) {
    throw 'Bundle identity, API or assembly version mismatch.'
}
$deps = Get-Content -LiteralPath (Join-Path $bundle 'BetterGlamorPlates.deps.json') -Raw | ConvertFrom-Json -AsHashtable
$runtimeFiles = @($deps.targets.Values | ForEach-Object { $_.Values } | ForEach-Object { if ($_.runtime) { $_.runtime.Keys } })
if ($runtimeFiles -notcontains 'BetterGlamorPlates.Exportaramus.dll') {
    throw 'Dependency manifest is missing the pinned consumer runtime.'
}
$icon = [IO.File]::ReadAllBytes((Join-Path $bundle 'images/icon.png'))
if ($icon.Length -lt 24 -or [Convert]::ToHexString($icon[0..7]) -ne '89504E470D0A1A0A') {
    throw 'Plugin icon is not a PNG.'
}
$width = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($icon, 16))
$height = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($icon, 20))
if ($width -ne $height -or $width -lt 64 -or $width -gt 512) { throw 'Plugin icon dimensions are invalid.' }
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace('\', '/')
        if ($name.EndsWith('/')) { continue }
        if (-not $names.Add($name)) { throw "Duplicate package entry: $name" }
        if ($name -notin $allowed) { throw "Unexpected package entry: $name" }
        $localPath = Join-Path $bundle $name
        if (-not (Test-Path -LiteralPath $localPath -PathType Leaf)) { throw "Package entry missing from bundle: $name" }
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $localPath -Algorithm SHA256).Hash) { throw "Package bytes differ: $name" }
    }
    foreach ($name in $required) {
        if (-not $names.Contains($name)) { throw "Package is missing $name." }
    }
}
finally { $archive.Dispose() }
Write-Output "Package smoke passed: $($manifest.AssemblyVersion); $($names.Count) payload files. No game code was executed."
