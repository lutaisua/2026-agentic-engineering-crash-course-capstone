[CmdletBinding()]
param(
    [string]$ConfigurationPath = (Join-Path $env:USERPROFILE '.analyticset/runtime/publishers.json'),
    [string]$ExperimentDirectory = '',
    [string]$DiscoveryPacketDirectory = '',
    [ValidateSet('Discover', 'Installs', 'InstallsReport', 'AddOnAcquisitions')]
    [string]$Stage = 'Discover',
    [string]$DotnetPath = 'dotnet',
    [switch]$RunLive,
    [switch]$UseUtcTimestampPreview,
    [switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$step = 'prerequisites'
$hubExit = 1
$setupCategory = 'private-prerequisite-failed'

function Stop-PrivateSetup([string]$Category) {
    $script:setupCategory = $Category
    throw 'Private input validation stopped.'
}

function Assert-PrivatePath([string]$Path) {
    $ancestor = [IO.Path]::GetFullPath($Path)
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                (Test-Path -LiteralPath (Join-Path $ancestor '.git')) -or
                (Test-Path -LiteralPath (Join-Path $ancestor 'openspec'))) {
                throw 'Private paths must be physical and outside repositories/planning workspaces.'
            }
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
}

function Assert-PrivateAcl([string]$Path, [bool]$Directory) {
    $entry = if ($Directory) { [IO.DirectoryInfo]::new($Path) } else { [IO.FileInfo]::new($Path) }
    $acl = $entry.GetAccessControl([Security.AccessControl.AccessControlSections]'Access, Owner')
    $user = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    if (-not $acl.GetOwner([Security.Principal.SecurityIdentifier]).Equals($user) -or
        ($Directory -and -not $acl.AreAccessRulesProtected)) { throw 'Unsafe owner or inherited directory permissions.' }
    $userFull = $false
    $systemFull = $false
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne 'Allow' -or
            (-not $rule.IdentityReference.Equals($user) -and -not $rule.IdentityReference.Equals($system))) {
            throw 'Only the operator and SYSTEM may access diagnostic storage.'
        }
        $full = ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq
            [Security.AccessControl.FileSystemRights]::FullControl
        if ($rule.IdentityReference.Equals($user) -and $full) { $userFull = $true }
        if ($rule.IdentityReference.Equals($system) -and $full) { $systemFull = $true }
    }
    if (-not $userFull -or -not $systemFull) { throw 'Operator and SYSTEM full access is required.' }
}

function New-PrivateDirectory([string]$Path) {
    Assert-PrivatePath $Path
    if (-not (Test-Path -LiteralPath $Path)) {
        $acl = [Security.AccessControl.DirectorySecurity]::new()
        $user = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl.SetOwner($user)
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($sid in @($user, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
                $sid, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow'))
        }
        $directory = [IO.DirectoryInfo]::new($Path)
        [IO.Directory]::CreateDirectory($directory.Parent.FullName) | Out-Null
        $directory.Create($acl)
    }
    Assert-PrivateAcl $Path $true
}

function Read-Masked([string]$Label) {
    $secure = Read-Host "$Label (masked local entry)" -AsSecureString
    $pointer = [IntPtr]::Zero
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
        $secure.Dispose()
    }
}

function Write-NewInput([object]$Value) {
    if (Test-Path -LiteralPath $InputPath) { throw 'Existing sample is preserved.' }
    $temporary = $InputPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
        Assert-PrivateAcl $temporary $false
        [IO.File]::Move($temporary, $InputPath)
        Assert-PrivateAcl $InputPath $false
    }
    finally {
        if ([IO.File]::Exists($temporary)) { Remove-Item -LiteralPath $temporary }
    }
}

function Get-Sha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($Path))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Read-Window([string]$Dataset) {
    $startText = Read-Host "$Dataset start boundary UTC date (yyyy-MM-dd)"
    $endText = Read-Host "$Dataset end boundary UTC date (yyyy-MM-dd; service inclusion remains unverified)"
    $startDate = [DateTime]::MinValue
    $endDate = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact($startText, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::None, [ref]$startDate) -or
        -not [DateTime]::TryParseExact($endText, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::None, [ref]$endDate)) { Stop-PrivateSetup 'utc-date-format-invalid' }
    if ($startDate -le [DateTime]::new(1970, 1, 1)) { Stop-PrivateSetup 'utc-start-date-invalid' }
    if ($endDate -gt [DateTime]::UtcNow.Date) { Stop-PrivateSetup 'utc-end-date-not-completed' }
    if ($endDate -le $startDate) { Stop-PrivateSetup 'utc-window-end-not-after-start' }
    if (($endDate - $startDate).TotalDays -gt 7) { Stop-PrivateSetup 'utc-window-longer-than-seven-days' }
    return [ordered]@{ startUtc = $startText + 'T00:00:00Z'; endUtc = $endText + 'T00:00:00Z' }
}

if ($Help) {
    Write-Output 'Store Downloads Hub experiment. Discover records metadata; Installs currently performs read-only query preview.'
    Write-Output 'Default: prepare protected input offline; no password/vault/network access.'
    Write-Output 'Identifiers use masked local prompts; choose completed one-to-seven-day UTC windows with known activity.'
    Write-Output '-RunLive -Stage Discover: explicit consumer metadata GET plus required authentication.'
    Write-Output '-ExperimentDirectory: existing/new GUID directory outside Git/OpenSpec; paths only.'
    Write-Output '-Stage Installs requires existing -ExperimentDirectory and -DiscoveryPacketDirectory; it sends no reporting POST.'
    Write-Output '-Stage InstallsReport (same paths) first checks installs-partner-center-small.csv privately, then creates one query and one one-time report,'
    Write-Output '  polls it for up to 10 minutes and downloads the CSV without the API token. Both resources stay in Partner Center; nothing is deleted.'
    Write-Output '-UseUtcTimestampPreview: explicit Installs-only read-only control using the same saved boundaries as UTC timestamps; no automatic fallback.'
    Write-Output 'Installs preview exit 2 is expected while date-boundary proof/export implementation are pending. AddOnAcquisitions remains blocked.'
    Write-Output 'Build Release first. Share only the printed packet path or sanitized category/exit.'
    Write-Output 'Exit 0: metadata or report recorded, not export/comparison acceptance; 2 incomplete; 1 prerequisite/recording; 130 cancelled.'
    exit 0
}

try {
    if ($UseUtcTimestampPreview -and $Stage -cne 'Installs') { $step = 'date-wire-control-stage-invalid'; throw 'UTC timestamp control requires Installs.' }
    if ($Stage -ceq 'AddOnAcquisitions') { $step = 'export-mapping-unverified'; throw 'Installs download prerequisite and export implementation are not ready.' }
    $usesDiscovery = $Stage -ceq 'Installs' -or $Stage -ceq 'InstallsReport'
    if ($usesDiscovery -and (-not $ExperimentDirectory -or -not $DiscoveryPacketDirectory)) {
        $step = 'discovery-prerequisite-required'
        throw 'Installs stages require explicit recorded discovery and existing experiment paths.'
    }
    if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Use Windows PowerShell 5.1.' }
    $ConfigurationPath = [IO.Path]::GetFullPath($ConfigurationPath)
    $step = 'configuration-access'
    Assert-PrivatePath $ConfigurationPath
    Assert-PrivateAcl ([IO.Path]::GetDirectoryName($ConfigurationPath)) $true
    Assert-PrivateAcl $ConfigurationPath $false
    if ([IO.FileInfo]::new($ConfigurationPath).Length -gt 1048576) { throw 'Configuration too large.' }
    $configuration = [IO.File]::ReadAllText($ConfigurationPath) | ConvertFrom-Json
    if ($configuration.schemaVersion -ne 2) { throw 'Use a schema 2 profile.' }
    $step = 'private-storage'
    if (-not $ExperimentDirectory) {
        $ExperimentDirectory = Join-Path $env:USERPROFILE ('.analyticset/validation/store-downloads-hub/' + [Guid]::NewGuid().ToString('D'))
    }
    $ExperimentDirectory = [IO.Path]::GetFullPath($ExperimentDirectory).TrimEnd('\')
    $experimentId = [Guid]::Empty
    if (-not [Guid]::TryParseExact([IO.Path]::GetFileName($ExperimentDirectory), 'D', [ref]$experimentId) -or
        $experimentId -eq [Guid]::Empty -or [IO.Path]::GetFileName($ExperimentDirectory) -cne $experimentId.ToString('D')) { throw 'Expected lowercase GUID experiment directory.' }
    $InputPath = Join-Path $ExperimentDirectory 'input.json'
    if ($usesDiscovery -and -not [IO.File]::Exists($InputPath)) {
        $step = 'discovery-prerequisite-required'
        throw 'Existing experiment input is required; Installs does not prepare a replacement sample.'
    }
    New-PrivateDirectory $ExperimentDirectory
    if ($usesDiscovery) {
        $step = 'discovery-path'
        $DiscoveryPacketDirectory = [IO.Path]::GetFullPath($DiscoveryPacketDirectory).TrimEnd('\')
        Assert-PrivatePath $DiscoveryPacketDirectory
        Assert-PrivateAcl $DiscoveryPacketDirectory $true
        if ([IO.Path]::GetDirectoryName($DiscoveryPacketDirectory) -cne $ExperimentDirectory) { throw 'Discovery is outside the selected experiment.' }
    }
    if ([IO.File]::Exists($InputPath)) {
        Assert-PrivatePath $InputPath
        Assert-PrivateAcl $InputPath $false
        if ([IO.FileInfo]::new($InputPath).Length -gt 16384) { throw 'Input too large.' }
        Write-Output 'Existing input preserved. Live preflight validates its strict schema and frozen binding.'
    }
    else {
        if (Test-Path -LiteralPath (Join-Path $ExperimentDirectory 'binding.json')) { throw 'Bound experiment is missing its original input.' }
        $step = 'publisher-alias-input'
        $alias = Read-Masked 'Existing stable publisher alias from publishers.json'
        if ($alias -cnotmatch '^[a-z0-9][a-z0-9-]{0,63}$' -or
            $alias -in @('con', 'prn', 'aux', 'nul') -or $alias -match '^(com|lpt)[1-9]$') { Stop-PrivateSetup 'publisher-alias-invalid' }
        $step = 'publisher-selection'
        $profiles = @($configuration.publishers | Where-Object { $_.id -ceq $alias })
        if ($profiles.Count -eq 0) { Stop-PrivateSetup 'publisher-alias-not-found' }
        if ($profiles.Count -ne 1) { Stop-PrivateSetup 'publisher-alias-ambiguous' }
        if (-not $profiles[0].PSObject.Properties['authorization'] -or
            $null -eq $profiles[0].authorization -or
            -not $profiles[0].authorization.PSObject.Properties['store-analytics'] -or
            $null -eq $profiles[0].authorization.'store-analytics') { Stop-PrivateSetup 'store-analytics-binding-missing' }
        if (-not $profiles[0].PSObject.Properties['accounts'] -or
            $null -eq $profiles[0].accounts -or
            -not $profiles[0].accounts.PSObject.Properties['microsoft-store'] -or
            $null -eq $profiles[0].accounts.'microsoft-store') { Stop-PrivateSetup 'store-account-scope-missing' }
        $step = 'parent-store-id-input'
        $parent = Read-Masked 'Parent application Store ID'
        if ($parent -cnotmatch '^[A-Za-z0-9-]{1,128}$') { Stop-PrivateSetup 'parent-store-id-invalid' }
        $step = 'subscription-store-id-input'
        $addon = Read-Masked 'Known Subscription add-on Store ID'
        if ($addon -cnotmatch '^[A-Za-z0-9-]{1,128}$') { Stop-PrivateSetup 'subscription-store-id-invalid' }
        if ($parent -ieq $addon) { Stop-PrivateSetup 'parent-child-id-equal' }
        $step = 'sample-confirmation'
        $confirmation = Read-Host 'Confirm published parent, Subscription type, parent relationship and selected publisher in Partner Center: type CONFIRM'
        if ($confirmation -cne 'CONFIRM') { Stop-PrivateSetup 'sample-confirmation-required' }
        Write-Output 'Choose completed UTC day boundaries spanning 1-7 days. For one day, use its start midnight and the next midnight; service inclusivity is still unverified.'
        $step = 'installs-window'
        $installs = Read-Window 'Installs'
        $step = 'addon-acquisitions-window'
        $acquisitions = Read-Window 'AddOnAcquisitions'
        $step = 'input-write'
        Write-NewInput ([ordered]@{
            schemaVersion = 1; experimentId = $experimentId.ToString('D'); publisherId = $alias
            parentApplicationStoreId = $parent; subscriptionStoreId = $addon; expectedProductType = 'Subscription'
            appPublishedConfirmed = $true; subscriptionParentConfirmed = $true
            confirmedAt = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            installsWindow = $installs; addOnAcquisitionsWindow = $acquisitions
        })
        $alias = $null
        $parent = $null
        $addon = $null
    }
    Write-Output "Experiment directory: $ExperimentDirectory"
    if (-not $RunLive) {
        Write-Output 'Offline preparation finished. No credential was read and no Microsoft request was made.'
        exit 0
    }

    $step = 'live-run'
    $names = @('ANALYTICSET_HUB_CONFIG', 'ANALYTICSET_HUB_INPUT', 'ANALYTICSET_HUB_EXPERIMENT', 'ANALYTICSET_HUB_DISCOVERY')
    $previous = @{}
    foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    Push-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
    try {
        [Environment]::SetEnvironmentVariable($names[0], $ConfigurationPath, 'Process')
        [Environment]::SetEnvironmentVariable($names[1], $InputPath, 'Process')
        [Environment]::SetEnvironmentVariable($names[2], $ExperimentDirectory, 'Process')
        [Environment]::SetEnvironmentVariable($names[3], $DiscoveryPacketDirectory, 'Process')
        $testFilter = if ($Stage -ceq 'Discover') { 'FullyQualifiedName=AnalyticsET.Tests.StoreHubLiveValidation.DiscoverDatasets' }
        elseif ($Stage -ceq 'InstallsReport') { 'FullyQualifiedName=AnalyticsET.Tests.StoreHubReportLiveValidation.RecordInstallsReport' }
        elseif ($UseUtcTimestampPreview) { 'FullyQualifiedName=AnalyticsET.Tests.StoreHubQueryLiveValidation.RecordInstallsUtcTimestampPreview' }
        else { 'FullyQualifiedName=AnalyticsET.Tests.StoreHubQueryLiveValidation.RecordInstallsPreview' }
        # Windows PowerShell 5.1 turns redirected native stderr into errors; under Stop the first line would hide the marker.
        $ErrorActionPreference = 'Continue'
        try {
            $nativeOutput = @(& $DotnetPath test 'tests/AnalyticsET.Tests/AnalyticsET.Tests.csproj' --configuration Release --no-build --no-restore --filter $testFilter --logger 'console;verbosity=detailed' 2>&1 |
                ForEach-Object { [string]$_ })
            $nativeExit = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = 'Stop' }
        $markers = @($nativeOutput | ForEach-Object {
            $line = [string]$_
            if ($line -match '^\s*ANALYTICSET_HUB_RESULT (\{.*\})\s*$') { $Matches[1] }
        })
        $nativeOutput = $null
        $step = 'result-validation'
        if ($nativeExit -eq 130 -and $markers.Count -eq 0) { throw [OperationCanceledException]::new() }
        if ($markers.Count -ne 1) { throw 'No unique marker.' }
        $result = $markers[0] | ConvertFrom-Json
        if ($result.exitCode -notin @(0, 1, 2, 130)) { throw 'Invalid result code.' }
        $hubExit = [int]$result.exitCode
        if ($result.PSObject.Properties['packetDirectory']) {
            if ($nativeExit -ne 0) { throw 'Native process did not complete.' }
            $packetDirectory = [IO.Path]::GetFullPath([string]$result.packetDirectory)
            $runId = [Guid]::Empty
            $leaf = [IO.Path]::GetFileName($packetDirectory)
            $prefix = $Stage + '-'
            if ([IO.Path]::GetDirectoryName($packetDirectory) -cne $ExperimentDirectory -or -not $leaf.StartsWith($prefix) -or
                -not [Guid]::TryParseExact($leaf.Substring($prefix.Length), 'D', [ref]$runId)) { throw 'Unexpected packet path.' }
            $step = 'packet-permissions'
            Assert-PrivatePath $packetDirectory
            Assert-PrivateAcl $packetDirectory $true
            $receiptPath = Join-Path $packetDirectory 'receipt.json'
            $evidencePath = Join-Path $packetDirectory 'evidence.json'
            Assert-PrivateAcl $receiptPath $false
            Assert-PrivateAcl $evidencePath $false
            $step = 'packet-size'
            if ([IO.FileInfo]::new($receiptPath).Length -gt 8192 -or [IO.FileInfo]::new($evidencePath).Length -gt 4194304) { throw 'Evidence bound exceeded.' }
            $step = 'receipt-hash'
            $receipt = [IO.File]::ReadAllText($receiptPath) | ConvertFrom-Json
            if ($receipt.schemaVersion -ne 1 -or $receipt.runId -cne $runId.ToString('D') -or $receipt.stage -cne $Stage -or
                $receipt.exitCode -ne $hubExit -or $receipt.evidenceSha256 -cne (Get-Sha256 $evidencePath)) { throw 'Receipt mismatch.' }
            $step = 'packet-context-source'
            $evidence = [IO.File]::ReadAllText($evidencePath) | ConvertFrom-Json
            $testAssemblyPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'tests/AnalyticsET.Tests/bin/Release/net10.0/AnalyticsET.Tests.dll'
            if ($evidence.schemaVersion -ne 1 -or $evidence.runId -cne $receipt.runId -or $evidence.stage -cne $Stage -or
                $evidence.exitCode -ne $hubExit -or $evidence.inputSha256 -cne (Get-Sha256 $InputPath) -or
                $evidence.contractSha256 -cne 'da38b521fb5b2d5bfda47e11adcb06214801126ecc446bc2ab91da25ae1b3742' -or
                $evidence.sourceSha256 -cne (Get-Sha256 $testAssemblyPath)) { throw 'Evidence context/source mismatch.' }
            if ($Stage -ceq 'Installs' -and $hubExit -eq 0) { throw 'A query preview cannot establish export completion.' }
            if ($Stage -ceq 'Installs') {
                $expectedWireFormat = if ($UseUtcTimestampPreview) { 'utc-timestamp' } else { 'date-only' }
                if ($evidence.plan.dateWireFormat -cne $expectedWireFormat) { throw 'Date representation mismatch.' }
            }
            if ($Stage -ceq 'InstallsReport') {
                $step = 'report-file'
                if ($evidence.plan.dateWireFormat -cne 'date-only' -or $evidence.proofBasis -cne 'baseline-identity') { throw 'Report scope mismatch.' }
                $reportPath = Join-Path $packetDirectory 'report.csv'
                $downloaded = $null -ne $evidence.download -and $null -ne $evidence.download.sha256
                if ($downloaded) {
                    Assert-PrivateAcl $reportPath $false
                    if ([IO.FileInfo]::new($reportPath).Length -ne [long]$evidence.download.bytes -or
                        $evidence.download.sha256 -cne (Get-Sha256 $reportPath)) { throw 'Report file mismatch.' }
                }
                elseif ([IO.File]::Exists($reportPath) -or $hubExit -eq 0) { throw 'Unexpected report file state.' }
            }
            if ($Stage -ceq 'Discover' -and $hubExit -eq 0 -and ($null -eq $evidence.metadata -or $evidence.outcome -cne 'metadata-recorded')) { throw 'Incomplete metadata.' }
            if ($Stage -ceq 'Discover') { Write-Output "Discovery packet path: $packetDirectory" }
            elseif ($Stage -ceq 'InstallsReport') { Write-Output "Installs report packet path: $packetDirectory" }
            else { Write-Output "Installs query packet path: $packetDirectory" }
            Write-Output "$Stage exit: $hubExit. Exports, comparisons, inventory completeness and v0.2 remain unverified."
            if ($Stage -ceq 'Installs') { Write-Output 'Read-only query preview recorded; date boundaries remain unverified. No query/report resource was created.' }
            if ($Stage -ceq 'InstallsReport') {
                $recorded = @($evidence.creation | Where-Object { $_.state -ceq 'recorded' }).Count
                Write-Output "Outcome: $($evidence.outcome). Reporting resources recorded in Partner Center: $recorded of 2 (query and one-time report)."
                Write-Output "CSV downloaded: $downloaded."
                if ($null -ne $evidence.analysis) {
                    $a = $evidence.analysis
                    Write-Output "CSV rows: $($a.rowCount); rows before start: $($a.rowsBeforeStart); in window: $($a.rowsInWindow); on end day: $($a.rowsOnEndDay); after end day: $($a.rowsAfterEndDay)."
                    Write-Output "End boundary: $($a.boundaryState). Days with equal totals to the baseline: $($a.matchingDays) of $(@($a.days).Count)."
                }
                Write-Output 'The comparison is evidence for review; DH6, task 3.3 and v0.2 stay unaccepted.'
            }
            if ($Stage -ceq 'Discover' -and $null -ne $evidence.metadata) {
                $count = @($evidence.metadata.datasets).Count
                Write-Output "Normalized datasets recorded: $count."
                foreach ($target in @('Installs', 'AddOnAcquisitions')) {
                    $present = @($evidence.metadata.datasets | Where-Object { $_.datasetName -ceq $target }).Count -eq 1
                    Write-Output "$target discovered: $present."
                }
            }
        }
        elseif ($hubExit -eq 1) { Write-Output 'Discovery prerequisite/recording failed. Inspect private fields and permissions locally.' }
        else { throw 'Missing private packet.' }
    }
    finally {
        foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
        Pop-Location
    }
}
catch [System.Management.Automation.PipelineStoppedException] {
    [Console]::Error.WriteLine('Store discovery cancelled. Only a verified receipt can establish recorded evidence.')
    $hubExit = 130
}
catch [OperationCanceledException] {
    [Console]::Error.WriteLine('Store discovery interrupted. Verify private receipts locally.')
    $hubExit = 130
}
catch {
    # Only these fixed categories can reach ordinary output; exception text is never printed.
    $allowedCategories = @('publisher-alias-invalid', 'publisher-alias-not-found', 'publisher-alias-ambiguous',
        'store-analytics-binding-missing', 'store-account-scope-missing', 'parent-store-id-invalid',
        'subscription-store-id-invalid', 'parent-child-id-equal', 'sample-confirmation-required',
        'utc-date-format-invalid', 'utc-start-date-invalid', 'utc-end-date-not-completed',
        'utc-window-end-not-after-start', 'utc-window-longer-than-seven-days')
    $safeCategory = if ($setupCategory -cin $allowedCategories) { $setupCategory } else { 'private-prerequisite-failed' }
    [Console]::Error.WriteLine("Store discovery stopped at $step ($safeCategory). Inspect the private prerequisites locally; do not paste IDs, JSON or credentials.")
    $hubExit = 1
}
exit $hubExit

