[CmdletBinding()]
param(
    [string]$RegistrationPath = (Join-Path $env:USERPROFILE '.analyticset/onboarding/entra-application.json'),
    [string]$PrivateRoot = (Join-Path $env:USERPROFILE '.analyticset'),
    [string]$PublisherId,
    [string]$DisplayName,
    [string]$PublisherAccountId,
    [string]$CredentialTarget,
    [switch]$PromptForRegistrationIds,
    [switch]$RepairPublisherAlias
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-RegistrationGuid([string]$Label) {
    $masked = Read-Host "$Label (masked local entry)" -AsSecureString
    $pointer = [IntPtr]::Zero
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($masked)
        $value = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        $parsed = [Guid]::Empty
        if (-not [Guid]::TryParse($value, [ref]$parsed) -or $parsed -eq [Guid]::Empty) {
            throw 'Registration identifiers must be nonempty GUIDs.'
        }
        return $parsed
    }
    finally {
        $value = $null
        if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
        $masked.Dispose()
    }
}

function Assert-PrivatePath([string]$Path) {
    $itemPath = [IO.Path]::GetFullPath($Path)
    while ($itemPath) {
        if (Test-Path -LiteralPath $itemPath) {
            $item = Get-Item -LiteralPath $itemPath -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Choose a physical private directory without links.'
            }
            if ((Test-Path -LiteralPath (Join-Path $itemPath '.git')) -or
                (Test-Path -LiteralPath (Join-Path $itemPath 'openspec'))) {
                throw 'Private files must be outside repository and planning workspaces.'
            }
        }
        $itemPath = [IO.Path]::GetDirectoryName($itemPath)
    }
}

function Assert-PrivateAcl([string]$Path, [bool]$RequireProtected) {
    $script:setupStage = 'acl-read'
    $entry = if ([IO.Directory]::Exists($Path)) { [IO.DirectoryInfo]::new($Path) } else { [IO.FileInfo]::new($Path) }
    $acl = $entry.GetAccessControl([Security.AccessControl.AccessControlSections]'Access, Owner')
    $script:setupStage = 'acl-owner-check'
    $userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    if (-not $acl.GetOwner([Security.Principal.SecurityIdentifier]).Equals($userSid) -or
        ($RequireProtected -and -not $acl.AreAccessRulesProtected)) {
        throw 'Private storage needs current-user ownership and protected directory permissions.'
    }
    $userAllowed = $false
    $systemAllowed = $false
    $script:setupStage = 'acl-rules-check'
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne 'Allow' -or
            (-not $rule.IdentityReference.Equals($userSid) -and -not $rule.IdentityReference.Equals($systemSid))) {
            throw 'Private storage must grant access only to the operator and SYSTEM.'
        }
        $full = ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq
            [Security.AccessControl.FileSystemRights]::FullControl
        if ($rule.IdentityReference.Equals($userSid) -and $full) { $userAllowed = $true }
        if ($rule.IdentityReference.Equals($systemSid) -and $full) { $systemAllowed = $true }
    }
    $script:setupStage = 'acl-full-access-check'
    if (-not $userAllowed -or -not $systemAllowed) { throw 'Operator and SYSTEM access is required.' }
}

function New-PrivateDirectory([string]$Path) {
    Assert-PrivatePath $Path
    if (-not (Test-Path -LiteralPath $Path)) {
        $security = [Security.AccessControl.DirectorySecurity]::new()
        $userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $security.SetOwner($userSid)
        $security.SetAccessRuleProtection($true, $false)
        foreach ($sid in @($userSid, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
            $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
                $sid, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow'))
        }
        $directory = [IO.DirectoryInfo]::new($Path)
        [IO.Directory]::CreateDirectory($directory.Parent.FullName) | Out-Null
        # Windows PowerShell 5.1 creates the leaf with its restricted ACL before writing identifiers.
        $directory.Create($security)
    }
    Assert-PrivateAcl $Path $true
}

function Write-NewPrivateJson([string]$Path, [object]$Value) {
    if (Test-Path -LiteralPath $Path) { throw 'Destination already exists; existing setup is preserved.' }
    $temporaryPath = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporaryPath, ($Value | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
        Assert-PrivateAcl $temporaryPath $false
        [IO.File]::Move($temporaryPath, $Path)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath }
    }
}

function Repair-ExistingPublisherAlias {
    $runtimeDirectory = Join-Path $PrivateRoot 'runtime'
    $mappingDirectory = Join-Path $PrivateRoot 'credentials'
    $configurationPath = Join-Path $runtimeDirectory 'publishers.json'
    $mappingPath = Join-Path $mappingDirectory 'references.json'
    foreach ($directory in @($runtimeDirectory, $mappingDirectory)) {
        Assert-PrivatePath $directory
        Assert-PrivateAcl $directory $true
    }
    foreach ($path in @($configurationPath, $mappingPath)) {
        Assert-PrivatePath $path
        Assert-PrivateAcl $path $false
    }
    $script:setupStage = 'alias-repair-read'
    $configuration = [IO.File]::ReadAllText($configurationPath) | ConvertFrom-Json
    $mapping = [IO.File]::ReadAllText($mappingPath) | ConvertFrom-Json
    if ($configuration.schemaVersion -ne 2 -or @($configuration.publishers).Count -ne 1 -or
        $mapping.schemaVersion -ne 1 -or @($mapping.references.PSObject.Properties).Count -ne 1) {
        throw 'Alias recovery supports only the initial single-publisher helper setup.'
    }
    $profile = $configuration.publishers[0]
    $oldAlias = [string]$profile.id
    $newAlias = $oldAlias.ToLowerInvariant()
    $oldReference = $oldAlias + '-store'
    $newReference = $newAlias + '-store'
    $mappingProperty = @($mapping.references.PSObject.Properties)[0]
    if ($newAlias -cnotmatch '^[a-z0-9](?:[a-z0-9-]{0,56}[a-z0-9])?$' -or
        $newAlias -cmatch '^(con|prn|aux|nul|com[1-9]|lpt[1-9])$' -or
        $mappingProperty.Name -cne $oldReference -or @($profile.programs).Count -ne 1 -or
        $profile.programs[0] -cne 'microsoft-store' -or
        @($profile.authorization.PSObject.Properties).Count -ne 2 -or
        $profile.authorization.'store-analytics'.credentialReference -cne $oldReference -or
        $profile.authorization.'store-inventory'.credentialReference -cne $oldReference) {
        throw 'Recovery requires the original helper profile and matching credential reference.'
    }
    if ($oldAlias -ceq $newAlias) {
        Write-Output 'Publisher alias is already lowercase. Existing files were preserved.'
        return
    }
    $profile.id = $newAlias
    $profile.authorization.'store-analytics'.credentialReference = $newReference
    $profile.authorization.'store-inventory'.credentialReference = $newReference
    $newReferences = [ordered]@{}
    $newReferences[$newReference] = $mappingProperty.Value
    $mapping.references = $newReferences
    $operation = [Guid]::NewGuid().ToString('N')
    $configurationTemporary = $configurationPath + '.' + $operation + '.tmp'
    $mappingTemporary = $mappingPath + '.' + $operation + '.tmp'
    $configurationBackup = $configurationPath + '.' + $operation + '.bak'
    $mappingBackup = $mappingPath + '.' + $operation + '.bak'
    $mappingReplaced = $false
    $configurationReplaced = $false
    try {
        $script:setupStage = 'alias-repair-prepare'
        [IO.File]::WriteAllText($configurationTemporary, ($configuration | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($mappingTemporary, ($mapping | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
        Assert-PrivateAcl $configurationTemporary $false
        Assert-PrivateAcl $mappingTemporary $false
        $script:setupStage = 'alias-repair-commit'
        [IO.File]::Replace($mappingTemporary, $mappingPath, $mappingBackup)
        $mappingReplaced = $true
        [IO.File]::Replace($configurationTemporary, $configurationPath, $configurationBackup)
        $configurationReplaced = $true
    }
    catch {
        if ($mappingReplaced -and -not $configurationReplaced) {
            $script:setupStage = 'alias-repair-rollback'
            # Windows PowerShell can coerce a null string argument to an invalid empty path.
            [IO.File]::Replace($mappingBackup, $mappingPath, $mappingPath + '.' + $operation + '.failed-repair.bak')
        }
        throw
    }
    finally {
        foreach ($temporary in @($configurationTemporary, $mappingTemporary)) {
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
        }
    }
    Write-Output 'Publisher and credential-reference aliases normalized to lowercase. Original files retained as private .bak files. No credential was read and no Microsoft request was made.'
    Write-Output "Configuration path: $configurationPath"
    Write-Output "Mapping path: $mappingPath"
}

$setupStage = 'registration-access'
try {
    if ($PSVersionTable.PSEdition -ne 'Desktop') {
        throw 'Run this helper with Windows PowerShell 5.1 (powershell.exe).'
    }
    if ($RepairPublisherAlias) {
        if ($PromptForRegistrationIds -or $PublisherId -or $DisplayName -or $PublisherAccountId -or $CredentialTarget) {
            throw 'Alias repair cannot be combined with new-profile inputs.'
        }
        Repair-ExistingPublisherAlias
        exit 0
    }
    if ($PromptForRegistrationIds) {
        $setupStage = 'masked-registration-input'
        $clientGuid = Read-RegistrationGuid 'Application Client ID'
        $tenantGuid = Read-RegistrationGuid 'Directory Tenant ID'
    }
    else {
        $setupStage = 'registration-path'
        Assert-PrivatePath $RegistrationPath
        $setupStage = 'registration-acl'
        Assert-PrivateAcl $RegistrationPath $false
        $setupStage = 'registration-read'
        $registrationText = [IO.File]::ReadAllText([IO.Path]::GetFullPath($RegistrationPath))
        $setupStage = 'registration-json'
        $registration = $registrationText | ConvertFrom-Json
        $registrationText = $null
        $setupStage = 'registration-fields'
        $clientGuid = [Guid]::Empty
        $tenantGuid = [Guid]::Empty
        if (-not [Guid]::TryParse([string]$registration.clientId, [ref]$clientGuid) -or
            -not [Guid]::TryParse([string]$registration.tenantId, [ref]$tenantGuid) -or
            $clientGuid -eq [Guid]::Empty -or $tenantGuid -eq [Guid]::Empty) {
            throw 'The registration record needs root clientId and tenantId GUID fields.'
        }
    }
    $setupStage = 'profile-input'
    if (-not $PublisherId) { $PublisherId = Read-Host 'Stable local publisher alias (lowercase, digits, hyphens)' }
    if ($PublisherId -cnotmatch '^[a-z0-9](?:[a-z0-9-]{0,56}[a-z0-9])?$' -or
        $PublisherId -cmatch '^(con|prn|aux|nul|com[1-9]|lpt[1-9])$') { throw 'Invalid publisher alias. Use at most 58 lowercase letters/digits/hyphens.' }
    if (-not $DisplayName) { $DisplayName = Read-Host 'Publisher display name' }
    if (-not $PublisherAccountId) { $PublisherAccountId = Read-Host 'Partner Center Store publisher/account ID' }
    if (-not $CredentialTarget) { $CredentialTarget = Read-Host 'Exact existing Generic Credential target (no password)' }
    foreach ($value in @($DisplayName, $PublisherAccountId, $CredentialTarget)) {
        if ([string]::IsNullOrWhiteSpace($value) -or $value -ne $value.Trim() -or $value -match '[\x00-\x1f]') {
            throw 'Setup values must be nonempty trimmed text without control characters.'
        }
    }
    $runtimeDirectory = Join-Path $PrivateRoot 'runtime'
    $mappingDirectory = Join-Path $PrivateRoot 'credentials'
    $setupStage = 'private-directories'
    New-PrivateDirectory $runtimeDirectory
    New-PrivateDirectory $mappingDirectory
    $configurationPath = Join-Path $runtimeDirectory 'publishers.json'
    $mappingPath = Join-Path $mappingDirectory 'references.json'
    $setupStage = 'existing-destinations'
    if ((Test-Path -LiteralPath $configurationPath) -or (Test-Path -LiteralPath $mappingPath)) {
        throw 'A runtime configuration or mapping already exists. Use the documented add/update workflow; this helper preserves it.'
    }
    $reference = $PublisherId + '-store'
    if ($reference.Length -gt 64) { throw 'Choose a publisher alias of at most 58 characters for the credential reference.' }
    $settings = [ordered]@{
        clientId = $clientGuid.ToString('D')
        authorizedAccountId = $PublisherAccountId
        credentialReference = $reference
    }
    $configuration = [ordered]@{
        schemaVersion = 2
        publishers = @([ordered]@{
            id = $PublisherId; displayName = $DisplayName; tenantId = $tenantGuid.ToString('D')
            programs = @('microsoft-store')
            accounts = @{ 'microsoft-store' = @{
                publisherAccountId = $PublisherAccountId; productIds = @()
                discovery = @{ enabled = $true; coverage = @{ expectedOfferTypes = @() } }
                exportSelection = @()
            } }
            authorization = @{ 'store-analytics' = $settings; 'store-inventory' = $settings }
        })
    }
    $references = @{}
    $references[$reference] = @{ target = $CredentialTarget; generation = '1' }
    $setupStage = 'mapping-write'
    Write-NewPrivateJson $mappingPath @{ schemaVersion = 1; references = $references }
    $setupStage = 'profile-write'
    Write-NewPrivateJson $configurationPath $configuration
    Write-Output 'Private schema 2 Store profile and credential-reference mapping created. No credential was read and no Microsoft request was made.'
    Write-Output "Configuration path: $configurationPath"
    Write-Output "Mapping path: $mappingPath"
    Write-Output 'Validate the profile offline before onboarding. Publisher association and inventory completeness remain unverified.'
}
catch {
    # Do not print PowerShell exception details, which can include registration data or arguments.
    $errorCategory = $_.Exception.GetType().Name
    [Console]::Error.WriteLine("Private profile setup stopped at $setupStage ($errorCategory). Check registration fields, owner/ACLs, input syntax, and existing destinations in your private terminal.")
    if ($setupStage -eq 'registration-json') {
        [Console]::Error.WriteLine('The private registration file could be read but was not accepted as JSON. Run again with -PromptForRegistrationIds to enter the two IDs locally using masked prompts. The existing registration record is preserved.')
    }
    exit 1
}
