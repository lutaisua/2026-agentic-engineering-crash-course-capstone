# Store Downloads Hub validation

Implementation contract reviewed 2026-10-10. This is an explicitly invoked
experiment; dataset availability does not verify report export or v0.2.

## Official consumer contract

The [consumer OpenAPI](https://manage.devcenter.microsoft.com/consumer/insights/v1/swagger/v1.1/swagger.json)
is OpenAPI 3.0.1, Programmatic Export APIs v1.1. Downloaded contract SHA-256:
`da38b521fb5b2d5bfda47e11adcb06214801126ecc446bc2ab91da25ae1b3742`.
Its server is `https://manage.devcenter.microsoft.com/consumer`.
All routes below have prefix `/insights/v1.1/`. Use explicit `scope=consumer`
where declared; testQueryResult has no scope parameter in this OpenAPI.

| Method | Route | Purpose |
|---|---|---|
| GET | ScheduledDataset | Dataset schema; optional datasetName |
| GET / POST | ScheduledQueries | Find / create queries |
| GET | ScheduledQueries/testQueryResult | Validate query and custom dates |
| GET / POST | ScheduledReport | Find / create reports |
| GET | ScheduledReport/execution/{reportId} | Observe saved report execution |

Dataset envelopes contain value, totalCount, statusCode and message; no continuation
field is specified. Require the advertised count to match unique returned names.
Unexpected continuation/count mismatch blocks discovery. Query/report listings
also have no documented pagination in this contract. Query creation requires name
and query. Immediate reports use executeNow, queryId, format, queryStartTime and
queryEndTime. The date fields are nullable strings: the schema does not establish
inclusion boundaries. Execution lookup defaults to Completed/latest; a precompletion
404 requires state checks, rather than an empty-data conclusion. Mutating,
recurring, callback, deletion and downloadadlfile routes are outside this experiment.

The [Store first-call guide](https://learn.microsoft.com/en-us/partner-center/insights/apps-games-make-your-first-api-call)
describes metadata, query creation, execution and download. Its example moves a
product filter into returned productInfo. Effective scope must include that field;
query text alone is insufficient. Its published blob example uses exactly
`pxanltx.blob.core.windows.net`, the initial download-host candidate. This is not
permission to trust arbitrary blob hosts, redirects, or to forward an API bearer.
Signed links must remain in memory. Review an unexpected host before downloading.
Some sample commands have quoting/spacing defects and linked query-language pages
describe Marketplace. Do not copy their query IDs, operators or quotas into Store
requests. Consumer metadata and safe query validation must establish applicability.

The [dataset definitions](https://learn.microsoft.com/en-us/partner-center/insights/downloads-hub-datasets)
identify ProductID and Date for Installs, with metric SuccessfullInstallCount.
Keep actual metadata spelling. DateStamp is described as an acquisition transaction
identifier; it must not be treated as a date automatically. AddOnAcquisitions
child and parent field/filter semantics remain an explicit mapping gate.

The [Store aggregation guide](https://learn.microsoft.com/en-us/partner-center/insights/apps-games-aggregation-and-timeline)
describes daily aggregation for installs and add-on acquisitions, relative date
options and differing single/all-app timeframes. Those UI choices do not establish
fixed API date inclusivity. This experiment requests locally chosen completed UTC
days, one to seven days, without changing to a rolling window.

[Store authentication prerequisites](https://learn.microsoft.com/en-us/windows/uwp/monetize/access-analytics-data-using-windows-store-services)
require the associated Entra application and suitable Partner Center access.
Use the configured store-analytics binding and existing Windows credential
reference; a successful inventory token does not prove reporting access.

## Implementation and live gates

The planned sequence is Discover, then a recorded Installs download, then
AddOnAcquisitions. Each export must validate its own schema, product/parent mapping,
fixed dates, aggregation and effective filters before creating anything. No
all-products fallback is permitted. A valid empty download can permit the following
stage, but cannot pass a populated comparison.

Consumer grammar now has a dedicated official source, described below. Actual
app-to-report identity and fixed-date boundary semantics remain live gates.
AddOnAcquisitions also requires a known
Subscription child/parent mapping and populated acquisition evidence. These are
specific unresolved gates, not conclusions that the API is unsupported.

All actual input, metadata, reports and evidence belong under a protected directory
outside Git/OpenSpec. Shared output contains fixed states, counts and equality
results only. Keep IDs, queries, errors, correlations, CSV rows and signed links
private. Existing runtime profiles, credential mappings and inventory are read-only.
The completed 135-test Subscription diagnostic remains historical evidence.

## Protected discovery implementation

Schema 1 [fictional input](../examples/store-downloads-hub-input.fictional.json)
illustrates the exact fields. It is an offline parser fixture, never an authorized
sample and never a live input path. Real input contains one experiment GUID,
explicit publisher alias, distinct parent/Subscription Store IDs, expected type,
local published/relationship confirmations, UTC confirmation time and two fixed
windows. Each window has startUtc/endUtc at UTC midnight and spans one to seven
completed days. Requested boundaries are recorded; their service interpretation
remains unverified until safe query checks and comparison establish it.

Create a dedicated physical directory:
`%USERPROFILE%/.analyticset/validation/store-downloads-hub/<experiment-guid>/`.
Place real input at input.json in that directory. Current operator ownership and
protected operator/SYSTEM permissions are required. Configuration and credential
mapping paths remain the existing private runtime locations. Preflight reads them
without vault/network access, rejecting malformed/duplicate/unknown fields,
missing analytics bindings, redirected/repository paths and unsafe permissions.
After all preflight checks, live discovery freezes the input/configuration/
contract/credential-generation binding in binding.json. A changed binding stops
reuse. Configuration and inventory files are never changed by this harness.

Each discovery attempt creates a new Discover-GUID directory with normalized
evidence.json and a final receipt.json. Verify its evidence hash and ACL before
using it. A partial directory, an HTTP response or metadata presence alone is not
a completed report. Previous records are preserved. If private input is invalid,
repair it locally before first discovery. Once bound, create a new experiment for
changed identifiers/windows/configuration; preserve the previous experiment.

Discovery permits only the selected tenant's token endpoint and the exact
consumer ScheduledDataset GET. Two minutes and 24 aggregate attempts include
token/retry calls; JSON operations have 60 seconds/4 MiB, safe-read transient
retries at most three, one safe-read 401 renewal, and Retry-After above 30 seconds
stops the stage. Redirects and credential changes stop further requests. These
are experiment limits, not service quotas.

The safe projection exposes stage, completion, exitCode, outcome, transportAttempts,
observations (operation/attempt/status/failure), metadata counts and targets.
Each target has dataset, availability, schema, export and comparison states.
For fictional two-dataset evidence both targets are discovered/schema present,
while export and comparison stay unverified. Absent targets are unavailable;
omitted needed fields make schema incomplete. Count mismatch/continuation or
ambiguous metadata is a contract failure, never successful empty data. Ordinary
output never includes the normalized columns, identifiers, error bodies or links.

Discover exit 0 means metadata recorded, including unavailable target datasets;
2 means an incomplete/contract/authentication check; 1 means invalid prerequisites
or recording failure; 130 means cancellation. No value accepts export comparison
or v0.2. Remote report creation, CSV download and populated Partner Center
comparisons are not yet implemented or performed for this change.

## Local helper and next evidence

Discover is implemented and tested. Installs now supports a read-only query preview,
described below; report creation/download is pending. AddOnAcquisitions remains
blocked until a recorded Installs download. The complete task 2.7 stays pending.
See the [discovery checkpoint](store-downloads-hub-checkpoint.md).
In ordinary Windows PowerShell, run from the submission directory:

```powershell
.\scripts\Invoke-StoreDownloadsHubValidation.ps1
$LASTEXITCODE
```

Default mode prepares input offline. Enter the existing publisher alias, published
parent app Store ID and known Subscription Store ID at masked prompts; confirm
their publisher/type/parent relationship locally. Supply separate completed UTC
start/end date boundaries (yyyy-MM-dd) for each dataset, preferably one day with
known installs/acquisitions. No activity is assumed from dates. Each interval spans
one to seven days. The helper prints the protected experiment directory. Do not
reuse the fictional example or put IDs in command arguments.

Run discovery for that same directory:

```powershell
.\scripts\Invoke-StoreDownloadsHubValidation.ps1 -ExperimentDirectory '<private experiment directory>' -RunLive -Stage Discover
$LASTEXITCODE
```

Alternatively -RunLive -Stage Discover without ExperimentDirectory prepares a
new private input and runs discovery. Only required authentication and dataset
GETs occur; no report job is created. Existing inputs are preserved and strictly
validated before live access. Temporary path environment settings are restored;
detailed dotnet output stays in memory. Receipt, hashes, input/contract/assembly
provenance and permissions are verified before a packet is advertised. Interrupted
or corrupt packets cannot claim completion. Share only Discovery packet path and
exit code, or the sanitized step/category; keep JSON, IDs and credentials private.

Actual metadata requires private review before query preparation. Column presence
does not establish operators, parent/child mappings or date inclusivity. Failed
discovery or unresolved semantics keep creation blocked. Reporting POST/recovery/
download and live comparison tasks remain unchecked at this checkpoint.

For later comparisons, preserve the original Partner Center download and copy it
into protected experiment storage outside Git. Match account, product/child/parent,
window, filters and aggregation; record capture time and available freshness.
Keep original bytes. Baseline parsing/comparison acceptance is not yet implemented.
Do not share CSV rows in chat.

## Setup failure categories

The operator's first attempt exited 1 at masked-sample-input, before input
publication, credential access or HTTP. That former combined step did not identify
the failing check. A subsequent retry identified a local rejection as
installs-window / utc-window-longer-than-seven-days: a 29-day interval was rejected
by the experiment's 1–7-day rule, before input publication or authentication.
The later two-day input passed and discovery completed. For a new experiment,
choose a short completed window containing known activity. The helper reports
specific fixed steps/categories, without entered values or exception text:

| Category | Local correction |
|---|---|
| publisher-alias-invalid | Use the existing lowercase publisher id, not its display name; no spaces |
| publisher-alias-not-found / publisher-alias-ambiguous | Match exactly one publishers.json publisher id |
| store-analytics-binding-missing / store-account-scope-missing | Verify selected profile has Store analytics authorization and a Store account |
| parent-store-id-invalid / subscription-store-id-invalid | Enter the plain Store ID, without URL, quotes or spaces |
| parent-child-id-equal | Select distinct parent app and Subscription IDs |
| sample-confirmation-required | Recheck the relationship/account/type locally and type exactly CONFIRM |
| utc-date-format-invalid / utc-start-date-invalid | Use a valid yyyy-MM-dd date after 1970-01-01 |
| utc-end-date-not-completed | End boundary must be at or before today's UTC midnight |
| utc-window-end-not-after-start | Use successive dates for one day; end must be later than start |
| utc-window-longer-than-seven-days | Select an interval spanning at most seven UTC days |

The installs-window and addon-acquisitions-window steps identify which dataset's
dates failed. These are local interval rules; service inclusivity remains unverified.
Use known activity when available and do not invent it. Existing input and frozen
bindings are preserved. When correcting a setup rejection, rerun the command and
share only the fixed step/category and exit, or the verified packet path.

## Reviewed live discovery and consumer query preparation

The operator's protected Discover run exited **0**. Private ACLs, final receipt,
evidence/input hashes, contract and executing assembly match the 167-test source
checkpoint. Token and dataset GET each returned **200** on one attempt.
**16 unique datasets** were recorded, including both targets with columns, metrics,
date ranges and aggregation mappings. No reporting resource, CSV or comparison
was created. Actual experiment, publisher/product IDs and input dates stay private.

The [Apps and Games query specification](https://learn.microsoft.com/en-us/partner-center/insights/apps-games-custom-query-specification)
documents filtering selectable columns/metrics, optional TIMESPAN, metric grouping
by selected nonmetric columns, and custom QueryStartTime/QueryEndTime report fields.
Its native example has an empty columnFilters object; that shape does not imply
WHERE is forbidden. Custom intervals must fit the aggregation's returned mapping.
The page does not settle timezone or inclusion boundaries. No rolling date clause
is substituted for the fixed input. Query preview uses the consumer OpenAPI's
documented custom-date fields, retaining unknown date semantics as a creation gate.

| Dataset | Observed schema and prepared mapping | Outstanding live proof |
|---|---|---|
| Installs | ProductId, Date, SuccessfulInstallCount; Daily | Selected Store app identity, preview shape/dates and exact UTC boundary convention |
| AddOnAcquisitions | ProductId, ParentProductId, PurchaseQuantity; Daily | Selected Subscription/parent identity, date semantics and populated acquisitions |

These are returned schema spellings and prepared mappings, not proven report rows.
Installs uses the single confirmed app ID. AddOnAcquisitions requires both the
confirmed child ProductId and parent ParentProductId filters. DateStamp is not
selected as a date. The native Installs metric spelling supersedes the overview's
typo for this schema; drift blocks preparation rather than correcting it silently.

Prepared queries select only required identity/date/count fields. System/owned
query compatibility requires the complete literal scope or normalized query plus
matching productInfo; a conflicting group/product, parent, aggregation, rolling
date clause or schema cannot be reused. Creation proof must match publisher
binding, experiment/sample, schema, query and window, with populated identity/
parent observations and verified fixed UTC semantics. A valid empty preview or
observed in-range rows alone cannot establish that proof.

Run the tested Installs preview with the existing experiment and exact Discover
packet directory, using paths only:

```powershell
.\scripts\Invoke-StoreDownloadsHubValidation.ps1 -RunLive -Stage Installs -ExperimentDirectory '<private experiment directory>' -DiscoveryPacketDirectory '<private Discover packet directory>'
$LASTEXITCODE
```

This stage performs only required authentication and one narrowly scoped
testQueryResult GET, with bounded safe-read recovery. It creates no query/report
resource. It verifies the historical discovery receipt/source/binding before vault
access and refuses changed credentials. Original discovery/input bytes remain
unchanged. Preview rows are discarded; only normalized observations, allowlisted
field types/counts, safe errors/correlations and the private prepared query are
recorded with a new final receipt. No arbitrary response-field names or values
are retained. Two minutes/24 transport attempts and 60-second/4-MiB operations
bound this preview. Path environment settings are restored.

**Exit 2 is expected while UTC boundary proof or export implementation is pending**,
including a successful preview. It does not mean a report was exported. Share only
Installs query packet path/status; never raw preview JSON, query strings or IDs.
Add-on query fixtures do not authorize a live AddOnAcquisitions stage before the
recorded Installs download prerequisite. Live comparison tasks remain unchecked.

## Installs preview discrepancy and next evidence

The authorized Installs testQueryResult GET returned **HTTP 200 and zero rows**.
The private final receipt was verified against its evidence, executing source,
input, contract, discovery and binding hashes. Token acquisition was also 200.
The stage recorded **exit 2 / hub-date-boundary-unverified**; identity/date fields
are unobserved in an empty response. No query or report resource was created.
This is a query preview, not a downloaded CSV or a passed comparison.

The operator reports installs in Partner Center for the same parent app and
two-day window. The discrepancy remains unresolved. The request uses the consumer
OpenAPI's exportQuery, queryStartTime and queryEndTime parameters and the custom
date form shown in the consumer query guide. Their documented presence does not
prove how the service interpreted this request. Do not infer no activity, an API
limitation, a permission defect, ignored dates or an incorrect ID from this alone.

Preserve the existing experiment input and both stage receipts. In Partner Center,
download the original Installs data for that app/window with Daily aggregation,
and save it as **installs-partner-center.csv** in the existing protected experiment
directory. Record the displayed start/end dates, timezone if shown, filters,
report section/metric and capture time privately. If a timezone or aggregation
choice is unavailable, record that omission; do not substitute an assumption.
Share only the full file path and non-identifying scope settings. Keep rows,
product/account names and IDs private. Files saved in Downloads or another broadly
accessible directory need protected placement before review.

The operator subsequently supplied headerless rows and confirmed that the download
was **Acquisitions** from Downloads Hub. Do not use its quantities as successful
installs. Obtain an original Installs download with its header and exact scope. The
[dataset definitions](https://learn.microsoft.com/en-us/partner-center/insights/downloads-hub-datasets#installs)
distinguish Installs fields/metric from acquisition quantity and monetary fields.
An Acquisitions baseline cannot establish Installs parity. Raw operator rows and
identifiers are not stored in shared docs. The existing experiment directory is
the path printed as Experiment directory by the successful Discover helper; keep
using that directory rather than creating a new input or mixing experiments.

Review owner/ACLs, repository/reparse exclusions and file bounds before parsing.
Compare product identity when present, per-day dates, active filters and the
definition of the successful-install metric. Check whether Partner Center includes
the displayed end day; activity exclusively on that boundary would differ from
the locally intended start-inclusive/end-exclusive interval. That is a hypothesis
to test, not an established cause. A missing product column requires an explicit
private scope declaration rather than an invented identity match.

Microsoft's [MSIX acquisitions report](https://learn.microsoft.com/en-us/windows/apps/publish/analyze-app-performance/acquisitions-report)
distinguishes acquisition-funnel measures from successful installs and describes
filters. Verify which section the baseline represents before asserting equivalent
metrics. No exact count tolerance or aggregate mapping is assumed. A baseline
inspection may identify a scope difference; unresolved differences keep report
creation and DH6 blocked. Do not change to a rolling window or all-products query
to make the zero-row result disappear. Task progress is **5/18** and v0.2
is unaccepted.

The later supplied file passed private path/ownership/ACL checks. Its 35 CSV
headers contain PurchaseQuantity and purchase price/tax fields, but no Date or
successful-install metric; report rows were not parsed and no comparison passed.
The operator then found Installs in Downloads Hub, correcting the temporary
not-listed report. At resumption, installs-partner-center-confirmed.csv passed
private path/ACL checks and has 12 unique headers including Date and
SuccessfulInstallCount. Its original bytes were preserved. The operator selected
three months and downloaded it on 2026-10-10; exact dates, timezone, aggregation
and filters were not supplied. This is broader than the fixed two-day experiment.
Rows were not parsed and no comparison was accepted. Obtain a matching Custom/Daily
export as installs-partner-center-small.csv, preserving both earlier files. Use the
[current resumption note](../pause-resume.md#current-resumption-2026-10-11).

## Fixture-verified creation journal and single-send boundary

Task 2.2 is implemented in the separate test harness, behind the existing exact
sample/schema/binding/query/window and populated/UTC scope-proof gate. The normal
CLI and live helper do not invoke it yet. A proof failure stops before token or
intent access; compatible remote query lookup and the live scope-proof producer
remain part of unfinished task 2.1.

Each dataset has at most one query and one report creation journal. An exclusive
experiment lock serializes intent/ID commits. Current-user/SYSTEM path permissions,
bounded strict JSON, scope fingerprints and a checksum over the complete journal
are verified before reuse. Journals are atomically replaced with the existing
writer; previous dataset/stage records remain separate. Opaque operation names
contain only the experiment GUID, allowed dataset and query/report label.

Creation first obtains the frozen-generation token, then durably commits exact
request JSON/fingerprint and creation-intent before sending. Query requests contain
only name/query. Report requests contain reportName, verified queryId,
executeNow=true, CSV and exact fixed start/end fields. They require a recorded
matching query. No recurrence, callback, deletion or publishing field is sent.

One POST is sent, with no automatic retry or 401 renewal. Redirects/cookies are
disabled; response origin, a 60-second/4-MiB response bound, GUIDs, operation marker,
effective query/productInfo, dates and one-time report fields must verify before
the returned ID is committed. A malformed/rejected/lost response, cancellation or
failed ID recording leaves creation-uncertain; any unresolved intent blocks further
creation. If recording uncertainty also fails, the durable intent remains the
barrier. A known validated journal can supply its saved ID without another POST.
This is an at-most-one client send, not a service exactly-once guarantee.

Report-creation responses may omit the dates: queryStartTime/queryEndTime that
are null or absent record **creation-recorded-dates-not-echoed**, and the
downloaded CSV dates must prove scope later. An echo in the other midnight form of
the same sent boundary (date-only or explicit UTC midnight) is accepted. Any other
day, a non-midnight time or a non-string value leaves creation-uncertain. The hub
script runs `dotnet test` with ErrorActionPreference Continue and string-converted
output, so native stderr under Windows PowerShell 5.1 cannot hide the result marker.

Journal contents, payloads and IDs remain private; the public projection exposes
only Dataset, Kind, State, HttpStatus, Outcome and ExitCode. Raw service messages,
tokens and signed links are not retained. Remote listing/reconciliation of uncertain
resources, execution polling, CSV download and complete live stage integration
remain pending tasks 2.3–2.7. Do not edit/delete an uncertain journal to retry.

Nine creation tests cover both datasets and exact payloads, durable ordering,
HTTP 401/403/429/500/503/redirect rejection without replay, lost responses and
malformed IDs/scope, response bounds, report identity/date/recurrence/callback
checks, unsafe transport, pre-/post-commit failures, cancellation, corrupt/partial
journals, cross-operation uncertainty and concurrent invocation. All use fictional
credentials and HTTP. The full verifier passed **186 non-live tests**, Release
build and formatting; the [87-file source manifest](store-downloads-hub-creation-source-hashes.json)
preserves the 177-test checkpoint and all 85 previous source hashes. No new live
Microsoft request or reporting POST was made. v0.2 and live comparisons are unaccepted.

## Small baseline and explicit UTC date control: 2026-10-11

The small original Installs CSV is now supplied and privately inspected. Closing
Excel resolved its sharing violation. Protected owner/ACL/path checks passed;
original bytes were unchanged. All 148 rows match the selected app, and all 12
unique headers match native metadata. The operator confirms direct download on
2026-10-10, the intended two calendar days, Daily aggregation and selected-app-only
filtering. Slash-form dates fit that month/day/year declaration and include
positive SuccessfulInstallCount values. No timezone was selected; the file does
not carry a timezone marker. Calendar-day alignment does not establish service
UTC boundaries or an accepted CSV comparison. Keep all original files private.

The earlier preview sent fixed date-only queryStartTime/queryEndTime values. The
consumer OpenAPI declares strings without defining their timezone/inclusion grammar.
An explicitly selected control sends the exact same saved instants as UTC timestamps;
it does not change the query, app, input or time window. There is no automatic retry
with another format, moving window or all-products query. For a deliberately
requested control after building/verifying Release, use the existing paths:

```powershell
powershell.exe -NoProfile -File scripts\Invoke-StoreDownloadsHubValidation.ps1 `
  -RunLive -Stage Installs -UseUtcTimestampPreview `
  -ExperimentDirectory '<existing-protected-experiment-directory>' `
  -DiscoveryPacketDirectory '<matching-Discover-packet-directory>'
```

This flag is valid only for Installs. A separate non-parallel Live test makes the
bound authentication and testQueryResult GET, saving a unique receipt. Path-only
environment settings are restored. The private plan and safe projection record
dateWireFormat (date-only or utc-timestamp), and the helper checks it against the
selected mode. Creation proof must match the chosen wire format; observed rows
alone still cannot assert UTC/inclusion semantics. AddOnAcquisitions is unchanged.

The tested control has already run for this experiment: token and preview HTTP 200,
zero rows, verified receipt/source/input/discovery/binding and exit 2. Query and
window match the prior preview. Changing the date representation did not resolve
the discrepancy. No report job or API CSV was created. Do not repeat this control
without new evidence; investigate consumer effective query/date semantics next.
The cause remains unresolved. The [UTC preview checkpoint](store-downloads-hub-checkpoint.md#current-utc-preview-checkpoint-2026-10-11)
records 195 passing non-live tests; the later offline
[date-echo and stderr fixes](store-downloads-hub-checkpoint.md#date-echo-and-stderr-fixes-2026-10-11)
record 198 and preserve all earlier source manifests. Task 2.1,
populated comparisons and v0.2 remain unaccepted.

## Report-path diagnostic

The approved `-Stage InstallsReport` (live test
StoreHubReportLiveValidation.RecordInstallsReport) skips the testQueryResult
preview; the consumer OpenAPI documents that GET only as a validity test. Creation
is allowed for Installs with a date-only, metadata-validated narrow plan plus a
baseline identity proof (proof basis `baseline-identity`): the private
`installs-partner-center-small.csv` in the experiment folder must have ProductId,
Date and the metric columns, and every row's ProductId must equal the plan's Store
ID. Other stages keep the preview basis. Run it only deliberately, with paths only:

```powershell
.\scripts\Invoke-StoreDownloadsHubValidation.ps1 -RunLive -Stage InstallsReport -ExperimentDirectory '<experiment folder>' -DiscoveryPacketDirectory '<Discover packet>'
$LASTEXITCODE
```

The stage sends one query POST and one executeNow report POST; existing journals
are reused, so a rerun sends no new POST. It polls every 15 seconds for up to
10 minutes (Completed lookup, then Pending) and downloads the CSV without
credentials from `pxanltx.blob.core.windows.net` only (HTTPS, no redirects or
cookies, 120 seconds, 32 MiB). An expired link is recovered only by rereading the
same execution once. Original bytes are saved as `InstallsReport-<run>\report.csv`.
It prints the outcome, recorded resources (of 2), whether the CSV downloaded, row
counts before, inside, on the end day and after the window, the end-boundary state
and how many days have totals equal to the baseline. Totals stay in memory only;
date semantics come from the downloaded CSV. **A live run creates 1 query and
1 report in Partner Center that the stage does not delete.** Only the operator
starts it.

Amendment 1 (accepted 2026-10-11): the stage releases the experiment lock while
StoreHubCreation creates the query and report, because CreateAsync takes that same
lock itself. The lock is held for the offline checks and again for polling,
download, saving and analysis. See the
[report-path checkpoint](store-downloads-hub-checkpoint.md#report-path-diagnostic-2026-10-11).
