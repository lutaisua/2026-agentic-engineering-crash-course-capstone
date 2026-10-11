[CmdletBinding()]
param(
    [string]$ConfigurationPath = (Join-Path $env:USERPROFILE '.analyticset/runtime/publishers.json'),
    [string]$InputPath = (Join-Path $env:USERPROFILE '.analyticset/diagnostics/store-subscriptions/sample.json'),
    [string]$OutputRoot = (Join-Path $env:USERPROFILE '.analyticset/diagnostics/store-subscriptions/runs'),
    [string]$DotnetPath = 'dotnet',
    [switch]$RunLive,
    [switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$stage = 'prerequisites'
$diagnosticExit = 1

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

if ($Help) {
    Write-Output 'Prepare a protected Subscription sample using masked local publisher/parent/add-on prompts.'
    Write-Output 'Default: offline preparation only; no credential read or Microsoft request.'
    Write-Output '-RunLive: explicitly authorize the fixed six-GET comparison through the Release Live test.'
    Write-Output '-ConfigurationPath, -InputPath, -OutputRoot: paths only; never put IDs or secrets in arguments.'
    Write-Output 'Build Release first. Existing inputs and prior packets are preserved.'
    Write-Output 'Exit: 0 recorded six-probe matrix (HTTP 400 allowed), 2 partial, 1 invalid/recording failure, 130 cancelled.'
    exit 0
}

try {
    if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Use Windows PowerShell 5.1.' }
    $ConfigurationPath = [IO.Path]::GetFullPath($ConfigurationPath)
    $InputPath = [IO.Path]::GetFullPath($InputPath)
    $OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
    $stage = 'configuration-access'
    Assert-PrivatePath $ConfigurationPath
    Assert-PrivateAcl ([IO.Path]::GetDirectoryName($ConfigurationPath)) $true
    Assert-PrivateAcl $ConfigurationPath $false
    if ([IO.FileInfo]::new($ConfigurationPath).Length -gt 1048576) { throw 'Configuration too large.' }
    $configuration = [IO.File]::ReadAllText($ConfigurationPath) | ConvertFrom-Json
    if ($configuration.schemaVersion -ne 2) { throw 'Use a schema 2 profile.' }
    $stage = 'private-storage'
    New-PrivateDirectory ([IO.Path]::GetDirectoryName($InputPath))
    New-PrivateDirectory $OutputRoot
    Assert-PrivatePath $InputPath
    if ([IO.File]::Exists($InputPath)) {
        Assert-PrivateAcl $InputPath $false
        Write-Output 'Existing private sample preserved; no new identifier prompts.'
    }
    else {
        $stage = 'masked-sample-input'
        $alias = Read-Masked 'Existing stable publisher alias from publishers.json'
        $parent = Read-Masked 'Parent application Store ID'
        $addon = Read-Masked 'Known Subscription add-on Store ID'
        if ($alias -cnotmatch '^[a-z0-9][a-z0-9-]{0,63}$' -or
            $parent -cnotmatch '^[A-Za-z0-9-]{1,128}$' -or $addon -cnotmatch '^[A-Za-z0-9-]{1,128}$' -or $parent -ceq $addon) {
            throw 'Malformed sample identifiers.'
        }
        $profiles = @($configuration.publishers | Where-Object { $_.id -ceq $alias })
        if ($profiles.Count -ne 1 -or
            $profiles[0].accounts.'microsoft-store'.discovery.enabled -ne $true -or
            -not $profiles[0].authorization.'store-inventory') { throw 'Selected Store inventory profile is unavailable.' }
        $confirmation = Read-Host 'Confirm in Partner Center that this Subscription belongs to this parent and publisher: type CONFIRM'
        if ($confirmation -cne 'CONFIRM') { throw 'Operator confirmation required.' }
        $stage = 'input-write'
        Write-NewInput ([ordered]@{
            schemaVersion = 1; publisherId = $alias; parentApplicationStoreId = $parent
            subscriptionStoreId = $addon; expectedProductType = 'Subscription'
            confirmedAt = [DateTimeOffset]::UtcNow.ToString('o')
        })
        $alias = $null
        $parent = $null
        $addon = $null
    }
    Write-Output "Private sample path: $InputPath"
    if (-not $RunLive) {
        Write-Output 'Offline preparation finished. No credential was read and no Microsoft request was made.'
        exit 0
    }

    $stage = 'live-run'
    $names = @('ANALYTICSET_LIVE_CONFIG', 'ANALYTICSET_DIAGNOSTIC_INPUT', 'ANALYTICSET_DIAGNOSTIC_OUTPUT')
    $previous = @{}
    foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    Push-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
    try {
        [Environment]::SetEnvironmentVariable($names[0], $ConfigurationPath, 'Process')
        [Environment]::SetEnvironmentVariable($names[1], $InputPath, 'Process')
        [Environment]::SetEnvironmentVariable($names[2], $OutputRoot, 'Process')
        # Detailed MSTest output stays in memory. Only the validated result marker is presented.
        $nativeOutput = & $DotnetPath test 'tests/AnalyticsET.Tests/AnalyticsET.Tests.csproj' --configuration Release --no-build --no-restore --filter 'FullyQualifiedName=AnalyticsET.Tests.StoreInventoryLiveDiagnostics.CompareKnownSubscriptionCollectionsAndDetail' --logger 'console;verbosity=detailed' 2>&1
        $nativeExit = $LASTEXITCODE
        $markers = @($nativeOutput | ForEach-Object {
            $line = [string]$_
            if ($line -match '^\s*ANALYTICSET_DIAGNOSTIC_RESULT (\{.*\})\s*$') { $Matches[1] }
        })
        $nativeOutput = $null
        $stage = 'result-validation'
        if ($nativeExit -eq 130 -and $markers.Count -eq 0) { throw [OperationCanceledException]::new() }
        if ($markers.Count -ne 1) { throw 'No unique diagnostic receipt marker.' }
        $result = $markers[0] | ConvertFrom-Json
        if ($result.exitCode -notin @(0, 1, 2, 130)) { throw 'Invalid result code.' }
        $diagnosticExit = [int]$result.exitCode
        if ($diagnosticExit -in @(0, 2, 130) -and $result.PSObject.Properties['packetDirectory']) {
            $stage = 'packet-location'
            if ($nativeExit -ne 0) { throw 'Test process did not complete.' }
            $packetDirectory = [IO.Path]::GetFullPath([string]$result.packetDirectory)
            $runId = [Guid]::Empty
            if ([IO.Path]::GetDirectoryName($packetDirectory) -cne $OutputRoot -or
                -not [Guid]::TryParseExact([IO.Path]::GetFileName($packetDirectory), 'D', [ref]$runId)) { throw 'Unexpected packet location.' }
            $stage = 'packet-permissions'
            Assert-PrivatePath $packetDirectory
            Assert-PrivateAcl $packetDirectory $true
            $receiptPath = Join-Path $packetDirectory 'receipt.json'
            Assert-PrivateAcl $receiptPath $false
            $stage = 'packet-receipt'
            $receipt = [IO.File]::ReadAllText($receiptPath) | ConvertFrom-Json
            if ($receipt.schemaVersion -ne 1 -or $receipt.runId -cne $runId.ToString('D') -or
                $receipt.exitCode -ne $diagnosticExit -or
                ($diagnosticExit -eq 0 -and $receipt.completion -cne 'recorded') -or
                ($diagnosticExit -ne 0 -and $receipt.completion -cne 'partial')) { throw 'Receipt mismatch.' }
            foreach ($file in @('evidence.json', 'support-attachment.md')) {
                $stage = 'packet-hashes'
                $path = Join-Path $packetDirectory $file
                Assert-PrivateAcl $path $false
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $hash = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($path))).Replace('-', '').ToLowerInvariant() }
                finally { $sha.Dispose() }
                $expected = if ($file -eq 'evidence.json') { $receipt.evidenceSha256 } else { $receipt.attachmentSha256 }
                if ($hash -cne $expected) { throw 'Packet hash mismatch.' }
            }
            $stage = 'packet-completion'
            $evidence = [IO.File]::ReadAllText((Join-Path $packetDirectory 'evidence.json')) | ConvertFrom-Json
            $observedCount = @($evidence.probes | Where-Object { $null -ne $_.status }).Count
            if ($evidence.exitCode -ne $diagnosticExit -or @($evidence.probes).Count -ne 6 -or
                ($diagnosticExit -eq 0 -and $observedCount -ne 6)) { throw 'Probe completion mismatch.' }
            Write-Output "Diagnostic packet path: $packetDirectory"
            Write-Output "HTTP probe outcomes observed: $observedCount of 6."
            Write-Output "Diagnostic exit code: $diagnosticExit. Inventory completeness and v0.2 acceptance remain unverified."
        }
        elseif ($diagnosticExit -eq 1) {
            Write-Output 'Diagnostic prerequisite or recording failed. Inspect configuration, mapping and private paths locally.'
        }
        else { throw 'Partial/cancelled result has no verified packet.' }
    }
    finally {
        foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
        Pop-Location
    }
}
catch [System.Management.Automation.PipelineStoppedException] {
    [Console]::Error.WriteLine('Diagnostic cancelled. A packet is usable only if its final receipt and file hashes verify.')
    $diagnosticExit = 130
}
catch [OperationCanceledException] {
    [Console]::Error.WriteLine('Diagnostic process interrupted. No completed packet was confirmed; check receipts locally.')
    $diagnosticExit = 130
}
catch {
    $category = $_.Exception.GetType().Name
    [Console]::Error.WriteLine("Subscription diagnostic stopped at $stage ($category). Check private paths, profile selection, permissions, sample and Release build locally; do not paste their contents.")
    $diagnosticExit = 1
}
exit $diagnosticExit

