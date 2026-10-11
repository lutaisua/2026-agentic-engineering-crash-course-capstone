# AnalyticsET version gates

Versions are cumulative. Every version requires its predecessor's gate and its
own acceptance evidence. Missing authorized live evidence remains blocked.
The OpenSpec implementation plan is `develop-analytics-et-in-iterations` in the
parent AnalyticsET workspace; application source and evidence stay in this submission.
The [current development/setup status](project-status.md) records 9 of 47 sibling
tasks complete and the in-progress `discover-publisher-products` change, which adds
cumulative D1/D2/S1/S2/M1/M2/L1/R1 gates. Task progress does not accept a version.

The separate [Store Downloads Hub experiment](verification/store-downloads-hub-checkpoint.md)
has protected metadata discovery, a read-only Installs preview and fixture-verified
atomic creation journals/single-send POSTs, with 195
non-live tests. Live discovery found 16 datasets including both targets; the
date-only and UTC timestamp previews both returned zero rows while the private
matching app/calendar-day Partner Center baseline has positive install counts.
Scope/date/metric comparison, async exports and populated Installs/Subscription
acquisition comparisons remain pending. Five completed tasks do not replace v0.2 installs/
ratings, complete apps/add-ons inventory, account/publication or later-publisher
requirements. A favorable future sample needs a separate adoption decision;
no production reporting route or version gate is changed here.
The later offline [date-echo and stderr fixes](verification/store-downloads-hub-checkpoint.md#date-echo-and-stderr-fixes-2026-10-11)
record 198 non-live tests in a new [source manifest](verification/store-downloads-hub-date-echo-stderr-fixes-source-hashes.json);
v0.2 remains unaccepted and progress stays 5/18.
The offline [report-path diagnostic](verification/store-downloads-hub-checkpoint.md#report-path-diagnostic-2026-10-11) records 236 non-live tests in its [source manifest](verification/store-downloads-hub-report-path-source-hashes.json); no live request was made.

| Version | Scope | Acceptance / verification | Current state |
|---|---|---|---|
| v0.1 | Local profiles, selection/isolation, help/version, offline validation | V01-A1..A4: CRUD, schema/scope validation, same-tenant isolation, offline CLI and verifier | [Accepted offline](verification/v0.1.md): 42 tests, build and format pass |
| v0.2 | Discovery-ready profiles; Store apps/add-ons; installs/ratings, CSV/raw JSON, manifests | D1/D2/S1/S2/L1/R1 plus V02-A1..A4 and authorized comparison | Store discovery fixture verified; private profile offline-valid; [live/report gates pending](verification/store-onboarding.md) |
| v0.3 | Marketplace offers/plans; dataset discovery and first applicable analytics export | M1/M2/L1/R1 plus V03-A1..A4 and authorized type/dataset comparison | Planned; type-contract and account coverage pending |
| v0.4 | Store/Marketplace earnings and payments | V04-A1..A4: proven local delegated MFA/renewal, program provenance, async download and financial comparisons | Planned; local authorization proof pending |
| v0.5 | Reliable multi-publisher batches and recovery | V05-A1..A5: independent outcomes, retries, cancellation/resume fixtures and two-publisher live run | Planned |
| v1.0 | Verified catalog and Windows distribution | V10-A1..A5: cumulative PRD traceability, per-report live gates, review, clean-user package check and demo | Planned |

See [PRD acceptance criteria](prd.md#9-acceptance-criteria-and-verification).
Record actual results in `docs/verification/v<version>.md`, including commands,
test counts, code identity, acceptance status, live scope, and gaps. Fixture success
does not establish Microsoft access or turn a planned connector into supported coverage.
