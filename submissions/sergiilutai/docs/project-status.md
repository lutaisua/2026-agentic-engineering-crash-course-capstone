# AnalyticsET development and setup status

Reviewed: 2026-10-11. Development binary version: **0.1.0**; v0.2 remains unaccepted.

**Report-path diagnostic (2026-10-11):** offline only, no live request; see the [checkpoint](verification/store-downloads-hub-checkpoint.md#report-path-diagnostic-2026-10-11) and [manifest](verification/store-downloads-hub-report-path-source-hashes.json) (236 non-live tests, 5/18).

**Date-echo and stderr fixes (2026-10-11):** offline only, no live request. The
[checkpoint](verification/store-downloads-hub-checkpoint.md#date-echo-and-stderr-fixes-2026-10-11)
and [manifest](verification/store-downloads-hub-date-echo-stderr-fixes-source-hashes.json)
record 198 non-live tests; v0.2 remains unaccepted and progress stays 5/18.

**Latest baseline and UTC preview follow-up (2026-10-11):** the small original
Installs file passed protected path/ACL/hash checks. Its 148 rows all match the
selected app and its 12 unique headers match native metadata. The operator
confirms a direct download on 2026-10-10, the matching two calendar days, Daily
aggregation and only the selected-app filter. Dates use month/day/year without
a timezone marker; no timezone was selected. Positive successful-install activity
is observed, but UTC/inclusion semantics remain unverified.
The tested explicit UTC timestamp control preserved query/app/binding/window;
token and preview again returned HTTP 200 with zero rows. Its receipt/source/input/
discovery/binding verified, exit 2. Date representation alone did not resolve the
discrepancy. No report resource or API CSV was created. Full verification passed
195 non-live tests, Release build and formatting; the
[87-file UTC preview manifest](verification/store-downloads-hub-utc-preview-source-hashes.json)
preserves earlier checkpoints. Seven harness/helper files changed; production
source is unchanged. Progress remains 5/18; task 2.1 and live gates stay unchecked.
Continue consumer query/date-scope validation before creation; remaining offline
reconciliation/polling/download work also remains pending. The historical
resumption notes below describe the earlier baseline wait.

**Resumed at the operator's request (2026-10-11).** The
[pause/resume checkpoint](pause-resume.md#current-resumption-2026-10-11) was read
first; all 85 saved source hashes and the executing assembly matched. The new
confirmed file has Installs headers and passed private path/ACL checks, but the
operator selected three months and downloaded it on 2026-10-10. That is broader
than the saved two-day API sample. Rows were not parsed, bytes were preserved,
and no comparison passed. Await a matching Custom/Daily Installs download as
installs-partner-center-small.csv and its displayed scope settings. Keep the
three-month file and original Acquisitions file. No new live request, vault read
or reporting POST was made during resumption.

**Store Downloads Hub creation checkpoint (2026-10-11):** change
`validate-store-downloads-hub-exports` has **5/18 tasks complete** (1.1–1.4 and 2.2).
Protected schema 1 input, explicit Store analytics selection, consumer dataset
discovery, normalized private evidence and verified receipts are implemented.
The tested helper now also supports a read-only Installs query preview. Narrow
queries for both datasets, query/productInfo compatibility and creation-proof
rejection are fixture verified. Atomic creation-intent/ID journals and the
single-send query/report boundary are now implemented and tested with fake HTTP.
Missing scope proof blocks them; uncertain creation blocks further POSTs. Remote
reconciliation, execution polling, downloads and complete live export stages remain
pending. Release build, **186 non-live tests** and formatting pass, with **87 matching
source files** and all 74 historical Subscription source hashes unchanged. See the
[checkpoint](verification/store-downloads-hub-checkpoint.md) and
[procedure](verification/store-downloads-hub-validation.md).
The earlier local setup failures are historical. The operator successfully used
two-day windows; the reviewed Discover receipt records exit **0**, **16 datasets**,
both targets present, and token/dataset **HTTP 200**. Actual Installs schema uses
ProductId, Date and SuccessfulInstallCount; AddOnAcquisitions provides ProductId,
ParentProductId and PurchaseQuantity. Empty columnFilters is a documented shape.
The [creation source manifest](verification/store-downloads-hub-creation-source-hashes.json)
preserves the earlier 153/167/177-test manifests. All 85 previous source files are
unchanged; the two new files implement and test creation in the separate harness.

The authorized read-only Installs preview returned **HTTP 200 with zero rows**,
recorded as exit **2**, hub-date-boundary-unverified. Its private receipt, source,
input, discovery and binding hashes match. No query/report resource was created.
The operator reports installs in Partner Center for the same app/window. This is
an unresolved discrepancy, not evidence of an empty activity window or API
non-support. A protected original Partner Center CSV and its scope settings are
needed to compare product identity, daily dates, filters and metric definitions.
UTC/inclusion semantics, product mapping and populated comparison remain unverified.
The operator supplied headerless rows and then confirmed downloading **Acquisitions**
from Downloads Hub. That export is not an equivalent Installs baseline; its
quantities have not been treated as install counts. The later confirmed Installs
file has the correct metric, but its three-month scope cannot accept the two-day
comparison. A matching short baseline is still needed. No raw rows or identifiers
were copied into shared project files.
Task 2.1 and tasks 2.7/3.1–3.4 stay unchecked. No report export or CSV comparison
has run. Normal reporting and sibling version/inventory gates remain pending.

**Subscription diagnostic implementation (2026-10-10):** a protected local helper
and dedicated read-only Live comparison are implemented. The runner checks the
known parent and Subscription detail before four collection variants, captures
allowlisted exact errors/correlations per attempt, and prepares a unique private
support packet with a verified final receipt. The complete offline suite passed
**135 non-live tests**, a Release build with zero warnings/errors and formatting.
The [diagnostic checkpoint](verification/store-subscription-diagnostic-checkpoint.md)
records the matching 74-file source manifest and each gate's evidence.
Actual live diagnostic DG7 was reviewed: the operator's protected six-probe run
on 2026-10-10 returned parent control **200** with matching Published metadata,
and Subscription detail plus all four collections **400** with
**NotAllowed / productType / Subscription**, extracted from a recognized message
pattern. The verified private packet matches the checkpoint's executing assembly.
No accepted Store correlation was captured; a token-only Entra request identifier
stays private. Detail identity/parent/wire metadata remain unavailable.
Follow the [diagnostic guide](verification/store-subscription-diagnostic.md).
The existing 105-test/source checkpoint remains historical. Production
Durable/Consumable policy, publication classification, inventory/account gates,
apps-and-add-ons scope and v0.2 acceptance are unchanged. The diagnostic change is
**14/14 tasks complete**; strict validation passed for it and both related changes.
It remains unarchived; no specs were synced.

**Historical Store implementation checkpoint:** parent failure isolation is implemented
and verified with a Release build, **105 non-live tests** and formatting.
Child failures no longer hide results for later parents; unresolved failure and
publication coverage still prevent successful snapshots. The refreshed live scope
remains 85 apps (55 with publication evidence, 30 unknown) and zero add-ons.
A published parent and the account-wide add-on collection both return HTTP 400
with and without `top=10`; an unknown parent returns HTTP 200 with zero children.
The first failing parent is published, so skipping reserved apps does not resolve
the error. The operator confirms that all publisher add-ons are **Subscription**.
The [subscription investigation](verification/store-subscription-inventory.md)
records an unresolved publisher API contract and a prepared Microsoft support
request; subscriptions are not silently excluded or inferred from analytics.
See
[the 105-test checkpoint](verification/v0.2-store-parent-isolation.md).

**Work resumed at the operator's request (2026-10-06).** The
[pause/resume checkpoint](pause-resume.md) was read first. All 59 implementation
and test source files matched the saved manifest at resumption; none were missing,
added or changed during the pause.
The repository remains on `codex/analytics-et-iterations` with the submission
untracked. Existing source/checkpoints were retained. At resumption, the original
Partner Center association/role status was unverified. The screenshot and
operator reply showed AnalyticsET with a disabled checked checkbox
in the Add picker, but missing from the main application/roles list. Tenant-switching guidance
is visible; it does not establish an incorrect current sign-in. The
[setup troubleshooting](entra-application-setup.md#visible-in-the-add-picker-but-missing-from-the-roles-list)
records the unresolved portal discrepancy and checks.
The operator subsequently reported replacing both Store Client IDs and saving a
new secret in Windows Credential Manager. The replacement runtime offline check
passed: schema 2, one Store publisher, matching Store Client IDs and both safe
references mapped. That offline step did not read the vault or contact Microsoft.
The operator subsequently
confirmed the replacement's main Partner Center entry with Manager(Windows),
and a new vault entry with a different address. The replacement credential was
then accepted in a read-only diagnostic: token and app collection **HTTP 200**,
decoded audience/tenant/client matching, but **0 apps**. The latest private
discovery review inspected at that point matched the offline attempt and was
partial/exit 2. The operator subsequently reported a populated review with exit 2.
Two newer reviews now show **85 apps: 55 with publication evidence and 30 unknown**,
with missing account evidence and failed add-on discovery. Their exact operator-
selected path awaits confirmation. A new read-only diagnostic recorded nine
app-page HTTP 200 responses and the first add-on collection **HTTP 400**.
Do not apply the earlier zero-app conclusion to this populated follow-up.
The operator now reports 30 apps in **Not started** or **Not submitted** states,
consistent with name-reserved/not-yet-published products. Private per-ID matching
to the 30 unknown API records is still required; runtime classification was not
changed from this count alone. The
[Store development next steps](store-development-next-steps.md) prioritize verified
draft handling, per-parent add-on HTTP 400 diagnosis, inventory/account acceptance,
and the remaining v0.2 report pipeline.
The diagnostic's nonempty-inventory assertion failed (test exit 1). Confirm the
product-owning Windows publisher and privately compare its Partner Center
Client ID/Tenant ID with the profile; do not create account evidence for the empty
result. See the [replacement diagnostic](verification/store-replacement-diagnostic.md) and
[replacement checkpoint](entra-application-setup.md#replacement-runtime-checkpoint-2026-10-06).
The protected runtime configuration was revalidated offline on 2026-10-06:
schema 2, one publisher, valid; credentials were not read and no Microsoft
account request was made for that validation. The previous 93-test evidence remains
historical. Subsequent independent task 3.2 work added Marketplace inventory token
bindings and seven tests. The updated Release build, **100 non-live tests**, and
formatting pass are recorded in
[the authentication foundation checkpoint](verification/v0.3-authentication.md).

The offline v0.1 foundation is accepted. The active
`discover-publisher-products` change has **17 of 31 tasks complete**: schema 2,
private inventory/credential references, CLI orchestration, and the Store
app/add-on provider are implemented and fixture verified. Store documentation
and its current verification checkpoint are updated. Marketplace
providers and report exports remain pending.

The original `develop-analytics-et-in-iterations` change remains **9 of 47 tasks
complete**. Its Store/Marketplace report and live gates are cumulative with the
new discovery requirements. Task counts and fixture results do not accept v0.2.

## Development completed

- .NET 10 local console application in `submissions/sergiilutai`, with offline
  profile CRUD, validation, explicit publisher selection, cancellation and help.
- Schema 2 discovery-ready profiles requiring no product IDs, typed export
  selections, and explicit migration to a new protected file. Original bytes and
  ambiguous legacy selection meaning are preserved.
- Private normalized inventory, independent refresh outcomes, lifecycle history,
  binding isolation, protected atomic snapshots and offline cache inspection.
- Private alias-to-Windows-Generic-Credential resolution; only the secret value
  is used at the authentication boundary. Offline validation never reads a vault.
- Store client-credentials authentication and a process-memory token cache scoped
  by publisher/account/application/resource/flow/credential generation.
- Marketplace inventory authentication bindings for Product Ingestion and
  submission, verified with fake HTTP/credentials and isolated from analytics
  and delegated financial access. Inventory providers remain pending.
- Store applications and per-app add-ons, all continuation pages, child detail
  and parent validation, publication proof, bounded retries and safe coverage
  outcomes. Unknown publication evidence keeps completeness unverified.
- `publisher onboard`, `products list`, `products refresh`, and offline product
  listing. Discovery does not create report jobs or publish products.

The [historical v0.1 acceptance](verification/v0.1.md) records 42 passing tests.
The [foundation checkpoint](verification/v0.2-foundation.md) records 71 tests at
that earlier source state. The Store implementation subsequently passed a Release
build with zero warnings/errors, **93 non-live tests**, and formatting, including
the helper alias correction and recovery checks. The
[Store checkpoint](verification/v0.2-store-discovery.md) and its source manifest
identify that source separately. Historical manifests are retained. The resumed
Marketplace authentication foundation has its own 100-test/source checkpoint.
No live request was made during that earlier foundation checkpoint; the later
replacement diagnostic above adds failed live evidence at the unchanged source.

## Entra and private runtime setup

The operator confirmed Client ID/Tenant ID setup, only **Manager(Windows)**,
initial Partner Center application association, and Secret ID/value storage in
Windows Credential Manager. Actual identifiers, credential targets and secret
values are omitted from shared files. The original secret's expiry is
**2028-10-04**; its time and time zone remain unspecified. The operator reports a
replacement secret, whose value was accepted in the later live diagnostic; its
expiry remains unverified. Manager selection alone does not establish
Store reporting, Marketplace or financial API permission.

The canonical registration record is
`%USERPROFILE%/.analyticset/onboarding/entra-application.json`. The earlier
AppData location was redirected into the desktop app's package cache. The
operator's later terminal successfully read the canonical file but rejected it
as JSON. Its contents have not been inspected; reconciliation remains private.

The operator now confirms the masked recovery helper created the schema 2
runtime configuration and credential-reference mapping. The runtime file is
`%USERPROFILE%/.analyticset/runtime/publishers.json`; the mapping is
`%USERPROFILE%/.analyticset/credentials/references.json`. Dedicated directories
restrict access to the operator and SYSTEM. Creation used local masked ID prompts
and made no credential read or Microsoft request.

The first actual offline validation rejected the publisher alias. The helper's
case-insensitive PowerShell regex could accept uppercase letters; the application
requires lowercase. The corrected helper and backup-preserving alias recovery
passed six checks, and actual recovery succeeded. Offline validation now passes:
**schema 2, one publisher**. Only validation status/counts and a static earlier
error were reported; identifiers and JSON were suppressed. The credential was
not read and no Microsoft request was made during setup/validation.

The first authorized live run resolved the saved Generic Credential and acquired
a token. Store discovery returned **zero apps and zero add-ons**, with complete
collection coverage but **unverified account attribution** (`account-evidence-missing`)
and overall partial/exit 2. Detailed JSON stays in the protected runtime directory;
only counts/categories were reported. The operator now reports roughly **43
published apps and 86 add-ons**, so the empty result fails account comparison.
The read-only diagnostic confirmed HTTP 200 for both token and app collection,
and decoded token claims match the configured audience/tenant/client.
AnalyticsET is reported missing from Partner Center's main application/roles
list, while the Add picker shows a disabled checked checkbox for the existing
application. [Reconfirm the saved association and role](entra-application-setup.md#visible-in-the-add-picker-but-missing-from-the-roles-list)
before another comparison; no successful complete snapshot was accepted.
This does not establish reporting permission or populated child coverage. Tokens
stay in process memory; persisted/delegated authentication is future scope.
The [diagnostic record](verification/store-association-diagnostic.md) distinguishes
the failed live gate from the passing 93-test non-live gate.

## Documentation and verification

| Area | Record |
|---|---|
| Requirements and project guidance | [PRD](prd.md), [AGENTS.md](../AGENTS.md) |
| Commands, schema 2, migration and cache | [README](../README.md), [publisher setup](publisher-setup.md) |
| Version gates | [Versions](versions.md) |
| Registration, roles and private storage | [Entra setup](entra-application-setup.md) |
| Local helper and recovery | [Runtime profile](runtime-profile-setup.md) |
| Credential-reference behavior | [Credential mapping](credential-references.md) |
| Store contracts and live evidence | [Contracts](verification/store-inventory-contracts.md), [private comparison](verification/store-discovery-live.md), [onboarding](verification/store-onboarding.md) |
| Subscription diagnosis and support | [Diagnostic procedure](verification/store-subscription-diagnostic.md), [support draft](verification/store-subscription-support-request.md) |

## Resume point

For the active Downloads Hub change, preserve the Discover, date-only and UTC
timestamp preview packets, the small original Installs CSV and earlier downloads.
The populated small baseline has matching app/calendar-day observations and
operator-confirmed Daily/selected-app scope, but both API previews are empty.
Investigate the consumer query/date interpretation without widening products or
dates; timezone/inclusion proof and populated API coverage remain unresolved. Then
continue one-time export implementation and both populated comparisons as their
prerequisites are verified. See the
[procedure](verification/store-downloads-hub-validation.md#installs-preview-discrepancy-and-next-evidence).
Existing Store inventory work below remains outstanding.

The Subscription comparison and protected support attachment have been reviewed.
Review the updated [support draft](verification/store-subscription-support-request.md)
and use authenticated Microsoft support if the operator decides to submit it.
No support case has been submitted. This sample does not authorize a complete
inventory, exports, or version acceptance. Subsequent development remains:

1. Follow the [Store next-step plan](store-development-next-steps.md). Match the
   30 unknown API records privately to the operator's Not started/Not submitted
   products and define verified draft handling; investigate add-on collection
   HTTP 400 without assuming the failing parent is unpublished. The
   replacement's saved Manager(Windows) role is operator-confirmed and token
   acquisition passed; the earlier empty result is historical. Privately compare
   app/add-on identities, parents, counts and publication with
   the selected Partner Center publisher, then record account evidence. Missing
   populated add-on or second-publisher evidence stays unverified.
2. Implement the sibling Store report path (installs/ratings, dates, pagination,
   CSV/raw JSON and manifests), then connect discovery to explicit export selection.
   Discovery task 2.7 depends on those missing commands; task 2.10 also requires
   their original live acceptance. Discovery alone cannot accept v0.2.
3. Continue Marketplace work against verified offer-type contracts and separate
   permissions after the cumulative v0.2 gate. Public Submission Swagger has now
   been retrieved. The [dated Marketplace contract research](verification/marketplace-inventory-contracts.md)
   clarifies that AI Apps and Agents is a category using underlying offer types,
   and records readable baseline schemas, a newer product schema, and remaining
   submission/version ambiguities. Task 3.1 remains in progress; no Marketplace
   adapter or live offer/plan coverage is implemented.

The checked toolchain is Node.js 24.19.0, OpenSpec 1.14.0 and .NET SDK 10.0.401.
OpenSpec lives in the parent AnalyticsET workspace. The submission remains an
uncommitted working tree on `codex/analytics-et-iterations`; no release, pull
request, spec sync or archive is recorded. Source manifests identify checkpoints.
