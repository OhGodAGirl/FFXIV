param(
    [Parameter(Mandatory)][string]$TestProjectPath,
    [string]$Configuration = 'Release',
    [Parameter(Mandatory)][string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'

function ConvertTo-CoverageSourceMap([string[]]$Lines, [string]$PluginRoot) {
    $localRoot = [IO.Path]::GetFullPath($PluginRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $ownedCount = 0
    $mapped = foreach ($line in $Lines) {
        if ($line -notmatch '^([^|]+)\|([^=]+)=(.+)$') { throw 'Malformed coverage source-root mapping.' }
        $project = $Matches[1]
        $original = $Matches[2]
        $destination = $Matches[3]
        if ($original.Equals($localRoot, [StringComparison]::OrdinalIgnoreCase)) {
            if (!$destination.Equals($localRoot, [StringComparison]::OrdinalIgnoreCase) -and
                $destination -cne '/_/CollectionShopper/') {
                throw 'Unexpected existing mapping for the owned coverage source root.'
            }
            $ownedCount++
            "$project|$original=/_/CollectionShopper/"
        }
        else { $line }
    }
    if ($ownedCount -eq 0) { throw 'Coverage source-root map has no owned project mapping.' }
    return $mapped
}

$root = Split-Path -Parent $PSScriptRoot
$project = Get-Item -LiteralPath $TestProjectPath
$outputRoot = Join-Path $project.DirectoryName "bin/$Configuration"
$assemblies = @(Get-ChildItem -LiteralPath $outputRoot -Recurse -File -Filter ($project.BaseName + '.dll'))
if ($assemblies.Count -ne 1) { throw 'Expected exactly one built test assembly for coverage mapping.' }
$mapPath = Join-Path $assemblies[0].DirectoryName ('CoverletSourceRootsMapping_' + $project.BaseName)
$original = [IO.File]::ReadAllText($mapPath)
$lines = Get-Content -LiteralPath $mapPath
$mapped = ConvertTo-CoverageSourceMap -Lines $lines -PluginRoot $root
[void][IO.Directory]::CreateDirectory($EvidenceDirectory)
$beforeHash = (Get-FileHash -LiteralPath $mapPath).Hash
[IO.File]::WriteAllText((Join-Path $EvidenceDirectory ($project.BaseName + '-source-map-before.txt')), $original)
$mapped | Set-Content -LiteralPath $mapPath -Encoding unicode
[ordered]@{
    testAssembly = $assemblies[0].FullName
    sourceMap = $mapPath
    beforeSha256 = $beforeHash
    afterSha256 = (Get-FileHash -LiteralPath $mapPath).Hash
    publicSourceRoot = '/_/CollectionShopper/'
    purpose = 'Resolve deterministic PDB document paths to local files for coverage instrumentation only; no assemblies or symbols are changed.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory ($project.BaseName + '-source-map.json')) -Encoding utf8
Write-Host "Coverage source map initialized for $($project.BaseName)."
