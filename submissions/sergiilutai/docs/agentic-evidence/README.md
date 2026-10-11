# Capstone evidence copies

Captured: **2026-10-11**.

These public documentation copies make the PR's evidence links accessible in the
capstone fork. The active OpenSpec planning home and skill files remain in the
outer AnalyticsET workspace. This directory is evidence for review, not an active
OpenSpec project or an installed skill. It does not change implementation,
acceptance criteria, task progress or release status.

| Evidence | Original workspace-relative source | Published copy |
|---|---|---|
| OpenSpec implementation workflow | `.agents/skills/openspec-apply-change/SKILL.md` | [Workflow](workflows/openspec-apply-change.md) |
| Development iterations | `openspec/changes/develop-analytics-et-in-iterations/design.md` | [Design](planning/changes/develop-analytics-et-in-iterations/design.md) |
| Iteration proposal | `openspec/changes/develop-analytics-et-in-iterations/proposal.md` | [Proposal](planning/changes/develop-analytics-et-in-iterations/proposal.md) |
| Discovery design and linked specification deltas | `openspec/changes/discover-publisher-products/` | [Design](planning/changes/discover-publisher-products/design.md) |
| Subscription diagnostic proposal | `openspec/changes/diagnose-store-subscription-inventory/proposal.md` | [Proposal](planning/changes/diagnose-store-subscription-inventory/proposal.md) |

The discovery proposal and its three specification deltas are included so the
design's relative links work. All eight other source copies are byte-identical.
The iteration design has one Markdown target adjusted: its PRD link now resolves
to the submission's existing PRD. Original and published SHA-256 values are in
[source-manifest.json](source-manifest.json).

These are publication snapshots. Their capture date is not proof that the original
specifications preceded implementation. Any claim about chronology needs the
original dated planning/session evidence or existing commit history.

Commit and push these files together with the submission and PR template to
`codex/analytics-et-iterations`. The template's evidence URLs point to that branch
in the fork, so the upstream PR does not depend on being merged. Update the URL
prefix if the submission branch or repository changes.
