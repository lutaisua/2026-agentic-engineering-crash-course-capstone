# AnalyticsET pause and resume checkpoint

## Current resumption: 2026-10-11

Work resumed at the operator's request for **validate-store-downloads-hub-exports**.
The pause note was read first. All 85 saved source hashes and the executing
assembly matched before new implementation. The pause ZIP below remains unchanged.

Next resume step: the operator's own live `-Stage InstallsReport` run (see the [report-path checkpoint](verification/store-downloads-hub-checkpoint.md#report-path-diagnostic-2026-10-11) and [manifest](verification/store-downloads-hub-report-path-source-hashes.json)).

Latest follow-up: the small original Installs CSV is now readable after Excel was
closed. Protected path/owner/ACL and byte-preservation checks passed. Its **148
rows** all match the selected app; all **12 unique headers** occur in the native
schema. The operator confirms a direct Partner Center download, two matching
calendar days, Daily aggregation and only the selected-app filter. Download date
is operator-reported as 2026-10-10. Slash-form dates fit the declared month/day/year
selection and contain positive successful-install counts. No timezone was selected;
the file has no timezone marker. This establishes baseline observations, not API
parity or UTC/inclusion proof.

A tested explicit UTC timestamp preview control kept the same query, app, binding
and fixed UTC instants. Token and preview returned **HTTP 200**, again **zero rows**,
with a verified separate private receipt and exit **2**. Changing date wire format
did not resolve the discrepancy; its cause remains unknown. No reporting POST or
CSV export occurred. The full verifier now passes **195 non-live tests**, build
with zero warnings/errors and formatting. The
[UTC preview source manifest](verification/store-downloads-hub-utc-preview-source-hashes.json)
has **87 matching files**. Seven harness/helper files changed; production files
and historical manifests are preserved. Progress remains **5/18**. Continue narrow
consumer query/date-scope investigation and the remaining offline export/recovery
implementation; creation and live comparisons remain blocked by unresolved proof.
Preserve the small CSV and all earlier input, receipts and original downloads.

The following creation checkpoint describes the earlier state of this resumption.

Task **2.2** is now fixture verified: private atomic creation-intent/ID journals,
single-send query/report requests, scope checks and no replay after an uncertain
response or recording failure. The complete verifier passed **186 non-live tests**,
Release build with zero warnings/errors, and formatting. The
[creation source manifest](verification/store-downloads-hub-creation-source-hashes.json)
has **87 matching files**; all 85 previous source files are unchanged. Progress
is **5/18**, including tasks 1.1–1.4 and 2.2. Remote reconciliation, polling,
downloads, comparison records and the complete live export helper remain pending.
The Installs live helper still performs only a read-only preview.

The new installs-partner-center-confirmed.csv passed protected path/ACL checks
and has 12 unique headers including Date and SuccessfulInstallCount. Original
bytes were preserved. The operator reports a three-month selection downloaded
on 2026-10-10; exact boundaries/timezone/aggregation/filters remain unspecified.
Its window is broader than the saved two-day experiment, so rows were not parsed
and no comparison was accepted. Preserve this file and the Acquisitions download.

At that checkpoint, the next private input was a Custom/Daily export for the two days in the existing
experiment, saved as **installs-partner-center-small.csv** alongside them. Obtain
its full path and displayed dates/timezone/filter settings, or the specific UI
limitation if Custom/Daily is unavailable. Keep IDs and rows private. Do not widen
the API experiment to three months or infer equivalence from file headers alone.
Task 2.1, live comparisons and v0.2 remain unaccepted. No new Microsoft request,
vault read or reporting POST was made during this resumption.

## Current pause: 2026-10-11

This snapshot records the earlier operator-requested pause; work has since resumed
as described above. At the pause, **validate-store-downloads-hub-exports** had
**4/18 tasks complete**. The snapshot and October 5 checkpoint remain historical.

| Area | Saved state |
|---|---|
| Application | Development binary 0.1.0; v0.1 accepted offline, v0.2 unaccepted |
| Offline verification | Last full verifier: 177 non-live tests, Release build and formatting pass |
| Source identity | All 85 query-checkpoint source hashes and executing assembly hash matched at this pause |
| Downloads Hub discovery | Reviewed private receipt: exit 0, 16 datasets, Installs and AddOnAcquisitions present |
| Installs API preview | HTTP 200, zero rows, exit 2 / hub-date-boundary-unverified; no query/report resource created |
| Partner Center baseline | Supplied file passed private path/ACL checks, but its 35 headers have Acquisitions fields and no Date/successful-install metric; rows were not parsed |
| Latest operator correction | Installs was found in Downloads Hub; the earlier not-listed statement is superseded |
| Next private input | Original Daily Installs download, saved alongside the existing file as installs-partner-center-confirmed.csv; its path/scope has not yet been supplied |
| Sibling changes | Discovery 17/31; iterations 9/47; Subscription diagnostic 14/14, unarchived |
| Release gates | Scope/date/identity proof, one-time reports, downloads and populated comparisons remain pending |

The verified source manifest is
[store-downloads-hub-query-source-hashes.json](verification/store-downloads-hub-query-source-hashes.json).
See [current status](project-status.md) and the
[validation procedure](verification/store-downloads-hub-validation.md#installs-preview-discrepancy-and-next-evidence).
No task checkbox, normal reporting route, inventory completeness or version gate
was accepted by this pause. No live call or credential read was made to save it.
No background continuation or scheduled resume was created.

Keep the workspace, this chat and the existing protected runtime/credential files.
Preserve the private experiment's input.json, binding.json, Discover and Installs
preview packets, and original Acquisitions CSV under
`%USERPROFILE%/.analyticset/validation/store-downloads-hub/<experiment-id>/`.
The exact experiment path was supplied in this chat; do not place its private
contents in the repository or checkpoint ZIP. Preserve the existing CSV even
though its filename says installs; use the new confirmed filename for the next
download. Windows Generic Credentials do not need recreation for a pause.

The shared working-tree snapshot for this pause is
`.local/checkpoints/analyticset-pause-2026-10-11-d71cc59a.zip` under the parent AnalyticsET workspace,
with a companion SHA-256 file. It contains submission source/tests/scripts/docs
and OpenSpec artifacts, excluding Git metadata, build outputs, dependencies,
private runtime files and vault data. Use the live workspace normally; restore
selected files from the snapshot only if needed, without overwriting newer work.

To resume, send in this chat:

> Resume AnalyticsET using $openspec-apply-change validate-store-downloads-hub-exports.
> Read submissions/sergiilutai/docs/pause-resume.md and docs/project-status.md in
> the capstone repository first. Continue from the saved checkpoint; keep all
> incomplete tasks and v0.2 gates unchecked.

On resumption, review guidance and refreshed OpenSpec instructions, compare source
hashes and local changes, and verify any new baseline's protected path/ACLs before
reading it. Use the existing experiment and confirmed Installs dataset. Obtain
only the new file path and displayed dates/timezone/filters/metric settings; keep
IDs and rows private. Compare its schema/scope with the saved zero-row preview
before report creation. Do not substitute acquisition quantities, broaden products
or switch to rolling dates to make the discrepancy disappear. No fresh live
request is required merely to resume; unresolved proof continues to block creation.

## Historical pause: 2026-10-05

Paused at the operator's request: **2026-10-05**.
Resumed at the operator's request: **2026-10-06**. The paused snapshot below is
retained as history; follow [current status](project-status.md) for resumed work.
At resumption, all 59 source files matched the saved diagnostic manifest; no
implementation files were missing, added or changed during the pause. Independent
Marketplace authentication work subsequently passed the updated 100-test verifier;
see [its checkpoint](verification/v0.3-authentication.md). The Partner Center association
status must be reconfirmed before another live check.
All commands started for the previous checks finished; no background continuation
or scheduled work was created for this pause. This is an unfinished working-tree
checkpoint, not a version acceptance or OpenSpec archive.

## Saved state

| Area | State |
|---|---|
| Application | .NET 10 console project in `submissions/sergiilutai`; development binary reports 0.1.0 |
| Accepted version | v0.1 offline foundation |
| Discovery change | `discover-publisher-products`: **16/31** tasks complete; 15 remain |
| Original iteration change | `develop-analytics-et-in-iterations`: **9/47** tasks complete |
| Offline verification | Release build: zero warnings/errors; **93 non-live tests passed**; format passed |
| Actual private profile | Schema 2, one Store publisher; offline validation passed after protected alias repair |
| Live diagnostic | Token HTTP 200; app collection HTTP 200 with zero apps; decoded token identity matches configuration |
| Dashboard comparison | Operator reports about 43 published apps and 86 add-ons; empty API inventory fails comparison |
| Current blocker | AnalyticsET is absent from Partner Center's application list but still exists in Entra; association recovery/reconfirmation is required |
| v0.2 acceptance | Unaccepted; discovery live/account evidence and original Store report gates remain incomplete |

The [current status](project-status.md), [live diagnostic](verification/store-association-diagnostic.md),
and [source manifest](verification/v0.2-store-association-diagnostic-source-hashes.json)
record the implementation and verification evidence. Task 2.9 remains unchecked.
Task 2.7 depends on the sibling Store report commands; task 2.10 requires their
original acceptance checks as well. Marketplace providers and later version gates
remain pending. No release, commit, PR, spec sync or change archive was made as
part of this pause.

## Preserve during the pause

Keep the existing workspace and this chat. Shared source, tests, scripts, docs and
OpenSpec planning artifacts were saved in a verified local checkpoint ZIP under the
parent workspace's `.local/checkpoints` directory. Git metadata, build outputs,
dependencies, private runtime data and Windows credentials are excluded.

Keep these existing protected files on the same Windows account/PC:

- `%USERPROFILE%/.analyticset/runtime/publishers.json`
- `%USERPROFILE%/.analyticset/credentials/references.json`
- Their protected alias-repair `.bak` files and private review/evidence files.
- `%USERPROFILE%/.analyticset/onboarding/entra-application.json`; this older record
  is preserved but its JSON repair remains a separate private follow-up.
- The existing Windows Credential Manager Generic Credential.

The shared checkpoint ZIP is not a credential backup. Do not copy private files
into the repository/archive or paste their contents into chat. No identifier,
secret, registration or profile needs to be recreated merely to pause. The
new-profile helper preserves existing destinations and should not be rerun for
an already configured publisher. Tokens were kept only in process memory; a
later live run acquires a fresh token.

## Resume in this workspace

Send this in the existing chat:

> Resume AnalyticsET using $openspec-apply-change discover-publisher-products.
> Read submissions/sergiilutai/docs/pause-resume.md and docs/project-status.md in
> the capstone repository first. Continue from the saved checkpoint and keep
> incomplete/live tasks unchecked.

The agent should:

1. Read this note, current project guidance/status, and refreshed OpenSpec apply
   instructions from the parent AnalyticsET workspace. Review local changes and
   source hashes before assuming that the saved verification still applies.
2. Confirm the operator has restored/reconfirmed the existing AnalyticsET app
   under the correct publisher, with Manager(Windows) and matching private IDs.
   Follow [the association recovery procedure](entra-application-setup.md#restore-a-missing-partner-center-association).
   Do not replace the existing registration or credential as a workaround.
3. Offline-validate the private profile, then rerun authorized read-only discovery
   in the operator's Windows logon session. Compare returned products with Partner
   Center privately. Record account evidence only when the discrepancy is resolved;
   populated add-on and later-publisher coverage need their own evidence.
4. Continue the remaining discovery/report prerequisites and cumulative gates.
   Refresh verification/source evidence after changes. Do not accept v0.2 or
   sync/archive an incomplete change.

Normal resumption uses the existing files; no ZIP extraction is needed. If files
were lost or changed unexpectedly, compare the checkpoint archive with the current
workspace and recover selected files without blindly overwriting newer work.
The archive restores shared implementation/planning only; private credentials and
configuration stay outside it.
