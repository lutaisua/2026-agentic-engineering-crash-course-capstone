# Private Store discovery and account comparison

Updated: 2026-10-06. Store discovery/authentication is fixture verified. The first
authorized live check resolved the saved credential and acquired a token, then
returned **zero apps and zero add-ons**. Collection coverage was complete, while
account attribution remained unverified (`account-evidence-missing`); overall
result was **partial/exit 2**. Detailed review JSON remains private. The operator
reports roughly 43 published apps and 86 add-ons, so this fails account comparison.
The read-only diagnostic confirmed HTTP 200 with an empty collection and matching
configured token claims. The original app subsequently had a disabled checked
picker entry and a missing main roles entry. The operator now confirms a
replacement's saved Manager(Windows) role and changed runtime Client IDs.
Replacement offline validation passed, then its read-only diagnostic accepted
the mapped secret and confirmed token/app collection HTTP 200 with matching
decoded identities. That diagnostic's inventory was empty and failed comparison.
The operator subsequently reports a populated review with exit 2. Two newer
candidate reviews now contain 85 apps (55 with publication evidence, 30 unknown),
missing account evidence and failed add-on discovery. A subsequent diagnostic
captured the first add-on collection HTTP 400. The exact operator-selected file
awaits confirmation; earlier empty-result evidence does not describe this later
data. Account evidence alone cannot resolve publication/traversal gaps.
See the [replacement diagnostic](store-replacement-diagnostic.md) and the
[replacement checkpoint](../entra-application-setup.md#replacement-runtime-checkpoint-2026-10-06).
Populated add-on and later-publisher evidence are
still missing; see [the diagnostic checkpoint](store-association-diagnostic.md).
Create and offline-validate the [runtime profile](../runtime-profile-setup.md) first.
The operator's existing registration file was readable but rejected as JSON by
Windows PowerShell; the masked recovery mode preserves it and creates a separate
runtime profile. The operator confirms creation succeeded. Its first offline
validation rejected the local alias; the documented backup-preserving recovery
now succeeded and offline validation passed (schema 2, one publisher). Live
comparison remains a separate check; no complete successful snapshot was accepted.

An empty API response cannot establish a valid empty publisher when the dashboard
contains products. Do not write the account evidence file for this mismatched
result. Reconfirm the correct publisher/tenant/application association first.

## Read-only discovery

Use your ordinary operator terminal and the explicit protected configuration:

```powershell
$runtimeConfig = Join-Path $env:USERPROFILE '.analyticset/runtime/publishers.json'
$privateReview = Join-Path $env:USERPROFILE '.analyticset/runtime/store-discovery-review.json'
dotnet bin/Release/net10.0/AnalyticsET.dll --config $runtimeConfig products list --all --program microsoft-store > $privateReview
$LASTEXITCODE
```

Keep the review JSON private. It contains normalized product IDs and names for
your local comparison, coverage, and hashed configuration binding. It contains
neither raw Microsoft payloads nor a client secret/token. Native GETs use the
Store submission API; only token acquisition uses POST. No report/export job or
publishing operation is started. The Generic Credential is read in the executing
Windows user's logon session, so run as the user who saved it.

Initial exit 2/partial is expected until account evidence is established. Check
the category and each coverage state. A 401/403 is authorization failure; a 409
is unsupported coverage. Missing/null publication evidence stays unknown and
blocks completeness. Troubleshoot permission/coverage failures before attesting.

Compare returned apps and children with the **selected publisher's** Partner
Center inventory: Store IDs, names, published submissions, parent-child mapping,
counts and absent/draft items. The collection exposes reserved/draft metadata as
well as published products; `partialProducts` is diagnostic and cannot authorize
an export. Check the application's saved account association and Manager role.
Do not assume a shared tenant means the result belongs to each publisher. See
[the verified contract interpretation](store-inventory-contracts.md).

## Record account attribution after comparison

The production proof verifier reads a private evidence file in
`%USERPROFILE%\.analyticset\inventory`, which discovery provisions with restricted
operator/SYSTEM ACLs. Evidence is an operator's dated comparison, not Microsoft
token proof. It must match the profile/program/configuration binding and both
app/add-on scopes. A changed application/account/resource setting invalidates it.

After completing the private comparison:

1. In the private runtime JSON, set Store `discovery.coverage.verifiedAt` to your
   actual ISO timestamp and `evidenceReference` to a safe alias such as
   `store-account-comparison`. Preserve the rest of the profile.
2. Run offline validation, then refresh again with those settings. Keep the new
   private review output. Its `binding.configurationKey` reflects the new settings.
3. Write `<evidenceReference>.json` in the already restricted inventory directory.
   Use the actual profile alias, that configuration hash, and the identical date.
   The schema below is fictional; do not claim comparison before doing it.
4. Refresh once more. A complete snapshot requires this proof **and** complete
   traversal/publication coverage. Unknown publication or unsupported features
   remain gaps even when account comparison is recorded.

```json
{
  "schemaVersion": 1,
  "publisherId": "fictional-store",
  "program": "microsoft-store",
  "configurationKey": "replace-with-the-private-review-configuration-hash",
  "verifiedAt": "2026-10-05T12:00:00Z",
  "source": "partner-center-inventory-comparison",
  "kinds": ["store-app", "store-addon"]
}
```

Only allowlisted proof fields are accepted. Missing proof yields unverified
binding; inconsistent dates/configuration/kinds do not grant completeness. Keep
screenshots or detailed notes separately in private storage. Shared evidence
records only counts, categories, verified kinds, date, and API-contract provenance.

S2 requires a populated add-on comparison to advertise populated child coverage;
zero children verifies only the empty case. Live L1 needs a later-added publisher
and an existing later-publication observation when accounts/products are available.
Do not create/publish test products. Installs/ratings report permissions and
comparisons are separate sibling acceptance gates. Discovery alone cannot accept
v0.2. Export integration task 2.7 remains blocked by the missing report commands.
