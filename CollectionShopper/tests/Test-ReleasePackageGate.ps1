param([string]$BuildOutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'bin/Release'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$checker = Join-Path $root 'tools/Test-ReleasePackage.ps1'
$fixture = Join-Path $root ('artifacts/package-tests/' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $fixture)
$project = Join-Path $root 'CollectionShopper.csproj'
$payload = @('CollectionShopper.deps.json','CollectionShopper.dll','CollectionShopper.Exportaramus.dll','CollectionShopper.Exportaramus.pdb','CollectionShopper.json','CollectionShopper.xml','images/icon.png')
Add-Type -AssemblyName System.IO.Compression
function New-Fixture([string]$Name, [string]$Omit, [string]$Extra, [string]$Corrupt, [string]$Duplicate, [string]$ManifestProperty, [object]$ManifestValue) {
    $path = Join-Path $fixture "$Name.zip"
    $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entryName in @($payload) + @($Extra, $Duplicate)) {
            if (!$entryName -or $entryName -ceq $Omit) { continue }
            $entry = $zip.CreateEntry($entryName)
            $stream = $entry.Open()
            try {
                if ($entryName -ceq $Corrupt -or $entryName -ceq $Extra) {
                    $bytes = [Text.Encoding]::UTF8.GetBytes('invalid')
                } elseif ($entryName -ceq 'CollectionShopper.json' -and $ManifestProperty) {
                    $manifest = Get-Content (Join-Path $BuildOutputPath $entryName) -Raw | ConvertFrom-Json
                    $manifest.$ManifestProperty = $ManifestValue
                    $bytes = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json))
                } else {
                    $bytes = [IO.File]::ReadAllBytes((Join-Path $BuildOutputPath $entryName))
                }
                $stream.Write($bytes, 0, $bytes.Length)
            } finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
    return $path
}
$passed = 0
function Assert-Rejected([string]$Name, [scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw "Unsafe package was accepted: $Name" }
    $script:passed++
}
$tokens = $null
$parseErrors = $null
$checkerAst = [Management.Automation.Language.Parser]::ParseFile($checker, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Package checker parse failure.' }
$pathValidator = @($checkerAst.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-PublicDebugPath'
}, $true))
if ($pathValidator.Count -ne 1) { throw 'Expected one public debug-path validator.' }
. ([scriptblock]::Create($pathValidator[0].Extent.Text))
foreach ($path in '/_/CollectionShopper/src/Bootstrap/Plugin.cs', '/_/CollectionShopper/reference/ConsumerKit/obj/File.g.cs') {
    Assert-PublicDebugPath $path
    $passed++
}
Assert-PublicDebugPath 'CollectionShopper.pdb' -AllowFileName
$passed++
foreach ($path in 'C:\Users\Fixture\Plugin.cs', 'D:/Development/Fixture/Plugin.cs', '\\host\share\Plugin.cs',
    '/Users/fixture/Plugin.cs', '/_/CollectionShopper/../private/Plugin.cs', '/_/Other/Plugin.cs',
    '/_/CollectionShopper//Plugin.cs', 'Plugin.cs', '/_/CollectionShopper/C:/private/Plugin.cs') {
    Assert-Rejected 'private debug document path' { Assert-PublicDebugPath $path }
}
Assert-Rejected 'private CodeView path' { Assert-PublicDebugPath 'C:\Users\Fixture\Plugin.pdb' -AllowFileName }
$valid = New-Fixture 'valid'
& $checker -ZipPath $valid -ProjectPath $project -BuildOutputPath $BuildOutputPath
$passed++
foreach ($file in $payload) {
    $zip = New-Fixture ('missing-' + $passed) -Omit $file
    Assert-Rejected "missing $file" { & $checker -ZipPath $zip -ProjectPath $project -BuildOutputPath $BuildOutputPath }
}
foreach ($file in @('tests/Test.cs','development/artifact.json','../escape.dll','GlamourShopper/latest.zip','CollectionShopper.DLL','images\icon.png')) {
    $zip = New-Fixture ('extra-' + $passed) -Extra $file
    Assert-Rejected "extra $file" { & $checker -ZipPath $zip -ProjectPath $project -BuildOutputPath $BuildOutputPath }
}
foreach ($file in $payload) {
    $zip = New-Fixture ('corrupt-' + $passed) -Corrupt $file
    Assert-Rejected "corrupt $file" { & $checker -ZipPath $zip -ProjectPath $project -BuildOutputPath $BuildOutputPath }
}
$zip = New-Fixture 'duplicate' -Duplicate 'CollectionShopper.dll'
Assert-Rejected 'duplicate entry' { & $checker -ZipPath $zip -ProjectPath $project -BuildOutputPath $BuildOutputPath }
foreach ($tag in 'wrong-v0.1.77.29','collectionshopper-v1.2.3.4','collectionshopper-vx.y.z','collectionshopper-v0.1.77.29.0') {
    Assert-Rejected "tag $tag" { & $checker -ZipPath $valid -ProjectPath $project -BuildOutputPath $BuildOutputPath -ExpectedTag $tag }
}
[xml]$projectXml = Get-Content $project -Raw
$version = [string]($projectXml.Project.PropertyGroup | Select-Object -First 1).Version
& $checker -ZipPath $valid -ProjectPath $project -BuildOutputPath $BuildOutputPath -ExpectedTag "collectionshopper-v$version"
$passed++
# Matching tampered output cannot bypass the independent project/assembly/source-icon checks.
$copy = Join-Path $fixture 'output'
foreach ($file in $payload) {
    $destination = Join-Path $copy $file
    [void](New-Item -ItemType Directory -Path (Split-Path $destination) -Force)
    Copy-Item -LiteralPath (Join-Path $BuildOutputPath $file) -Destination $destination
}
$originalOutput = $BuildOutputPath
$BuildOutputPath = $copy
foreach ($change in @(@('InternalName','WrongPlugin'), @('InternalName','collectionshopper'),
        @('AssemblyVersion','1.2.3.4'), @('DalamudApiLevel',14), @('DalamudApiLevel',15.1))) {
    Copy-Item -LiteralPath (Join-Path $originalOutput 'CollectionShopper.json') -Destination (Join-Path $copy 'CollectionShopper.json') -Force
    $manifest = Get-Content (Join-Path $copy 'CollectionShopper.json') -Raw | ConvertFrom-Json
    $manifest.($change[0]) = $change[1]
    $manifest | ConvertTo-Json | Set-Content (Join-Path $copy 'CollectionShopper.json')
    $zip = New-Fixture ('metadata-' + $passed)
    Assert-Rejected "metadata $($change[0])" { & $checker -ZipPath $zip -ProjectPath $project -BuildOutputPath $copy }
}
Copy-Item -LiteralPath (Join-Path $originalOutput 'CollectionShopper.json') -Destination (Join-Path $copy 'CollectionShopper.json') -Force
[IO.File]::WriteAllText((Join-Path $copy 'images/icon.png'), 'not the approved icon')
$zip = New-Fixture 'wrong-icon'
Assert-Rejected 'unapproved source icon' { & $checker -ZipPath $zip -ProjectPath $project -BuildOutputPath $copy }
Copy-Item -LiteralPath (Join-Path $originalOutput 'images/icon.png') -Destination (Join-Path $copy 'images/icon.png') -Force
Copy-Item -LiteralPath (Join-Path $copy 'CollectionShopper.Exportaramus.dll') -Destination (Join-Path $copy 'CollectionShopper.dll') -Force
$zip = New-Fixture 'wrong-assembly'
Assert-Rejected 'wrong assembly identity with matching bytes' { & $checker -ZipPath $zip -ProjectPath $project -BuildOutputPath $copy }
Copy-Item -LiteralPath (Join-Path $originalOutput 'CollectionShopper.dll') -Destination (Join-Path $copy 'CollectionShopper.dll') -Force
$deps = Get-Content (Join-Path $copy 'CollectionShopper.deps.json') -Raw | ConvertFrom-Json
$deps.targets.($deps.runtimeTarget.name).("CollectionShopper/$version").dependencies.PSObject.Properties.Remove('CollectionShopper.Exportaramus')
$deps | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $copy 'CollectionShopper.deps.json')
$zip = New-Fixture 'missing-runtime-dependency'
Assert-Rejected 'missing runtime dependency with matching bytes' { & $checker -ZipPath $zip -ProjectPath $project -BuildOutputPath $copy }
foreach ($change in 'extra-runtime', 'assemblyVersion', 'fileVersion') {
    $deps = Get-Content (Join-Path $originalOutput 'CollectionShopper.deps.json') -Raw | ConvertFrom-Json -AsHashtable
    $target = $deps.targets[$deps.runtimeTarget.name]
    $companionVersion = $target["CollectionShopper/$version"].dependencies['CollectionShopper.Exportaramus']
    if ($change -eq 'extra-runtime') {
        $target["CollectionShopper/$version"].dependencies['UnshippedLibrary'] = '1.0.0'
        $target['UnshippedLibrary/1.0.0'] = @{ runtime = @{ 'UnshippedLibrary.dll' = @{} } }
        $deps.libraries['UnshippedLibrary/1.0.0'] = @{ type = 'project' }
    } else {
        $target["CollectionShopper.Exportaramus/$companionVersion"].runtime['CollectionShopper.Exportaramus.dll'][$change] = '9.9.9.9'
    }
    $deps | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $copy 'CollectionShopper.deps.json')
    $zip = New-Fixture $change
    Assert-Rejected "dependency graph $change" { & $checker -ZipPath $zip -ProjectPath $project -BuildOutputPath $copy }
}
Copy-Item -LiteralPath (Join-Path $originalOutput 'CollectionShopper.deps.json') -Destination (Join-Path $copy 'CollectionShopper.deps.json') -Force

function Replace-DebugToken([string]$Path, [string]$Original, [string]$Replacement) {
    if ($Original.Length -ne $Replacement.Length) { throw 'Debug fixture replacements must preserve metadata offsets.' }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $text = [Text.Encoding]::Latin1.GetString($bytes)
    if (!$text.Contains($Original, [StringComparison]::Ordinal)) { throw 'Expected debug metadata token was not found.' }
    [IO.File]::WriteAllBytes($Path, [Text.Encoding]::Latin1.GetBytes($text.Replace($Original, $Replacement)))
}

# Matching ZIP/output hashes must not bless private paths inside valid metadata.
$pdbPath = Join-Path $copy 'CollectionShopper.Exportaramus.pdb'
Replace-DebugToken $pdbPath 'CollectionShopper' 'NonPublicRootXXXX'
$zip = New-Fixture 'private-pdb-documents'
Assert-Rejected 'private Portable PDB documents with matching bytes' { & $checker -ZipPath $zip -ProjectPath $project -BuildOutputPath $copy }
Copy-Item -LiteralPath (Join-Path $originalOutput 'CollectionShopper.Exportaramus.pdb') -Destination $pdbPath -Force

$assemblyPath = Join-Path $copy 'CollectionShopper.Exportaramus.dll'
$stream = [IO.File]::OpenRead($assemblyPath)
$pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $entry = @($pe.ReadDebugDirectory() | Where-Object Type -EQ ([System.Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView))
    if ($entry.Count -ne 1) { throw 'Expected one fixture CodeView record.' }
    $debugPath = $pe.ReadCodeViewDebugDirectoryData($entry[0]).Path
}
finally { $pe.Dispose(); $stream.Dispose() }
Replace-DebugToken $assemblyPath $debugPath ('C:\' + ('x' * ($debugPath.Length - 3)))
$zip = New-Fixture 'private-codeview-path'
Assert-Rejected 'private CodeView path with matching bytes' { & $checker -ZipPath $zip -ProjectPath $project -BuildOutputPath $copy }
[ordered]@{ passed = $passed; failed = 0; fixtureDirectory = $fixture } | ConvertTo-Json | Set-Content (Join-Path $fixture 'results.json')
Write-Host "Release package contracts passed: $passed cases. Evidence: $fixture"
