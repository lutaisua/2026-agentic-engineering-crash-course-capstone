# Replacement Store application diagnostic

Date: 2026-10-06. **Earlier empty-inventory comparison failed; v0.2 remains unaccepted.**
This is a follow-up to the [original diagnostic](store-association-diagnostic.md).
No actual identifiers, vault targets, response bodies, tokens or secrets are
included here.

The later [parent-isolation implementation checkpoint](v0.2-store-parent-isolation.md)
records 105 passing non-live tests and expanded diagnostics: the first failed
parent is published, query omission does not resolve its HTTP 400, and the
documented account-wide add-on collection also returns HTTP 400. The provider
now continues after individual parent failures. Earlier evidence below remains
historical and does not replace that newer source/checkpoint.

**Latest operator update:** a Store discovery review now contains the account's
applications while the command still exits 2. Two newer populated review
candidates have now been inspected; the operator's exact file path is still
awaiting confirmation. See the populated follow-up below. The empty-result
findings describe the earlier run and must not be generalized to the newer data.

## Confirmed setup and saved review

The operator reports replacing both Store runtime Client IDs, creating a new
Generic Credential at a different address, and seeing the replacement application
in Partner Center's main list with Manager(Windows) after reloading.

The protected schema 2 profile passed offline validation with one Store publisher.
Both Store Client IDs match and both credential references have mapping entries.
The private review inspected earlier was modified at **2026-10-06T10:43:30Z**.
Its binding and attempt timestamp matched the offline attempt at that inspection.
That review reports:

| Item | Result |
|---|---|
| Overall outcome/category | `partial` / `incomplete-coverage-or-binding` (exit 2) |
| Account coverage | `unverified` / `account-evidence-missing` |
| App collection coverage | `complete` / `all-pages-and-publication-evidence` |
| Add-on coverage label | `complete` / `all-pages-and-publication-evidence`; no parent apps were returned |
| Apps / add-ons returned | **0 / 0** |
| Accepted complete snapshot | None |

The empty-parent case cannot establish coverage of the operator's roughly
43 published apps and 86 add-ons. Do not create account-comparison evidence for
this mismatch or reinterpret exit 2 as only a missing evidence file.

## Read-only live diagnostic

The existing `StoreInventoryLiveDiagnostics` test was run once using the current
protected profile, existing Release build, private resolver and bounded Store
HTTP reader. It made token POST and app-collection GET requests:

- Token endpoint: **HTTP 200**.
- App collection: **HTTP 200**, **0 apps**.
- Decoded token audience, tenant and application match the configured values.
- No parent apps were returned, so no child requests were made. The zero add-on
  count is not proof that the publisher has no add-ons.
- The known-published-app assertion failed; diagnostic test exit was **1**.
  This is separate from the user's discovery-command exit **2**.

The decoded claims are neither signature validation nor Partner Center account
proof. The mapped secret was accepted for the configured application. Its value
and bearer token stayed in memory; no publishing or report request occurred.
Only HTTP statuses, identity-match booleans and counts were returned for review.

All **60** implementation/test files matched the
[100-test authentication source manifest](v0.3-authentication-source-hashes.json).
No implementation changed during this follow-up. The earlier non-live gate remains
historical; the new live comparison failed and does not accept discovery task 2.9.

## Populated follow-up

Two newer saved reviews, modified at **2026-10-06T15:27:09Z** and
**2026-10-06T15:28:48Z**, have identical sanitized results:

| Item | Result |
|---|---|
| Current returned app records (`partialProducts`) | **85** |
| Apps with publication evidence | **55** |
| Apps with unknown publication | **30** |
| Current returned add-ons | **0**, with failed discovery; not an empty-account conclusion |
| Account coverage | `unverified` / `account-evidence-missing` |
| App publication coverage | `unverified` / `publication-evidence-missing` |
| Add-on coverage | `failed` / `http-request-failed` |
| Overall | `partial` / `incomplete-coverage-or-binding`, exit **2** |

These app records are from the current attempt's `partialProducts`, not an older
snapshot. Both reviews contain no complete snapshot. The exact operator-selected
file remains unconfirmed, so these are identified as review candidates.

The existing read-only live diagnostic was then rerun once at unchanged source.
It recorded token **HTTP 200**, nine app-collection **HTTP 200** responses, then
the first add-on collection **HTTP 400**. Decoded audience, tenant and application
again matched the configuration. The generic `http-request-failed` assertion
failed, while the earlier empty-inventory assertion did not fire. The test stops
at that add-on failure and does not log final counts; use the saved review counts
above instead. No child-detail request or publishing/report operation was made.

The 400 response's body/code and affected parent have not been inspected; do not
infer that it is an invalid secret, an unsupported feature, or a genuinely empty
add-on collection. The next diagnostic should isolate that parent/request and
compare behavior for a known published app. The request route and optional `top`
parameter match Microsoft's
[add-on collection contract](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-add-ons-for-an-app).

Compare the 30 unknown app records privately with Partner Center. Missing
publication metadata does not automatically establish draft-only status under
the current verified contract. The 55 publication-evidence records are not an
attestation that all are currently available or that the account matches the
operator's earlier approximate count. Record account evidence only after the
private identity/parent/publication comparison. Account evidence alone cannot
resolve the publication and HTTP 400 gaps.

## Next diagnostic decision

The operator now reports 30 apps with dashboard status **Not started** (only a
reserved name) or **Not submitted** (not published). This is operator-supplied
status evidence, not a verified per-ID mapping or automatic API classification
rule. Record the private identity comparison, define supported unpublished proof,
and keep genuinely unknown metadata distinct. The failed add-on parent has not
yet been correlated with one of these 30 records. See the
[development sequence](../store-development-next-steps.md).

First inspect the exact populated review the operator is viewing. A partial
result can contain returned apps/add-ons in `partialProducts` while exit 2 still
signals incomplete account, publication or provider coverage. A `snapshot`, when
present, may be an earlier successful inventory; distinguish it from the current
attempt's products and configuration binding. No populated-run completeness or
account attestation is established by the operator's report alone.

If applicable product coverage is complete and only `account-evidence-missing`
remains, privately compare the returned identities, parents, publication and
counts with Partner Center, then follow
[account attribution](store-discovery-live.md#record-account-attribution-after-comparison).
Unknown publication or failed/unsupported traversal must be investigated
separately; recording account evidence does not resolve those gaps.

The operator has been asked to privately compare Client ID and Tenant ID shown
in Partner Center with the runtime profile and verify that the saved role is
under the same **Windows developer publisher account** owning the products.
This comparison is not yet confirmed. A token for the configured tenant/client
does not establish that publisher relationship.

For the historical empty result, if a mismatch is found, correct only the relevant private configuration or
association, then perform one fresh read-only comparison. If those contexts
match, investigate with Microsoft Partner Center support rather than repeatedly
recreating registrations or rotating accepted credentials. A diagnostic GET
for one privately selected known app can further distinguish app-specific access
from an empty collection; no such request has been made yet. Microsoft documents
both [collection and app-detail methods](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-app-data).

Use [Windows Developer Support](https://developer.microsoft.com/en-us/windows/support)
and its non-technical account/program support option. The following draft is
historical draft only; it must be revised for the populated follow-up and HTTP 400
before use. No case or message has been submitted:

> A Microsoft Store MSIX/UWP publisher has approximately 43 published apps and
> 86 add-ons. A newly registered Entra application appears in Partner Center's
> main application list with Manager(Windows). A client-credentials token request
> returns HTTP 200; decoded application, tenant and audience match the private
> configuration. GET /v1.0/my/applications returns HTTP 200 with an empty collection.
> Please investigate the application-to-Windows-publisher association and Store
> submission API account attribution. Publisher/tenant/client details and exact
> request timestamps will be supplied privately in the support case.

Do not add an assertion that the publisher context matches until that comparison
has been made. Add actual account/client/tenant identifiers only in the private
support case. Exclude passwords, bearer tokens, cookies and authorization headers.
