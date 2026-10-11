# Design

## Context

See [proposal.md](proposal.md) for motivation and the [publisher discovery](specs/publisher-product-discovery/spec.md), [Store inventory](specs/store-product-inventory/spec.md), and [Marketplace inventory](specs/marketplace-product-inventory/spec.md) deltas for behavior. The target is `2026-agentic-engineering-crash-course-capstone/submissions/sergiilutai`; the planning home is the outer AnalyticsET workspace.

Observed baseline on 2026-10-05:

- v0.1 is an accepted offline foundation: 9 of the sibling iteration change's 47 tasks are complete. Its historical verification reports 42 passing tests. Authentication and online export connectors are not implemented.
- `PublisherConfiguration`, `ConfigurationValidator`, and `PublisherStore` support schema 1. Enabled programs require nonempty `ProductIds` and analytics authorization settings. `PublisherCommands` performs offline CRUD only; configuration writes already have locking and atomic commit support.
- Existing resource keys are `store-analytics`, `marketplace-analytics`, and `financial`. Product inventory is absent. Marketplace analytics dataset discovery is report-schema discovery, not offer discovery.
- The PRD requires local execution, multiple publishers, program separation, official APIs, analytics and financial reports, private identifiers, and explicit report selection. The operator has confirmed MSIX/UWP and has authorized accounts for future live verification.
- No main OpenSpec specs exist. The sibling change `develop-analytics-et-in-iterations` contains draft profile and report requirements. Its manual-product-ID prerequisite needs reconciliation during apply; this proposal leaves that change and the accepted v0.1 evidence unchanged.

## Goals / Non-Goals

**Goals:** Provide a reusable inventory boundary, migrate configuration safely, preserve offline administration, and make completeness an auditable outcome for an account and supported product scope.

**Non-Goals:** Implement the sibling change's entire export pipeline, modify publishing state, scrape dashboard pages, discover reseller catalog products, export private commercial deal resources, or guarantee recovery of products no longer exposed by Microsoft. Add-on inventory does not add app-only report support for add-ons. Financial authentication remains the separate v0.4 workstream.

## Decisions

### 1. Introduce schema 2 without rewriting schema 1

Keep schema 1 readable and its offline CRUD valid. Introduce schema 2 with per-program `Discovery` settings and optional typed `ExportSelection` entries. Discovery defaults to enabled for newly created schema 2 profiles. Account and authorization settings remain required; product IDs are optional until a report selection is resolved. Missing inventory credentials can be reported as unconfigured during offline administration, but online onboarding cannot succeed without them.

Migration is explicit: `config migrate --to-version 2 --output <different-private-path>`. Preserve profile IDs, accounts, authorization references, extensions, and old `ProductIds` as export selections; retain their legacy identifier meaning until discovery resolves them. Do not infer app versus add-on, or offer versus plan, from string shape. An ambiguous selection requires operator resolution. Preserve the original file even after successful migration, and reject a destination equal to it. Do not migrate on load or on discovery.

Schema 1 profiles remain usable offline. Online inventory commands report migration-required before network access; after explicit migration existing publishers participate in the same discovery flow as later additions.

Schema 2 selection uses product kind plus source ID, with a parent ID for child products. A private coverage setting records expected Marketplace offer types and the origin/date of account-scope verification; it never asks for a hand-entered product inventory. If the API cannot enumerate every type, account-wide completeness remains unverified until the operator confirms the account's offer-type scope against Partner Center.

**Alternative:** Make `ProductIds` optional in schema 1. Rejected because it silently changes v0.1 validation semantics and confuses inventory with export scope. A versioned, explicit migration gives old binaries a clear rejection and preserves rollback.

### 2. Preserve offline add; add explicit online onboarding

Proposed commands use the existing one/many/all publisher selector conventions:

| Command | Result |
| --- | --- |
| `publisher add --file <private-profile>` | Offline validation and save; authorization and discovery remain unverified. |
| `publisher onboard --publisher <id> [--program <program>]` | Validate account binding, authenticate, then discover enabled programs. |
| `products list --publisher <id> [--program <program>]` | Refresh selected scope, then show published inventory and coverage. |
| `products list --publisher <id> --offline` | Show the last matching successful snapshot, its age, and any later failed attempt; no HTTP. |
| `products refresh --publisher <id> [--program <program>]` | Refresh explicitly; `--all` follows existing publisher selection rules. |

Every online operation that resolves report product scope refreshes first. A profile added later follows the identical path. Start with sequential per-publisher operations and bounded provider requests; support independent outcomes without introducing the sibling change's full batch-export engine. A program omitted from a profile is not contacted.

**Alternative:** Make `publisher add` always online, or refresh only at initial setup. Rejected because the first breaks the accepted offline contract and the second misses later publications. A cache TTL can be considered after measuring API cost; it is not a completeness shortcut in this version.

### 3. Keep inventory providers separate from reporting connectors

Add a core inventory model and an `IProductInventoryProvider` boundary, using the existing dependency injection and HTTP conventions. Store and Marketplace providers return normalized candidate inventory, publication evidence, coverage, and categorized failures. The coordinator owns publisher isolation, lifecycle comparison, persistence, and selection resolution. Providers perform GET operations only; token acquisition is the necessary authentication POST.

Each item contains publisher/profile and program binding, product kind, source ID, external ID where different, parent identity, safe display name, source offer type, publication evidence, observed availability, source API/schema version, and observation timestamps. Keep `published`, `draft-only`, `preview-only`, and `unknown` publication states separate from `available`, `retired`, `not-returned`, and `unknown` availability observations. A published item with a pending draft remains published; retirement is set only from explicit evidence. Retain previous observations for historical report identity.

**Alternative:** Derive inventory from analytics rows or merge it into the report catalog. Rejected because products without sales or usage disappear and report applicability becomes confused with product ownership.

### 4. Use official Store read endpoints for apps and add-ons

The applications collection is `GET https://manage.devcenter.microsoft.com/v1.0/my/applications`. Follow its relative `@nextLink` against the documented `/v1.0/my/` base; the default response is limited to ten apps. Preserve Store identity rather than package-family or package-publisher names. See [Get all apps](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-all-apps) and [Get app data](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-app-data).

For every app, page through `GET /v1.0/my/applications/{applicationId}/listinappproducts`. Resolve each child with `GET /v1.0/my/inappproducts/{inAppProductId}`; retain Store `id`, developer-assigned `productId`, and the verified parent. Classify app and child publication from their respective last-published-submission metadata, not the parent's status or a placeholder date. See [Get add-ons for an app](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-add-ons-for-an-app) and [Manage add-ons](https://learn.microsoft.com/en-us/windows/uwp/monetize/manage-add-ons).

Only documented collection-empty responses mean zero apps or children. A detail 404, 401/403, or unsupported-feature 409 is not an empty account. Null publication fields are interpreted only according to the verified response contract; otherwise classify unknown. The [submission API prerequisites](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services) describe limitations, including unsupported features; expose those as coverage gaps.

**Alternative:** Discover apps only or use just the first global add-on list. Rejected because the confirmed scope includes children, their parent identity, and their own publication evidence.

### 5. Select Marketplace providers by documented offer coverage

Use a provider registry with explicit type coverage and a pinned schema version. The [submission API matrix](https://learn.microsoft.com/en-us/partner-center/marketplace-offers/submission-api-overview) assigns SaaS, VM, and containers to Product Ingestion, and Azure Application/Dynamics 365 to Partner Center submission. Its legacy-only types require an eligible existing integration; do not introduce a Cloud Partner Portal fallback for new publishers. The [Product Ingestion guidance](https://learn.microsoft.com/en-us/partner-center/marketplace-offers/product-ingestion-api) also mentions the newer AI Apps and Agents category; verify that category's concrete type/schema before advertising it. Report documentary differences as a contract-verification item, not as inferred support.

Product Ingestion currently uses `https://graph.microsoft.com/rp/product-ingestion` and `https://graph.microsoft.com/.default`. Enumerate account products and plan collections with continuation tokens and required `$version`. Preserve durable and external offer/plan IDs separately. Default queries expose draft state; obtain `resource-tree/<product-durableID>` with `targetType=live`, or documented successful live-submission evidence, to establish publication. Resolve published plans from the live representation rather than labeling all draft children as live. See [Product Ingestion query and authentication contracts](https://learn.microsoft.com/en-us/partner-center/marketplace-offers/product-ingestion-api).

For other supported types, Microsoft documents the separate audience `https://api.partner.microsoft.com` and links the [Ingestion Swagger](https://ingestionapi-swagger.dps.mp.microsoft.com/) from its [submission onboarding guide](https://learn.microsoft.com/en-us/partner-center/marketplace-offers/submission-api-onboard). The Swagger page was reachable during planning but its route/schema content was not extractable. The implementation contract gate must obtain that linked read-only product/plan pagination and published-state contract before writing the adapter; this design deliberately does not invent its routes. If inaccessible, mark that adapter's task and coverage blocked, while continuing independently verifiable providers.

Merge overlapping API results only through a verified identity mapping; conflicting or ambiguous mappings fail completeness. Standalone private-deal resources and Microsoft 365 product programs are outside this Azure Marketplace inventory. A Marketplace offer with a private audience remains an offer if the supported product API exposes it.

**Alternative:** Assume one generic Partner Center endpoint lists every offer, or use the CSP catalog. Rejected because the official publisher API coverage differs by type and the reseller catalog is a different ownership scope.

### 6. Bind authorization to both resource and account

Add explicit schema 2 inventory keys:

| Key | Token audience/scope | Binding |
| --- | --- | --- |
| `store-inventory` | `https://manage.devcenter.microsoft.com` | Store submission client credentials. |
| `marketplace-product-ingestion` | `https://graph.microsoft.com/.default` | Product Ingestion client credentials. |
| `marketplace-submission` | `https://api.partner.microsoft.com` | Partner Center submission client credentials. |

Do not rename the existing reporting keys. Store inventory can reference the same protected application/credential mapping as Store analytics because its audience matches; this does not grant report access automatically. Marketplace inventory bindings are distinct from analytics and from delegated financial authentication, even when audiences or client IDs coincide. Cache keys include profile, program, account, tenant, client, resource, authentication flow, and credential-reference generation. Changing those settings invalidates inventory and authorization state. No token reuse is justified solely by tenant equality.

Manager and saved application association are documented prerequisites in the API guides above. A Windows role confirmation does not verify Marketplace access. Where responses omit account identity, use private association evidence and a controlled Partner Center comparison to verify binding. If the same principal's response cannot be attributed to the intended account, report unverified binding and require a distinct account-specific association/application; do not invent an account-filter parameter.

**Alternative:** Use the existing analytics token for all management calls. Rejected because audience, flow, permissions, and publisher association differ.

### 7. Persist complete snapshots privately and commit atomically

Default state belongs under the operator's protected `%USERPROFILE%\.analyticset\inventory`; allow an explicit private state directory. Reject repository destinations. On Windows, verify/provision operator-and-SYSTEM access on the state directory and files; Git ignore rules do not grant privacy. Follow the existing onboarding guide's account/path checks and avoid dependence on a virtualized AppData directory. Credential references point to private mappings; inventory never reads or copies secret IDs/values into snapshots.

Persist normalized, allowlisted metadata only. Product management responses can contain unrelated settings, so raw resource trees must not be stored or logged. Interactive inventory output can show product identity to the local operator; shared diagnostics contain safe profile aliases, statuses, counts, and sanitized error categories.

Build each refresh as a separate candidate, validate all pages, children, publication classifications, and account scope, then atomically commit a successful snapshot using the existing storage approach. Keep incomplete-attempt metadata separately without replacing the last complete snapshot. Candidate products can be displayed as explicitly partial for diagnosis, but cannot satisfy an online report's scope. First discovery with coverage gaps has no complete snapshot. Handle interrupted writes, corrupt state, conflicting duplicates, and concurrent refreshes without accepting partial data. Compare stable composite identities on successful refresh; preserve absent products as history, not current returned items.

**Alternative:** Write each page directly into configuration. Rejected because failure leaves a deceptively complete product list and mixes discovery state with user choices.

### 8. Resolve exports explicitly and preserve release dependencies

Discovery does not start exports. For the sibling report connectors, resolve configured or command-line selections against refreshed inventory. Propose `--all-products` as explicit opt-in; report-specific identifier mapping and product-kind applicability remain mandatory. With incomplete coverage, refuse the full-scope selection and report the gap. Explicit items also require a verified fresh snapshot in this first version; cached output remains available for diagnosis. Do not silently drop unavailable explicit selections.

Implement the shared boundary and Store discovery in v0.2, then Marketplace discovery in v0.3. Reorder the sibling v0.2 dependency from account-plus-manual-product-IDs to account/application setup, authentication, inventory, then report scope/export. Extend v0.3 with inventory authorization and offer/plan verification before report filters. Keep its report-dataset proof and financial/batch/distribution requirements intact. The existing v0.1 verification is historical; new changes require new verification evidence. Completing these discovery tasks alone cannot accept v0.2 or v0.3 without their original export gates.

**Alternative:** Add an independent discovery-only release and defer integration. Rejected because the user requested changed project logic across the already proposed application iterations.

## Acceptance Criteria and Verification

All implementation tasks map to the following discovery gates. Offline checks use fictional IDs and deterministic HTTP fixtures; live evidence stays private, with only sanitized counts/statuses and contract provenance in shared documents.

| Gate | Version | Acceptance criterion | Verification |
| --- | --- | --- | --- |
| D1 | v0.2 foundation | Schema 2 profiles need no product IDs; schema 1 stays usable; explicit migration preserves selections and unrelated data. | Existing CRUD regression checks plus migration round-trip, ambiguous selection, destination failure, and no-network onboarding fixtures. |
| D2 | v0.2 foundation | Inventory is isolated by profile/program/account/auth context and protected locally; partial/cancelled refresh cannot overwrite success. | Same-tenant two-profile and cross-program fixtures; binding-change, corruption, concurrent refresh, cancellation, failed commit, redaction, and Windows ACL checks. |
| S1 | v0.2 | All Store app/add-on pages and detail relationships are processed; publication and empty/unsupported outcomes are accurate. | More than ten apps, multiple add-on pages, relative links, duplicate/conflicting IDs, null metadata, draft child, pending update, empty collections, 401/403/409, and later-page failure fixtures. |
| S2 | v0.2 live | Published app and add-on identity/count/parent results match the authorized publisher's Partner Center inventory. | Controlled read-only API run and private dashboard comparison; record each kind's live coverage separately. A publisher with no add-ons verifies empty children, not a live populated add-on sample. |
| M1 | v0.3 | Each advertised offer type has a verified official adapter; all pages and live plans are resolved with correct identity mappings. | Official contract record plus multi-page offer/plan, live-versus-draft, preview-only, unsold offer, planless type, retirement, overlap, and unknown-type fixtures. |
| M2 | v0.3 live | Published offers/plans match Partner Center for each advertised and authorized type; gaps are explicit. | Read-only live comparison per type and source; an unavailable account, sample, or contract keeps that coverage gate blocked. |
| L1 | v0.2 Store / v0.3 Marketplace | Initial publisher and a later-added publisher need no manual IDs; subsequent products appear on refresh; names/history/selections remain correct. | Two-account lifecycle fixtures; live later-profile onboarding for available accounts. Use an existing real newly published item when available; do not create or publish test products. Mark missing live lifecycle evidence separately. |
| R1 | v0.2 / v0.3 integration | Full inventory does not widen exports; all-products is opt-in and report applicability is verified. | Selection resolution fixtures and the sibling version's export acceptance checks; verify no report/job requests during discovery. |

Run the application's Release build, relevant regression tests, and formatting verification using its documented verification script after implementation changes. Meaningful connector tests must exercise failure and contract boundaries, rather than mirror the implementation. Refresh source-hash evidence for each tested version without rewriting the v0.1 baseline. A gate has an explicit passed, failed, blocked, or unverified state; blocked live checks do not count as version acceptance.

## Risks / Trade-offs

- API coverage and preview contracts change -> pin verified schema versions, retain dated official references, and expose unsupported/unverified types rather than claiming universal completeness.
- Accounts may contain offer types invisible to a selected API -> establish account-type scope with private Partner Center evidence; complete means complete for that verified scope and time, not every Microsoft program or deleted historical product.
- Full refresh increases latency and API consumption -> paginate sequentially, deduplicate consistent identities, honor `Retry-After`, bound retries, and support cancellation. Do not introduce an unverified cache shortcut.
- Continuations or redirects can redirect authorization -> validate provider origin/path before requests, reject off-origin redirects and repeated continuations, and never log bearer headers.
- Windows privacy can be lost through directory inheritance or a different executing account -> verify ACLs and identity before private writes; keep configuration/state outside Git.
- Store or Marketplace association may not uniquely select one publisher -> leave binding unverified until account-specific association and live evidence establish the scope.
- Product APIs may not expose unavailable historical products -> retain observed history and report missing identities; do not infer retirement or promise historical reconstruction.

## Migration Plan

1. During apply, reconcile PRD FR-01/04/05 and add automatic-inventory acceptance criteria; update the sibling profile/Store/Marketplace drafts and v0.2/v0.3 tasks together. Preserve completed checkbox history and historical v0.1 evidence.
2. Implement schema 2, explicit migration, protected inventory state, and offline/online CLI boundaries. Verify D1/D2 before connecting inventory providers.
3. Deliver Store discovery and integration gates with v0.2. Gather S2/L1 live evidence before accepting advertised live coverage; unresolved checks remain recorded.
4. Deliver verified Marketplace providers and integration gates with v0.3. Publish an honest offer-type coverage table; legacy-only or unverifiable contracts remain gaps.
5. Rollback uses the unchanged schema 1 file and the older binary. Schema 2 state remains in private storage for later recovery; no automated downgrade or deletion is required. Discovery changes no remote product state.

## Open Questions

- Which Marketplace offer types exist in each authorized account? The provider registry and coverage states handle the answer without changing the specs; populate private scope evidence before that account's completeness gate.
- Which authorized publishers have populated add-on/plan samples and later-publication evidence for live verification? Missing samples block those live coverage claims without changing implementation behavior.
