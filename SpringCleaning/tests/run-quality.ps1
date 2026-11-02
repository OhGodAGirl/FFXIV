param(
    [string]$Configuration = 'Release',
    [ValidateSet('Stabilization', 'Milestone')][string]$Lane = 'Milestone'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source

function Invoke-DotNet {
    param([string]$Description, [string[]]$Arguments)

    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Normalize-RelativePath([string]$Path) {
    return $Path.Replace('\', '/').TrimStart('.', '/')
}

function Assert-BuiltRuntimeUnchanged {
    param([object[]]$Files)

    foreach ($file in $Files) {
        if ((Get-FileHash -LiteralPath $file.SourcePath -Algorithm SHA256).Hash -ne $file.Sha256) {
            throw "Built runtime changed during verification: $($file.Path)."
        }
    }
}

function Get-FileCoverage {
    param([System.Xml.XmlElement[]]$Classes, [string]$ExpectedPath)

    $normalizedExpected = Normalize-RelativePath $ExpectedPath
    $matches = @($Classes | Where-Object {
        $candidate = Normalize-RelativePath ([string]$_.filename)
        $candidate.Equals($normalizedExpected, [StringComparison]::OrdinalIgnoreCase) -or
            $candidate.EndsWith("/$normalizedExpected", [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matches.Count -eq 0) {
        throw "Coverage is missing included production file: $ExpectedPath"
    }

    $lines = @{}
    $branches = @{}
    foreach ($class in $matches) {
        foreach ($line in @($class.lines.line)) {
            $number = [int]$line.number
            $hits = [int]$line.hits
            if (-not $lines.ContainsKey($number) -or $hits -gt $lines[$number]) {
                $lines[$number] = $hits
            }

            if ([string]$line.branch -eq 'True') {
                $match = [regex]::Match(
                    [string]$line.'condition-coverage',
                    '\((\d+)/(\d+)\)')
                $covered = if ($match.Success) { [int]$match.Groups[1].Value } else { 0 }
                $valid = if ($match.Success) { [int]$match.Groups[2].Value } else { 0 }
                if (-not $branches.ContainsKey($number) -or $valid -gt $branches[$number].Valid) {
                    $branches[$number] = [pscustomobject]@{ Covered = $covered; Valid = $valid }
                }
            }
        }
    }

    if ($lines.Count -eq 0) {
        throw "Coverage contains no valid lines for included file: $ExpectedPath"
    }

    $coveredLines = @($lines.Values | Where-Object { $_ -gt 0 }).Count
    $validBranchSum = ($branches.Values | Measure-Object -Property Valid -Sum).Sum
    $coveredBranchSum = ($branches.Values | Measure-Object -Property Covered -Sum).Sum
    $validBranches = if ($null -eq $validBranchSum) { 0 } else { [int]$validBranchSum }
    $coveredBranches = if ($null -eq $coveredBranchSum) { 0 } else { [int]$coveredBranchSum }
    [pscustomobject]@{
        Path = $ExpectedPath
        CoveredLines = $coveredLines
        ValidLines = $lines.Count
        CoveredBranches = $coveredBranches
        ValidBranches = $validBranches
        LineCoverage = [math]::Round($coveredLines / $lines.Count * 100, 2)
        BranchCoverage = if ($validBranches -eq 0) { 100 } else { [math]::Round($coveredBranches / $validBranches * 100, 2) }
    }
}

Push-Location $root
try {
    $manifestPath = Join-Path $PSScriptRoot 'coverage-manifest.json'
    $coverageManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $included = @($coverageManifest.includedProductionFiles | ForEach-Object { Normalize-RelativePath ([string]$_) })
    $excluded = @($coverageManifest.excludedProductionFiles)
    $excludedPaths = @($excluded | ForEach-Object { Normalize-RelativePath ([string]$_.path) })

    foreach ($entry in $excluded) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.path) -or
            [string]::IsNullOrWhiteSpace([string]$entry.reason)) {
            throw 'Every coverage exclusion requires a path and a narrow reason.'
        }
    }

    $productionFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -File -Filter '*.cs' |
        Where-Object {
            $relative = Normalize-RelativePath ([IO.Path]::GetRelativePath($root, $_.FullName))
            $relative -notmatch '^(tests|bin|obj|artifacts|development|publish)/' -and
                $relative -notmatch '/(bin|obj)/'
        } |
        ForEach-Object { Normalize-RelativePath ([IO.Path]::GetRelativePath($root, $_.FullName)) })

    $overlap = @($included | Where-Object { $excludedPaths -contains $_ })
    if ($overlap.Count -gt 0) {
        throw "Coverage files cannot be both included and excluded: $($overlap -join ', ')"
    }
    $unclassified = @($productionFiles | Where-Object { $included -notcontains $_ -and $excludedPaths -notcontains $_ })
    if ($unclassified.Count -gt 0) {
        throw "Production files missing from coverage inventory: $($unclassified -join ', ')"
    }
    foreach ($classified in @($included + $excludedPaths)) {
        if ($productionFiles -notcontains $classified) {
            throw "Coverage inventory references a missing production file: $classified"
        }
    }

    & (Join-Path $PSScriptRoot 'test-consumer-pin.ps1')
    & python (Join-Path $root 'tools/verify-source.py')
    if ($LASTEXITCODE -ne 0) { throw 'Source verification failed.' }
    & python (Join-Path $root 'tools/test-source-tools.py')
    if ($LASTEXITCODE -ne 0) { throw 'Repository tooling regressions failed.' }

    Invoke-DotNet 'Locked restore' @('restore', '.\project\SpringCleaning.slnx', '--locked-mode')
    # Linked ConsumerKit files appear through multiple projects. Explicit owned
    # paths prevent solution formatting from crossing the pinned vendor boundary.
    $formatFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'src'), $PSScriptRoot -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|StrykerOutput|TestResults|Contracts)[\\/]' } |
        ForEach-Object { $_.FullName })
    Invoke-DotNet 'Format verification' (@('format', '.\project\SpringCleaning.slnx', '--no-restore', '--verify-no-changes', '--verbosity', 'minimal', '--include') + $formatFiles)
    Invoke-DotNet 'Release build' @('build', '.\project\SpringCleaning.slnx', '-c', $Configuration, '--no-restore')
    # Bind packaging to this build before mutation/coverage tooling can touch outputs.
    $runtimeRoot = Join-Path $root "project\bin\$Configuration"
    $runtimeNames = @('SpringCleaning.dll', 'SpringCleaning.deps.json', 'SpringCleaning.json',
        'images/icon.png')
    $runtimeFiles = @($runtimeNames | ForEach-Object {
        $source = Join-Path $runtimeRoot $_
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Built runtime is missing $_."
        }
        [pscustomobject]@{ Path = $_; SourcePath = $source; Sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash }
    })
    [xml]$projectDefinition = Get-Content -LiteralPath (Join-Path $root 'project\SpringCleaning.csproj') -Raw
    $packageVersion = $projectDefinition.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    $runtimeManifest = Get-Content -LiteralPath (Join-Path $runtimeRoot 'SpringCleaning.json') -Raw | ConvertFrom-Json
    if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $runtimeRoot 'SpringCleaning.dll')).Version.ToString() -ne $packageVersion -or
        $runtimeManifest.AssemblyVersion -ne $packageVersion -or $runtimeManifest.DalamudApiLevel -ne 15) {
        throw 'Built DLL or manifest does not match the project version and API 15.'
    }
    if (($runtimeFiles | Where-Object Path -eq 'images/icon.png').Sha256 -ne
        (Get-FileHash -LiteralPath (Join-Path $root 'images\icon.png') -Algorithm SHA256).Hash) {
        throw 'Built icon does not match the project icon.'
    }
    Invoke-DotNet 'Complete test suite' @('test', '.\project\SpringCleaning.slnx', '-c', $Configuration, '--no-build', '--no-restore')

    & (Join-Path $PSScriptRoot 'test-performance.ps1') -Configuration $Configuration
    if ($Lane -eq 'Milestone') {
        & (Join-Path $PSScriptRoot 'test-performance.ps1') -Configuration $Configuration -EnforceTiming
        if ($LASTEXITCODE -ne 0) {
            throw "Performance gate failed with exit code $LASTEXITCODE."
        }

        try {
            & (Join-Path $PSScriptRoot 'test-mutation.ps1') -Configuration $Configuration
            if ($LASTEXITCODE -ne 0) {
                throw "Mutation gate failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            # Stryker can leave instrumented or stale Domain/test binaries behind.
            # Rebuild even after a failed run; never feed those outputs to coverage.
            Invoke-DotNet 'Restore unmutated build outputs' @(
                'build', '.\project\SpringCleaning.slnx', '-c', $Configuration, '--no-restore', '-t:Rebuild')
            Assert-BuiltRuntimeUnchanged -Files $runtimeFiles
        }
    }

    $resultsRoot = Join-Path $PSScriptRoot ('TestResults\quality-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    $testProjects = @(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File -Filter '*.Tests.csproj')
    if ($testProjects.Count -eq 0) {
        throw 'No test projects were found for coverage.'
    }
    foreach ($testProject in $testProjects) {
        $projectResults = Join-Path $resultsRoot $testProject.BaseName
        Invoke-DotNet "Coverage run for $($testProject.BaseName)" @(
            'test', $testProject.FullName, '-c', $Configuration, '--no-build', '--no-restore',
            '--collect:XPlat Code Coverage', '--settings', (Join-Path $PSScriptRoot 'coverage.runsettings'),
            '--logger', 'trx;LogFileName=quality.trx',
            '--results-directory', $projectResults)
    }

    $reports = @(Get-ChildItem -LiteralPath $resultsRoot -Recurse -File -Filter 'coverage.cobertura.xml')
    if ($reports.Count -eq 0) {
        throw 'No coverage reports were generated.'
    }
    $classes = @()
    foreach ($report in $reports) {
        [xml]$coverage = Get-Content -LiteralPath $report.FullName -Raw
        if ([int]$coverage.coverage.'lines-valid' -eq 0) {
            throw "Coverage report contains no valid lines: $($report.FullName)"
        }
        $classes += @($coverage.coverage.packages.package.classes.class)
    }

    foreach ($path in $included) {
        $summary = Get-FileCoverage -Classes $classes -ExpectedPath $path
        Write-Host ("Coverage {0}: lines {1}%, branches {2}%" -f
            $path, $summary.LineCoverage, $summary.BranchCoverage)
        if (100 * $summary.CoveredLines -lt [double]$coverageManifest.minimumLineCoverage * $summary.ValidLines) {
            throw "Line coverage for $path is $($summary.LineCoverage)%."
        }
        if (100 * $summary.CoveredBranches -lt [double]$coverageManifest.minimumBranchCoverage * $summary.ValidBranches) {
            throw "Branch coverage for $path is $($summary.BranchCoverage)%."
        }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Assert-BuiltRuntimeUnchanged -Files $runtimeFiles
    $packagePath = Join-Path $resultsRoot "SpringCleaning-$packageVersion.zip"
    $archive = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $runtimeFiles) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $file.SourcePath, $file.Path, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally {
        $archive.Dispose()
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        foreach ($required in 'SpringCleaning.dll', 'SpringCleaning.deps.json', 'SpringCleaning.json') {
            if ($entries -notcontains $required) { throw "Package is missing $required." }
        }
        if ($entries.Count -ne $runtimeFiles.Count) {
            throw 'Package entry count does not match the runtime allowlist.'
        }
        foreach ($file in $runtimeFiles) {
            $matching = @($archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $file.Path })
            if ($matching.Count -ne 1) { throw "Package is missing or duplicates $($file.Path)." }
            $stream = $matching[0].Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($hash -ne $file.Sha256) { throw "Package bytes differ from the tested build: $($file.Path)." }
        }
        $forbidden = @($entries | Where-Object {
            $_ -match '(^|/)(tests?|bin|obj|development|publish)(/|$)' -or
                $_ -match '\.(cs|slnx|csproj|log)$'
        })
        if ($forbidden.Count -gt 0) {
            throw "Package contains forbidden files: $($forbidden -join ', ')"
        }
    }
    finally {
        $archive.Dispose()
    }

    [ordered]@{
        Version = $packageVersion
        Configuration = $Configuration
        Lane = $Lane
        PackagePath = $packagePath
        PackageSha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
        Files = @($runtimeFiles | Select-Object Path, Sha256)
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $resultsRoot 'package-verification.json') -Encoding utf8

    & (Join-Path $root 'tools/Build-Package.ps1') -OutputDirectory $runtimeRoot
    & (Join-Path $root 'tools/Inspect-Package.ps1') -OutputDirectory $runtimeRoot
    Write-Host "$Lane quality gate passed."
}
finally {
    Pop-Location
}
