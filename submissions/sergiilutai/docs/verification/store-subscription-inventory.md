# Store subscription inventory investigation

Reviewed: **2026-10-10** (historical live observations: 2026-10-06).
This note records a contract investigation, not a
change to product scope or a v0.2 acceptance decision.

## Confirmed evidence

The operator confirms that **all add-ons for the current Store publisher are
Subscription products**. This describes this publisher, not every future
publisher. The earlier approximate count of 86 add-ons still needs a private
identity, parent and publication comparison.

The [105-test checkpoint](v0.2-store-parent-isolation.md) records successful
authentication and 85 app records: 55 with publication evidence and 30 unknown.
The sampled published parent's add-on collection and the account-wide add-on
collection both return HTTP 400, with `top=10` and without query parameters.
An unknown-publication parent returns HTTP 200 with zero children. The sanitized
error contains a type-related signal; its machine code is unclassified.

The subscription type is a plausible explanation for these failures, but it
does not establish the service's cause or justify an automatic unsupported-type
classification. Zero returned add-ons in this failed run is not zero products
in the publisher's account. Replacing working Entra credentials again is not
supported by the current evidence.

## Official contracts checked

| API or feature | Documented scope | Consequence for AnalyticsET |
|---|---|---|
| [Submission add-on resource](https://learn.microsoft.com/en-us/windows/uwp/monetize/manage-add-ons) | `productType` lists Durable and Consumable; provides parent and last-published-submission metadata. | The documentation does not describe a Subscription value or explain this publisher's HTTP 400. Do not infer a universal subscription exclusion solely from this enum. |
| [Per-app add-on collection](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-add-ons-for-an-app) and [account-wide collection](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-all-add-ons) | Publisher inventory collections; optional paging parameters. | Both documented routes were probed. Removing `top` did not resolve the failure. |
| [Submission prerequisites and limits](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services) | Manager association; documented 409 restrictions for mandatory updates and Store-managed consumables. | These restrictions do not establish that the observed subscription-related HTTP 400 is an expected empty response. |
| [Subscription Store SDK examples](https://learn.microsoft.com/en-us/windows/uwp/monetize/enable-subscription-add-ons-for-your-app) | `StoreContext.GetAssociatedStoreProductsAsync` finds add-ons for the current app; subscription SKUs appear under Durable products. | Durable can include subscriptions in this SDK. Its current-app/customer context is not a verified publisher-wide inventory replacement for this local console tool. |
| [Subscription acquisition analytics](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-subscription-acquisitions) | `GET /v1.0/my/analytics/subscriptions` requires an app Store ID; the subscription ID filter is optional. | Subscription reporting can be investigated separately. Date-bounded acquisition rows do not prove all products, including unsold subscriptions, their publication or parent completeness. No report request was made in this investigation. |
| [Get subscriptions for a user](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-subscriptions-for-a-user) | Customer entitlement query requiring a Store user ID key and a separate audience. | Customer-owned subscriptions are not the publisher's complete product catalog. |

No verified public publisher API alternative for complete subscription add-on
inventory was established by this review. That is an unresolved contract, not a
claim that Microsoft has no such API. Neither analytics nor consumer entitlement
data is installed as an inventory fallback.

## Development decision and acceptance impact

The existing requirement to discover Store apps **and all their add-ons** remains
in force. Failed add-on coverage remains failed, unknown publication remains
unknown, and incomplete refreshes cannot authorize report selections. Discovery
task 2.9, the v0.2 gates and release acceptance remain incomplete.

The concrete next external step is the
[prepared Microsoft support request](store-subscription-support-request.md).
It asks whether the documented submission routes support Subscription add-ons,
how to resolve the 400, and which official publisher route exposes unsold/hidden
products with parent and publication evidence if those routes cannot do so.
The draft has not been submitted.

Independent installs/ratings implementation and fixture verification can proceed
under the existing report requirements, with live execution and v0.2 acceptance
still gated on inventory/account verification. This does not reduce the accepted
product scope or make analytics a substitute inventory.

If the operator instead chooses an earlier app-report milestone, update both
OpenSpec changes before implementing that exception. The revised design must
explicitly separate app and add-on completeness, restrict scope to verified app
identities, reject add-on selection, report the subscription gap, and preserve
publisher binding and unpublished/unknown handling. Fixtures must show that an
app-only selection cannot claim complete account inventory or widen into children.
That is a proposed scope change, not approved behavior or an accepted v0.2.

The 30 unknown apps remain a separate issue: a private per-ID comparison and a
coherent publication-proof design are still needed. Subscription information
does not prove those app states, account attribution, report permissions or
later-publisher onboarding.

## Verification and resume point

The historical 105-test/61-file checkpoint is preserved. The separate
[Subscription diagnostic guide](store-subscription-diagnostic.md) now describes
the implemented six-probe comparison and protected helper. It captures finite
associated code/target/type evidence and validated per-attempt correlations,
checks direct known add-on detail before collections, and writes a unique private
evidence/support packet with a final receipt. Production type/publication policy
is unchanged. No report or inventory snapshot is produced by the comparison.

The [April 2022 Q&A precedent](https://learn.microsoft.com/en-us/answers/questions/806501/not-supported-managedconsumable-product-type-when)
reports NotAllowed/productType/ManagedConsumable collection failure and recovery
after removal. HTTP status is not stated and no authoritative service explanation
is recorded; this does not confirm Subscription exclusion or a fix. The
[support draft](store-subscription-support-request.md) records that distinction.

The operator ran the protected sample comparison on **2026-10-10**,
**17:19:35–17:19:48 UTC**. The receipt/hashes/private ACLs and matching executing
assembly were verified locally. Parent control was **200**, identity matched and
publication evidence was Published. Direct known Subscription detail and all four
per-parent/account collection variants returned **400**, each with associated
**NotAllowed / productType / Subscription**. The type comes from the recognized
message pattern, not readable detail metadata. Seven attempts included one token
request; no retries. No accepted Store correlation was captured. A token-only
Entra request identifier remains private.

This strengthens the sample/account product-type limitation hypothesis and closes
the exact-error/direct-detail diagnostic gap. It does not establish a
collection-only failure, universal exclusion, complete alternative route or fix.
Detail identity/parent/wire type/parser compatibility remain unavailable, rather
than mismatched. The private support attachment has been reviewed; the updated
shared draft contains only sanitized actual results. The support case is still
unsubmitted. The [diagnostic checkpoint](store-subscription-diagnostic-checkpoint.md)
separates 135 passing offline tests from reviewed live diagnostic DG7.

Discovery progress remains **17/31**, iterations **9/47**; v0.2 is unaccepted and
neither related change is synced or archived.

Next, review the updated support draft and submit it through the operator's
authenticated Microsoft support channel if desired. Resolve the complete official
Subscription inventory contract before changing completeness rules. The separate
30 unknown-publication apps and account comparison remain unresolved.
