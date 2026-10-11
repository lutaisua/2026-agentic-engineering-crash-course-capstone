# Register a new AnalyticsET Store application

Reviewed: 2026-10-06. This procedure was recorded before replacement setup.
The operator subsequently reported replacing both Store runtime Client IDs and
saving a new secret. The [replacement checkpoint](entra-application-setup.md#replacement-runtime-checkpoint-2026-10-06)
records the passing offline check and current association/vault-target status.
The replacement's saved Manager(Windows) role is now operator-confirmed; its
new mapped credential was subsequently accepted. Later reviews now contain apps,
with incomplete account/publication evidence and add-on HTTP 400. See the
[replacement live diagnostic](verification/store-replacement-diagnostic.md).

This procedure covers the Store inventory and analytics authentication needed
for v0.2. Marketplace and financial authorization need their own verified setup;
the Windows role does not grant access to those programs.

## Register in the existing linked tenant

Use Microsoft's [app registration instructions](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app).
In the Entra admin center, select the directory already linked to the intended
Store publisher, then open **Entra ID > App registrations > New registration**.

| Setting | Proposed value |
|---|---|
| Name | `AnalyticsET-Store-2026` (a descriptive example distinguishing it from the earlier app) |
| Supported account types | Single tenant: accounts in this organizational directory only |
| Redirect URI / Reply URL | Leave blank for the current client-credentials flow |

Select **Register**. Privately record the new **Application (client) ID** and
**Directory (tenant) ID** from Overview; do not substitute Object ID. Using the
same directory retains the existing Tenant ID, while a new registration gets a
new Client ID. Multiple publisher profiles can use separate registrations; this
local setup does not require a single multitenant registration.

The current Store token request contains no redirect URI; see the
[Store analytics authentication request](https://learn.microsoft.com/en-us/windows/uwp/monetize/access-analytics-data-using-windows-store-services#step-2-obtain-a-microsoft-entra-access-token).
Keep public-client and implicit-grant options disabled for this setup. Do not add
Microsoft Graph directory permissions for the Store path; its authorization is
the documented Partner Center association and role.

## Associate with the intended Partner Center publisher

The human doing this must have Partner Center Manager access and Entra Global
Administrator permissions in that tenant, as documented in
[Manage Microsoft Entra applications](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/manage-azure-ad-applications-in-partner-center).

1. In the publisher owning the expected Store products, open **Account settings
   > User management > Microsoft Entra applications**.
2. Choose **Add Microsoft Entra application**, then add an existing application.
3. Select the new application, choose **Next**, select **Manager(Windows)**,
   and choose **Add**. Do not assign the application Entra Global Administrator.
4. Reload and reopen its main list entry. Privately compare Client ID and Tenant
   ID and verify that Manager(Windows) persisted. A checked picker alone is not
   this verification.

If creating directly through Partner Center instead, its form requires a Reply
URL. An unused example for this flow is
`https://localhost:3000/analyticset/store-2026/callback`; use a URL unique within
your directory. AnalyticsET has no listener at that address. The Entra-first
procedure above avoids this unused field.

If the new app develops the same disabled-picker/missing-roles symptom, follow
the [association troubleshooting](entra-application-setup.md#visible-in-the-add-picker-but-missing-from-the-roles-list).
A new registration does not guarantee resolution of a portal/account problem.

## Create and protect the new credential

In the new registration, choose **Certificates & secrets > Client secrets >
New client secret**. Suggested description: `AnalyticsET local Store access`;
suggested lifetime: **6 months**, subject to your organization's policy. Save
the generated **Value** immediately; it is shown only at creation. Record the
new Secret ID and actual expiration privately. The old secret and its recorded
expiry belong to the old registration.

Microsoft recommends certificate credentials for production; the current
AnalyticsET development implementation uses a client secret.
See [Microsoft's credential instructions](https://learn.microsoft.com/en-us/entra/identity-platform/how-to-add-credentials).

Create a separate Windows Credential Manager **Generic Credential**:

| Field | Value |
|---|---|
| Internet or network address | A distinct target you choose; fictional example: `AnalyticsET/example-store/new-app` |
| User name | Optional metadata such as Secret ID; AnalyticsET does not use it |
| Password | The new secret **Value**, not Secret ID |

Keep secret values out of profile JSON, command arguments, chat and Git. The
[credential-reference guide](credential-references.md) explains the vault mapping.

## Update the existing private runtime profile

Make protected backups first and preserve the current user/SYSTEM file access.
The helper is a creation tool and refuses existing destinations; rerunning it at
the default location does not update the existing profile.

In `%USERPROFILE%/.analyticset/credentials/references.json`, add a new safe
lowercase reference mapped to the new Generic Credential target, with its own
generation label. Preserve the old reference for rollback.

In the intended publisher in
`%USERPROFILE%/.analyticset/runtime/publishers.json`, update both
`authorization.store-analytics` and `authorization.store-inventory`:

- `clientId`: the new Application (client) ID.
- `credentialReference`: the new safe reference alias.

Keep the publisher's stable local ID and Store account context. Its `tenantId`
remains the same when registering in the same tenant. Each account association
must be verified separately; do not change unrelated publishers or programs.
Reconcile the older private onboarding record separately so it does not describe
the new application using old identifiers or an old expiry.

Run offline validation in your private terminal from the submission directory:

```powershell
dotnet bin/Release/net10.0/AnalyticsET.dll --config "$env:USERPROFILE\.analyticset\runtime\publishers.json" config validate
```

Keep its JSON output private. Offline success does not establish authorization.
Once the saved Partner Center role is verified, run read-only discovery and
compare identities, parents, publication and counts with the dashboard. A token
HTTP 200 or an empty app collection alone is not a successful inventory check.
Account evidence made for the earlier Client ID must not be reused as proof for
the new binding. Retain the earlier registration until the replacement is verified.
