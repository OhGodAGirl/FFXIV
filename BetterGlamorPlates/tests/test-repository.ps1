[CmdletBinding()]
param(
    [ValidateSet('Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = @(Get-Command dotnet -CommandType Application -ErrorAction Stop)[0].Source
if (-not $OutputDirectory) {
    $runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $OutputDirectory = Join-Path $PSScriptRoot "TestResults/repository/$runId"
}
$results = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $results) { throw 'Use a new output directory to preserve previous verification evidence.' }
New-Item -ItemType Directory -Path $results | Out-Null

function Invoke-DotNet([string]$Description, [string[]]$Arguments) {
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

. (Join-Path $PSScriptRoot 'coverage-gate.ps1')
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'test-release-dependencies.ps1')
    & (Join-Path $PSScriptRoot 'test-release-dependencies-gate.ps1')
    & (Join-Path $PSScriptRoot 'test-coverage-gate.ps1')
    $coverageManifest = Test-CoverageInventory -Root $root -ManifestPath (Join-Path $PSScriptRoot 'coverage-manifest.json')
    Invoke-DotNet 'Locked restore' @('restore', 'BetterGlamorPlates.slnx', '--locked-mode')
    # Provider-owned source is checked by its pins rather than plugin formatting rules.
    Invoke-DotNet 'Format verification' @('format', 'BetterGlamorPlates.slnx', '--no-restore', '--verify-no-changes', '--exclude', 'reference/ConsumerKit', 'src/Contracts', '--verbosity', 'minimal')
    Invoke-DotNet 'Release build' @('build', 'BetterGlamorPlates.slnx', '-c', $Configuration, '--no-restore')
    Invoke-DotNet 'Ordinary tests, including performance budgets' @('test', 'BetterGlamorPlates.slnx', '-c', $Configuration, '--no-build', '--no-restore', '--logger', 'trx;LogFileName=ordinary.trx', '--results-directory', (Join-Path $results 'ordinary'))
    $testProjects = @(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File -Filter '*.Tests.csproj' |
        Where-Object { $_.Name -notlike '*.Mutation.Tests.csproj' })
    if ($testProjects.Count -eq 0) { throw 'No ordinary test project found.' }
    foreach ($testProject in $testProjects) {
        Invoke-DotNet "Coverage: $($testProject.BaseName)" @('test', $testProject.FullName, '-c', $Configuration, '--no-build', '--no-restore', '--filter', 'Category!=Performance', '--collect:XPlat Code Coverage', '--settings', (Join-Path $PSScriptRoot 'coverage.runsettings'), '--results-directory', (Join-Path $results "coverage/$($testProject.BaseName)"))
    }
    $reports = @(Get-ChildItem -LiteralPath $results -Recurse -Filter 'coverage.cobertura.xml' -File | ForEach-Object FullName)
    Test-CoverageReports -CoverageManifest $coverageManifest -ReportPaths $reports
    $packages = @(Get-ChildItem -LiteralPath (Join-Path $root 'bin/Release') -Recurse -Filter latest.zip -File)
    if ($packages.Count -ne 1) { throw "Expected one plugin package; found $($packages.Count)." }
    & (Join-Path $PSScriptRoot 'test-smoke.ps1') -BundlePath (Join-Path $root 'bin/Release') -PackagePath $packages[0].FullName
    [ordered]@{
        completedAtUtc = [DateTime]::UtcNow.ToString('o')
        stage = 'Repository verification'
        build = 'Passed'
        ordinaryTests = 'Passed'
        configuredCoverage = 'Passed'
        packageSmoke = 'Passed'
        packageSha256 = (Get-FileHash -LiteralPath $packages[0].FullName -Algorithm SHA256).Hash
        mutation = 'Not run by this command; run tests/test-mutation.ps1 separately.'
        artwork = 'Not qualified by this command.'
        liveAcceptance = 'Not performed.'
        publication = 'Not performed.'
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'repository-verification.json') -Encoding utf8
    Write-Output "Repository verification passed: $results"
    Write-Output 'This is not full release approval. Mutation, contextual copy review, artwork and in-game acceptance remain separate checks.'
}
finally { Pop-Location }
