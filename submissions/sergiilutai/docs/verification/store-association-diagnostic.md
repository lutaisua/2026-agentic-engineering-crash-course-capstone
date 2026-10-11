# Store account association diagnostic

Date: 2026-10-05. **Live inventory/account gate failed; v0.2 remains unaccepted.**

The operator reports roughly **43 published Store apps** and **86 add-ons** across
published apps. The initial API result was zero apps/add-ons. A subsequent read-only
diagnostic reused the existing protected configuration and credential resolver:

- Token endpoint: HTTP **200**.
- App collection: HTTP **200**, empty inventory.
- Decoded token audience, Tenant ID and Client ID matched the configured values.
  This is a decoded-claim comparison, not signature validation or account proof.
- No app records were returned for child enumeration. Zero reported add-ons
  therefore does not prove the publisher has no add-ons.
- The Live-category assertion failed because a publisher known to have published
  apps must not be accepted from an empty inventory.

The operator then found **AnalyticsET absent from Partner Center's application
list while its registration still exists in Entra**. Earlier association
confirmation is historical; the current association must be restored/reconfirmed.
We do not know why it disappeared. Wrong publisher/tenant selection or a removed/
unsaved association must be distinguished locally.

Microsoft documents that removing an application from Partner Center revokes
Partner Center access while preserving the Entra registration. Existing applications
can be added back through User management, with the intended role and a final
**Add** action. See [Microsoft's application management guide](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/manage-azure-ad-applications-in-partner-center#add-microsoft-entra-application)
and [Store submission prerequisites](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services#how-to-associate-an-azure-ad-application-with-your-partner-center-account).

Follow the [association recovery procedure](../entra-application-setup.md#restore-a-missing-partner-center-association).
Preserve the existing registration, runtime profile and Generic Credential. Do not
write account-comparison evidence while the actual inventory does not match.

## Reproduce privately

This diagnostic is explicitly categorized `Live` and excluded by the ordinary
non-live verifier. It requires a single deliberately selected schema 2 publisher:

```powershell
$env:ANALYTICSET_LIVE_CONFIG = Join-Path $env:USERPROFILE '.analyticset/runtime/publishers.json'
dotnet test AnalyticsET.slnx --configuration Release --no-restore --filter FullyQualifiedName~StoreInventoryLiveDiagnostics --logger 'console;verbosity=detailed'
Remove-Item Env:\ANALYTICSET_LIVE_CONFIG
```

Its output contains only status codes, counts and identity-match booleans. It
never prints bearer tokens, secrets, private IDs, product names, response bodies
or request headers. Redirects are disabled and the existing bounded HTTP/reader
path supplies destination, size, retry, pagination and cancellation protections.
It sends token POST and read-only Store GETs; no publishing or report jobs.

The updated Release build passed with zero warnings/errors; **93 non-live tests**
passed separately, and formatting verification passed. The single authorized
live diagnostic **failed** with the mismatch above. These outcomes remain separate.
[Source identity](v0.2-store-association-diagnostic-source-hashes.json) includes the
new diagnostic; the earlier [Store checkpoint](v0.2-store-discovery.md) is retained.
Discovery task 2.9 remains unchecked, as do original report and later-publisher gates.
