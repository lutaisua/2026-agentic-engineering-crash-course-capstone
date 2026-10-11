# Proposal

## Why

AnalyticsET v0.1 requires product IDs before a publisher profile can be added, so
operators must copy and maintain the Store/Marketplace product list themselves.
Discovering published products through official Microsoft APIs will support both
the publishers configured initially and publishers added later without that manual
inventory prerequisite.

## What Changes

- Introduce discovery-ready profiles that need account and authorization settings,
  but no pre-entered product IDs; preserve offline profile management.
- Discover Microsoft Store MSIX/UWP apps and add-ons, and Azure Marketplace offers
  and their plans, following all pages and retaining parent-child relationships.
- Run discovery on explicit online onboarding, and automatically before an online
  product/report operation for an existing or newly added publisher. Provide
  explicit refresh and an offline view of the last successful inventory.
- Identify previously published/live products from source evidence; distinguish
  drafts, preview-only products, retired products, and unknown publication state.
- Keep inventory private and isolated by publisher, program, account, and
  authorization context. Keep explicit export selections separate from discovered
  inventory; discovery must not silently export all products.
- Report coverage by product type and source. Failed pagination, unsupported API
  features, unknown publication state, and insufficient permissions cannot produce
  a claim that all published products were discovered.
- **BREAKING:** Add configuration schema version 2 for discovery settings and new
  inventory authorization resources. Version 1 remains readable; explicit migration
  preserves existing IDs as export selections and never overwrites the original.
- Add cumulative Store and Marketplace discovery gates to the unaccepted v0.2 and
  v0.3 iterations. This proposal does not accept either version or implement exports.

## Capabilities

### New Capabilities

- `publisher-product-discovery`: Discovery-ready onboarding, refresh, private
  inventory persistence, publisher isolation, and explicit report scope resolution.
- `store-product-inventory`: Read-only Store app/add-on enumeration, publication
  evidence, pagination, and parent/child identity mapping.
- `marketplace-product-inventory`: Read-only offer/plan enumeration across supported
  official publisher APIs, account binding, live publication evidence, and coverage.

### Modified Capabilities

None: `openspec list --specs` currently reports no main specs. The sibling change
`develop-analytics-et-in-iterations` contains draft publisher/report requirements.
This change adds discovery behavior and explicitly supersedes its manual-product-ID
onboarding assumption; implementation must reconcile those drafts before syncing
either change. Existing publisher CRUD and report correctness requirements remain.

## Impact

- Application: `2026-agentic-engineering-crash-course-capstone/submissions/sergiilutai`.
  Affected areas are `Core/Publishers`, `Core/Authentication`, `Core/Storage`, CLI
  routing, configuration/isolation tests, and new inventory connectors.
- Store inventory uses the submission API's read operations, separate from report
  endpoints. Marketplace inventory needs product-management authorization, separate
  from Marketplace analytics and financial access; API coverage varies by offer type.
- PRD FR-01/04/05, setup examples, version gates, and onboarding evidence need updates
  during implementation. The accepted v0.1 evidence remains a historical baseline.
- No cloud backend, scraping, publishing/submission mutations, credential retrieval,
  or live Microsoft API calls are part of this planning change. All actual account,
  product, application, and credential identifiers stay in private local storage.

Official contract research and the staged verification plan are recorded in
[design.md](design.md). Complete discovery is a verified outcome for an authorized
publisher scope; it is not a universal promise for offer types Microsoft does not
expose through a supported publisher API.
