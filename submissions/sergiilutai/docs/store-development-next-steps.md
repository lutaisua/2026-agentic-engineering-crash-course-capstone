# Next development steps for the Store iteration

Reviewed: 2026-10-06. This is the recommended work sequence, not an acceptance
record or a change to runtime classification. Discovery remains **17/31 tasks
complete**, the sibling version plan **9/47**, and v0.2 is unaccepted.

The first implementation follow-up is now recorded in the
[parent-isolation checkpoint](verification/v0.2-store-parent-isolation.md):
per-parent failure recovery and sanitized probes are implemented; **105 non-live
tests**, build and formatting pass. The published-parent and account-wide add-on
HTTP 400 remain live blockers. Publication classification has not been relaxed.

## Current evidence

- The replacement application has operator-confirmed Manager(Windows), accepted
  mapped credentials, and matching decoded tenant/client/audience checks.
- The two newer reviews contain 85 app records: 55 with published-submission
  evidence and 30 classified `unknown` by the current implementation.
- The operator reports 30 dashboard apps are **Not started** (name reserved) or
  **Not submitted** (not yet published). Their statuses explain a plausible
  unpublished group, but a count match is not a per-app identity comparison.
- The first add-on collection returned HTTP 400. No successful populated add-on
  comparison or complete snapshot has been accepted.
- The operator confirms all add-ons for this publisher are **Subscription**.
  The [subscription contract investigation](verification/store-subscription-inventory.md)
  records the documented API differences and a prepared Microsoft support request.

The operator does not need to publish those 30 apps to complete this project.
Microsoft describes [name reservation](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/reserve-your-apps-name)
as an early step before publishing. Confirmed unpublished records belong outside
the report-export selection. Source identity/publication evidence still needs to
be explicit so genuinely unknown metadata is not silently reclassified.

## 1. Resolve reserved and unpublished classification

Privately match the 30 unknown source IDs to the corresponding dashboard states.
Reconcile the 55 published-submission records with the dashboard scope too; the
earlier roughly 43 published-app estimate is not the acceptance target.

Check whether documented app-detail/submission data reliably establishes that a
specific app was never published. The current classifier deliberately treats
absent/null/malformed last-published metadata as unknown; the
[app resource documentation](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-app-data)
does not explicitly define every absent-field case as never published.
If a per-product private dashboard attestation is needed, first update the active
OpenSpec design/tasks with its binding, provenance, stale/conflicting-evidence
rules and tests. The existing account-proof file is not a per-product draft proof.
Do not implement a count-based or blanket null-to-draft exception.

Acceptance: verified reserved/unpublished records remain visible for inventory
review and excluded from published exports; a published app with a pending update
stays published; genuinely unknown/conflicting metadata still blocks completeness.
Verify those cases with fictional fixtures and the private per-app comparison.

## 2. Diagnose and isolate the add-on HTTP 400

Identify the failed parent privately, inspect a sanitized service error code,
and compare one known-published parent with a reserved/unsubmitted parent.
HTTP 400 is not established as the documented no-add-ons outcome; Microsoft's
[add-on collection contract](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-add-ons-for-an-app)
documents 404 for no add-ons and 409 for unsupported features.
The current evidence does not prove that the failed parent was one of the 30.
The implementation follow-up now confirms that the first failed parent has
published-submission evidence. Both query variants return HTTP 400 for that
parent and for the account-wide collection, while the unknown parent returns
HTTP 200 with zero children. The operator confirms Subscription products; the
documented submission resource lists Durable/Consumable, while the customer-facing
Store SDK represents subscriptions as Durable. This is an unresolved publisher
contract, not proof of an empty inventory or a confirmed unsupported response.
Use the [prepared support request](verification/store-subscription-support-request.md)
to resolve the 400 and identify a complete official subscription inventory route.

Resolve a request/contract defect if found. Continue other parents after an
individual failure while preserving explicit incomplete coverage. Do not skip
all unpublished parents' child queries or label HTTP 400 not-applicable without
a verified contract and agreed scope; child publication has its own evidence.

Acceptance: one parent failure does not hide other successful app/add-on results;
all applicable pages and child details are read; unresolved failures retain exit
2 and preserve the last successful snapshot. Test a failing parent followed by a
successful parent, pagination and child-parent conflicts, then verify live counts
and publication for accessible add-ons against Partner Center.

## 3. Complete the Store inventory/account gate

Compare app/add-on identities, parents, publication and counts with the selected
Windows publisher. Record protected account evidence only after that comparison
and rerun the matching scope. Use
[the private comparison procedure](verification/store-discovery-live.md#record-account-attribution-after-comparison).
Account evidence does not close a publication or HTTP error gap.

Acceptance: known publication, applicable child coverage and verified account
binding produce a complete snapshot and exit 0. Missing later-publisher or
populated-child evidence remains unverified. This advances discovery task 2.9;
it does not itself accept v0.2 or grant report access.

## 4. Finish v0.2 installs and ratings exports

Implement the sibling Store report path: report catalog/applicability, explicit
UTC date ranges and product selection, complete pagination, CSV/raw JSON,
categorized run folders and manifests. Connect discovery to narrow selections
and explicit all-products only after those commands exist (discovery task 2.7).

Acceptance: fixtures cover dates, paging failure, CSV fidelity, cancellation and
publisher/program isolation. Compare authorized installs/ratings with matching
dashboard data, then run Release build, regression tests, formatting and source
evidence capture. Accept v0.2 only when both original and discovery gates pass
(discovery task 2.10 and the sibling version gate).

## 5. Continue the cumulative version sequence

After the v0.2 gate, deliver v0.3 Marketplace offer/plan providers and the first
applicable analytics export. Inventory token bindings are already fixture verified;
providers and live per-type coverage are pending. Follow with v0.4 delegated
earnings/payments, v0.5 multi-publisher batch recovery, and v1.0 distribution and
complete verification. See [version gates](versions.md).

No implementation, private configuration, publishing state or OpenSpec checkbox
was changed by recording this plan. Any new draft-proof mechanism requires a
coherent design/spec/task update before implementation; the current unknown-state
behavior remains in force until that work is verified.
