# Local publisher setup

Updated: 2026-10-05. The accepted v0.1 baseline uses schema 1. The current
development build also implements schema 2, migration, private credential
reference resolution, inventory orchestration/CLI boundaries, and the Store
app/add-on provider. Store HTTP fixtures pass; live access and report exports
remain unverified. Marketplace discovery returns `provider-unavailable-or-disabled`
(exit 2). Store without private comparison evidence returns partial coverage.

For your first real Store profile, use the
[private setup helper](runtime-profile-setup.md). Actual identifiers and mappings
stay outside the repository; credentials stay in Windows Credential Manager.

Use `--config <file>` anywhere in the command to select a non-secret JSON file.
The default is `%LOCALAPPDATA%/AnalyticsET/publishers.json`. Relative paths resolve
against the current directory. Help/version never load that file.

When running through a packaged desktop app, AppData writes can be redirected
to that app's private cache. For configuration shared with an independent terminal,
select a path outside AppData, for example:

```powershell
dotnet bin/Release/net10.0/AnalyticsET.dll --config (Join-Path $env:USERPROFILE '.analyticset/publishers.json') config path
```

The [registration troubleshooting notes](entra-application-setup.md#powershell-starting-directory-errors)
record the observed redirection and verified location of the onboarding record.
The onboarding JSON is separate from this runtime publisher configuration.

The [Entra application setup guide](entra-application-setup.md) records the selected
Partner Center role and explains where actual Client ID/Tenant ID values are kept
privately. Use it when completing a real profile; the current commands validate
local configuration without authenticating or confirming report permissions.

Keep actual identifiers in private local configuration with Windows file
permissions limited to the operator and trusted system contexts. Shared docs and
examples use placeholders or fictional identifiers. v0.1 stores plain JSON and
does not enforce restrictive Windows permissions automatically. Its inspection
commands display profile settings, so use a private console session for real
profiles. Credential references contain no secrets; DPAPI protection for secret
values and token caches remains planned for later authentication iterations.

The runnable [offline examples](examples/v0.1-smoke.ps1) exercise every command
below with two fictional profiles sharing a tenant. Run from the submission:

```powershell
powershell -NoProfile -File docs/examples/v0.1-smoke.ps1
```

The script prints its temporary configuration path and leaves the fictional
configuration there for inspection. It refuses to overwrite an existing file.
These examples do not authenticate or call Microsoft.

## Commands

Invoke `dotnet bin/Release/net10.0/AnalyticsET.dll` followed by:

| Arguments | Effect |
|---|---|
| `--help` | List commands offline |
| `--version` | Display application version |
| `--config <file> config path` | Display the resolved configuration path |
| `--config <file> publisher add --file <profile.json>` | Create a profile; create a schema-versioned configuration if absent |
| `--config <file> publisher list` | Display non-secret profile JSON |
| `--config <file> publisher update <id> --file <profile.json>` | Replace declared settings while retaining the stable ID and unrelated extension metadata |
| `--config <file> publisher remove <id>` | Remove the local profile |
| `--config <file> publisher show --publisher <id[,id]>` | Inspect explicitly selected profiles and authorization contexts |
| `--config <file> publisher show --all` | Inspect all configured profiles |
| `--config <file> config validate` | Validate the entire configuration offline |
| `--config <file> config validate --publisher <id[,id]>` | Validate configuration and inspect selected references |
| `--config <file> config validate --all` | Validate all profiles explicitly |

Only the global `--config` option can move freely. Keep other arguments in the
documented order. Unknown arguments, missing values, duplicate selections, and
unknown profile IDs return exit code 3.

## Schema 1 profile (accepted v0.1 compatibility)

Copy [publisher-one.json](examples/publisher-one.json) into a private local file
and replace the fictional identifiers. Configure only programs for which you know
the publisher account and product/offer IDs. Each enabled program requires account
settings and its analytics authorization settings. The optional `financial`
authorization entry records future financial setup; v0.1 does not sign in.

- `id`: stable lowercase letters/digits/hyphens, 1-64 characters, beginning and
  ending with a letter/digit. Windows device names such as `con` are prohibited.
- `displayName`: trimmed descriptive text.
- `tenantId`: required Entra tenant GUID, distinct from the application Client ID.
  Multiple profiles may share a tenant.
- `programs`: distinct `microsoft-store` and/or `azure-marketplace` values.
- `accounts.<program>.publisherAccountId`: program-specific publisher account ID.
- `accounts.<program>.productIds`: nonempty, distinct Store product IDs or
  Marketplace offer IDs. These are scope declarations; their entitlement is unverified.
- `authorization.<resource>.clientId`: Entra application GUID.
- `authorization.<resource>.authorizedAccountId`: the account/application context
  authorized for that resource. Record the actual authorized identity during onboarding.
- `authorization.<resource>.credentialReference`: a safe reference ID using the
  same identifier syntax as `id`. Put no credential values in profile JSON.

Resources are `store-analytics`, `marketplace-analytics`, and `financial`; their
audiences are respectively `https://manage.devcenter.microsoft.com`,
`https://api.partnercenter.microsoft.com`, and `https://api.partner.microsoft.com`.
Authorization storage keys include the profile, tenant, client, account, program,
resource, and reference. Sharing a tenant does not share a key.

The configuration envelope is `{"schemaVersion":1,"publishers":[...]}`. Unsupported
versions, duplicate JSON properties/profile IDs, null/missing required fields,
unknown programs/resources, and explicit secret/token fields are rejected without
echoing input values. Keep credentials out of arbitrary extension fields too.
Non-secret extension metadata is preserved across CRUD updates. Declared collections
are replaced on update, so provide the complete desired profile.

Writes use a lock and a temporary file in the destination directory, then replace
the complete configuration. A concurrent writer fails clearly rather than losing
another process's changes. The `.lock` file can remain after a successful command.

`config validate` reports `validationStatus: valid`, `authorizationStatus: unverified`,
and `credentialAvailability: not-checked-offline`. It does not resolve credentials,
test Entra consent/Partner Center roles, or confirm product/report access.

## Schema 2 discovery profiles

New profiles can omit product IDs. [publisher-discovery.json](examples/publisher-discovery.json)
is a runnable fictional Store profile. Its empty `productIds` and optional
`exportSelection` preserve the distinction between discovery and export scope.
Adding or validating this profile stays offline:

```powershell
dotnet bin/Release/net10.0/AnalyticsET.dll --config "$env:USERPROFILE\.analyticset\runtime\publishers.json" publisher add --file <private-profile.json>
dotnet bin/Release/net10.0/AnalyticsET.dll --config "$env:USERPROFILE\.analyticset\runtime\publishers.json" config validate
```

The new envelope is `{"schemaVersion":2,"publishers":[...]}`. Each enabled program
retains its account and analytics settings. Inventory authorization is separate:

| Resource key | Audience / flow |
|---|---|
| `store-inventory` | `https://manage.devcenter.microsoft.com`, client credentials |
| `marketplace-product-ingestion` | `https://graph.microsoft.com/.default`, client credentials |
| `marketplace-submission` | `https://api.partner.microsoft.com`, client credentials |

Missing inventory credentials are allowed during offline administration and
reported as unconfigured online. See [private reference mapping](credential-references.md).
Authorization keys include logical resource, authentication flow, and credential
generation in addition to publisher/program/account/tenant/client/reference.
Equal token audiences or tenants do not make inventory and reporting bindings equal.

`discovery.enabled` defaults to true. For Marketplace, private
`discovery.coverage.expectedOfferTypes` records the account's expected type scope.
`verifiedAt` and `evidenceReference` must be supplied together for private scope
attestation; they do not automatically establish authorization. Unknown types
remain unverified coverage. Store products/add-ons and Marketplace offers/plans
use distinct typed identities. Child `exportSelection` entries require `parentId`;
an empty selection does not opt into exporting everything.

## Explicit migration and rollback

```powershell
dotnet bin/Release/net10.0/AnalyticsET.dll --config <schema-1-file> config migrate --to-version 2 --output <different-private-file>
```

Migration requires a new file in a dedicated private directory outside Git and
OpenSpec workspaces. It preserves the original bytes, authorization references,
and extensions. Old product IDs become `kind: legacy` selections, whose app/add-on
or offer/plan meaning remains unresolved until inventory can match it unambiguously.
Discovery does not migrate files automatically. Rollback uses the original
configuration with the original binary; no private inventory is deleted.

## Inventory commands and private state

Each command requires `--publisher <id[,id]>` or `--all` and optionally
`--program microsoft-store` or `--program azure-marketplace`:

```powershell
dotnet bin/Release/net10.0/AnalyticsET.dll --config <private-schema-2-file> publisher onboard --publisher <id> --program microsoft-store
dotnet bin/Release/net10.0/AnalyticsET.dll --config <private-schema-2-file> products refresh --publisher <id>
dotnet bin/Release/net10.0/AnalyticsET.dll --config <private-schema-2-file> products list --all
dotnet bin/Release/net10.0/AnalyticsET.dll --config <private-schema-2-file> products list --publisher <id> --offline
```

Online commands refresh before returning inventory; they never start report jobs.
`--offline` is available only for `products list`; it reads matching last-success
state without resolving credentials or making HTTP requests. It includes cache
age, coverage, historical missing identities, and last-attempt metadata, and
labels authorization/credential generation unchecked offline. Missing or changed
configuration bindings cannot be presented as a matching cached success.

Default state is `%USERPROFILE%\.analyticset\inventory`. `--state <private-directory>`
selects a dedicated alternative. Current-user ownership and operator/SYSTEM ACLs
are verified for directories and files. Successful normalized snapshots commit
atomically under a per-profile/program lock; failures/cancellation preserve last
success and record separate attempts. Raw Microsoft payloads, tokens, secrets,
and unrelated resource-tree settings have no fields in persisted inventory.

Coverage states are `complete`, `not-applicable`, `unsupported`, `unverified`, and
`failed`. Only verified account binding/scope and complete applicable coverage
allow successful snapshots. Draft/preview/unknown publication is distinct from
availability; absence on refresh becomes `not-returned` history, not proof of
retirement. Store provider fixtures verify apps/add-ons and publication gaps;
live completeness is unverified. See [private Store comparison](verification/store-discovery-live.md).

## Exit codes

| Code | v0.1 meaning |
|---|---|
| 0 | Local command succeeded |
| 1 | Local file operation failed (permissions, storage, concurrent writer) |
| 2 | Incomplete discovery, unavailable provider, corrupt or missing matching cache |
| 3 | Invalid command, profile, configuration, or selection |
| 130 | Operation cancelled |

Schema 1 CRUD/help/version retain their offline behavior. Authentication,
Marketplace product providers, report cataloging, and exports still need their
version-specific implementation and acceptance gates.
