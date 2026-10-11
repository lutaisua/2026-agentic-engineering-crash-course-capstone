# v0.2 Store onboarding checkpoint

Date: 2026-10-05  
Status: **Private profile offline-valid; live token acquired; account comparison failed; Partner Center association recovery required**
OpenSpec: `discover-publisher-products` extends the sibling v0.2 gate.

The operator confirmed that authorized accounts can be provided, supplied the
application Client ID, and confirmed selecting only **Manager(Windows)**.
The Client ID is stored in the operator's private local registration JSON. The
[Entra application setup guide](../entra-application-setup.md) records the role
selection and private storage instructions.
The operator subsequently confirmed that both Client ID and Tenant ID are set up
locally; their private values have not been inspected. The operator has now also
confirmed the Partner Center association and storage of Secret ID/value in a
Windows Credential Manager generic credential. The supplied screenshot shows the
credential entry; its secret value was subsequently resolved only inside the
authorized runtime token flow and never displayed. The operator confirms the
[local helper](../runtime-profile-setup.md) created schema 2 configuration and a
private credential mapping without product IDs using masked ID prompts. Its first
offline check rejected the alias; the backup-preserving recovery now succeeded
and offline validation passed (schema 2, one publisher), without reading a credential.
The first authorized Store run returned zero apps/add-ons with complete collection
coverage but missing account evidence (partial/exit 2). Account attribution,
populated coverage and report access remain unverified. The operator's dashboard
has roughly 43 published apps and 86 add-ons. A subsequent diagnostic received
HTTP 200 with an empty app collection and matching configured token claims. The
operator then found AnalyticsET absent from Partner Center while its Entra
registration remains; restore/reconfirm the existing application association.

## Official contract recheck

Checked the official Microsoft documentation on 2026-10-05:

- [Store prerequisites and authentication](https://learn.microsoft.com/en-us/windows/uwp/monetize/access-analytics-data-using-windows-store-services):
  associate the Entra application with the publisher's Partner Center account and
  assign the documented Manager role. Client credentials target resource
  `https://manage.devcenter.microsoft.com` at the tenant's `/oauth2/token` endpoint.
  Token issuance does not itself establish product/report permission.
- [Installs](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-app-installs):
  `GET /v1.0/my/analytics/installs`; required `applicationId`; date, aggregation,
  filter/group/order options; `Value`, `TotalCount`, and `@nextLink` pagination.
- [Ratings](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-app-ratings):
  `GET /v1.0/my/analytics/ratings`; required `applicationId`; date, aggregation,
  filter/group/order options; the same page envelope. Rating filters include
  `isRevised` in addition to applicable dimensions.

Both methods document page size up to 10,000. Use a smaller page size during live
pagination verification when data is available. Request explicit dates and align
the comparison's product, aggregation, dimensions, and data freshness. No report
request or token request was made during this documentation check.

## Required non-secret inputs

| Input | State |
|---|---|
| Stable publisher profile ID/display name | Entered locally; schema 2 validation passed after alias recovery |
| Entra application Client ID | Saved in private local registration JSON; see the [setup guide](../entra-application-setup.md) |
| Entra Tenant ID | Local setup confirmed by operator; private value not inspected |
| Store publisher account/application authorization context | Entered locally; empty API result mismatches roughly 43 published apps/86 add-ons |
| MSIX/UWP apps and add-ons | Discover through official APIs after private runtime setup; no manually entered IDs prerequisite |
| Manager(Windows) role selection | Confirmed by operator and screenshot; only selected role |
| Saved Partner Center application association | Previously confirmed; application now reported absent from Partner Center. Existing-registration recovery required |
| Store analytics access | Unverified; token acquisition passed, but no analytics report request has been performed |
| Representative date range and corresponding dashboard comparison | Awaiting selection |

The canonical registration record is under `%USERPROFILE%/.analyticset/onboarding`;
the initial AppData write was redirected to the desktop app's package cache.
Keep real runtime configuration in a protected file under `%USERPROFILE%/.analyticset`
or an ignored `.local/` location with appropriate permissions. Use `--config` for
an explicit runtime path; the onboarding record is not a complete publisher profile.
Restrict file permissions for actual identifiers; Git ignore rules alone do not
provide access control. Keep actual identifiers out of shared docs and examples.
Keep client secrets out of chat, repository files, ordinary output, and this record.
The discovery foundation now resolves private references with a Windows Generic
Credential adapter; tests use a fake vault. The actual credential was resolved
inside the first authorized live token flow without displaying it. Store
authentication, pagination, add-on resolution, and coverage handling pass fake HTTP
tests. Live inventory/account comparison and reporting authorization remain pending; see
[read-only discovery and private comparison](store-discovery-live.md) and the
[dated Store contracts](store-inventory-contracts.md).

Live authentication needs the application's client secret/key **value**
and its expiry. The operator confirms the value is already in Windows Credential
Manager. The operator confirmed expiry as **October 4, 2028 (`2028-10-04`)**;
an expiration time/time zone was not supplied. The helper created the private
mapping to the runtime credential reference and offline validation passed. Live
resolution/token acquisition also passed. Do not add the secret to registration/profile JSON.
See the [credential setup checklist](../entra-application-setup.md#credential-needed-for-v02-live-checks).
An actual secret is not required for implementation or fixture verification.

## Resume point

The private runtime profile is created and offline-valid. First follow
[association recovery](../entra-application-setup.md#restore-a-missing-partner-center-association),
then rerun discovery and resolve the mismatch documented in the
[live diagnostic](store-association-diagnostic.md). Complete
the remaining Store integration/live tasks, then privately compare discovered app/add-on
identities, parents, counts, and publication evidence with the selected Partner
Center account. Inventory is separate from analytics/report permissions; empty
add-on results do not verify populated add-on coverage. Complete the sibling
installs/ratings export tests and live comparisons too. v0.2 remains
unaccepted; v0.3-v1.0 remain pending. No later acceptance criterion was passed or
replaced with fixture-only evidence at this checkpoint.
