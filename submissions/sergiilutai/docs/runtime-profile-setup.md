# Create the private runtime profile

Updated: 2026-10-05. This is an offline setup step for the discovery implementation
in progress. The Entra registration record and runtime publisher profile have
different purposes. Creating the profile does not prove Microsoft authorization.

Open your ordinary Windows PowerShell terminal as the same Windows user who saved
the registration record and Generic Credential. Run from the project:

```powershell
Set-Location -LiteralPath 'C:\Users\serge\Documents\ChatGPT\AnalyticsET\2026-agentic-engineering-crash-course-capstone\submissions\sergiilutai'
powershell.exe -NoProfile -File .\scripts\New-PrivatePublisherProfile.ps1
dotnet bin/Release/net10.0/AnalyticsET.dll --config "$env:USERPROFILE\.analyticset\runtime\publishers.json" config validate
```

The verified helper uses Windows PowerShell 5.1. It reads root `clientId` and
`tenantId` fields from `%USERPROFILE%\.analyticset\onboarding\entra-application.json`
without displaying their values. If your private record uses different fields,
adjust its field names locally; no identifiers need to be pasted into chat.
Build the Release binary first if it does not exist.

If the helper reports `registration-json`, its file read succeeded but Windows
PowerShell did not accept the JSON. The operator reported this condition; the
actual private content has not been inspected. Continue without altering that
record using masked local ID prompts:

```powershell
powershell.exe -NoProfile -File .\scripts\New-PrivatePublisherProfile.ps1 -PromptForRegistrationIds
```

Paste the application's Client ID and Directory Tenant ID into their respective
masked prompts. They do not appear in command-line arguments, shell history, or
ordinary output. This mode preserves the existing registration record and creates
the runtime profile from the locally entered identifiers. Repair/reconciliation
of that older record remains a separate private follow-up.

The helper prompts locally for:

- A stable publisher alias you choose locally, such as `publisher-one` (lowercase
  letters, digits, hyphens; at most 58 characters). Microsoft does not issue this
  alias. Choose a distinct alias for each publisher and preserve it during updates.
- A publisher display name.
- The Store publisher/account ID associated with this application in Partner
  Center. Copy the appropriate ID from your publisher's account settings. This is
  neither the Tenant ID nor a package certificate's publisher name.
- The exact **Internet or network address** of your existing Generic Credential.
  Enter its target name, not the password or Secret ID.

The saved `authorizedAccountId` is initially your declaration of that account's
application association. Successful account attribution still needs private
association evidence and comparison with Partner Center during live verification.
These local fields are not extra Microsoft token-request parameters.

The helper creates dedicated directories with access restricted to the current
Windows user and SYSTEM before writing identifiers:

| File | Purpose |
|---|---|
| `%USERPROFILE%\.analyticset\runtime\publishers.json` | Schema 2 Store profile, analytics/inventory authorization references, no product IDs |
| `%USERPROFILE%\.analyticset\credentials\references.json` | Private alias-to-existing-Generic-Credential mapping; no password or Secret ID |

It refuses repository paths, links, broad existing directory permissions, and
existing destination files. It preserves the registration record and never
reads or changes the Generic Credential. Actual credential availability remains
unchecked until an online provider uses the resolver. Check failures in your
private terminal; the error reports a setup stage without echoing input values.
If a file operation stops between writing the two files, inspect the protected
destinations locally before retrying. The helper will preserve an existing file.

Only Store is enabled because that is the account/application association currently
confirmed. Add a Marketplace program later after its account, application
permissions, inventory resources, and offer-type coverage are established.

`config validate` is offline and may display identifiers in the private terminal.
Share only the configuration path and success/failure status; keep its JSON output
private. After successful setup, the expected path is
`C:\Users\serge\.analyticset\runtime\publishers.json`.

## Recover an uppercase alias from the earlier helper

The operator confirmed successful profile/mapping creation using masked inputs.
The first offline check then rejected the publisher alias. Its backup-preserving
repair subsequently succeeded and offline validation passed (schema 2, one
publisher); no credential was read or Microsoft request made. An earlier helper used
PowerShell's default case-insensitive match and could incorrectly accept uppercase.
The new-profile check now requires lowercase before creating directories/files.

For that initial single-Store-publisher setup, run this explicit recovery in your
ordinary Windows PowerShell, with no concurrent AnalyticsET configuration edits:

```powershell
powershell.exe -NoProfile -File .\scripts\New-PrivatePublisherProfile.ps1 -RepairPublisherAlias
dotnet bin/Release/net10.0/AnalyticsET.dll --config "$env:USERPROFILE\.analyticset\runtime\publishers.json" config validate
```

Recovery lowercases the publisher alias and its two credential references, and
renames the matching mapping key. The existing Generic Credential target and its
password are unchanged and never read. The original JSON files remain in their
protected directories as uniquely named `.bak` files. Individual file replacements
are atomic; a failed second replacement rolls back the first. If the process or
machine stops between replacements, retain the private backups and reconcile both
files locally before retrying. A lowercase alias is a no-op. Other structures or
invalid characters are refused rather than guessed. This is an offline repair;
it does not verify account association or inventory completeness.

See [schema 2 and commands](publisher-setup.md#schema-2-discovery-profiles),
[credential mapping](credential-references.md), and
[the Store live checkpoint](verification/store-onboarding.md). API discovery and
report-export acceptance require later checks; setup alone does not accept v0.2.
