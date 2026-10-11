# Subscription diagnostic implementation checkpoint

Date: **2026-10-10**. Change: diagnose-store-subscription-inventory.
Binary remains **0.1.0**; v0.2 is unaccepted. Authorized live diagnostic DG7
was reviewed separately from the offline gate.

## Implementation and cumulative verification

The dedicated Live harness and masked-input PowerShell helper compare the known
Subscription detail and parent before four collection variants. Finite associated
error evidence and validated per-attempt correlations are recorded in unique
protected packets. Atomic evidence/support files are verified before a final
receipt. The wrapper checks receipt/hash/completion consistency and restores
temporary environment settings. No support request, report or inventory snapshot
is created by the comparison.

The runner enforces official destinations, GET without bodies, one immutable
publisher binding, a three-minute deadline, 24-request aggregate cap, bounded
transient retries and one forced 401 recovery. HTTP 400 is not retried.
Production Durable/Consumable and publication policy remain unchanged.

The documented `powershell.exe -NoProfile -File scripts\verify.ps1` passed:

- Release build: **0 warnings, 0 errors**.
- **135 non-live tests**, zero failures and skips.
- Formatting verification: passed.
- [Source manifest](store-subscription-diagnostic-source-hashes.json): **74 files**,
  all hashes matched after capture; generated obj/bin files excluded.

The historical 105-test/61-file [parent isolation checkpoint](v0.2-store-parent-isolation.md)
and its manifest remain unchanged. The new manifest records source/build/script
files at this later checkpoint. Private live packets record the executing test
assembly SHA-256 separately. SDK/schema commands required authorized sandbox
access. Verification used fictional fixtures; no Microsoft request or real vault.

| Gate | Offline evidence | Live status |
|---|---|---|
| DG1 | Strict sample/scope, single/multiple same-tenant publishers, zero-call invalid preflight, offline helper/help, rejected repository/junction/broad ACL and owner | Protected operator sample reviewed |
| DG2 | Finite associated records, nested/case variants, ambiguity/limits and payload canaries | Five matching NotAllowed/productType/Subscription records |
| DG3 | Per-attempt GUID/cV validation, distinct retries, missing/conflicting/invalid values and public redaction | Store omissions reviewed; token-only request identifier private |
| DG4 | Exact six routes, 400/detail 200, type rejections, Subscription/Durable, 404/mismatch, retries, 401/403, redirects, budgets/deadline/cancellation | Parent 200; detail and four collections 400 |
| DG5 | Real Windows ACLs, unique atomic publication, overwrite refusal, write/commit/corruption/cancellation failures, unchanged prior state, receipt/hash checks and restored environment | Receipt, hashes, ACLs and source assembly verified |
| DG6 | Deterministic support views, private-only identifiers/correlations, correct Q&A analogy, explicit unknowns and no posting call | Attachment reviewed; shared draft sanitized; case unsubmitted |
| DG7 | Cumulative build/tests/format/source checkpoint | Diagnostic comparison reviewed; inventory/version acceptance still separate |

The additional 30 passing cases cover the parser (4), transport (2), private input
(5), packet (4), wrapper (7) and runner (8). Existing 105 checks are retained.
Helper fixtures substitute fictional masked input and fake dotnet; they never
read Windows Credential Manager or call Microsoft.

## Authorized live evidence and acceptance

The operator entered the known parent and Subscription Store IDs through the
[private helper](store-subscription-diagnostic.md) and ran it with -RunLive.
The packet and attachment were reviewed locally, without sharing their private
identifiers or returned correlation value. Interval: **2026-10-10
17:19:35–17:19:48 UTC**. All six statuses were recorded; diagnostic exit **0**.
Seven transport attempts, one each, included one token HTTP 200 response.

Parent HTTP 200 matched the requested ID and had Published evidence. Detail
and four collection variants returned HTTP 400 with associated
NotAllowed/productType/Subscription, type captured from the recognized message
pattern. No capture limitation on those error tuples. No accepted Store
correlation value; the Entra token-only X-MS-Request-ID is private. Successful
detail identity/parent/publication/wire/parser checks are unavailable, not a
mismatch. The matching product-type limitation remains a sample/account/time
hypothesis; a complete official inventory route and fix are unresolved.

The source assembly SHA-256 matches the executing Release test assembly at the
135-test checkpoint. Final receipt/hashes and private permissions verified; the
deterministic support attachment includes sample IDs, error provenance, omission
states and the Q&A precedent. The shared draft now contains actual sanitized
results. No case was submitted and no additional live request was needed for
this packet review.

The diagnostic change is **14/14 tasks complete**. Strict OpenSpec validation
passed for diagnose-store-subscription-inventory, discover-publisher-products
and develop-analytics-et-in-iterations. Six affected documentation files have
no broken local links. The 74 source hashes still match after the final review.

Discovery remains **17/31** tasks, iterations **9/47**. Complete apps/add-ons
inventory, account/publication verification, the 30 unknown apps, report exports
and v0.2 acceptance remain separate outstanding gates. Nothing is synced,
archived, released or committed by this diagnostic checkpoint.
