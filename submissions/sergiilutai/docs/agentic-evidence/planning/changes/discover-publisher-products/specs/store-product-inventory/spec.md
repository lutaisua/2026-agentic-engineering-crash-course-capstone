# Spec Delta

## Purpose

Discover Microsoft Store MSIX/UWP apps and their add-ons through official publisher APIs, with reliable pagination, product relationships, and evidence of publication.

## ADDED Requirements

### Requirement: Store account authorization
The tool SHALL authenticate Store inventory requests using the official Store submission API audience and a Partner Center application associated with the selected Windows publisher account. Store inventory authorization SHALL be evaluated separately from individual analytics-report availability.

#### Scenario: Application association is missing
- **WHEN** an application has a valid token but cannot access the selected publisher's Store inventory
- **THEN** discovery reports an account-access failure and does not treat the response as a publisher with no products

### Requirement: Enumerate apps beyond the first page
The tool SHALL enumerate every app returned by the official Store applications collection, follow all documented continuation pages, and retain Store product ID, display name, parent-independent identity, and publication evidence. Existing configured product IDs SHALL NOT filter this collection.

#### Scenario: Account has more than ten apps
- **WHEN** the API returns multiple application pages including a relative continuation link
- **THEN** the tool resolves the continuation against the documented API base and includes every unique app from all pages
- **AND** it reports the application query as complete only after the final page

### Requirement: Enumerate and identify add-ons
The tool SHALL enumerate all add-ons for each discovered app and retrieve the metadata needed to verify child identity, parent relationship, and publication evidence. It SHALL preserve the distinction between an add-on's Store product ID and its developer-assigned product ID.

#### Scenario: Published app has paginated add-ons
- **WHEN** an app's add-on list spans multiple pages
- **THEN** every unique child is resolved and associated with that app
- **AND** both identifiers are retained with distinct meanings

#### Scenario: Add-on metadata conflicts with its parent
- **WHEN** add-on detail identifies a different application from the enumerated parent
- **THEN** the tool reports a relationship conflict and does not publish a complete Store snapshot

### Requirement: Store publication classification
The tool SHALL classify apps and add-ons using their own documented published-submission evidence. A published parent SHALL NOT prove that a child is published. Draft-only resources SHALL be excluded from the published view; unavailable or contradictory evidence SHALL remain unknown. A pending update SHALL NOT erase evidence of an existing published version.

#### Scenario: Published app has a draft add-on
- **WHEN** an app has a last published submission but its add-on is confirmed never published
- **THEN** the app appears in published inventory and the draft add-on does not

#### Scenario: Published app has a pending update
- **WHEN** an app has both a last published submission and a pending submission
- **THEN** it remains classified as previously published while the pending update is recorded separately

#### Scenario: Publication evidence cannot be interpreted
- **WHEN** required publication metadata is missing or inconsistent with the verified contract
- **THEN** the item is retained as unknown and the published-product coverage cannot be reported as complete

### Requirement: Empty collections differ from unsupported features
The tool SHALL distinguish a documented empty Store collection from authentication failures, detail lookup failures, unsupported Partner Center features, and incomplete pagination. Only a verified collection-empty response SHALL establish zero products or zero add-ons.

#### Scenario: App has no add-ons
- **WHEN** the authenticated add-on collection returns the documented no-add-ons outcome
- **THEN** the app's add-on coverage is complete with zero children

#### Scenario: Store API returns unsupported feature
- **WHEN** the API reports an unsupported feature, including a documented conflict response
- **THEN** the affected product coverage is unsupported and is not reported as an empty successful inventory

### Requirement: Store inventory does not imply report entitlement
The tool SHALL expose app and add-on identities as inventory kinds and SHALL validate each report's documented supported kind and identifier independently. Discovering an add-on SHALL NOT promise app-only telemetry reports for that add-on.

#### Scenario: Add-on is selected for an app-only report
- **WHEN** the operator selects a discovered add-on for a report documented only for apps
- **THEN** the selection receives an inapplicable outcome before the report request is sent
