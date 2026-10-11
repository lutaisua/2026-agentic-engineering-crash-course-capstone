# Marketplace inventory contract research

Checked: **2026-10-06**. Task 3.1 is **in progress**, not accepted. No Marketplace
adapter or account coverage is advertised and no live Marketplace request was made.

## Offer types and provider boundaries

Microsoft's [submission API matrix](https://learn.microsoft.com/en-us/partner-center/marketplace-offers/submission-api-overview)
defines the following baseline. Schema enum membership alone does not establish
that an API supports an offer type for the operator's account.

| Offer type | Documented provider | Current implementation |
|---|---|---|
| SaaS, Azure VM, Azure Container | Product Ingestion; SaaS also appears in the submission matrix | Pending |
| Azure Application, Dynamics 365 | Partner Center submission | Pending |
| Consulting Service, Managed Service, Power BI App | Legacy Cloud Partner Portal | Unsupported until a separately verified eligible integration exists |
| Unknown/new type | Requires its own contract and account proof | Unverified; never an empty-account result |

Microsoft describes **AI Apps and Agents as a listing category**, including SaaS
and container delivery models. Route by the actual offer type rather than
inventing an `aiAppsAndAgents` product enum. A linked SaaS offer remains a SaaS
offer; the Microsoft 365 agent itself is outside this Azure Marketplace program
scope. This interpretation follows [Microsoft's AI publishing guidance](https://learn.microsoft.com/en-us/partner-center/marketplace-offers/artificial-intelligence-app-agent-publish-release).
It does not establish live AI-offer coverage.

## Product Ingestion versions and reads

The [API guide](https://learn.microsoft.com/en-us/partner-center/marketplace-offers/product-ingestion-api)
and its schemas supply these readable baseline contracts:

| Resource | Verified baseline schema |
|---|---|
| Product | [2022-03-01-preview3](https://schema.mp.microsoft.com/schema/product/2022-03-01-preview3) |
| Plan | [2022-03-01-preview2](https://schema.mp.microsoft.com/schema/plan/2022-03-01-preview2) |
| Live resource tree | [2022-03-01-preview2](https://schema.mp.microsoft.com/schema/resource-tree/2022-03-01-preview2) |
| Submission | [2022-03-01-preview2](https://schema.mp.microsoft.com/schema/submission/2022-03-01-preview2) |

Candidate read routes are account-wide `GET product`, per-product `GET plan` with
the `product` durable-ID parameter, and `GET resource-tree/<product-durable-ID>`
with `targetType=live`. Every request needs its documented `$version`; collection
continuation tokens must all be followed. Product/plan collection data is draft
configuration. Published plans require their own live representation, not merely
a published parent. Resource-tree request versions act as an upper bound on each
returned resource's schema version; the envelope and contained resources can differ.

Keep durable IDs and external IDs separate. The baseline product/plan schemas
spell identity `externalId`, while the guide's examples spell `externalID`.
This discrepancy must be handled explicitly and conflicts rejected; a parser
must not silently invent or overwrite an identity. A default `generallyAvailable`
lifecycle value does not prove publication.

The linked [resource index](https://schema.mp.microsoft.com/schema/resources-index/2022-07-01)
now lists newer product/plan versions, including `2026-10-01`. The newer
[product schema](https://schema.mp.microsoft.com/schema/product/2026-10-01) was
downloaded successfully (HTTP 200) into the workspace's public contract cache.
SHA-256: `e88b146ca4da7d3c28b1ed2cfc585d5d745f43a19cc272cc70bc64c1cc174242`.
It adds product types for other Microsoft programs and changes lifecycle options.
It still has no standalone AI-category product enum. Availability of that schema
does not prove Product Ingestion wire compatibility or account entitlement.
Final request-version pins and newer schema compatibility remain to be verified.

## Partner Center submission Swagger

Microsoft's [onboarding guide](https://learn.microsoft.com/en-us/partner-center/marketplace-offers/submission-api-onboard)
links the [Ingestion Swagger](https://ingestionapi-swagger.dps.mp.microsoft.com/).
The cached public `swagger.json` is OpenAPI **3.0.1**, contract version **1.0**,
with base `https://api.partner.microsoft.com/v1.0/ingestion`.
SHA-256: `26203d333a6b5cd390d1a5260989f6df944e8dc84454e09c8579d07f4643d946`.

| GET route | Identity and paging contract |
|---|---|
| `/products` | BaseProduct collection; `value` and `@odata.nextLink`; optional `$filter` and `$skipToken` |
| `/products/{productID}/variants` | BaseVariant collection; `AzureSkuVariant` carries `ID`, `externalID`, and `friendlyName`; `$skipToken` and `@odata.nextLink` |
| `/products/{productID}/submissions` | Submission collection; `state`, `substate`, `targets`, `variantResources`, and `publishedTimeInUtc`; paged envelope |

Product and variant `ID` are distinct from external identifiers. AzureSkuVariant
`Active` alone does not prove a published plan. Submission states include
`Inprogress` and `Published`; the default collection is described as Retail.
Live child attribution still needs verified target and variant-resource mapping.

The submission description shows `state`, `sandboxID`, and `flightID` examples,
but its declared parameters list `$filter` and `$expand`; no `$skipToken` parameter
is declared there. Variant examples misspell `friendlyName` as `friedlyName`.
These discrepancies remain explicit contract checks for task 3.5. Do not invent
query parameters, targets, child publication rules, or broad type support.

## Remaining gate

Finish version/identity compatibility decisions and submission target/child
publication contracts before accepting task 3.1 or writing the affected adapter.
Authenticate inventory separately from analytics and delegated financial access.
The authentication boundary now supports the independently documented bindings:
Product Ingestion uses `/oauth2/v2.0/token` with Graph `.default` scope;
submission uses `/oauth2/token` with resource `https://api.partner.microsoft.com`.
Token-cache keys retain publisher/account/application/resource/flow/generation
isolation. Analytics and delegated financial contexts cannot substitute for these
inventory requests, even when an audience matches. The focused tests use fictional
credentials; live Marketplace authentication and inventory remain unverified.
Marketplace account association, expected offer types, populated plans, and all
live comparison gates remain unverified. The Store association issue is a separate
blocker; this public research does not establish any publisher's inventory.
