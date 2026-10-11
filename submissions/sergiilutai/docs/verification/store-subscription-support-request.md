# Draft Microsoft support request: subscription add-on inventory

Prepared: **2026-10-06**; diagnostic procedure updated **2026-10-10**.
This is a reviewable draft; no request has been sent.
Use Partner Center's authenticated support channel for any account-specific
identifiers. Keep client secrets, access tokens, credential values and raw report
data out of the case. Replace placeholders locally only if Microsoft requests
the relevant identifier.

## Suggested subject

Store submission API returns HTTP 400 when enumerating Subscription add-ons

## Suggested case text

We are developing a local reporting tool that uses official Microsoft APIs to
discover the apps and add-ons owned by a Windows MSIX/UWP publisher account.
All add-ons in this publisher account are Subscription products.

The Entra application is associated with the correct Windows publisher in Partner
Center with Manager(Windows). Token acquisition and the applications collection
succeed. Local checks confirm that the token's client, tenant and audience match
the configured submission API authorization context.

Observed read-only results on 2026-10-06:

- `GET https://manage.devcenter.microsoft.com/v1.0/my/applications` returns HTTP
  200; all pages yield 85 app records.
- `GET https://manage.devcenter.microsoft.com/v1.0/my/applications/{applicationId}/listinappproducts`
  returns HTTP 400 for a sampled app with last-published-submission evidence.
- The same per-app request with `?top=10` returns HTTP 400.
- `GET https://manage.devcenter.microsoft.com/v1.0/my/inappproducts` returns HTTP
  400; adding `?top=10` also returns HTTP 400.
- The same per-app collection for a sampled app without publication evidence
  returns HTTP 200 with totalCount 0.
- Sanitized inspection of the error detects a type-related message. Its exact
  service code/cause has not been established; the raw body is not included here.

We have not changed submissions, published products or treated these errors as
empty inventories. The failed sampled parent is published, so omitting reserved
or not-yet-submitted apps would not resolve this failure.

Could you confirm:

1. Do the documented per-app and account-wide Store submission collections
   support existing Subscription add-ons? If yes, what request or account change
   is needed to resolve HTTP 400?
2. Is there a known service defect or limitation for mixed/current subscription
   product representations? Which sanitized diagnostic code or correlation
   information would help identify it?
3. If these collections do not support subscriptions, which official publisher
   API can enumerate **all** subscription add-ons, including unsold or hidden
   products, with their Store IDs, developer IDs, parent app IDs and publication
   evidence? We need catalog inventory rather than date-bounded acquisition rows
   or an individual customer's entitlements.
4. Which official field/response reliably distinguishes an app that was never
   submitted/published from unavailable or missing last-published metadata?

References checked: [Manage add-ons](https://learn.microsoft.com/en-us/windows/uwp/monetize/manage-add-ons),
[per-app collection](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-add-ons-for-an-app),
[account-wide collection](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-all-add-ons),
and [submission API prerequisites/support](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services).

The submission resource documents Durable/Consumable values, while the
[Store SDK subscription example](https://learn.microsoft.com/en-us/windows/uwp/monetize/enable-subscription-add-ons-for-your-app)
represents subscription products as Durable. We are asking for the publisher API
contract rather than assuming that the different terminology proves exclusion.

## Optional details to add privately

Add the selected publisher/account, tenant and application identifiers only in the
authenticated support case if needed. Add a representative parent app identifier
and request time/correlation ID if available through a private diagnostic that
does not expose authorization headers or secrets. The historical sanitized probe
did not retain correlation headers. The new bounded diagnostic supports validated
private capture, and its actual live packet has now been reviewed below. Store
correlation values were not captured; only an Entra token-response request ID is
available privately. Distinguish its provenance from the rejected Store calls.

## Reviewed live collection/detail comparison (2026-10-10)

The operator ran the [tested diagnostic procedure](store-subscription-diagnostic.md)
with one privately confirmed Subscription and parent. The six-probe packet and
generated support attachment were reviewed locally. Their receipt, hashes and
operator/SYSTEM permissions verified; the executing test assembly matches the
135-test checkpoint. The following are actual observations, separate from fixtures.

| Probe | Actual HTTP status | Metadata or exact associated rejection |
|---|---|---|
| Parent app control | 200 | Requested identity matches; Published evidence. |
| Known add-on detail | 400 | NotAllowed / productType / Subscription; successful metadata unavailable. |
| Per-parent collection, top=10 | 400 | NotAllowed / productType / Subscription. |
| Per-parent collection, default | 400 | NotAllowed / productType / Subscription. |
| Account collection, top=10 | 400 | NotAllowed / productType / Subscription. |
| Account collection, default | 400 | NotAllowed / productType / Subscription. |

Observation interval: **2026-10-10 17:19:35–17:19:48 UTC**. Diagnostic exit **0**;
all six HTTP outcomes recorded, seven transport attempts including one token
request, no retries. All five failures had the same associated allowlisted tuple,
without capture limitations. Subscription was extracted from the recognized
error-message pattern, **not** from successful add-on wire metadata.

No accepted Store response correlation value was captured. The allowlisted Store
headers were absent; the token response supplied a validated X-MS-Request-ID,
retained privately and attributable to Entra token acquisition only. Actual
correlation values, sample IDs, source/binding information and local run ID remain
in the protected packet/support-attachment.md. The local run ID is not a server
correlation. Detail identity, parent, publication, wire type and production-parser
compatibility are unavailable because detail was rejected; this is not an observed
identity or parent mismatch.

The matching explicit detail/collection rejection strengthens a product-type
limitation hypothesis for this account/sample/time. It does not establish a
collection-only defect, universal Subscription exclusion or a supported fix. No
local parser-gap observation is available; the service rejected detail first.
Diagnostic exit 0 means recorded comparison, not complete inventory or v0.2
acceptance. Please investigate this tuple on both direct and collection routes
and confirm a supported complete publisher Subscription inventory route.

## Dated community precedent

The [April 2022 Microsoft Q&A report](https://learn.microsoft.com/en-us/answers/questions/806501/not-supported-managedconsumable-product-type-when)
describes account collection failure with code NotAllowed, target productType and
the type ManagedConsumable. The developers reported recovery after removing a
Store-managed consumable. The thread does **not** state an HTTP status or provide
an authoritative service explanation. This is an analogy for type-dependent
collection failure, not a confirmed Subscription fix or advice to delete products.
Rechecked 2026-10-10.

The complete official publisher inventory route for Subscription products remains
an open question for Microsoft. Complete apps/add-ons coverage, account evidence,
publication proof, incomplete-refresh refusal and v0.2 acceptance still apply.
DG1..DG7 and shared/private evidence traceability are documented in the diagnostic
guide. No script, API or connector submits this request.

Microsoft's [submission API help](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services#additional-help)
directs users to assisted Partner Center support and, when asked, the problem type
**App submission and certification** and category **Submitting an app**. Follow
the categories actually available in the portal; this draft does not submit a case.
