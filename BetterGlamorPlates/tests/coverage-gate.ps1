function Normalize-RelativePath([string]$Path) {
    return $Path.Replace('\', '/').TrimStart('.', '/')
}

function Get-FileCoverage {
    param([System.Xml.XmlElement[]]$Classes, [string]$ExpectedPath)

    $normalizedExpected = Normalize-RelativePath $ExpectedPath
    $expectedWithoutSourceRoot = if ($normalizedExpected.StartsWith(
            'src/', [StringComparison]::OrdinalIgnoreCase)) {
        $normalizedExpected.Substring(4)
    }
    else {
        $normalizedExpected
    }
    $matches = @($Classes | Where-Object {
        $candidate = Normalize-RelativePath ([string]$_.filename)
        $candidate.Equals($normalizedExpected, [StringComparison]::OrdinalIgnoreCase) -or
            $candidate.EndsWith("/$normalizedExpected", [StringComparison]::OrdinalIgnoreCase) -or
            $candidate.Equals($expectedWithoutSourceRoot, [StringComparison]::OrdinalIgnoreCase) -or
            $candidate.EndsWith("/$expectedWithoutSourceRoot", [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matches.Count -eq 0) {
        throw "Coverage is missing included production file: $ExpectedPath"
    }

    $lines = @{}
    $branches = @{}
    foreach ($class in $matches) {
        foreach ($line in @($class.lines.line)) {
            if ($null -eq $line) { continue }
            $number = [int]$line.number
            $hits = [int]$line.hits
            if (-not $lines.ContainsKey($number) -or $hits -gt $lines[$number]) {
                $lines[$number] = $hits
            }

            if ([string]$line.branch -eq 'True') {
                $covered = 0
                $valid = 0
                foreach ($condition in @($line.conditions.condition)) {
                    $match = [regex]::Match([string]$condition.coverage, '\((\d+)/(\d+)\)')
                    if ($match.Success) {
                        $covered += [int]$match.Groups[1].Value
                        $valid += [int]$match.Groups[2].Value
                    }
                }
                if ($valid -eq 0) {
                    $lineCoverage = [regex]::Match(
                        [string]$line.'condition-coverage',
                        '\((\d+)/(\d+)\)')
                    if ($lineCoverage.Success) {
                        $covered = [int]$lineCoverage.Groups[1].Value
                        $valid = [int]$lineCoverage.Groups[2].Value
                    }
                }
                if ($valid -le 0 -or $covered -gt $valid) {
                    throw "Coverage branch counts are invalid for included file: $ExpectedPath"
                }
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


function Test-CoverageInventory {
    param([string]$Root, [string]$ManifestPath)
    $coverageManifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($coverageManifest.minimumLineCoverage -ne 100 -or $coverageManifest.minimumBranchCoverage -ne 100) {
        throw 'Coverage requires explicit 100 percent line and branch thresholds.'
    }
    $included = @($coverageManifest.includedProductionFiles | ForEach-Object { Normalize-RelativePath ([string]$_) })
    $excluded = @($coverageManifest.excludedProductionFiles)
    $excludedPaths = @($excluded | ForEach-Object { Normalize-RelativePath ([string]$_.path) })

    foreach ($entry in $excluded) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.path) -or
            [string]::IsNullOrWhiteSpace([string]$entry.reason)) {
            throw 'Every coverage exclusion requires a path and a narrow reason.'
        }
    }

    $productionFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.cs' |
        Where-Object {
            $relative = Normalize-RelativePath ([IO.Path]::GetRelativePath($root, $_.FullName))
            $relative -notmatch '^(tests|bin|obj|artifacts|development|publish|github)/' -and
                $relative -notmatch '/(bin|obj)/' -and
                $relative -notmatch '^reference/ConsumerKit/1[.]1/' -and
                ($relative -notmatch '^reference/ConsumerKit/1[.]2/' -or
                    $relative -match '^reference/ConsumerKit/1[.]2/src/Contracts/1[.]2/')
        } |
        ForEach-Object { Normalize-RelativePath ([IO.Path]::GetRelativePath($root, $_.FullName)) })

    if (@($included | Group-Object | Where-Object Count -gt 1).Count -gt 0 -or
        @($excludedPaths | Group-Object | Where-Object Count -gt 1).Count -gt 0) {
        throw 'Coverage inventory contains duplicate paths.'
    }

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

    return $coverageManifest
}

function Test-CoverageReports {
    param($CoverageManifest, [string[]]$ReportPaths)
    $reports = @($ReportPaths)
    if ($reports.Count -eq 0) {
        throw 'No coverage reports were generated.'
    }
    $classes = @()
    foreach ($report in $reports) {
        [xml]$coverage = Get-Content -LiteralPath $report -Raw
        if ([int]$coverage.coverage.'lines-valid' -eq 0) {
            throw "Coverage report contains no valid lines: $($report)"
        }
        $classes += @($coverage.coverage.packages.package.classes.class)
    }

    foreach ($path in $coverageManifest.includedProductionFiles) {
        $summary = Get-FileCoverage -Classes $classes -ExpectedPath $path
        Write-Host ("Coverage {0}: lines {1}%, branches {2}%" -f
            $path, $summary.LineCoverage, $summary.BranchCoverage)
        if ([decimal]$summary.CoveredLines * 100 -lt [decimal]$summary.ValidLines * [decimal]$coverageManifest.minimumLineCoverage) {
            throw "Line coverage for $path is $($summary.LineCoverage)%."
        }
        if ([decimal]$summary.CoveredBranches * 100 -lt [decimal]$summary.ValidBranches * [decimal]$coverageManifest.minimumBranchCoverage) {
            throw "Branch coverage for $path is $($summary.BranchCoverage)%."
        }
    }

}
