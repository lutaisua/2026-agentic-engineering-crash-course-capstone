# AnalyticsET

A local .NET console tool for Partner Center reporting across publishers.
v0.1 implements local publisher setup and offline validation. Microsoft Store,
Azure Marketplace, and financial report exports are planned in later versions.

The development build adds schema 2 profiles without product IDs, explicit
migration, protected credential mapping, and private inventory orchestration.
The Store inventory provider is implemented and verified with HTTP fixtures;
the initial live token/discovery check returned zero apps/add-ons. Account
comparison and report access remain unverified. Marketplace providers
remain pending. Create the actual private profile with
the [runtime setup helper](docs/runtime-profile-setup.md), then validate it offline.
Marketplace inventory token bindings now have
[fixture verification](docs/verification/v0.3-authentication.md); inventory adapters
and live Marketplace access remain pending.
The [Store parent-isolation follow-up](docs/verification/v0.2-store-parent-isolation.md)
now passes 105 non-live tests. Live app enumeration returns 85 records; add-on
API errors and unpublished/account evidence remain unresolved. An individual
parent failure no longer hides other parents' child results.
The publisher's add-ons are operator-confirmed Subscription products; the
[subscription inventory investigation](docs/verification/store-subscription-inventory.md)
records the unresolved API contract without changing the completeness gate.
The [Subscription diagnostic guide](docs/verification/store-subscription-diagnostic.md)
provides the tested masked-input helper and six-probe comparison. The reviewed
live run returns NotAllowed/productType/Subscription on detail and all four
collections. The [diagnostic checkpoint](docs/verification/store-subscription-diagnostic-checkpoint.md)
records 135 passing non-live tests and separate live evidence. v0.2 remains
unaccepted; recorded HTTP 400 comparison is diagnostic completion only.

The [Store Downloads Hub experiment](docs/verification/store-downloads-hub-validation.md)
adds protected consumer dataset discovery and a read-only Installs query preview.
Its [checkpoint](docs/verification/store-downloads-hub-checkpoint.md) records 195
passing non-live tests. Live discovery found both target datasets among 16.
The populated small Partner Center baseline has matching app/calendar-day scope;
both date-only and explicit UTC timestamp API previews returned zero rows. UTC
semantics and the cause remain unresolved; no populated comparison has passed.
Atomic creation journals and single-send POSTs are fixture verified; live export
integration, reconciliation, polling and downloads remain pending. The normal CLI
and v0.2 gate are unchanged.

Read the [current development/setup status](docs/project-status.md),
[PRD](docs/prd.md), [version gates](docs/versions.md), and
[publisher setup guide](docs/publisher-setup.md). Registration progress and private
configuration guidance are in the [Entra application setup guide](docs/entra-application-setup.md).

## Requirements and commands

Windows and .NET 10 SDK. Run from `submissions/sergiilutai`:

```powershell
dotnet build AnalyticsET.slnx --configuration Release
dotnet bin/Release/net10.0/AnalyticsET.dll --help
dotnet bin/Release/net10.0/AnalyticsET.dll --version
powershell -NoProfile -File docs/examples/v0.1-smoke.ps1
powershell -NoProfile -File scripts/verify.ps1
```

The smoke examples create two fictional profiles in a new temporary directory
and exercise CRUD, selection, and validation. No credentials or Microsoft account
are required. Authorization remains unverified.

The verifier builds Release, runs non-live MSTest/VSTest tests, and checks formatting.
It stops and returns nonzero on any failed check. NuGet access is needed for the
first dependency restore; the application setup commands themselves work offline.
Use `dotnet format AnalyticsET.slnx --no-restore` to apply formatting before verification.

## Development conventions

Nullable references, compiler warnings as errors, and XML documentation generation
are enabled. Keep the console project at this directory; its compile items exclude
`tests/**`. Feature namespaces use `Console.AnalyticsET` and `Core.AnalyticsET`.
Use Microsoft dependency injection, asynchronous I/O, input validation, cancellation,
and focused interfaces. Document public APIs when introducing them.

Tests use MSTest with VSTest and FluentAssertions. Use Moq where substitution helps;
the current file-publication failure tests use a small focused test double. Add live
checks separately as `TestCategory=Live` and require explicit authorized profiles.

Profile configuration is non-secret and schema-versioned. Keep credentials, tokens,
signed URLs, and sensitive report contents out of configuration, logs, and Git.
Store discovery state outside the repository under `%USERPROFILE%/.analyticset`;
inventory refuses Git/OpenSpec workspace paths. Generated outputs are ignored.
Keep actual registration/publisher identifiers out of shared docs and examples.
Use restricted Windows file permissions for real local configuration; `.gitignore`
does not provide access control. Credentials are resolved from Windows Credential
Manager. Tokens are cached only in process memory; persisted token encryption
remains a later iteration.
The [v0.1 acceptance record](docs/verification/v0.1.md) records the historical passing build,
42 tests, formatting check, and source hashes. The next milestone is v0.2 Store
exports; its [onboarding checkpoint](docs/verification/store-onboarding.md) lists
the required inputs and pending authorized verification.
