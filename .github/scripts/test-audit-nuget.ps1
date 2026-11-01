$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('exportaramus-audit-test-' + [Guid]::NewGuid().ToString('N'))
function global:git { $global:LASTEXITCODE = 0; @('project/one.csproj', 'tests/two.csproj') }
function global:dotnet {
    $global:calls += ,@($args)
    $global:LASTEXITCODE = $(if ($global:failFirst -and $global:calls.Count -eq 1) { 1 } else { 0 })
    'Simulated audit output'
}
try {
    foreach ($fail in @($false, $true)) {
        $global:calls = @(); $global:failFirst = $fail
        $output = Join-Path $temporary ([string]$fail)
        $threw = $false
        try { & "$PSScriptRoot/audit-nuget.ps1" -OutputDirectory $output } catch { $threw = $true }
        if ($threw -ne $fail) { throw 'Audit failure was not propagated correctly.' }
        if ($global:calls.Count -ne 2) { throw 'Not every project was audited.' }
        foreach ($call in $global:calls) {
            foreach ($required in @('--locked-mode', '--force', '-p:NuGetAudit=true', '-p:NuGetAuditMode=all', '-p:WarningsAsErrors=NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904%3BNU1905')) {
                if ($required -notin $call) { throw "Missing audit argument: $required" }
            }
        }
        $receipt = Get-Content -LiteralPath (Join-Path $output 'nuget-audit.json') -Raw | ConvertFrom-Json
        if ($receipt.projects.Count -ne 2 -or $receipt.failed.Count -ne [int]$fail) { throw 'Incorrect audit receipt.' }
        if ($receipt.status -ne $(if ($fail) { 'Failed' } else { 'Passed' })) { throw 'Incorrect audit status.' }
    }
    Write-Output 'NuGet audit success and failure handling passed.'
}
finally {
    Remove-Item Function:\git,Function:\dotnet
    # Delete only this test's exact temporary folder after validating its parent.
    if ((Split-Path -Parent ([IO.Path]::GetFullPath($temporary))) -ne ([IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar))) { throw 'Unexpected test path.' }
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
