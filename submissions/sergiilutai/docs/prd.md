# AnalyticsET product requirements

Status: Draft for implementation planning  
Date: 2026-10-04  
Last reviewed: 2026-10-11
Project: `submissions/sergiilutai/AnalyticsET.csproj`

## 1. Product and problem

AnalyticsET is a .NET console tool that runs on a local PC and exports reports
available through official Microsoft APIs for Partner Center. It supports many
publisher accounts and organizes exported data by publisher and Microsoft program.

Publishers currently visit several Partner Center dashboards to download app,
offer, earnings, and payment reports. Repeating this work across publishers is
time-consuming, and files can be confused when different accounts and programs
use similar report names. AnalyticsET provides one local workflow to select
publishers, identify supported reports, request exports, and save clearly identified
files for analysis or reconciliation.

The current .NET 10 console application implements the v0.1 local publisher
foundation: profile CRUD, explicit selection and authorization-context isolation,
help/version, and offline configuration validation. Its [acceptance record](verification/v0.1.md)
contains the passing build, 42 tests, formatting check, and verified code identity.
Subsequent development implements isolated client-credentials authentication and
fixture-verified Store inventory, whose live completeness remains unverified.
A separate protected Store Downloads Hub dataset-discovery and read-only Installs
query harness is implemented, with fixture-verified atomic creation journals and
single-send reporting requests. Normal reporting, live asynchronous exports and
populated comparisons remain pending.
See the [current development/setup status](project-status.md) for progress and
the remaining onboarding inputs.

## 2. Users and outcomes

Primary users are publishers and authorized operators who manage reporting for
one or more Partner Center publisher accounts. Users may manage Microsoft Store
apps, Azure Marketplace offers, or both.

The product succeeds when a user can:

1. Configure multiple publisher profiles with the permissions needed for each API.
2. See which reports the tool supports for a selected publisher and program.
3. Export a chosen report and date range through the official API.
4. Export a selection of reports across several publisher profiles in one run.
5. Find each export in the correct publisher and program folder.
6. Understand which exports succeeded, had no data, failed, or were unavailable.

## 3. Confirmed scope and proposed defaults

Confirmed requirements:

- A .NET console application running on a local PC.
- Multiple publisher accounts.
- Separate exported data for Microsoft Store and Azure Marketplace programs.
- Both performance analytics and financial reports.
- Windows products are MSIX/UWP packages.
- Official, documented Microsoft APIs are the data sources.
- Automatically discover Store apps/add-ons and Azure Marketplace offers/plans
  for initial and later-added publishers, without manually entering product IDs.
  Refresh includes products published later when exposed by the official API;
  unsupported or unverified coverage must be explicit.
- Actual Entra Client ID/Tenant ID and publisher/account/product identifiers stay
  in private local configuration. Shared documentation and examples use placeholders
  or fictional values. This is the operator's privacy preference; these identifiers
  are distinct from credentials and still require appropriate file permissions.

Proposed defaults for the first release:

- Windows is the initial supported local operating system; the starter targets .NET 10.
- Users initiate exports from the command line and choose a local output directory.
- CSV is the default tabular format. Preserve API-native JSON and downloaded
  CSV/TSV files where requested or needed for traceability.
- Publisher profiles contain non-secret settings and references to credentials
  stored securely on the local machine.
- Begin with on-demand exports. Automatic scheduling is a later capability.
- Begin with a representative report from each API family, then expand coverage
  through the same report catalog and export workflow.

Marketplace offer types and final first-release report coverage remain open.

## 4. Program and API boundaries

Partner Center has several API families. AnalyticsET must model their distinct
authentication, report discovery, and export behavior.

| Program/data | Official API | Retrieval model | Authentication boundary |
|---|---|---|---|
| Store product inventory | Store submission read APIs | Paginated apps and per-app add-ons with publication evidence | Entra client credentials; resource `https://manage.devcenter.microsoft.com` |
| Marketplace product inventory | Product Ingestion and Partner Center submission read APIs by offer type | Offers/plans and live publication evidence; explicit coverage gaps | Separate inventory bindings: Microsoft Graph scope or submission resource `https://api.partner.microsoft.com` |
| Microsoft Store performance | Store analytics JSON endpoints; consumer Downloads Hub v1.1 candidate | Paginated JSON; separate experimental dataset discovery and read-only query preview, with async CSV exports still pending | Entra client credentials; resource `https://manage.devcenter.microsoft.com` |
| Azure Marketplace performance | Marketplace analytics v1.1 | Dataset discovery, report queries, asynchronous CSV/TSV generation | Entra client credentials; resource `https://api.partnercenter.microsoft.com` |
| Store and Marketplace financials | Partner Payouts/Earnings | Asynchronous earnings/transaction and payment exports | Documented delegated authorization with user sign-in/MFA; resource `https://api.partner.microsoft.com` |

Registering an Entra application alone does not establish reporting access.
Each publisher must complete the documented Partner Center association, roles,
and consent for the relevant API. A successful token request does not prove that
the account can access a report.

## 5. Report catalog

### Microsoft Store

Initial candidates include app and add-on acquisitions, installs, daily/monthly
usage, health/errors, ratings, reviews, and insights. Legacy JSON endpoints use
documented descriptors. Microsoft's consumer Downloads Hub also exposes
`GET /consumer/insights/v1.1/ScheduledDataset`, with metadata for columns, metrics,
filters, date ranges and aggregation. Dataset discovery is distinct from inventory
and does not prove report access, product mapping or Subscription coverage.

The [Downloads Hub validation guide](verification/store-downloads-hub-validation.md)
records the reviewed consumer contract, actual dataset discovery and experimental
query preview. Both datasets were discovered. The small original Partner Center
Installs CSV has populated matching app/calendar-day observations, while both
date-only and explicit UTC timestamp API previews returned zero rows. UTC/inclusion
semantics and the cause remain unresolved; no populated comparison has passed.
One small Installs export followed by AddOnAcquisitions, each compared privately
with Partner Center, is the planned reporting-route experiment. Safe consumer
filters, fixed-date semantics and child/parent mapping must be established first.
No normal CLI route or release requirement changes are adopted here. Original
installs/ratings, complete inventory, account/publication and later-publisher gates
remain outstanding; a favorable sample needs a subsequent adoption decision.

The Store overview labels some acquisition endpoints as legacy. Validate the
appropriate endpoint against an authorized MSIX/UWP app before including it in
the supported first-release catalog. Do not infer replacement compatibility from
Xbox-specific examples.

Respect report-specific history limits. The documented daily/monthly usage APIs
provide the last 90 days; aggregate error reporting provides the last 30 days.
Availability of other dates and dimensions is determined by each endpoint.

### Azure Marketplace

Discover datasets and their columns, metrics, and date ranges through
`GET /insights/v1.1/cmp/ScheduledDataset`.

Documented dataset candidates are:

| Report | Dataset |
|---|---|
| Orders | `ISVOrder` |
| Usage | `ISVUsage` |
| Customers | `ISVCustomer` |
| Listing engagement | `ISVMarketplaceInsights` |
| Revenue | `ISVRevenue` |
| Customer retention | `ISVOfferRetention` |
| Quality of service | `ISVQualityOfService` |
| Licenses | `ISVLicense` |
| VM image versions | `ISVVMImageVersion` |

Report applicability depends on the publisher's offer types and entitlements.
Support documented system queries and, where needed, queries built from discovered
schema. Listing existing scheduled reports is distinct from listing available datasets.

### Financial reports

Provide earnings/underlying transactions and payments exports for the two programs
through the Partner Payouts API. Apply documented program filters and validate the
resulting program identity. Confirm actual program identifiers during onboarding;
the UI label, API filter value, and local folder key need not have the same spelling.

Marketplace revenue analytics and financial earnings/payment reports remain
separate report types. Preserve transaction, earning, and payment identifiers,
currencies, fees, taxes, refunds, and adjustments when supplied by the source.

The catalog covers reports supported by public APIs and authorized for the publisher,
not every dashboard widget. Microsoft's documentation currently excludes API
support for ineligible revenue/usage details.

## 6. Functional requirements

| ID | Requirement |
|---|---|
| FR-01 | Create, list, update, and remove local publisher profiles offline. Each profile has a unique stable ID, display name, tenant information, enabled programs, API-specific account settings, discovery settings, optional export selections, and credential references. Discovery-enabled schema 2 profiles do not require product IDs; schema 1 remains readable and explicitly migratable. |
| FR-02 | Allow explicit selection of one, several, or all configured publishers. A tenant ID alone must not be treated as a unique publisher identity; multiple publishers may share a tenant. |
| FR-03 | Authenticate separately for each publisher and API resource. Support required initial browser sign-in and MFA for financial access, token renewal, and a clear reauthentication outcome. |
| FR-04 | List supported reports for a selected publisher/program, with format, scope, date constraints, and known access/applicability status. Product inventory is separate from report catalog/dataset discovery; finding a product does not establish report entitlement. |
| FR-05 | Accept publisher selection, program, report selection, date range, output path, and supported product/offer filters. Refresh inventory before resolving product scope; require explicit selections or explicit all-products, verify identity and report applicability, and never widen selections merely because discovery finds new products. |
| FR-06 | Retrieve every page of Store JSON results and export all returned records. Support documented filters and aggregations per report. |
| FR-07 | Create or reuse Marketplace report queries, submit a report, track its execution, and download its completed file. Use API v1.1. |
| FR-08 | Submit earnings/payment export requests, poll their status, and download completed files. Each final report file must be attributable to one publisher and program. |
| FR-09 | Run a batch across publishers, programs, and reports. Continue independent items after an item fails and provide a final itemized summary. |
| FR-10 | Save exports using the organization and metadata requirements below. Do not silently overwrite a previous successful export. |
| FR-11 | Distinguish successful empty reports from authentication failures, insufficient permissions, unsupported requests, quota limits, and download failures. |
| FR-12 | Show export progress and provide a machine-readable summary plus documented exit codes for success, partial failure, failure, and cancellation. |
| FR-13 | Allow users to cancel safely. Record unfinished work and use temporary files so incomplete downloads are not presented as successful exports. |
| FR-14 | Discover all API-exposed published Store apps/add-ons and Marketplace offers/plans for the verified account scope, on online onboarding and subsequent online product/report operations. Provide explicit refresh and offline cached views; follow all pages, preserve parent/child identity and history, protect local snapshots, isolate publisher/program/auth context, and report incomplete/unsupported coverage without replacing the last successful inventory. |

Report catalog entries must distinguish connector support, applicability, and access.
An empty dataset is not evidence that the report is unsupported.

The logical user workflow is:

```text
Configure publishers --> Authenticate --> Discover products --> List reports --> Select exports
                                                        |
                                                        v
                    Summary <-- Save files <-- Fetch / poll / download
```

Command names and configuration schema will be defined in the design.

## 7. Export organization and metadata

Default layout:

```text
exports/
  <publisher-id>/
    microsoft-store/
      <report-id>/<run-id>/
    azure-marketplace/
      <report-id>/<run-id>/
```

Use stable, filesystem-safe IDs and unique run IDs. Place earnings/payment reports
under the corresponding program, using distinct report IDs from analytics reports.
Reports may contain multiple products within the same publisher/program; product
scope is recorded in metadata. Cross-publisher or cross-program report files must
not be presented as correctly categorized exports. If the financial response cannot
be assigned reliably, report an error rather than guessing a program.

Each export has a manifest containing:

- Publisher profile ID and program key.
- Report/dataset ID, API family and version.
- Requested date range, date interpretation, filters, and product/offer scope.
- Export start/completion timestamps in UTC.
- Report, request, and execution IDs returned by Microsoft, where applicable.
- Completion status, local file paths, format, and row count when available.
- Sanitized error details and correlation IDs for unsuccessful items.

Exclude access tokens, secrets, refresh tokens, and signed download URLs from
manifests and logs. Retain original field names and values in source exports.
CSV conversion must handle delimiters, quotes, line breaks, Unicode, nulls, and
culture-independent numbers correctly.

## 8. Reliability, access, and local operation

- Use asynchronous network and file I/O, cancellation, configurable timeouts,
  and bounded retries for transient failures and throttling.
- Respect service retry guidance and quotas. The documented Marketplace limits
  include five immediate reports per hour, 50 active reports, and 100 active queries.
  Discover/reuse owned resources before creating duplicates; do not delete a
  user's existing queries or schedules to free quota.
- Track asynchronous request IDs so polling does not repeatedly create exports.
  Treat Marketplace execution 404 responses according to execution state: the
  default completed-execution filter can return 404 before the first completion.
- Record source date semantics explicitly. Apply endpoint-specific UTC boundaries
  and supported history windows; never silently truncate the user's requested range.
- Keep credentials and token caches isolated by publisher, account, and resource.
  Use an appropriate secure local store. Credentials must not be committed or
  included in normal console output.
- Keep actual registration and publisher identifiers out of shared documentation.
  Restrict access to real local configuration using operating-system permissions;
  Git ignore rules do not provide access control. The [Entra setup guide](entra-application-setup.md)
  records the private location, confirmed setup, and planned credential protection.
- Financial token renewal must persist updated token state safely and surface a
  need for interactive sign-in. Unattended operation must not assume that delegated
  access remains valid indefinitely.
- Keep report files and configuration on the user's PC. Network access is required
  for Microsoft authentication and API/download calls; no hosted AnalyticsET backend
  is required for the first release.
- Fail clearly on insufficient disk space, invalid output paths, and incomplete
  downloads. Publish a final file only after download/conversion succeeds.

## 9. Acceptance criteria and verification

| ID | Acceptance scenario |
|---|---|
| AC-01 | Configure two authorized publishers and export the same report for both. Files and manifests appear under separate publisher IDs with the correct account context. |
| AC-02 | Export Store and Marketplace analytics and financial reports. Every published report is under the correct program and retains its source identifiers and currencies. |
| AC-03 | Retrieve a Store result spanning multiple pages. The exported row count equals the retrieved record count, with no pages silently omitted. |
| AC-04 | Discover Marketplace dataset metadata, generate a supported report, handle pending execution, and download its completed file. |
| AC-05 | Complete MFA-enabled financial sign-in, submit a filtered earnings/payment request, poll to completion, and save the resulting file. |
| AC-06 | In a batch, one publisher lacks permission while another succeeds. The successful file remains available; the summary identifies the failed item and the process reports partial failure. |
| AC-07 | Request a date range outside a report's retention window. The tool explains the limitation without silently narrowing the range. |
| AC-08 | Receive an empty successful response. Mark it as a successful empty export rather than an authorization or availability error. |
| AC-09 | Cancel or interrupt a download. No incomplete file is marked successful and unfinished items are visible in the summary. |
| AC-10 | Rerun the same selection. Earlier successful files remain intact and the new run has distinct output metadata. |
| AC-11 | Add an initial and a later publisher without product IDs; discover all authorized pages of Store apps/add-ons or Marketplace offers/plans, including published products with no sales. Compare publication and parent/child identity with Partner Center for each verified type. |
| AC-12 | Refresh after a product is published, renamed, or no longer returned. Preserve stable identity, publication/history evidence, and explicit selections; changed account/auth settings invalidate the old scope. A failed or cancelled refresh preserves the last successful snapshot and reports its age. |
| AC-13 | Exercise mixed API/type coverage and two publishers sharing a tenant. Inventory stays isolated; draft/preview-only children are not labeled published. Unsupported, unknown, or failed coverage never implies a complete or empty account. Discovery starts no report exports. |

Automated tests should cover publisher isolation, pagination, date validation,
polling transitions, retries, token expiry, partial failure, and CSV correctness.
Use sanitized fixtures for routine tests. Live integration checks require authorized
accounts and must compare representative reports with matching Partner Center
downloads, filters, aggregation, and data freshness. Documentation research alone
does not establish live compatibility.

## 10. First-release boundaries and open decisions

The first release provides product discovery, report discovery/cataloging, and local export. Read-only product/submission metadata is permitted; publishing mutations remain outside scope. A hosted
service, graphical dashboard, automatic scheduler, publishing/submission management,
and accounting interpretation are outside this release. Future programs can be added
through additional connectors using the same publisher/program organization.

Resolve these during discovery and design:

1. Which Marketplace offer types and representative reports are first-release targets?
2. Which authorized publisher accounts and sample data are available for integration checks?
3. What are the actual program/filter identifiers and financial export schemas for those accounts?
4. Which secure local credential/token storage and supported sign-in flow will be used?
5. What date defaults and catalog expansion order best fit users? Product selection is explicit and separate from automatically discovered inventory.
6. Should exports offer optional raw JSON in addition to the default tabular output?

## 11. Official references

These references informed this PRD on 2026-10-04. Recheck API contracts and
permissions before implementation; validate behavior against authorized accounts.

- [Publish Windows apps](https://learn.microsoft.com/en-us/windows/apps/publish/)
- [Store application inventory](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-all-apps)
- [Store add-on inventory](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-add-ons-for-an-app)
- [Marketplace submission API coverage](https://learn.microsoft.com/en-us/partner-center/marketplace-offers/submission-api-overview)
- [Marketplace Product Ingestion](https://learn.microsoft.com/en-us/partner-center/marketplace-offers/product-ingestion-api)
- [Microsoft Store analytics overview and authentication](https://learn.microsoft.com/en-us/windows/uwp/monetize/access-analytics-data-using-windows-store-services)
- [Store installs and pagination](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-app-installs)
- [Store Downloads Hub workflow](https://learn.microsoft.com/en-us/partner-center/insights/apps-games-make-your-first-api-call)
- [Consumer reporting OpenAPI](https://manage.devcenter.microsoft.com/consumer/insights/v1/swagger/v1.1/swagger.json)
- [Consumer custom query specification](https://learn.microsoft.com/en-us/partner-center/insights/apps-games-custom-query-specification)
- [Downloads Hub dataset definitions](https://learn.microsoft.com/en-us/partner-center/insights/downloads-hub-datasets)
- [Store aggregation and timelines](https://learn.microsoft.com/en-us/partner-center/insights/apps-games-aggregation-and-timeline)
- [Store daily usage](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-app-usage-daily)
- [Store error reporting](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-error-reporting-data)
- [Marketplace prerequisites](https://learn.microsoft.com/en-us/partner-center/insights/analytics-prerequisites)
- [Marketplace datasets and first API call](https://learn.microsoft.com/en-us/partner-center/insights/analytics-make-your-first-api-call)
- [Dataset discovery](https://learn.microsoft.com/en-us/partner-center/insights/analytics-api-get-all-datasets)
- [Marketplace report creation and execution](https://learn.microsoft.com/en-us/partner-center/insights/analytics-programmatic-access)
- [Marketplace quotas and errors](https://learn.microsoft.com/en-us/partner-center/insights/analytics-programmatic-faq)
- [Earnings API authentication](https://learn.microsoft.com/en-us/partner-center/developer/earnings-payments)
- [Earnings and payments API overview](https://learn.microsoft.com/en-us/partner-center/developer/manage-earnings)
- [Store earnings schema](https://learn.microsoft.com/en-us/partner-center/earnings/earnings-store)
- [Earnings reports and API coverage gaps](https://learn.microsoft.com/en-us/partner-center/earnings/earnings-reports)
