# Spec Delta

## Purpose

Let local operators discover published products for every configured publisher without copying product IDs, while preserving account isolation, explicit export choices, and honest coverage results.

## ADDED Requirements

### Requirement: Discovery-ready publisher onboarding
The tool SHALL accept a discovery-enabled publisher profile without pre-entered product IDs. Offline profile management SHALL remain offline. Online onboarding SHALL validate the configured account and authorization context before discovering products for each enabled program.

#### Scenario: Initial publisher has no product list
- **WHEN** an operator adds a valid discovery-enabled profile without product IDs
- **THEN** offline addition succeeds without an HTTP request and reports authorization and inventory as unverified
- **AND** explicit online onboarding authenticates and discovers inventory for its enabled programs

#### Scenario: Publisher is added after initial setup
- **WHEN** another discovery-enabled publisher is added later
- **THEN** it follows the same onboarding behavior without inheriting the first publisher's inventory or authorization result

### Requirement: Compatible configuration migration
The tool SHALL read valid version 1 profiles and support version 2 discovery settings. Migration SHALL require explicit invocation, write to a different destination, and preserve existing product IDs as export selections. Migration SHALL preserve unrelated configuration and leave the original unchanged on success or failure.

#### Scenario: Existing profile migrates without losing selections
- **WHEN** a version 1 configuration is explicitly migrated to version 2
- **THEN** its publisher identities, program accounts, credential references, extension data, and product selections are preserved
- **AND** those selections do not limit automatic inventory discovery

#### Scenario: Migration destination is unavailable
- **WHEN** migration cannot safely commit the destination
- **THEN** the original bytes remain unchanged and no partially written configuration is accepted

#### Scenario: Existing schema 1 publisher requests discovery
- **WHEN** an operator requests automatic discovery using a schema 1 profile
- **THEN** the tool reports that explicit migration is required before discovery and leaves the original configuration unchanged
- **AND** after migration its existing product selections do not limit discovered inventory

### Requirement: Refresh for online product operations
The tool SHALL refresh inventory before an online product listing, online onboarding, or report operation that resolves product scope. It SHALL support explicit refresh and an offline inventory view. Refresh SHALL include only selected publishers and enabled programs; an offline view SHALL show its timestamp, authorization context match, and coverage status.

#### Scenario: A product is published after onboarding
- **WHEN** the next online product operation runs after a new product is published
- **THEN** refreshed inventory includes it when the official API exposes it, without editing configured product IDs

#### Scenario: Offline inventory view
- **WHEN** the operator requests an offline view
- **THEN** the tool makes no network request and labels the persisted result as cached rather than freshly verified
- **AND** absence of a matching snapshot is reported explicitly

### Requirement: Publisher and program isolation
The tool SHALL bind each inventory and authorization outcome to the selected publisher, program, Partner Center account, tenant, application, and authorization resource. A changed binding SHALL invalidate the prior inventory for current scope resolution. A shared tenant or application SHALL NOT establish shared account ownership.

#### Scenario: Same tenant contains separate publishers
- **WHEN** two profiles share a tenant but represent different publisher accounts
- **THEN** their inventory, refresh outcomes, and authorization state remain separate
- **AND** an account binding that cannot be established is reported as unverified, rather than attributed to both profiles

#### Scenario: Authorization or account settings change
- **WHEN** the operator changes a profile's account or authorization binding
- **THEN** an older snapshot cannot satisfy its current online or offline product scope

### Requirement: Scoped completeness and independent outcomes
The tool SHALL report discovery outcomes and coverage separately for each publisher, program, product type, and API source. Complete discovery SHALL require verified account binding, all pages and child queries, known publication classification, and supported coverage of the declared account scope. Unknown scope, unsupported types, and failed requests SHALL NOT be reported as complete or as an empty inventory.

#### Scenario: One publisher fails during a multi-publisher refresh
- **WHEN** one selected publisher receives an authorization failure and another succeeds
- **THEN** the successful publisher receives its own result and the failing publisher retains a failed outcome
- **AND** the overall operation reports that not all selected scopes completed

#### Scenario: A product type is unsupported
- **WHEN** an account contains a type without a verified official discovery path
- **THEN** coverage identifies that gap and the tool does not claim to have discovered all published products for the account

### Requirement: Atomic inventory refresh and history
The tool SHALL replace a successful inventory snapshot only after a complete validated refresh for that publisher and program. Failure or cancellation SHALL preserve the prior successful snapshot. Successful refreshes SHALL retain stable product identity across name changes and record previously observed products that are no longer returned without inferring deletion or retirement.

#### Scenario: Later page fails
- **WHEN** a refresh discovers products but fails on a later page or child detail
- **THEN** the previous successful snapshot remains available, with its original timestamp
- **AND** the failed attempt is reported separately and cannot satisfy an online report operation

#### Scenario: Product disappears from a complete response
- **WHEN** a previously observed product is absent from a later complete refresh
- **THEN** its historical identity is retained with a not-returned observation
- **AND** the tool does not silently classify it as retired or delete an explicit export selection

### Requirement: Private local inventory
The tool SHALL persist product and account identifiers only in protected local operator storage outside the repository. It SHALL retain only required inventory metadata, exclude credentials and unrelated management payloads, and avoid exposing actual identifiers in shared diagnostic logs. Credential and bearer-token values SHALL never appear in inventory or diagnostic output.

#### Scenario: API response contains unrelated sensitive settings
- **WHEN** a product-management response includes settings beyond inventory metadata
- **THEN** only allowed identity, relationship, publication, and provenance fields are retained
- **AND** raw payloads are neither persisted nor logged

### Requirement: Read-only and bounded discovery
Product discovery SHALL use official Microsoft read operations and SHALL NOT create, update, submit, publish, or delete products. It SHALL support cancellation, bounded retries, and validated pagination destinations that prevent bearer-token transmission to an unapproved origin. Malformed or conflicting inventory identities SHALL produce an incomplete result.

#### Scenario: Continuation escapes the approved API origin
- **WHEN** a continuation points outside the provider's approved HTTPS origin and route scope
- **THEN** the tool rejects it before sending credentials or a bearer token and reports incomplete discovery

#### Scenario: Operator cancels discovery
- **WHEN** cancellation occurs during retry or pagination
- **THEN** remaining requests stop, incomplete candidates do not replace successful snapshots, and completed independent scopes remain identifiable

### Requirement: Inventory and export selection are separate
The tool SHALL discover the full authorized inventory independently of export selections. Product scope for a report SHALL use an explicit selection or an explicit all-products option, validate program and report applicability, and refuse missing, ambiguous, or unverified identities. Discovery SHALL NOT automatically start report exports.

#### Scenario: Existing selection remains narrow
- **WHEN** a migrated profile selects one app and discovery returns additional apps and add-ons
- **THEN** the full inventory is visible but the report selection remains the original app unless the operator changes it

#### Scenario: All products is explicitly selected
- **WHEN** the operator selects all products for a report
- **THEN** only verified published products applicable to that report are included
- **AND** excluded kinds, unresolved coverage, or unavailable selected products receive explicit outcomes rather than silent substitution
