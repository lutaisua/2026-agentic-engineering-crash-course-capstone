# AnalyticsET Microsoft Entra application setup

Last updated: 2026-10-06.

This document tracks registration progress and explains private local setup.
Actual registration identifiers are stored on the operator's PC in
`%USERPROFILE%/.analyticset/onboarding/entra-application.json`.
Report access has not yet been verified with Microsoft.

For an optional replacement registration, use the
[new Store application procedure](new-entra-application.md). It specifies proposed
values and private profile changes. The operator now reports replacing Client ID
in both Store runtime bindings and saving the new secret in Windows Credential
Manager. The operator confirmed the new application appears in the main
Partner Center list with Manager(Windows), and that the new credential has a
different address. The replacement's mapped credential was subsequently accepted
in a read-only diagnostic. Its first result was empty; newer reviews now return
85 apps, with publication/account gaps and add-on HTTP 400. See the
[replacement diagnostic](verification/store-replacement-diagnostic.md).

## Recorded registration details

| Setting | Value / confirmation |
|---|---|
| Application name | `AnalyticsET`, shown in the earlier Partner Center registration form |
| Application (client) ID | Original registration record is private; operator reports replacing both Store runtime Client IDs with the new application ID. The two runtime IDs match and offline validation passes; no actual value is shown |
| Directory (tenant) ID | Entered locally and read only by the private runtime; never displayed or copied into shared documentation |
| Registration workflow | Microsoft Entra application creation through Partner Center |
| Selected developer-program role | **Manager(Windows)** only, confirmed by the operator and the role-selection screenshot |
| Other listed roles | Developer, Business Contributor, Finance Contributor, and Marketer were not selected |
| Application added to the publisher's Partner Center account | Original app had a disabled checked picker entry and missing roles entry. Operator now confirms the replacement appears in the main list with Manager(Windows); new application inventory/report access remains unverified |
| Client secret storage | Windows Credential Manager confirmed; original and replacement token requests accepted their mapped secret values, never displayed. Secret ID is unused |
| Secret expiry | Original secret: **October 4, 2028 (`2028-10-04`)**, confirmed by the operator. New secret: expiry not yet supplied; do not inherit the original expiry |
| Reply URL | Previously suggested: `https://localhost:3000/analyticset/callback`; the saved value has not been confirmed |

The Client ID identifies the application. The Tenant ID identifies the Entra
directory in which it authenticates. Both are identifiers, not credential values;
the operator prefers to keep actual values private.

### Replacement runtime checkpoint (2026-10-06)

After the operator's replacement report, the protected runtime configuration was
validated offline: **schema 2, one Store publisher, exit 0**. Both Store Client ID
bindings match; both credential references have mapping entries and share a
reference. The mapping is schema 1. This checked mapping presence, not whether
its target is the newly saved vault entry. No credential value was read and no
Microsoft request was made. The canonical onboarding record was not inspected
or changed. The operator subsequently confirmed the new application's saved
Manager(Windows) role after reloading the main list. The new credential has a
different address. A subsequent live diagnostic accepted the mapped credential:
token and app collection HTTP 200, decoded audience/tenant/client matching, but
zero apps. The [replacement diagnostic](verification/store-replacement-diagnostic.md)
records the earlier failed inventory comparison and later populated reviews
(85 apps, including 30 with unknown publication), plus add-on collection HTTP 400.
Publisher association remains unverified against the actual product-owning account.

If the existing vault address was reused, the mapping target can remain unchanged;
increment its generation label for the deliberate replacement. If a different
vault address was created, update/add the protected mapping and point both Store
bindings at the intended safe reference. See
[credential references](credential-references.md).

For this confirmed different-address case, keeping the existing shared safe
reference is possible: update that entry's `target` to the exact new Generic
Credential address and advance `generation`, preserving other mappings and file
permissions. The mapping stores no password. Validate offline, then run the
[private read-only comparison](verification/store-discovery-live.md#read-only-discovery).
Do not reuse earlier account evidence for the changed application binding.

Manager role selection is recorded separately from successful authentication and
report authorization. The first authorized live run acquired a Store token and
returned zero apps/add-ons; account attribution is still unverified. No analytics
or financial report request has been performed.

## What application association means

The association is the saved addition of the Entra application to the publisher's
Partner Center account with its Partner Center role. Microsoft's
[Store analytics prerequisites](https://learn.microsoft.com/en-us/windows/uwp/monetize/access-analytics-data-using-windows-store-services#step-1-complete-prerequisites-for-using-the-microsoft-store-analytics-api)
require this association and the Manager role. Creating an app registration in
Entra alone does not grant the publisher's Store reporting access.

In **Partner Center > Account settings > User management > Microsoft Entra
applications**, reopen **AnalyticsET** for the intended publisher and confirm the
matching Client ID and saved **Manager(Windows)** role. The official
[application-management instructions](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/manage-azure-ad-applications-in-partner-center)
finish creation with **Create**, adding an existing application with **Add**, and
role changes with **Update**.

If the application/role is present when reopened for the intended publisher, this
setup requirement is satisfied. The earlier operator confirmation is historical:
the original application was reported absent from the main application/roles
list despite being visible in the Add picker. The replacement's saved role is now
operator-confirmed, as recorded above. Original token acquisition passed,
but inventory was empty despite roughly 43 published apps and 86 add-ons in the
publisher dashboard. Account attribution and report access remain unverified.

### Restore a missing Partner Center association

First confirm the selected Partner Center publisher and tenant are the ones that
own those products. Entra registration and Partner Center access are separate.
Microsoft's [removal documentation](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/manage-azure-ad-applications-in-partner-center#remove-a-microsoft-entra-application)
states that removing Partner Center access does not delete the Entra application.
We cannot determine why this application's list entry disappeared from the current
evidence.

For an existing registration, follow Microsoft's
[Add Microsoft Entra application](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/manage-azure-ad-applications-in-partner-center#add-microsoft-entra-application)
procedure:

1. In the publisher account containing the apps, open **Account settings > User
   management > Microsoft Entra applications**.
2. Select **Add Microsoft Entra application**, then the option to add an existing
   application. Search for AnalyticsET and verify the selected application's Client
   ID locally against the existing runtime profile.
3. Select it, choose **Next**, assign **Manager(Windows)**, then click **Add**.
4. Reload/reopen the list and application details; confirm the association, role,
   Client ID and Tenant ID persisted for this publisher.
5. Rerun read-only Store discovery and compare returned inventory with the dashboard.
   Do not attest account evidence until the discrepancy is resolved.

Microsoft documents a Partner Center Manager account plus Entra Global
Administrator permissions for managing applications. If the existing app is absent
from the picker, check the selected tenant and **Account settings > Tenants**;
the application's tenant must be associated with this publisher. See
[tenant association](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/associate-existing-azure-ad-tenant-with-partner-center-account).

The existing Client ID, Tenant ID and saved secret remain the configuration for
this existing registration. The successful token request established that the
configured credential worked during the 2026-10-05 diagnostic. Recovery adds Partner Center access;
it does not require replacing that registration or rotating its secret.
See [the sanitized live diagnostic](verification/store-association-diagnostic.md).

### Visible in the Add picker but missing from the roles list

The 2026-10-06 screenshot shows a grey checked AnalyticsET entry in the Add
picker. The operator confirmed its checkbox cannot be toggled and reports the
application missing from the main application/roles list. The background message
advises signing out and using a Manager account to manage a different Entra
tenant. That tenant-switching guidance does not establish an incorrect current
sign-in.

A disabled checked entry may indicate that the portal already considers the
application associated. This is an inference: Microsoft's application-management
page does not define this checkbox state. It does not prove that Manager(Windows)
is saved for the intended publisher. Check the following before repeating Add:

1. Confirm the signed-in user is in the intended linked tenant and has the human
   administrator permissions documented
   [above](#restore-a-missing-partner-center-association).
2. Verify the selected publisher owns the expected products and confirm the
   application's tenant under **Account settings > Tenants**. Check list filters,
   search and pagination; retry in a fresh private browser session.
3. If the existing application entry becomes visible, open it and confirm
   **Manager(Windows)**; save role changes with **Update**, then reopen it.
   If it becomes selectable as an unassociated application, complete **Next >
   roles > Add** using the documented procedure.
4. If the disabled checked entry and missing roles entry persist in the correct
   account/session, request Partner Center support to investigate the association.
   Keep the timestamp, sanitized error/status and request ID from any failed save.
   Microsoft's Store API guide links
   [assisted Partner Center support](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services#additional-help).

Privately compare the runtime Client ID and Tenant ID with the Entra registration
and linked tenant. In Entra **App registrations > AnalyticsET**, follow
**Managed application in local directory** to check the corresponding Enterprise
application/service principal and its Application ID. Microsoft explains these
separate objects in
[application and service principal objects](https://learn.microsoft.com/en-us/entra/identity-platform/app-objects-and-service-principals).
Use the Application (client) ID in the runtime profile, rather than an Object ID.
The two objects' distinct Object IDs are expected.

The historical token/collection HTTP 200 checks and matching token claims do not
resolve this portal inconsistency or the empty inventory mismatch. Keep the
existing registration and protected credential while investigating; do not
create replacement registrations or rotate the secret solely for this symptom.
No live check or new role confirmation is recorded for 2026-10-06.

## Private local storage

The canonical registration record is
`C:\Users\serge\.analyticset\onboarding\entra-application.json`. It was copied
there on 2026-10-05 after discovering that the initial AppData write had been
redirected into Codex's package cache. The new location is outside AppData.

The directory uses normal Windows user-profile permissions for terminal access.
The registration JSON has inheritance disabled and explicit access rules only
for the operator and SYSTEM. Its permissions, byte-for-byte equality with the
original, and physical path were verified without printing identifiers.
`Set-Location` succeeded during the migration. The operator's later helper read
of the canonical file succeeded. The original protected copy is retained
in the package cache; use the canonical file for future updates.
The record was stored as ordinary JSON protected by Windows file permissions; it
is not encrypted or a complete runtime publisher profile. During current setup,
the operator's Windows PowerShell read succeeded but JSON parsing failed with
ArgumentException. The actual contents have not been inspected. The
[masked recovery helper](runtime-profile-setup.md) preserves that record and creates
the runtime profile from locally entered identifiers. The operator confirmed
creation succeeded; the new schema 2 profile subsequently passed offline
validation after a backup-preserving alias correction. No credential was read
during that setup/validation step. Reconciliation of the older record remains a
private follow-up.

The `tenantId` field was `null` when migration was verified. The operator later
confirmed that Client ID and Tenant ID setup is complete; those later values are
used only by private runtime configuration/authentication and are not displayed.
Keep both values private.
Use placeholders or fictional values in shared documentation and examples.
Keep real publisher profiles in a user-restricted folder too, using the schema
in the [publisher setup guide](publisher-setup.md).

For the Windows console tool, the recommended arrangement is:

| Data | Storage |
|---|---|
| Tenant ID, Client ID, publisher/account/product identifiers | Private per-user configuration under `%USERPROFILE%/.analyticset`, with restricted Windows file permissions on configuration files |
| Secret ID and expiration date | Private credential metadata; the ID is not the secret value and is not used as `client_secret` |
| Client secret value | Existing Windows Credential Manager generic credential; the development resolver uses its value only at authentication |
| Authentication tokens | Store tokens remain in process memory; persisted/delegated token protection remains planned |
| Shared documentation and sample configuration | Setup instructions and placeholders/fictional identifiers |

[DPAPI CurrentUser](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope)
binds encryption to the Windows user context. Processes running as that user
remain able to decrypt; Windows administrators and SYSTEM are trusted privileged
contexts. A `.gitignore` rule prevents ordinary Git inclusion but does not restrict
file access. The [.NET Secret Manager](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
stores unencrypted development values, so it is not the proposed production
credential store.

For local client-secret storage, use **Windows
Credential Manager** with a separate generic credential for each intended
publisher/application context, or the operator's existing protected password
manager. Keep the secret value in the vault; Secret ID and expiry can remain
private metadata. Microsoft recommends the OS credential vault and DPAPI for
[local secret storage on Windows](https://learn.microsoft.com/en-us/windows/win32/secbp/handling-passwords#modern-credential-management).
Persisted token protection remains planned. The discovery development build
now has a protected generic-credential resolver, verified with a fake vault;
the accepted v0.1 binary does not read Credential Manager. Generic credentials can be accessed by processes running as
the same Windows user; see [Microsoft's generic-credential documentation](https://learn.microsoft.com/en-us/windows/win32/secauthn/kinds-of-credentials#generic-credentials).

The operator has confirmed that Secret ID and value are stored in a generic
credential, and provided a screenshot of the entry. Its actual target name and
credential contents are omitted from shared documentation. The operator confirmed
the original secret's expiry date is **October 4, 2028 (`2028-10-04`)**. The first
live token request accepted the original configured application's secret value.
The replacement secret was accepted in the later read-only diagnostic; record
its own expiry privately. Newer inventory now contains apps, but account,
publication and add-on coverage are not complete.
The actual expiration timestamps remain unverified.
The [private runtime helper](runtime-profile-setup.md) connects the existing target
to a safe credential alias without reading its password. Live authentication
uses the implemented Store provider. The initial live run returned zero apps and
zero add-ons; private account comparison and reporting authorization remain separate
verification steps. No secret value or token was displayed or saved in review JSON.
A Windows Credential Manager target is distinct from the
current profile's safe lowercase `credentialReference` alias; keep any mapping
in private local configuration.

### PowerShell starting-directory errors

The operator's ordinary PowerShell and `cmd.exe` sessions could not find
`C:\Users\serge\AppData\Local\AnalyticsET`, even though the tool's processes
could see it. Changing directory permissions did not resolve this difference.
`GetFinalPathNameByHandle` confirmed the original file's physical location was:

```text
C:\Users\serge\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\AnalyticsET\onboarding\entra-application.json
```

Microsoft documents this behavior for packaged desktop apps: newly created
AppData files can be written to a private app location and appear merged into the
ordinary AppData path inside the app's context. See
[MSIX AppData virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization).
The earlier process checks shared that redirected view and therefore did not
establish access from the operator's independent terminal.

Open **Win+R** and launch Windows Terminal from the operator's home folder:

```text
wt.exe new-tab -d "C:\Users\serge" powershell.exe -NoProfile
```

Microsoft documents the `-d` option in the
[Windows Terminal command-line reference](https://learn.microsoft.com/en-us/windows/terminal/command-line-arguments).
In that PowerShell session, verify the account and try entering the private folder:

```powershell
whoami
Set-Location -LiteralPath 'C:\Users\serge\.analyticset\onboarding'
```

The expected operator account is `SURFACELSM\serge`. The file's new physical path
was verified directly and matches the path under `.analyticset`; it is not in the
package cache. The Codex sandbox account is still denied access to the private
registration file.

## Obtain the Tenant ID

The Tenant ID is required for the Store analytics client-credentials flow and for
the current publisher profile's `tenantId` field. Microsoft's documented token
endpoint is:

```text
https://login.microsoftonline.com/<tenant_id>/oauth2/token
```

In Partner Center, open **Account settings > User management > Microsoft Entra
applications**, select the application, and copy its **Tenant ID**. Microsoft
documents this alongside retrieving the Client ID and application key in the
[Store analytics authentication prerequisites](https://learn.microsoft.com/en-us/windows/uwp/monetize/access-analytics-data-using-windows-store-services).

Alternatively, in the Microsoft Entra admin center, open **Entra ID > Overview >
Properties** and copy **Tenant ID**, as described in
[Find your Microsoft Entra tenant ID](https://learn.microsoft.com/en-us/entra/fundamentals/how-to-find-tenant).
Use the directory associated with this application and publisher account.
Record the value in the private local JSON file rather than in this document.

The Store analytics client-credentials flow does not use the Reply URL. AnalyticsET
does not currently implement a browser callback. Financial reporting has a
separate delegated authentication setup planned for v0.4; its eventual redirect
URI and permissions must be verified for that flow.

## Remaining onboarding details

- Stable local publisher profile ID and display name.
- Store publisher account and actual application authorization context.
- Product discovery and a private Partner Center inventory comparison after
  the runtime profile is configured; schema 2 needs no hand-entered product IDs.
- Representative report dates and corresponding dashboard comparison.
- Private alias mapping to the existing Windows Credential Manager entry;
  do not put a client secret in chat,
  this document, profile JSON, or Git.

Use the [publisher setup guide](publisher-setup.md) for the profile schema and
keep real runtime configuration in a protected file under `%USERPROFILE%/.analyticset`
or an ignored `.local/` location with appropriate Windows permissions.
The [Store onboarding checkpoint](verification/store-onboarding.md) tracks the
remaining v0.2 gate. This Windows role selection does not establish Marketplace
or financial API access; those contexts require their own onboarding and checks.
See the [current development/setup status](project-status.md) for the overall
implementation and verification state.

## Credential needed for v0.2 live checks

Microsoft's [Store authentication prerequisites](https://learn.microsoft.com/en-us/windows/uwp/monetize/access-analytics-data-using-windows-store-services)
require the application's key value as `client_secret`, in addition to Tenant ID
and Client ID. In Partner Center, open the application's settings and choose
**Add new key**. Preserve the **Key value**, which is shown only when created;
record its expiry for renewal. See [Manage application keys](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/manage-azure-ad-applications-in-partner-center#manage-keys-for-a-microsoft-entra-application).

The secret value is not a key/secret identifier. Do not put it in chat, the
registration JSON, runtime publisher JSON, source control, or command-line arguments.
The accepted v0.1 binary has no secret import/store command or Windows Credential Manager reader.
The operator has already stored the Secret ID and value in Windows Credential
Manager. Keep that entry protected; use the implemented private reference mapping
before live authentication. No extra plaintext copy is
needed for the current onboarding record.
No actual secret is required to implement and test this component with fixtures.

The schema 2 runtime profile needs private publisher/account context and a
descriptive `credentialReference` pointing to the protected credential. Product
IDs come from discovery after authentication.
The account-context fields support local isolation; they are not extra parameters
in Microsoft's documented token request. Choose representative installs/ratings
dates and corresponding dashboard data for the live acceptance comparison.
