# Design

## Context

See [proposal.md](proposal.md) for motivation. The implementation target is the
.NET 10 starter at
`../../../2026-agentic-engineering-crash-course-capstone/submissions/sergiilutai`.
Its [PRD](../../../../prd.md)
defines FR-01 through FR-14 and AC-01 through AC-13. The original proposal inspected a v0.0 starter; v0.1 is now accepted with offline profiles and 42 historical tests. Online authentication and API integration remain pending. Existing project settings enable
nullable references, XML documentation, and warnings as errors.

The planning home is outside the cloned capstone repository. Planning artifacts
remain here; code, tests, and later user documentation belong inside the submission.
Version labels below are proposed cumulative release milestones, not completed work
or calendar commitments. The inspected starter is the v0.0 baseline.

The linked [publisher-discovery change](../discover-publisher-products/design.md)
adds Store apps/add-ons in v0.2 and Marketplace offers/plans in v0.3. Its gates are
cumulative with the report gates below. Authentication precedes inventory discovery,
which precedes report product-scope resolution; pre-entered product IDs are no longer
an onboarding prerequisite for schema 2 profiles. Preserve the historical v0.1 gate.

## Goals / Non-Goals

**Goals:** Deliver an independently verifiable increment at each milestone; keep
Microsoft API families separable; retain publisher/program provenance; make release
claims traceable to tests and authorized live evidence.

**Non-Goals:** A remote backend, background scheduling, a GUI, reconciliation/accounting
decisions, and automatic publisher account creation. No attempt to recreate reports
without a public API. No promise of support for every Marketplace offer type.

## Decisions

### 1. One staged change, six release gates

Use one umbrella change with implementation tasks grouped by version. This keeps
shared contracts consistent while permitting the user to request one iteration at
a time. Every later gate depends on earlier gates; adding a connector must rerun
earlier verification. Separate unrelated features can become later changes.

An alternative is six independent changes now. That would duplicate shared profile,
output, and authentication decisions before the first connector is proven. This
proposal instead defines the complete direction while keeping per-version stopping
points explicit. Applying a single iteration never marks later tasks complete.

| Version | User-visible outcome | PRD traceability | Release evidence |
|---|---|---|---|
| v0.1 | Local publisher profiles, explicit selection, help/version, config validation | FR-01/02; foundations for FR-03/04/05 | Offline CLI and profile isolation tests |
| v0.2 | On-demand Store installs and ratings exports for one selected profile | FR-03/04/05/06/10/11/12/13; AC-03/07/08/09/10 | Pagination/CSV/error tests and an authorized Store comparison |
| v0.3 | Marketplace dataset discovery and one applicable CSV/TSV analytics export | FR-03/04/05/07/10/11; AC-04/07/08/10 | Asynchronous lifecycle tests and an authorized Marketplace comparison |
| v0.4 | Earnings/transactions and payments for Store and Marketplace | FR-03/05/08/10/11; financial part of AC-02; AC-05 | MFA/renewal/program-scoping tests and authorized financial comparisons |
| v0.5 | Multi-publisher batches with bounded retries and resumable asynchronous work | FR-02/09/11/12/13; AC-01/06/09/10 | Cross-publisher failure/cancellation tests and a two-publisher live run |
| v1.0 | Published local CLI with a verified report matrix and repeatable verification | All FRs and ACs for the advertised catalog | Cumulative acceptance matrix, clean-PC smoke check, release package and demo |

### 2. Concrete acceptance and verification for each version

#### v0.1 - Local publisher foundation

Acceptance:

- V01-A1: Create/list/update/remove two profiles without losing either profile's
  unrelated settings. IDs are unique; a tenant ID is not used as the profile ID.
- V01-A2: A profile can enable Store, Marketplace, or both and record Store IDs,
  publisher/account context, and resource-specific credential references.
- V01-A3: Duplicate IDs, missing mandatory fields, unsafe path IDs, unknown programs,
  and unknown publisher selections fail before network requests or export writes.
- V01-A4: Help/version and config validation work offline. No command implies that
  Microsoft access or a live export has been verified.

Verification: CLI integration tests for CRUD/invalid configuration, two profiles
sharing a tenant with separate account/resource settings, and an offline smoke
transcript. Use test doubles for credential references; real credentials are not
required to pass v0.1. Expected result is deterministic profiles/validation behavior,
not successful authentication. Record evidence in `docs/verification/v0.1.md` later.

#### v0.2 - First Store exports

Acceptance:

- V02-A1: An authorized profile authenticates for the Store resource and exports
  installs and ratings for an explicitly selected Store ID and date range.
- V02-A2: Follow all pages and preserve every retrieved row. CSV handles commas,
  quotes, line breaks, Unicode, nulls, and invariant numbers; raw JSON is an option.
- V02-A3: Files appear under the selected publisher and `microsoft-store`; the
  manifest records dates, scope, API, format, row count, and outcome. A second run
  preserves the first run's successful files.
- V02-A4: Expired tokens, insufficient access, unsupported date ranges, empty
  responses, cancellation, and interrupted file writes have distinct outcomes.
  Only fully written files are marked successful.

Additional required discovery gates: D1/D2/S1/S2/L1/R1 in the linked design,
covering schema migration, protected inventory, app/add-on pagination/publication,
authorized comparison, later publishers/products, and explicit report selection.

Verification: Fake HTTP fixtures containing at least two pages with distinct records,
empty data, 401/403, invalid dates, interrupted streams, and CSV edge cases. Perform
an authorized live installs/ratings export and compare it to Partner Center using
the same product, dates, dimensions, and source freshness. If the dashboard schema
differs, compare documented equivalent aggregates and record the mapping. Live
credentials or data absence blocks live verification, not the offline test suite.

#### v0.3 - First Marketplace exports

Acceptance:

- V03-A1: Discover and display available datasets and schema through API v1.1;
  distinguish a dataset from an existing scheduled report and from a verified
  implemented report adapter.
- V03-A2: Export one dataset applicable to the authorized publisher. Use
  `ISVMarketplaceInsights` as the initial candidate; the onboarding evidence fixes
  the exact accessible dataset and offer scope before this release is accepted.
  A different candidate requires an explicit planning update, not a silent fallback.
- V03-A3: Reuse a suitable system query or create an owned query; submit one-time
  generation, track pending/running/completed outcomes, and save the original CSV/TSV.
- V03-A4: Quota responses, stale links, failed execution, no-data results, and
  execution queries returning 404 before the first completion remain distinguishable.
  Polling never creates another report and cleanup never deletes unrelated resources.

Additional required discovery gates: M1/M2/L1/R1 in the linked design. Product
inventory authorization and offer/plan live evidence are separate from analytics
dataset access; coverage gaps prevent account-wide completeness claims.

Verification: Fixture-driven dataset/schema and lifecycle tests including the
initial completed-filter 404, missing download link, failure, and quota responses.
An authorized export must preserve source bytes and match the corresponding Partner
Center report or documented query result. Retain report/query/execution IDs in private
run state; share only sanitized evidence and schema/aggregate comparisons.

#### v0.4 - Financial exports

Acceptance:

- V04-A1: Complete the documented financial delegated sign-in with MFA on the local
  PC; renew access with isolated protected token state and surface reauthentication
  when renewal is rejected. A token alone is not reported as financial permission.
- V04-A2: Export earnings/underlying transactions and payments for each program
  using validated program/account filters. An ambiguous or mixed-program response
  fails provenance validation instead of being labeled as a valid program export.
- V04-A3: Preserve source identifiers, amounts, currencies, fees, taxes, refunds,
  and adjustments. Earnings and payments are distinct reports; analytics revenue
  is not substituted for either.
- V04-A4: Persist asynchronous request IDs, download only completed exports, and
  exclude secrets, tokens, signed links, and sensitive payloads from ordinary logs.

Verification: Prove the documented browser/redirect/token flow locally before
committing to the production authentication adapter. Tests cover token renewal,
reauthentication, program mapping, mixed/unknown programs, polling transitions,
failed downloads, and redaction. Authorized live checks must cover earnings and
payments for both Store and Marketplace, across whichever profiles have those
entitlements. Compare identifiers, totals by currency, and payment links against
matching official downloads. No cross-currency totals or accounting conclusions.

#### v0.5 - Reliable multi-publisher batches

Acceptance:

- V05-A1: Select one, multiple, or all profiles and export multiple reports across
  both programs. Credential and output scope stays bound to each item.
- V05-A2: A 403 or failed export for one publisher does not prevent independent
  publishers from completing; the summary reports partial failure accurately.
- V05-A3: Use bounded retries for transient/throttling failures, respecting retry
  hints and Marketplace quotas. Do not blindly retry report-creation POSTs with an
  uncertain outcome; record ambiguity and recover by ID when possible.
- V05-A4: Cancel a batch safely. Resume persisted asynchronous requests by their
  IDs without regenerating reports. Items without recoverable IDs require an
  explicit new export. Signed download URLs are reacquired rather than logged.
- V05-A5: Document exit codes and JSON summaries; reruns preserve earlier files and
  report empty successes separately from failures and unsupported items.

Verification: A mixed-success matrix with two profiles, two resources/programs,
403/429/5xx, token expiration, cancellation, and process restart. Assert request
authorization scope, output paths, outcomes, retry bounds, and no repeated creation
on polling/resume. Run an authorized batch with at least two distinct publisher
accounts and retain sanitized provenance evidence. Fixture isolation tests also
cover two different profile/account contexts within the same tenant.

#### v1.0 - Catalog coverage and local release

Acceptance:

- V10-A1: Publish a report coverage matrix for every candidate in PRD section 5:
  implemented and live verified, implemented but live blocked, unsupported by API,
  or not applicable to tested offers. Only implemented/live-verified adapters enter
  the supported release catalog; document any remaining PRD coverage gaps explicitly.
- V10-A2: Add Store usage/health/reviews and additional applicable Marketplace
  datasets according to the matrix. Confirm legacy acquisition endpoint behavior
  before claiming acquisition support. Every advertised report has a live acceptance
  record; dataset discovery alone is insufficient.
- V10-A3: All FR-01..14 and AC-01..13 pass for the advertised catalog. A skipped live
  check remains blocked and cannot make its report release-ready. Representative
  offer/account coverage is stated; no universal compatibility claim is made.
- V10-A4: A fresh Windows user can run the packaged CLI, follow onboarding/help,
  configure two publishers, and perform on-demand exports without a hosted backend.
  Package contents contain no credentials, cached tokens, or report data.
- V10-A5: A documented local verification command returns nonzero on build/test/
  formatting failure. Release evidence identifies version, code revision, test
  results, live report coverage, remaining gaps, and a short demonstration.

Verification: Run the cumulative offline suite and per-report live gates, review
the PRD traceability matrix, publish a Windows x64 self-contained package, and
perform clean-machine/user smoke checks. Retain versioned evidence with sanitized
commands and results. A review pass must check source, tests, contracts, and coverage
claims and record findings/fixes; no separate agent is required by this plan.

### 3. Shared contracts before connector growth

Keep `AnalyticsET.csproj` and `Program.cs` at their existing locations. Add feature
folders rather than moving the starter. Separate CLI orchestration, publisher
settings, authentication, report descriptors, API adapters, and output handling
through focused interfaces. Use a console host with dependency injection and
asynchronous handlers; keep console rendering out of connectors.

Add `AnalyticsET.slnx` and a `tests/AnalyticsET.Tests` project. Exclude the tests
subtree from the console project's default recursive compile items. Use MSTest
with the VSTest runner, FluentAssertions, and Moq when substitution is useful,
consistent with the loaded .NET guidance. Package choices and exact compatible
versions must be checked during implementation; no dependencies are added now.

This is simpler initially than multiple production assemblies, while interfaces
permit later extraction without changing the CLI or output contracts.

### 4. Explicit local configuration and authentication

Store versioned non-secret JSON configuration under the current Windows user's
local application data directory, with an explicit configuration-path override.
Separate protected credential/token storage from profile metadata; proposed Windows
storage uses user-scoped data protection and restrictive file access. Cache keys
include profile, tenant, client, authorized account, and resource. Tests use isolated
temporary locations. Exported configuration never includes credentials.

Store/Marketplace adapters use application credentials and their respective resource
audiences. Financial access follows Microsoft's documented delegated web-platform
authorization and renewal flow, with a local callback/redirect validated during the
v0.4 proof. Do not assume that public-client/device-code or app-only financial access
is interchangeable with this flow. A short-lived local callback listener is allowed;
an externally hosted service is not required. Secrets needed by a documented flow
are provided by the publisher and held in protected local storage.

One generic login/token is insufficient because audience, account association, and
authorization differ. Avoid password-grant workarounds. A local financial flow that
cannot be validated blocks that milestone and requires a reviewed design update.

### 5. Catalog and export contracts

Use curated Store descriptors and discovered Marketplace schema. A descriptor
records implementation status, applicability, live verification status, date limits,
and formats. Proposed commands are `publisher`, `auth`, `reports list`, `export`,
`runs status`, and `runs resume`; syntax is finalized with their implementation.

Make date ranges explicit for v0.2 onward: date-only inputs denote inclusive UTC
calendar days; adapters translate those boundaries to each API's supported contract.
Reject unsupported ranges rather than silently clipping them. Filters are validated
against descriptors/discovered columns, and exact server-side query/date semantics
are included in the manifest. A tenant/profile does not imply access to every product.

CSV is the tabular default. Store exports can additionally retain original JSON;
Marketplace and financial files preserve the downloaded format. Use:

```text
exports/<publisher-id>/<microsoft-store|azure-marketplace>/<report-id>/<run-id>/
  report.csv (or the original supported format)
  manifest.json
```

Run IDs are unique. Write temporary files and atomically publish completed files.
Persist sanitized run/job metadata separately from secret state. Never store signed
URLs as ordinary metadata. Summary/exit contract is proposed as: 0 all selected
items succeeded (including empty); 1 no item succeeded; 2 partial failure; 3 input
or configuration invalid; 130 cancelled. Unsupported selected items are explicit
non-success outcomes. Polling deadlines produce unfinished states with known IDs.

### 6. Verification is a release gate, not a claim from generated files

Implementation will add `scripts/verify.ps1`, executed from the submission directory.
Its offline gate is planned to run these commands and propagate failures:

```powershell
dotnet build AnalyticsET.slnx --configuration Release
dotnet test AnalyticsET.slnx --configuration Release --no-build --filter "TestCategory!=Live"
dotnet format AnalyticsET.slnx --verify-no-changes --no-restore
```

Live tests are separately opt-in (`TestCategory=Live`) and require explicitly
configured authorized profiles. The verifier records the absence of live access as
not verified; it does not pass a connector's release gate by skipping its live tests.
Fixtures are sanitized and generated deterministically where possible. Live evidence
must not commit customer rows or tokens. Version evidence files under
`docs/verification/` are authored during actual implementation/testing, not invented
now. Each release record maps the Vxx acceptance IDs above to command/test/evidence
and marks pass, fail, or blocked.

## Risks / Trade-offs

- [No authorized accounts yet] -> Keep offline tests useful; require live evidence
  before advertising each connector/report as supported.
- [Unconfirmed offer types] -> Begin with one Marketplace candidate and record the
  exact applicable dataset/offer scope; require a planning update if that target changes.
- [Financial local redirect/consent restrictions] -> Prove the documented flow at
  v0.4 before production integration; surface access blockers without alternate
  unsupported authentication.
- [Same tenant, different publishers] -> Isolate profile/account/resource context
  and require valid publisher-specific authorization; never guess account ownership.
- [Legacy endpoints or schema drift] -> Pin API family/version in descriptors and
  record official contract checks and actual live behavior per release.
- [Quota/ambiguous creation responses] -> Persist known IDs, reuse owned queries,
  bound concurrency and retries, and mark uncertain creation instead of duplicating it.
- [Reports change while comparisons run] -> Align dimensions, ranges, freshness,
  and currency; document differences rather than claiming byte equality with the UI.
- [Planning outside the capstone checkout] -> Keep the existing root unchanged;
  later include relevant release/design references in submission documentation.

## Migration Plan

Schema 1 is the accepted v0.1 baseline. The linked discovery design introduces
schema 2 with explicit migration to a different private file, preserving the
original and its product IDs as export selections. Schema 1 remains usable offline;
online discovery requires migration. Validate schema versions on load. Roll out each CLI
version only after its gate passes. Rollback uses the prior release package with
compatible configuration, preserving report files and protected credentials.
Do not delete remote reports/queries as part of local rollback.

The first implementation request can target v0.1 only. Finish its tasks and evidence,
report the gate, and leave later milestones unchecked when that is the requested
scope. No version has been implemented or accepted by this proposal.

## Open Questions

These are environment inputs with explicit validation tasks rather than architectural
decisions left to chance: authorized publisher/profile details, installed tenant
roles and consent, API-discovered product identities, actual financial program values, and source data
availability. They determine whether live checks pass or are blocked, not whether
the isolation and export contracts apply. Additional offer/report coverage is
selected through the v1.0 matrix; changing the first Marketplace target requires
an explicit planning update.

## Official references checked 2026-10-05

- [Store installs](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-app-installs)
- [Marketplace onboarding](https://learn.microsoft.com/en-us/partner-center/insights/analytics-prerequisites)
- [Marketplace dataset discovery](https://learn.microsoft.com/en-us/partner-center/insights/analytics-api-get-all-datasets)
- [Financial authentication](https://learn.microsoft.com/en-us/partner-center/developer/earnings-payments)

The PRD contains the broader source list. These documents inform the connector
boundaries; the milestones and architecture are proposed engineering decisions.
