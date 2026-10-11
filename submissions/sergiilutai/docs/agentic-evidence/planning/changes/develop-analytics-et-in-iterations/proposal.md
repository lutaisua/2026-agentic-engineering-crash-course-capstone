# Proposal

## Why

AnalyticsET currently prints a readiness message, while its PRD calls for local
Partner Center reporting across multiple publishers and Microsoft programs.
Delivering usable versions with explicit acceptance gates will expose API and
authentication constraints early and make progress verifiable.

## What Changes

- Deliver six cumulative versions: v0.1 local publisher foundation, v0.2 Store
  exports, v0.3 Marketplace exports, v0.4 financial exports, v0.5 resilient batch
  exports, and v1.0 verified report coverage and distribution.
- Add local publisher profiles and resource-specific authentication with isolated
  credentials and token state.
- Add official API connectors for MSIX/UWP Store analytics, Marketplace analytics
  v1.1, and Partner Payouts/Earnings.
- Organize files and manifests by publisher, Microsoft program, report, and run.
- Define acceptance criteria, PRD traceability, automated verification, live
  integration checks, and retained evidence for every version.
- Preserve a bounded first-release report catalog: all documented candidates are
  assessed, but only verified, applicable connectors are advertised as supported.
  Dashboard-only reports remain explicitly unavailable through this tool.

## Capabilities

### New Capabilities

- `publisher-profiles`: Local publisher configuration, explicit account selection,
  and credential/token isolation.
- `store-analytics-export`: Store report catalog, scope/date validation, paginated
  MSIX/UWP analytics retrieval, and tabular export.
- `marketplace-analytics-export`: Dataset discovery, report queries, asynchronous
  Marketplace analytics generation, and file download.
- `financial-report-export`: Delegated financial authorization and program-scoped
  earnings/transaction and payment exports.
- `export-runs`: Export organization, manifests, progress, batch outcomes,
  cancellation, retry behavior, and preservation of earlier files.

### Modified Capabilities

None. The existing OpenSpec capability inventory is empty.

## Impact

The implementation target is
`2026-agentic-engineering-crash-course-capstone/submissions/sergiilutai`, governed
by its `AGENTS.md` and `docs/prd.md`. The existing OpenSpec planning home is the
AnalyticsET workspace root; this change does not relocate it.

Future implementation will extend the .NET 10 console app, add meaningful tests,
local credential storage, CLI/configuration support, and release documentation.
Microsoft account onboarding and live verification need authorized publishers;
no access is assumed. This proposal creates planning artifacts only.

Windows local execution, on-demand exports, and CSV defaults are proposed release
defaults. Scheduling, a hosted backend, a GUI, and publishing management remain
outside v1.0. No existing product behavior or public contract is removed.
