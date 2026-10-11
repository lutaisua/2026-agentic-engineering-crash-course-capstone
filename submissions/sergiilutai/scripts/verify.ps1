[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$submissionPath = Split-Path -Parent $PSScriptRoot

function Invoke-DotnetCheck {
    param([string[]]$Arguments)
    & $DotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Push-Location -LiteralPath $submissionPath
try {
    Invoke-DotnetCheck -Arguments @('build', 'AnalyticsET.slnx', '--configuration', 'Release')
    Invoke-DotnetCheck -Arguments @('test', 'AnalyticsET.slnx', '--configuration', 'Release', '--no-build', '--filter', 'TestCategory!=Live')
    Invoke-DotnetCheck -Arguments @('format', 'AnalyticsET.slnx', '--verify-no-changes', '--no-restore')
    Write-Output 'Offline verification passed. Microsoft authorization and live reports remain unverified.'
}
finally {
    Pop-Location
}
exit 0
