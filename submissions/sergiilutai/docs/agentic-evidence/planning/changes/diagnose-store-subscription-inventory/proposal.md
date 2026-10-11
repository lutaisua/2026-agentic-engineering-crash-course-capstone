# Proposal

## Why

AnalyticsET's Store add-on collections return HTTP 400 for a publisher whose
add-ons are Subscription products, while authentication and app enumeration
succeed. Existing diagnostics suppress useful service fields, so a bounded,
private collection-versus-detail comparison is needed before changing product scope.

## What Changes

- Add an explicitly invoked Store diagnostic that accepts one publisher, parent
  app Store ID and operator-confirmed Subscription add-on Store ID through a
  protected local input file. These IDs are diagnostic inputs, not a manual
  inventory prerequisite or a report selection.
- Capture exact allowlisted service error codes, targets, recognized product-type
  names and validated returned correlation information, alongside UTC request
  times, HTTP statuses and explicit absent/unrecognized-field markers. Keep actual
  identities and correlations in protected local evidence; omit raw bodies,
  messages, credentials, tokens and arbitrary headers.
- Compare a parent-app control, per-app and account-wide collection requests
  with and without `top=10`, and one direct add-on detail GET. Separate server
  responses, identity/parent checks and production-parser compatibility.
- Produce an evidence-based, non-authoritative interpretation: collection-specific
  failure, matching type rejection, local parser gap, mismatch, or unresolved result.
  A completed diagnostic is not successful inventory or version acceptance.
- Update the prepared Microsoft support request during implementation with a
  sanitized comparison and the April 2022 Q&A precedent. Keep account-specific
  attachments private; case submission remains a separate operator action.
- Preserve Store apps-and-add-ons coverage, current publication classification,
  refresh/selection gates and successful snapshots. No production Subscription
  adapter, analytics-derived inventory, report export or publishing mutation is
  introduced by this change.

## Capabilities

### New Capabilities

- `store-inventory-diagnostics`: Explicit, publisher-bound, read-only Store probes,
  constrained evidence capture, private persistence and support-case preparation.

### Modified Capabilities

None. Main specs are currently absent. The in-flight `store-product-inventory`
delta in `discover-publisher-products` remains authoritative for inventory
completeness; this change supplies diagnostic evidence for its blocked live gate.

## Impact

- Target: `2026-agentic-engineering-crash-course-capstone/submissions/sergiilutai`.
  Extend the existing `StoreDiagnosticErrorSummary` and Live diagnostic harness,
  reuse authentication/HTTP/storage boundaries, and provide an operator helper.
- Planned documentation updates: the existing subscription investigation,
  support-request draft, project status and diagnostic verification instructions.
- APIs: official Store submission GETs under `/v1.0/my/`, plus existing token
  acquisition. Actual publisher/Entra/product identifiers remain outside shared files.
- Verification: fictional HTTP/privacy/storage fixtures, the documented Release
  verification, and a separately recorded authorized live comparison. Missing
  private input or live access leaves that task blocked rather than passed.
- This proposal creates planning artifacts only. Neither implementation nor live
  requests are performed during proposal creation; no version gate is relaxed.
