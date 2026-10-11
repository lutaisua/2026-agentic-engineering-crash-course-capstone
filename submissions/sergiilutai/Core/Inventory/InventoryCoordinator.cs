using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Publishers;

namespace Core.AnalyticsET.Inventory;

internal sealed record InventoryResult(string PublisherId, string Program, DiscoveryOutcome Outcome,
    string Category, InventorySnapshot? Snapshot, IReadOnlyList<InventoryCoverage> Coverage, DateTimeOffset AttemptedAt,
    IReadOnlyList<ProductRecord>? PartialProducts = null, InventoryBinding? Binding = null);

internal sealed class InventoryCoordinator(IEnumerable<IProductInventoryProvider> providers,
    IApplicationCredentialResolver credentials, InventoryStore store, TimeProvider clock)
{
    private readonly Dictionary<string, IProductInventoryProvider> providers = providers.ToDictionary(provider => provider.Program, StringComparer.Ordinal);

    internal async Task<IReadOnlyList<InventoryResult>> RefreshAsync(IReadOnlyList<PublisherProfile> profiles,
        string? program, CancellationToken cancellationToken)
    {
        List<InventoryResult> results = [];
        foreach (PublisherProfile profile in profiles)
        {
            foreach (string selected in Programs(profile, program))
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await RefreshScopeAsync(profile, selected, cancellationToken).ConfigureAwait(false));
            }
        }
        return results;
    }

    internal static IReadOnlyList<string> Programs(PublisherProfile profile, string? program)
    {
        if (program is not null && !profile.Programs.Contains(program, StringComparer.Ordinal))
        {
            throw new InputException("Selected program is not enabled for a selected publisher.");
        }
        return program is null ? profile.Programs : [program];
    }

    internal static IReadOnlyList<AuthorizationContext> Contexts(PublisherProfile profile, string program) =>
        profile.Authorization.Keys.Where(resource => ApiResources.IsInventory(resource) && ApiResources.MatchesProgram(resource, program))
            .Order(StringComparer.Ordinal).Select(resource => AuthorizationContext.Create(profile, program, resource)).ToArray();

    private async Task<InventoryResult> RefreshScopeAsync(PublisherProfile profile, string program, CancellationToken cancellationToken)
    {
        DateTimeOffset attemptedAt = clock.GetUtcNow();
        if (ConfigurationValidator.ProfileSchema(profile) != 2) { throw new InputException("Explicit schema 2 migration is required before product discovery."); }
        if (profile.Accounts[program].Discovery is { Enabled: false } || !providers.TryGetValue(program, out IProductInventoryProvider? provider))
        {
            return new(profile.Id, program, DiscoveryOutcome.Unconfigured, "provider-unavailable-or-disabled", null, [], attemptedAt);
        }
        IReadOnlyList<AuthorizationContext> configured = Contexts(profile, program);
        if (configured.Count == 0)
        {
            return new(profile.Id, program, DiscoveryOutcome.Unconfigured, "inventory-authorization-missing", null, [], attemptedAt);
        }
        List<AuthorizationContext> contexts = [];
        try
        {
            foreach (AuthorizationContext context in configured)
            {
                ApplicationCredential credential = await credentials.ResolveAsync(context, cancellationToken).ConfigureAwait(false);
                contexts.Add(context with { CredentialGeneration = credential.Generation });
            }
        }
        catch (Exception error) when (error is InputException or UnauthorizedAccessException or IOException)
        {
            return new(profile.Id, program, DiscoveryOutcome.Unconfigured, "credential-unavailable", null, [], attemptedAt);
        }
        InventoryBinding binding = InventoryBinding.Create(profile, program, contexts);
        InventoryStore.InventorySession session;
        try { session = await store.OpenAsync(binding, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InputException or InventoryException)
        {
            return new(profile.Id, program, DiscoveryOutcome.Failed,
                error is InventoryException inventory ? inventory.Category : "private-state-unavailable", null, [], attemptedAt);
        }
        await using (session.ConfigureAwait(false))
        {
            IReadOnlyList<InventoryCoverage> coverage = [];
            bool committed = false;
            try
            {
                DiscoveryCandidate candidate = await provider.DiscoverAsync(new(profile, program, binding, contexts), cancellationToken).ConfigureAwait(false);
                if (candidate.Binding != binding) { throw new InventoryException("provider-binding-mismatch"); }
                coverage = candidate.Coverage;
                IReadOnlyList<ProductRecord> normalized = InventoryValidation.Normalize(binding, candidate.Products);
                if (!InventoryValidation.Complete(candidate))
                {
                    await session.RecordAttemptAsync(new(binding, attemptedAt, DiscoveryOutcome.Partial, "incomplete-coverage-or-binding", coverage), cancellationToken)
                        .ConfigureAwait(false);
                    return new(profile.Id, program, DiscoveryOutcome.Partial, "incomplete-coverage-or-binding", session.Previous, coverage, attemptedAt, normalized, binding);
                }
                InventorySnapshot snapshot = new(1, binding, candidate.ObservedAt, History(session.Previous, normalized, candidate.ObservedAt), coverage);
                cancellationToken.ThrowIfCancellationRequested();
                await session.CommitAsync(snapshot, cancellationToken).ConfigureAwait(false);
                committed = true;
                await session.RecordAttemptAsync(new(binding, attemptedAt, DiscoveryOutcome.Complete, "complete", coverage), cancellationToken).ConfigureAwait(false);
                return new(profile.Id, program, DiscoveryOutcome.Complete, "complete", snapshot, coverage, attemptedAt, Binding: binding);
            }
            catch (OperationCanceledException)
            {
                await TryRecordAsync(session, new(binding, attemptedAt, DiscoveryOutcome.Cancelled, "cancelled", coverage)).ConfigureAwait(false);
                throw;
            }
            catch (Exception error) when (error is InventoryException or HttpRequestException or IOException or UnauthorizedAccessException or InputException)
            {
                string category = error is InventoryException inventory ? inventory.Category : "discovery-or-state-failed";
                DiscoveryOutcome outcome = committed ? DiscoveryOutcome.Complete : DiscoveryOutcome.Failed;
                await TryRecordAsync(session, new(binding, attemptedAt, outcome, committed ? "attempt-metadata-write-failed" : category, coverage)).ConfigureAwait(false);
                return new(profile.Id, program, outcome, committed ? "attempt-metadata-write-failed" : category, session.Previous, coverage, attemptedAt, Binding: binding);
            }
        }
    }

    private static async Task TryRecordAsync(InventoryStore.InventorySession session, DiscoveryAttempt attempt)
    {
        try { await session.RecordAttemptAsync(attempt, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InputException) { /* Last success is independently preserved. */ }
    }

    private static IReadOnlyList<ProductRecord> History(InventorySnapshot? previous, IReadOnlyList<ProductRecord> current, DateTimeOffset observedAt)
    {
        Dictionary<ProductIdentity, ProductRecord> old = previous?.Products.ToDictionary(item => item.Identity) ?? [];
        List<ProductRecord> result = [];
        foreach (ProductRecord product in current)
        {
            result.Add(old.TryGetValue(product.Identity, out ProductRecord? earlier) ? product with { FirstObservedAt = earlier.FirstObservedAt } : product);
            old.Remove(product.Identity);
        }
        result.AddRange(old.Values.Select(product => product with { Returned = false, Availability = AvailabilityState.NotReturned, LastObservedAt = observedAt }));
        return result;
    }
}
