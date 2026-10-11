# Store Subscription diagnostic

Implemented diagnostic procedure, 2026-10-10. The helper and comparison have
passed offline verification and an authorized live packet review. This guide
describes diagnostic evidence, not accepted inventory or a confirmed Subscription
API limitation.

## Evidence vocabulary and privacy

Errors are inspected at the root and known error-envelope paths, to depth 8,
with at most 8 error records and a 16 KiB error-body limit. Exact, case-insensitive
allowlists replace the previous code-prefix heuristic. Observed spelling is kept
only after a finite match; unknown suffixes are not accepted.

| Field | Initial recognized values |
|---|---|
| Code | NotAllowed, BadRequest, InvalidState, InvalidIapProductType, InvalidApplicationType, InAppProductsNotSupported, NotFound, NotSupported, Unsupported, Unauthorized, Forbidden |
| Target | productType, applicationId, inAppProductId, top, skip |
| Product type | Subscription, Durable, Consumable, ManagedConsumable, NotSet |

Each field has `state` and an optional `value`. States distinguish present,
absent, unrecognized, malformed and ambiguous evidence. `path` retains a known
JSON envelope location. `typeSource` distinguishes a productType field from the
recognized type-rejection message pattern. Records never merge fields from
different error objects; conflicting records and duplicate properties are explicit.
Generic message-category booleans do not establish a specific product type.
Raw messages, bodies, authorization/token data and arbitrary values are omitted.

Fictional parser fixture, **not an observed response for this publisher**:

```json
{
  "path": "$",
  "code": { "state": "present", "value": "NotAllowed" },
  "target": { "state": "present", "value": "productType" },
  "productType": { "state": "present", "value": "Subscription" },
  "typeSource": "known-message-pattern"
}
```

## Correlation capture

Capture candidates are X-Correlation-ID, X-MS-Correlation-Request-ID,
X-MS-Request-ID and Request-ID headers plus named correlationId/requestId error
fields. Their accepted values must have GUID D or N format. MS-CV values must
satisfy [Microsoft's cV 2.1 specification](https://github.com/microsoft/CorrelationVector/blob/master/cV%20-%202.1.md),
including its 128-byte bound and unsigned counter limits. These candidate names
are not a guarantee that the Store service returns those headers.

Missing, excessive, ambiguous, unsupported-format and oversized values get markers
without their contents. No more than 8 correlation fields survive an attempt;
an explicit limit marker replaces excess fields. Every transport attempt retains
its own UTC start/end, operation, query variant, attempt number and HTTP status.
Correlation values are private: ordinary output uses an explicit projection that
contains only operation/query/attempt/status/failure and an allowlisted service code.
The older diagnostic summary's JSON projection excludes body correlations too.

## Verification and gates

DG1 checks explicit scope/preflight; DG2 finite associated errors; DG3 private
per-attempt correlations; DG4 comparison and bounds; DG5 protected atomic evidence;
DG6 support-view redaction/provenance; DG7 separately recorded live verification.
The parser, transport, runner, packet and PowerShell helper use fictional fixtures.
The [separate diagnostic checkpoint](store-subscription-diagnostic-checkpoint.md)
records the passing Release build, 135 non-live tests, formatting and 74-file
source manifest. Actual live DG7 is recorded separately below.
Correlations or error fields absent from a response will not be invented.

The [diagnostic design](../../../../../openspec/changes/diagnose-store-subscription-inventory/design.md)
defines all seven gates and the six probes. Discovery still requires complete
apps-and-add-ons scope, publication and account evidence. A successful diagnostic
must not authorize reports, write a successful inventory snapshot or accept v0.2.

## Prepare and run privately

Use your ordinary Windows PowerShell 5.1 terminal, from the submission directory.
The existing schema 2 runtime profile and credential-reference mapping must be
private and valid. Manager(Windows) association is still required. No replacement
credential or new Entra application is implied by the historical HTTP 400.

```powershell
dotnet build AnalyticsET.slnx --configuration Release
powershell.exe -NoProfile -File scripts\Invoke-StoreSubscriptionDiagnostic.ps1 -Help
powershell.exe -NoProfile -File scripts\Invoke-StoreSubscriptionDiagnostic.ps1
$LASTEXITCODE
```

The last helper invocation prepares input offline. It prompts locally, with masked
entry, for the **existing publisher alias in publishers.json**, the parent app's
Store ID and one known Subscription add-on's Store ID. Use the add-on's Store ID,
not its developer product ID. Confirm the type and relationship in Partner Center
before typing CONFIRM. It never prompts for a password or secret. Do not send the
IDs, profile JSON or secret through chat.

Default input: `%USERPROFILE%/.analyticset/diagnostics/store-subscriptions/sample.json`.
Default results: the `runs` directory beside that file. The input has schemaVersion,
publisherId, parentApplicationStoreId, subscriptionStoreId, expectedProductType
and confirmedAt (UTC). The full C# preflight rejects duplicate/unknown fields,
unsafe paths/permissions and incorrect profile/resource bindings before vault/HTTP.
Multiple configured publishers are supported through that explicit alias.

Existing input is preserved. To select another sample, supply a **new** private
`-InputPath`; do not overwrite a prior record. `-ConfigurationPath`, `-InputPath`
and `-OutputRoot` accept paths only. The default mapping remains
`%USERPROFILE%/.analyticset/credentials/references.json`.

Explicitly authorize the read-only comparison after preparation:

```powershell
powershell.exe -NoProfile -File scripts\Invoke-StoreSubscriptionDiagnostic.ps1 -RunLive
$LASTEXITCODE
```

The helper can also prepare and run in one invocation with `-RunLive`. It sets only
the three path variables ANALYTICSET_LIVE_CONFIG, ANALYTICSET_DIAGNOSTIC_INPUT and
ANALYTICSET_DIAGNOSTIC_OUTPUT, restores their prior process values in finally, and
runs only CompareKnownSubscriptionCollectionsAndDetail. Detailed test output stays
in memory; the helper prints fixed outcomes and verified private file paths.
Routine verify.ps1 excludes all Live tests.

The six GETs use one Store inventory context, in this order:

| Probe | Relative route under https://manage.devcenter.microsoft.com/v1.0/my/ |
|---|---|
| Parent control | applications/{parentStoreId} |
| Known child detail | inappproducts/{subscriptionStoreId} |
| Per-parent collection | applications/{parentStoreId}/listinappproducts?top=10 |
| Per-parent collection | applications/{parentStoreId}/listinappproducts |
| Account collection | inappproducts?top=10 |
| Account collection | inappproducts |

The [direct detail contract](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-an-add-on)
requires the add-on Store ID and GET without a body. The
[per-app collection contract](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-add-ons-for-an-app)
documents optional top/skip. This diagnostic reads only initial collection pages,
never follows continuation links, and does not establish complete enumeration.

Limits are three minutes overall, 60 seconds per operation, three transient
attempts and 24 transport requests including token requests/retries. Only one
forced 401 recovery is allowed for the run. HTTP 400 is not retried. Destination,
redirect, authorization, credential-generation, exhausted retry/request budget
and cancellation failures stop later probes. No report, publishing or snapshot
operation is available in this harness.

## Inspect the packet and interpret completion

Each unique GUID run directory contains evidence.json, support-attachment.md and
receipt.json. Only the operator and SYSTEM have access. The receipt is written
last, after both files pass content/hash and ACL checks. The wrapper verifies
the receipt, its exit/completion values and both file hashes before printing the
packet path. Directory existence or a single file is insufficient evidence of a
completed recording. Prior input, packets, configuration, mapping and inventory
are preserved. The evidence records the test assembly SHA-256 and binding
fingerprint; the assembly hash is distinct from the source manifest.

| Exit | Meaning | Fictional verification example |
|---|---|---|
| 0 | All six HTTP outcomes recorded and packet verified; service success is separate. | Parent/detail 200 and four collections 400. |
| 2 | Partial comparison with missing probes, stored as partial if recording succeeds. | Parent 403 stops the later five probes. |
| 1 | Invalid prerequisites or recording/receipt failure. | Unsafe ACL or failure between packet files. |
| 130 | Operator cancellation, with partial evidence only when safely recorded. | Controlled cancellation stops later probes. |

Ctrl+C can terminate the test process before it writes a packet. Treat missing or
invalid receipts as unrecorded; never infer completion from a killed process.
These examples are runnable fixture tests, not this publisher's live evidence.

Open evidence.json and support-attachment.md locally using the packet path printed
by the helper. Actual IDs and returned correlations remain private. Report only
the packet path or the helper's sanitized stage/category if asking for help. A
configuration-access/private-storage failure calls for local path/owner/ACL checks;
masked-sample-input calls for the existing alias and correct Store IDs; live-run
or result-validation calls for a current Release build and the exact test entry.
Do not paste private JSON or unfiltered dotnet output.

| Observed evidence | Sample-limited interpretation |
|---|---|
| Collection failure, matched successful detail | Collection/detail difference for this sample and time. |
| Matching explicit associated type rejections | Product-type limitation hypothesis strengthened. |
| Matched detail with wire Subscription, parser rejection | Local parser gap; cannot cause an earlier server HTTP 400. |
| Detail 404, wrong identity/parent or malformed metadata | Unresolved or mismatched sample. |
| All six readable with valid metadata | Historical failure not reproduced at this time. |

Operator Subscription and wire Durable are recorded separately. Durable alone
does not prove an incorrect sample. A returned 400 with absent/unrecognized error
fields stays an observed 400 with limited evidence; no type or cause is invented.

The generated private support attachment separates facts, metadata checks,
interpretations, field/correlation omissions and remaining questions. Its run ID
is local, never presented as a Microsoft correlation. Review it before optionally
submitting to Microsoft. The helper does not send a case. The shared
[support draft](store-subscription-support-request.md) now contains the reviewed
sanitized actual comparison.

## Reviewed live comparison

The operator's protected sample run on **2026-10-10 17:19:35–17:19:48 UTC**
recorded all six HTTP outcomes, seven transport attempts including authentication,
and exit 0. The receipt, both file hashes and private operator/SYSTEM permissions
verified locally; its test assembly hash matches the cumulative checkpoint.

Parent control: **200**, identity matched and Published evidence. Direct
Subscription detail and all four collection variants: **400**, each with the
associated exact **NotAllowed / productType / Subscription** tuple. Type provenance
is the recognized rejection-message pattern; successful detail metadata was not
available. No retries or incomplete probes. No accepted Store correlation was
captured; one Entra token-response request identifier stays private.

The matching product-type rejection hypothesis is strengthened for this sample,
account and time. Detail identity/parent/publication/wire type/parser compatibility
are unavailable, not mismatched. This does not prove universal exclusion, a
collection-only defect or a supported inventory alternative. The support
attachment has been reviewed and the case remains unsubmitted. All existing
inventory and version gates remain outstanding.
