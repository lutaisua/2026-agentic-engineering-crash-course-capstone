# Store inventory contract checkpoint

Checked: 2026-10-05. API version: `v1.0`. This checkpoint specifies read-only
Store submission metadata for MSIX/UWP inventory. It does not grant report access
or implement publishing operations.

| Operation | Official contract | Handling |
|---|---|---|
| Account apps | [Get all apps](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-all-apps): `GET /v1.0/my/applications` | Default first ten; traverse all `@nextLink` values using `/v1.0/my/` as the base; validate `value`/`totalCount` |
| App detail | [Get an app](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-an-app) and [app resource](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-app-data) | Store `id` is distinct from package names and certificate publisher; publication comes from the last-published-submission object |
| App children | [Get add-ons for an app](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-add-ons-for-an-app): `GET /v1.0/my/applications/{id}/listinappproducts` | Traverse every child page; its `inAppProductId` is the Store ID |
| Child detail | [Get an add-on](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-an-add-on): `GET /v1.0/my/inappproducts/{id}` | Resolve Store `id`, developer `productId`, and exactly one matching parent in `applications.value` |

Collection 404 is documented as zero apps/add-ons; use it as empty only on the
initial collection request. A continuation-page 404 is incomplete traversal. A
detail 404 means a missing resource and fails the refresh. The collection/detail
articles document 409 for unsupported features. The
[submission prerequisites](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services)
identify mandatory updates and Store-managed consumables as unsupported; retain
that coverage gap instead of claiming an empty account. 401/403 never mean empty.

## Publication interpretation

The app resource defines `lastPublishedApplicationSubmission`; the
[add-on resource](https://learn.microsoft.com/en-us/windows/uwp/monetize/manage-add-ons)
defines `lastPublishedInAppProductSubmission`. A valid corresponding submission
ID and resource location identify the last published submission. An additional
pending update does not remove that evidence. Child publication must use the
child's own field, regardless of the parent's published state.

The documentation does **not** explicitly guarantee that an absent/null object
proves draft-only or never-published status. AnalyticsET therefore classifies
absent/null/malformed or mismatched publication evidence as **unknown**, preventing
complete snapshots until the gap is resolved. This is a conservative application
policy, not a claim that Microsoft defines null as unknown. A pending submission
alone does not prove no previous publication. The app resource example includes
`1601-01-01` alongside a published submission; neither that placeholder nor a
real `firstPublishedDate` alone is used as publication proof. No Store retirement
or preview-only field is advertised without a verified contract.

Fictional `StorePublicationTests` verify pending updates, placeholder dates,
null/missing/malformed metadata, mismatched locations, and independent child proof.
Raw submission payloads and unrelated product settings are excluded from inventory.

## Authorization and account attribution

The prerequisite guide requires the Entra application to be added to the intended
Partner Center account with Manager role. Token acquisition uses the tenant's
`/oauth2/token` endpoint, `grant_type=client_credentials`, and resource
`https://manage.devcenter.microsoft.com`. Only the protected key **value** is sent
as `client_secret`. The Reply URL and secret identifier are not used.

The collection response does not provide an account filter or verified Partner
Center account ID. Local account declarations and token issuance cannot alone
establish attribution. Keep private dated association evidence and compare the
returned inventory with that account in Partner Center. Until such binding/scope
evidence is recorded, results are explicitly partial/unverified. A Windows role
does not prove Marketplace or delegated financial access.

S1 contract/publication fixtures are separate from full provider traversal tests.
The actual runtime profile is now created and offline-valid. S2 and live L1 remain
unverified until authorized discovery and account comparison are recorded privately.
