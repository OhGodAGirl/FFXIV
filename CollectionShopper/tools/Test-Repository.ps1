[CmdletBinding()]
param([string]$DotNet = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$results = Join-Path $root ('artifacts/repository-check/' + [Guid]::NewGuid().ToString('N'))
function Invoke-DotNet([string[]]$Arguments) {
    & $DotNet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
function Normalize-RelativePath([string]$Path) {
    return $Path.Replace('\', '/').TrimStart('.', '/')
}
function Get-FileCoverage {
    param([System.Xml.XmlElement[]]$Classes, [string]$ExpectedPath)

    $normalizedExpected = Normalize-RelativePath $ExpectedPath
    $matches = @($Classes | Where-Object {
        $candidate = Normalize-RelativePath ([string]$_.filename)
        $candidate.Equals($normalizedExpected, [StringComparison]::OrdinalIgnoreCase) -or
            $candidate.EndsWith("/$normalizedExpected", [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matches.Count -eq 0) { throw "Coverage is missing included production file: $ExpectedPath" }

    $lines = @{}
    $branches = @{}
    foreach ($class in $matches) {
        foreach ($line in @($class.lines.line | Where-Object { $_ -is [System.Xml.XmlElement] })) {
            if ([string]$line.number -notmatch '^[1-9][0-9]*$' -or [string]$line.hits -notmatch '^[0-9]+$') {
                throw "Coverage has invalid line data: $ExpectedPath"
            }
            $number = [int]$line.number
            $hits = [int]$line.hits
            if (-not $lines.ContainsKey($number) -or $hits -gt $lines[$number]) {
                $lines[$number] = $hits
            }
            if ([string]$line.branch -eq 'True') {
                $match = [regex]::Match([string]$line.'condition-coverage', '^\d+(?:\.\d+)?% \((\d+)/(\d+)\)$')
                if (-not $match.Success) {
                    throw "Coverage has no valid branch counts: ${ExpectedPath}:$number"
                }
                $covered = [int]$match.Groups[1].Value
                $valid = [int]$match.Groups[2].Value
                if ($valid -le 0 -or $covered -gt $valid) {
                    throw "Coverage has inconsistent branch counts: ${ExpectedPath}:$number"
                }
                if ($branches.ContainsKey($number) -and
                    ($branches[$number].Covered -ne $covered -or $branches[$number].Valid -ne $valid)) {
                    throw "Coverage has conflicting branch counts: ${ExpectedPath}:$number"
                }
                $branches[$number] = [pscustomobject]@{ Covered = $covered; Valid = $valid }
            }
        }
    }
    if ($lines.Count -eq 0) { throw "Coverage contains no valid lines for included file: $ExpectedPath" }

    $coveredLines = @($lines.Values | Where-Object { $_ -gt 0 }).Count
    $validBranchSum = ($branches.Values | Measure-Object -Property Valid -Sum).Sum
    $coveredBranchSum = ($branches.Values | Measure-Object -Property Covered -Sum).Sum
    $validBranches = if ($null -eq $validBranchSum) { 0 } else { [int]$validBranchSum }
    $coveredBranches = if ($null -eq $coveredBranchSum) { 0 } else { [int]$coveredBranchSum }
    return [pscustomobject]@{
        Path = $ExpectedPath
        LineCoverage = $coveredLines / $lines.Count * 100
        BranchCoverage = if ($validBranches -eq 0) { 100 } else { $coveredBranches / $validBranches * 100 }
    }
}
Push-Location $root
try {
    & ./tools/Test-ConsumerKitPin.ps1
    Invoke-DotNet @('restore', 'CollectionShopper.slnx', '--locked-mode')
    Invoke-DotNet @('build', 'CollectionShopper.slnx', '-c', 'Release', '--no-restore', '-p:ShouldUnsetParentConfigurationAndPlatform=false', '-p:UseSharedCompilation=false')
    & ./tests/Test-CoverageGate.ps1
    & ./tests/Test-CoverageSourceMapGate.ps1
    & ./tests/Initialize-CoverageSourceMap.ps1 -TestProjectPath tests/CollectionShopper.Tests/CollectionShopper.Tests.csproj -EvidenceDirectory $results
    Invoke-DotNet @('test', 'tests/CollectionShopper.Tests/CollectionShopper.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore', '--collect:XPlat Code Coverage', '--settings', 'tests/coverage.runsettings', '--logger', 'trx;LogFileName=repository.trx', '--results-directory', $results, '--', 'xUnit.MaxParallelThreads=2')
    $reports = @(Get-ChildItem $results -Recurse -Filter coverage.cobertura.xml)
    if (!$reports.Count) { throw 'Coverage report missing.' }
    $classes = foreach ($report in $reports) {
        [xml]$coverage = Get-Content $report.FullName -Raw
        @($coverage.coverage.packages.package.classes.class)
    }
    $manifest = Get-Content tests/coverage-manifest.json -Raw | ConvertFrom-Json
    $files = @($manifest.includedProductionFiles | ForEach-Object { Get-FileCoverage -Classes $classes -ExpectedPath $_ })
    if (@($files | Where-Object { $_.LineCoverage -lt 100 -or $_.BranchCoverage -lt 100 }).Count) { throw 'Coverage must be 100% for every included file.' }
    $files | ConvertTo-Json | Set-Content (Join-Path $results 'coverage.json')
    & ./tests/Test-ReleasePackageGate.ps1 -BuildOutputPath bin/Release
    & ./tools/Test-ReleasePackage.ps1 -ZipPath bin/Release/CollectionShopper/latest.zip -BuildOutputPath bin/Release
    Write-Host "Repository checks passed. Evidence: $results"
}
finally { Pop-Location }