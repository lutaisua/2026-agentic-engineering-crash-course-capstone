# Store Downloads Hub implementation checkpoints

Date: **2026-10-10**. Change: validate-store-downloads-hub-exports.
Binary remains **0.1.0**; v0.2 is unaccepted.

The 153/167/177/186-test checkpoints below are historical. The 195-test UTC
preview checkpoint, its reviewed live observations and the current 198-test
date-echo and stderr fixes are recorded at the end;
earlier no-live statements describe their own source states. No report creation,
export or populated comparison has passed.

## Historical discovery checkpoint

The four protected-discovery tasks (1.1–1.4) are implemented and verified.
An injectable consumer ScheduledDataset reader, strict schema 1 private input,
explicit Store analytics binding, normalized metadata, immutable experiment
binding and hash/ACL-verified discovery receipts are implemented in the test
harness. Selection never substitutes submission bindings, merges publishers,
changes profiles or commits inventory. Rejected preflight has zero vault/HTTP
calls. Dataset presence never claims verified exports.

The first part of task 2.7 is available as a tested masked local helper and
non-parallel Live discovery entry point. Installs/AddOnAcquisitions requests are
refused locally until mapping/export work is implemented. Task 2.7 remains
unchecked because its complete export stages are not implemented.

The documented `powershell.exe -NoProfile -File scripts\verify.ps1` passed:

- Release build: **0 warnings, 0 errors**.
- **153 non-live tests**, zero failures and skips.
- Formatting verification: passed.
- [Source manifest](store-downloads-hub-source-hashes.json): **81 files**, all
  hashes matched after capture; generated obj/bin excluded.
- All **74 historical source hashes** still match the Subscription checkpoint.

Eleven discovery cases cover input/windows, context isolation, private storage,
metadata schema/count/continuation/omissions, safe HTTP outcomes/bounds, binding
rotation, cancellation/recording failure, no reporting/submission calls, and the
fictional documentation example. Seven helper cases cover offline/help/preservation,
four defined exits plus invalid status, hash corruption, native-output canaries,
environment restoration, interruption and premature export rejection.
Routine checks use fictional credentials/HTTP and never contact Microsoft or read
the real vault. The official public OpenAPI download was contract research only.

The full task 3.1 remains unchecked: this is a discovery checkpoint, not cumulative
verification of the still-pending query/report/download harness (DH3/DH4).

| Gate | Evidence at this checkpoint |
|---|---|
| DH1 | Discovery preflight/analytics isolation verified; export isolation pending |
| DH2 | Metadata/schema fixtures verified; actual filter/date/identity mapping pending |
| DH3 | Intent/creation/reconciliation pending; no reporting POST implemented |
| DH4 | Discovery HTTP limits verified; polling/CSV download pending |
| DH5 | Discovery receipts/helper/ACL/redaction verified; report/comparison packets pending |
| DH6 | No live Installs export or Partner Center comparison |
| DH7 | No live AddOnAcquisitions export or Subscription comparison |
| DH8 | Discovery build/test/source checkpoint recorded; final route assessment pending |

## Evidence requested at the historical discovery checkpoint

At that checkpoint no live Downloads Hub request had run. Task 2.1 needs consumer narrow filter,
identifier and fixed-date semantics; the public schema exposes strings/examples
without establishing those mappings. Do not invent them or substitute all-products
or rolling dates. Run the tested helper with privately confirmed identities/windows
and review actual metadata. Discovery alone may not resolve every semantic
question; safe query checks and equivalent Partner Center scope remain necessary.

The [procedure](store-downloads-hub-validation.md#local-helper-and-next-evidence)
gives the command and path-only response. Subsequent work remains one-time
creation/recovery, polling/download/CSV verification, staged exports, populated
private comparisons and an evidence-based route assessment. Fixtures cannot
recommend a route. Unresolved inventory/add-on HTTP 400, original installs/ratings
and later-publisher release gates remain outstanding. Discovery stays 17/31,
iterations 9/47, diagnostic 14/14. No sync/archive/commit/PR/release or scope reduction.

Strict OpenSpec validation passed for this change, discover-publisher-products,
develop-analytics-et-in-iterations and diagnose-store-subscription-inventory.
The six affected documentation files have no broken local links; all 81 current
source hashes still match after review. This is an interim review, not completion
of final task 4.3 or DH8 for the full experiment. The operator was asked to run
the tested discovery helper and share only its protected packet path/status.

## Historical setup diagnostic follow-up

The operator reported exit **1** at **masked-sample-input**, before input
publication, credential access or HTTP. This identifies a local setup failure;
the cause was unidentified at that point. The combined step has been split into alias,
publisher selection, parent/Subscription ID, confirmation and per-dataset window
steps. Finite categories identify the failing validation without entered values,
profile contents or exception text. Publisher selection now occurs before product
prompts. One-day intervals are explained as successive UTC date boundaries;
service date inclusion remains unverified. No validation rule was relaxed.

Fourteen new fictional failure cases verify exact safe categories, identifier
canaries, no input publication/runner invocation, unchanged configuration and
environment restoration. The documented verifier passed again: Release build
with **0 warnings/errors**, **167 non-live tests**, zero failures/skips, and
formatting. The [follow-up source manifest](store-downloads-hub-setup-diagnostics-source-hashes.json)
has **81 matching files** and the executing assembly hash. The previous Downloads
Hub manifest remains intact for its original source state; all **74 historical
Subscription source files** still match. Routine verification uses no actual
vault or Microsoft calls. The operator should rerun the helper and share only
its fixed step/category or verified packet path/status. Progress stays **4/18**;
no reporting or comparison gate has passed.

The subsequent operator retry stopped at installs-window with
utc-window-longer-than-seven-days. Its 29-day input exceeded the deliberate
1–7-day experiment limit. No input was published, credential read or HTTP request
made. Retry a short completed UTC interval containing known activity; retain the
guard. The initial combined-step failure cannot be attributed beyond this later
confirmed observation. No code/test/source hash or task checkbox changed for this
documentation-only clarification.

## Historical 177-test query checkpoint and live observations

The documented verifier subsequently passed a Release build with **0 warnings,
0 errors**, **177 non-live tests**, zero failures/skips, and formatting. The
[query source manifest](store-downloads-hub-query-source-hashes.json) contains
**85 files**, all matching, and the matching executing test assembly hash.
Its liveVerification field records the pre-preview capture state; this section
records the later live run at those unchanged hashes. The 153/167-test manifests
and all **74 historical Subscription source hashes** are preserved.

Nine new query cases and one helper case verify narrow actual-schema queries,
fixed-date URI parameters, literal/normalized productInfo compatibility, schema
drift, wrong identity/parent/date, empty previews, creation-proof rejection,
verified discovery reuse, pre-vault source/receipt rejection and output/environment
protection. Empty columnFilters is accepted as the documented metadata shape.
Prepared Installs uses ProductId, Date and SuccessfulInstallCount. Prepared
AddOnAcquisitions uses ProductId, ParentProductId and PurchaseQuantity; DateStamp
is not parsed as a date. Routine tests use fictional data/HTTP only.

The Installs helper now runs a bounded read-only testQueryResult GET after checking
the exact existing Discover receipt and immutable input/binding. It creates no
query/report, discards preview rows and records finite observations with a new
private receipt. Unknown identity or UTC boundary semantics forbid creation.
The AddOnAcquisitions live stage still requires a recorded Installs download and
is not implemented. Tasks 2.1/2.7/3.1–3.4 remain unchecked.

The operator's successful two-day Discover run was reviewed privately: exit **0**,
**16 unique datasets**, both targets present, matching count/schema/provenance,
token/dataset **HTTP 200**, protected ownership/ACLs and verified final receipt.
The authorized Installs preview then returned **HTTP 200 with zero rows**, recorded
as **exit 2 / hub-date-boundary-unverified**. Its receipt, source, input, contract,
discovery and binding hashes match. No identity/date match can be observed in
empty rows, and no reporting resource or CSV was created.

The operator reports Partner Center installs for the same app/window. This is an
unresolved discrepancy; UI activity is operator-reported, not a verified CSV
comparison. No cause has been established. The next input is the equivalent
Installs baseline; the operator confirmed that the later headerless rows came
from an Acquisitions download. Those quantities are not accepted as install counts.
An original Installs file/header is required; raw rows/IDs were not added to shared
files. Obtain the
original Daily Partner Center CSV in protected experiment storage, plus displayed
date/timezone/filter/metric settings. Review it privately before proceeding past
the scope gate. The [procedure](store-downloads-hub-validation.md#installs-preview-discrepancy-and-next-evidence)
records placement, privacy and the checks required. Preserve the current window,
receipts and original bytes; do not broaden scope to hide the mismatch.

| Gate | Current evidence and remaining work |
|---|---|
| DH1 | Discovery/preview isolation verified; full export/recovery isolation pending |
| DH2 | Actual datasets/schema reviewed, query guards fixture verified; live identity and date semantics unresolved |
| DH3 | No reporting POST; creation journal/reconciliation pending |
| DH4 | Bounded discovery/preview reads verified; polling/download pending |
| DH5 | Private discovery/preview receipts and helper verified; report/baseline/comparison records pending |
| DH6 | Empty query preview conflicts with operator-reported UI activity; no export or baseline comparison |
| DH7 | No live AddOnAcquisitions export or comparison |
| DH8 | 177-test/85-file interim checkpoint; final assessment/cumulative review pending |

Progress stays **4/18**, discovery **17/31**, iterations **9/47**, diagnostic
**14/14**. The reporting route and v0.2 remain unaccepted. No archive, spec sync,
commit, PR, release or scope reduction is recorded.

## Historical creation checkpoint: 2026-10-11

Resumption read the pause note first and verified all **85** saved source hashes
and the executing assembly before implementation. Task **2.2** now implements
private atomic intent/ID journaling and the single-send query/report boundary
behind the existing scope-proof gate. Both datasets are capped at one query and
one executeNow report per experiment; uncertain creation forbids further POSTs.
The normal CLI is unchanged and the live helper remains preview-only.

The documented verifier passed Release with **0 warnings/errors**, **186 non-live
tests**, zero failures/skips, and formatting. The
[creation source manifest](store-downloads-hub-creation-source-hashes.json) has
**87 matching files** and the new executing assembly hash. All **85** previous
source files, including the historical 74, are unchanged; the two added files
contain creation logic and nine focused tests. Earlier manifests remain intact.

The tests cover exact payload/intent/send/ID ordering, both datasets, no replay
after HTTP/transport errors, wrong IDs/scope/dates, unsafe redirects/cookies,
bounded bodies, cancellation, recording failures, journal corruption and locks.
Remote reconciliation, polling, download/CSV checks and complete live integration
are still pending; their tasks have not been checked from this partial evidence.

The confirmed Installs file passed protected path/ACL/header checks: **12 unique
headers**, including Date and SuccessfulInstallCount, with original bytes unchanged.
The operator reports a three-month selection downloaded on 2026-10-10. That is
broader than the fixed two-day sample; rows were not parsed and no comparison
passed. Exact boundaries/timezone/aggregation/filters remain unspecified. Await
the matching Custom/Daily export as installs-partner-center-small.csv and its
scope settings, preserving the three-month and Acquisitions files. No new live
Microsoft request, credential read or reporting POST occurred on resumption.

Progress is **5/18** (1.1–1.4 and 2.2). DH3's intent/single-send slice is fixture
verified; its remote recovery remains pending. DH6/DH7 and v0.2 remain unaccepted;
sibling 17/31, 9/47 and 14/14 history is unchanged. No archive, sync, commit or PR.

## Current UTC preview checkpoint: 2026-10-11

The small original Installs CSV was privately reviewed after a Windows sharing
violation was resolved by closing Excel. Protected path/ownership/ACL checks and
before/after SHA-256 equality passed. All **148 rows** belong to the selected app;
all **12 unique headers** occur in discovered Installs metadata. The operator
confirms direct Partner Center download, the matching two calendar days, Daily
aggregation, selected-app-only filtering and download on 2026-10-10. Its slash
dates fit month/day/year and the intended days, with positive successful-install
counts. No timezone was selected, and dates have no explicit timezone marker.
These are baseline observations; UTC/inclusion semantics and API parity remain
unverified. Neither earlier Acquisitions nor the three-month file is substituted.

The helper's explicit **UseUtcTimestampPreview** control changes only the wire
representation of the same saved UTC boundaries. Query, product, metadata, input
and binding stay fixed; no fallback or report creation is performed. The private
packet records the format, and any later creation proof must match that format.
Unknown formats and add-on use of this control are rejected before credentials.
Preview date parsing accepts date-only or explicit UTC midnight formats, rejects
locale/zoneless/non-midnight/boundary guesses, and still leaves semantics unverified.

The complete verifier passed **195 non-live tests**, zero failures/skips, Release
build with **0 warnings/errors**, and formatting. Nine additional cases cover
explicit scope/window preservation, wire-format proof isolation, timestamp/date
rejection and both helper selectors with output/environment preservation. Existing
creation tests now also reject invalid wire formats before token access. The
[UTC preview source manifest](store-downloads-hub-utc-preview-source-hashes.json)
contains **87 matching files** and the executing assembly hash. Seven harness/helper
files changed from the 186-test checkpoint; production and all 74 historical
Subscription files are unchanged. All earlier manifests remain intact.

After that capture, the authorized UTC timestamp control returned token/preview
**HTTP 200** with **zero rows**, exit **2 / hub-date-boundary-unverified**. The
separate packet's receipt/source/input/contract/discovery/binding hashes verified.
Its query and saved window match the previous date-only preview. The manifest's
liveVerification field records pre-control capture; this section records the later
live result at those unchanged hashes. Changing the date representation did not
resolve the discrepancy and does not establish what dates the service applied.
No query/report resource or API CSV was created. No raw rows/identifiers entered
shared files. Input, prior evidence and original downloads were preserved.

Progress remains **5/18**. Task 2.1's live scope/date proof and compatible consumer
query lookup remain unfinished. Recovery, polling, download, comparison receipts
and the complete staged helper remain pending. DH6/DH7 and v0.2 stay unaccepted.
Next investigate consumer query/date interpretation under the same narrow sample,
and preserve these observations for a precise Microsoft case if needed. No cause,
API limitation, alternate report scope or reporting-route adoption is inferred.

## Date-echo and stderr fixes (2026-10-11)

Two offline fixes follow the UTC preview checkpoint. **A1:** report creation now
accepts a response whose queryStartTime/queryEndTime are null, absent, or the
other midnight form of the same sent boundary (date-only versus explicit UTC
midnight). Null or absent dates record outcome
**creation-recorded-dates-not-echoed**; the downloaded CSV dates must prove scope
later. Any other day, a non-midnight time or a non-string value still ends
**creation-uncertain** with no further send. **A4:** the hub script runs
`dotnet test` with ErrorActionPreference Continue and converts its output to
strings, so a native stderr line under Windows PowerShell 5.1 no longer hides the
result marker; Stop is restored afterwards.

The complete verifier passed **198 non-live tests**, zero failures/skips, Release
build with **0 warnings/errors**, and formatting. The executing test assembly hash
is `a5e2a7499bb434b2ac963c629fd8330a9a02985680cf50de8819545a729cb09e`. The
[date-echo and stderr source manifest](store-downloads-hub-date-echo-stderr-fixes-source-hashes.json)
contains **87 matching files**; exactly four changed from the UTC preview manifest:
the hub script, StoreHubCreation.cs and its tests, and the helper tests. All
earlier manifests remain intact as history.

No live Microsoft request, credential read or reporting POST occurred for these
fixes. Progress remains **5/18**; no task was checked. DH6/DH7 and v0.2 stay
unaccepted, and the next step above is unchanged.

## Report-path diagnostic (2026-10-11)

The operator approved the report-path diagnostic and accepted Amendment 1 (the
stage releases the experiment lock while StoreHubCreation creates the query and
report, then retakes it for polling, download, saving and analysis). The new
`-Stage InstallsReport` skips the preview and creates one Installs query and one
executeNow report under a baseline identity proof, then polls, downloads and saves
the original CSV and records only counts and booleans; see the
[validation guide](store-downloads-hub-validation.md#report-path-diagnostic).

The complete verifier passed **236 non-live tests**, Release build with
**0 warnings/errors**, and formatting. The executing test assembly hash is
`d1ae62fc18bb096e7c544657a123fec844a889a214f98ea16da2f257ba4c6892`. The
[report-path source manifest](store-downloads-hub-report-path-source-hashes.json)
contains **92 matching files**. Nine files changed or are new: five new files
(StoreHubBaselineIdentity.cs, StoreHubExecution.cs, StoreHubReportDownload.cs,
StoreHubReportStage.cs, StoreHubReportStageTests.cs) and four changed from the
date-echo manifest (StoreHubCreation.cs, StoreHubHelperTests.cs,
StoreHubQueryPlan.cs and the hub script). All earlier manifests remain intact as
history.

No live Microsoft request, credential read or reporting POST occurred. A live run
creates 1 query and 1 report in Partner Center that are not deleted; only the
operator starts it. Progress remains **5/18**; tasks 2.4, 2.5 and 2.7 are partly
implemented for Installs only and stay unchecked. DH6/DH7 and v0.2 stay unaccepted.
