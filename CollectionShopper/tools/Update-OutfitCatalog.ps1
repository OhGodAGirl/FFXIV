[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$endpoint = 'https://ffxivcollect.com/api/outfits?limit=10000'
$response = Invoke-RestMethod -Uri $endpoint -Headers @{
    'User-Agent' = 'CollectionShopperCatalogBuilder/1.0'
}

if ($null -eq $response.results -or $response.results.Count -eq 0) {
    throw 'FFXIV Collect returned no outfit catalog entries.'
}

$results = foreach ($outfit in $response.results) {
    [ordered]@{
        id = [uint32]$outfit.id
        name = [string]$outfit.name
        patch = [string]$outfit.patch
        gender = $outfit.gender
        icon = $outfit.icon
        items = @($outfit.items | ForEach-Object {
            [ordered]@{
                id = [uint32]$_.id
                name = [string]$_.name
            }
        })
        sources = @($outfit.sources | ForEach-Object {
            [ordered]@{
                type = [string]$_.type
                text = [string]$_.text
            }
        })
    }
}

$catalog = [ordered]@{
    source = $endpoint
    count = $results.Count
    results = $results
}

$outputDirectory = Join-Path $PSScriptRoot '..\Data'
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$outputPath = Join-Path $outputDirectory 'outfits.json'
$json = $catalog | ConvertTo-Json -Depth 8 -Compress
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($outputPath),
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Wrote $($results.Count) outfits to $([System.IO.Path]::GetFullPath($outputPath))."
