[CmdletBinding()]
param(
    [string]$ConfigurationPath = (Join-Path ([System.IO.Path]::GetTempPath()) ('AnalyticsET-demo-' + [guid]::NewGuid().ToString('N') + '/publishers.json'))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$submissionPath = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$cliPath = Join-Path $submissionPath 'bin/Release/net10.0/AnalyticsET.dll'
if (-not (Test-Path -LiteralPath $cliPath -PathType Leaf)) { throw 'Build the Release solution before running examples.' }
if (Test-Path -LiteralPath $ConfigurationPath) { throw 'Use a new temporary configuration path for the examples.' }
$demoPath = Split-Path -Parent ([System.IO.Path]::GetFullPath($ConfigurationPath))
New-Item -ItemType Directory -Path $demoPath -Force | Out-Null
$firstPath = Join-Path $PSScriptRoot 'publisher-one.json'
$secondPath = Join-Path $demoPath 'publisher-two.json'
$updatePath = Join-Path $demoPath 'publisher-one-update.json'
$second = Get-Content -LiteralPath $firstPath -Raw | ConvertFrom-Json
$second.id = 'publisher-two'
$second.displayName = 'Example publisher two'
foreach ($account in $second.accounts.PSObject.Properties) { $account.Value.publisherAccountId = 'account-two' }
foreach ($authorization in $second.authorization.PSObject.Properties) { $authorization.Value.authorizedAccountId = 'account-two' }
$second | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $secondPath -Encoding utf8
$updated = Get-Content -LiteralPath $firstPath -Raw | ConvertFrom-Json
$updated.displayName = 'Updated example publisher one'
$updated | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $updatePath -Encoding utf8

function Invoke-Example {
    param([string[]]$Arguments)
    & dotnet $cliPath --config $ConfigurationPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Example failed with exit code $LASTEXITCODE." }
}

Invoke-Example -Arguments @('--help')
Invoke-Example -Arguments @('--version')
Invoke-Example -Arguments @('config', 'path')
Invoke-Example -Arguments @('publisher', 'add', '--file', $firstPath)
Invoke-Example -Arguments @('publisher', 'add', '--file', $secondPath)
Invoke-Example -Arguments @('publisher', 'list')
Invoke-Example -Arguments @('publisher', 'show', '--publisher', 'publisher-one')
Invoke-Example -Arguments @('publisher', 'show', '--publisher', 'publisher-one,publisher-two')
Invoke-Example -Arguments @('publisher', 'show', '--all')
Invoke-Example -Arguments @('config', 'validate')
Invoke-Example -Arguments @('config', 'validate', '--publisher', 'publisher-one')
Invoke-Example -Arguments @('config', 'validate', '--all')
Invoke-Example -Arguments @('publisher', 'update', 'publisher-one', '--file', $updatePath)
Invoke-Example -Arguments @('publisher', 'remove', 'publisher-one')
Invoke-Example -Arguments @('publisher', 'list')
Write-Output 'Example commands passed (offline). All identifiers are fictional and Microsoft access is unverified.'
