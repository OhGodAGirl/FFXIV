param([string]$Configuration = 'Release', [string]$Dotnet = 'dotnet', [string]$OutputDirectory, [string]$CollectorPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. "$projectRoot/tools/VerificationEnvironment.ps1"
$dotnetTool = Resolve-VerificationTool -Command $Dotnet
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'development/coverage-final' }
$run = New-VerificationRun -ProjectRoot $projectRoot -OutputDirectory $OutputDirectory -Name 'complete'
Push-Location $projectRoot
try {
    & $dotnetTool test tests/Exportaramus.Tests/Exportaramus.Tests.csproj -c $Configuration --no-restore --settings tests/coverage.runsettings --collect 'XPlat Code Coverage' --results-directory "$run/provider" --logger 'trx;LogFileName=offline.trx'
    Assert-VerificationExit 'Complete provider tests'
    $provider = Read-VerificationCoverage -Run "$run/provider" -Trx "$run/provider/offline.trx"
    if (!$CollectorPath) {
        $assets = Get-Content tests/Exportaramus.Tests/obj/project.assets.json -Raw | ConvertFrom-Json
        $collectors = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'coverlet.collector/*' })
        if ($collectors.Count -ne 1) { throw 'Resolve exactly one coverlet.collector package, or pass -CollectorPath.' }
        $relative = $collectors[0].ToLowerInvariant()
        $CollectorPath = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ "$relative/build/net10.0" } | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'coverlet.collector.dll') }) | Select-Object -First 1
        if (!$CollectorPath) { throw 'Restored coverlet.collector could not be located; pass -CollectorPath.' }
    }
    $settings = Join-Path $run 'consumer-coverage.runsettings'
    '<RunSettings><DataCollectionRunSettings><DataCollectors><DataCollector friendlyName="XPlat Code Coverage"><Configuration><Format>cobertura,json</Format><Include>[Exportaramus.ConsumerKit.V1_2]*,[Exportaramus.ConsumerKit.Tests]*</Include><IncludeTestAssembly>true</IncludeTestAssembly><ExcludeByFile>**/tests/**/*.cs</ExcludeByFile><ExcludeByAttribute>GeneratedCodeAttribute,ExcludeFromCodeCoverageAttribute</ExcludeByAttribute></Configuration></DataCollector></DataCollectors></DataCollectionRunSettings></RunSettings>' | Set-Content -LiteralPath $settings -Encoding utf8
    & $dotnetTool test tests/Exportaramus.ConsumerKit.Tests/Exportaramus.ConsumerKit.Tests.csproj -c $Configuration --no-restore --test-adapter-path $CollectorPath --settings $settings --collect 'XPlat Code Coverage' --results-directory "$run/consumer" --logger 'trx;LogFileName=consumer-kit.trx'
    Assert-VerificationExit 'Complete ConsumerKit tests'
    $client = Read-VerificationCoverage -Run "$run/consumer" -Trx "$run/consumer/consumer-kit.trx" -TypePrefix 'Exportaramus.ConsumerKit.V1_2.CollectionDataIpcClient'
    $reader = Read-VerificationCoverage -Run "$run/consumer" -Trx "$run/consumer/consumer-kit.trx" -TypePrefix 'CollectionExample.InventoryItemReader'
    $provider.consumerClient = $client; $provider.inventoryReader = $reader
    $provider.interpretation = 'Provider and canonical contract assemblies require 100% lines/branches. The separate consumer client and example reader also require 100%; shared canonical contract sources are covered by the provider run.'
    $summary = Join-Path $run 'summary.json'
    $provider | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summary -Encoding utf8
    Copy-Item -LiteralPath $summary -Destination (Join-Path (Split-Path $run -Parent) 'summary.json') -Force
    Write-Output $summary
}
finally { Pop-Location }
