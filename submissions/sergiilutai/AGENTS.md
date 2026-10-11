# AnalyticsET project guidance

This file applies to `submissions/sergiilutai` and its descendants.

## Product requirements

Read [docs/prd.md](docs/prd.md) before planning or implementing application changes.
It defines the local .NET reporting tool, multiple publisher support, Microsoft
program separation, official API boundaries, and acceptance criteria.

Read [docs/project-status.md](docs/project-status.md) for the current implementation,
setup confirmations, verification evidence, and resume point. Follow the version
gates rather than treating completed planning artifacts as completed development.

Treat confirmed requirements as the product baseline. Proposed defaults and open
decisions in the PRD remain distinguishable from confirmed requirements. Keep the
PRD accurate when requirements are explicitly changed, and never describe planned
features as already implemented.

## Development

- Follow the project conventions and build/run instructions in [README.md](README.md).
- Apply the loaded `dotnet-best-practices` guidance where relevant to the feature.
- Validate API contracts against official Microsoft documentation and authorized
  integration checks. Preserve publisher and program isolation throughout exports.
- Keep credentials, tokens, signed download URLs, and sensitive report contents out
  of committed files and ordinary logs.
- Keep actual Entra Client ID/Tenant ID and publisher/account/product identifiers
  in private local configuration. Use placeholders or fictional values in shared
  docs and examples, following [the setup guide](docs/entra-application-setup.md).
  These identifiers are not credentials; keeping them private is the operator's
  preference. Git ignore rules are not a substitute for Windows file permissions.
