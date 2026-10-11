# Spec Delta

## Purpose

Discover Azure Marketplace publisher offers and their plans through the appropriate official APIs, distinguish published resources from drafts, and disclose offer-type coverage gaps.

## ADDED Requirements

### Requirement: Marketplace inventory authorization
The tool SHALL use the authorization resource documented for the selected Marketplace inventory API and bind it to the selected publisher account. Inventory authorization SHALL remain distinct from Marketplace analytics and financial-report authorization, even when they use the same application or credential reference.

#### Scenario: Analytics token is available but inventory authorization is absent
- **WHEN** a profile has Marketplace analytics access but lacks the authorization required by its inventory API
- **THEN** inventory access is reported as missing and the analytics token is not substituted

### Requirement: Enumerate offers and plans with stable identity
The tool SHALL enumerate all offers and their plans exposed by supported official publisher APIs, follow every documented continuation, and retain offer type, source identity, external offer or plan ID, parent relationship, and API-version provenance. Different identity formats SHALL not be treated as interchangeable report identifiers.

#### Scenario: Offers and plans span multiple pages
- **WHEN** official collections return continuation tokens for offers or plans
- **THEN** discovery reads every page and associates each plan with its own offer
- **AND** durable IDs and external IDs remain separately identifiable

#### Scenario: Offer has no plans under its verified contract
- **WHEN** an offer type's official contract permits an offer without plans
- **THEN** the offer remains discoverable with zero applicable plans rather than being discarded

### Requirement: Published Marketplace state requires live evidence
The tool SHALL determine published offers and plans from documented live or successful live-publication evidence. A draft query, default lifecycle value, preview submission, or published parent alone SHALL NOT establish child publication. Draft changes SHALL not replace the identities of the current live version.

#### Scenario: Live offer has a new draft plan
- **WHEN** the draft configuration includes a plan absent from the verified live configuration
- **THEN** the new draft plan is excluded from the published-plan view

#### Scenario: Offer has only a preview submission
- **WHEN** an offer has successful preview evidence but no successful live-publication evidence
- **THEN** it is classified as preview-only and excluded from the published view

#### Scenario: Published offer has newer draft changes
- **WHEN** draft and live offer configurations differ
- **THEN** the tool identifies the live offer and its live plans using the live evidence and records the draft distinction

### Requirement: Offer-type coverage is explicit
The tool SHALL identify supported, unsupported, unverified, and failed discovery coverage by offer type. It SHALL use the appropriate documented API rather than assuming one API supports every type. Unknown account scope or an uncovered type SHALL prevent a claim of complete account-wide inventory.

#### Scenario: Account includes multiple supported API families
- **WHEN** a publisher has offers requiring different official inventory APIs
- **THEN** each applicable API family contributes its own verified result and the combined inventory retains source provenance

#### Scenario: Account contains an unsupported or unknown type
- **WHEN** a type lacks a verified discovery path or cannot be classified
- **THEN** the tool identifies the gap and marks account-wide coverage incomplete

### Requirement: Legacy API access is not an automatic fallback
The tool SHALL use a legacy Cloud Partner Portal API only for an explicitly configured, eligible pre-transition integration with verified contract and account coverage. It SHALL not assume legacy access is available to new publishers.

#### Scenario: New publisher has a legacy-only offer type
- **WHEN** current Microsoft guidance lists only a legacy API for that type and the publisher has no eligible existing integration
- **THEN** the tool reports unsupported coverage rather than automatically attempting the legacy API or claiming zero offers

### Requirement: Publisher inventory is independent of sales data
The tool SHALL discover publisher-owned offers without requiring orders, revenue, or usage records. A reseller catalog, analytics dataset, or order-derived product list SHALL NOT substitute for publisher inventory. Discovery SHALL not start analytics or financial report jobs.

#### Scenario: Published offer has no sales
- **WHEN** a published offer has no analytics or financial records
- **THEN** it remains in the inventory if exposed by the authorized publisher API

### Requirement: Preserve publication history without inventing availability
The tool SHALL retain source evidence of prior publication and explicit retirement when available. Products no longer returned SHALL remain historical observations. Lack of a current live representation SHALL not by itself prove that a previously published offer or plan was never published, deleted, or retired.

#### Scenario: Previously live plan is no longer returned
- **WHEN** a formerly discovered published plan disappears from a complete current inventory
- **THEN** its history is retained as not returned unless explicit source evidence establishes retirement
- **AND** current selections receive an unavailable or unverified outcome instead of silent reassignment
