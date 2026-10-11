using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;

namespace Console.AnalyticsET.Cli;

internal sealed class InventoryCommands(IPublisherStoreFactory stores, IApplicationCredentialResolver credentials,
    IEnumerable<IProductInventoryProvider> providers, IPrivateStorageGuard privacy, IAtomicFileWriter writer, TimeProvider clock)
{
    internal async Task<(int ExitCode, string Output)> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        string[] args = command.Arguments;
        if (args.Length < 2 || !(args[0] == "publisher" && args[1] == "onboard" || args[0] == "products" && args[1] is "list" or "refresh"))
        {
            throw new InputException("Invalid inventory command. Run 'AnalyticsET --help'.");
        }
        Dictionary<string, string> options = [];
        bool all = false;
        bool offline = false;
        for (int index = 2; index < args.Length; index++)
        {
            string option = args[index];
            if (options.ContainsKey(option)) { throw new InputException("Duplicate inventory option."); }
            if (option is "--all" or "--offline")
            {
                options[option] = "";
                all |= option == "--all";
                offline |= option == "--offline";
            }
            else if (option is "--publisher" or "--program" or "--state" && ++index < args.Length &&
                !string.IsNullOrWhiteSpace(args[index]) && !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                options[option] = args[index];
            }
            else { throw new InputException("Unknown inventory option or missing value. Run 'AnalyticsET --help'."); }
        }
        if (offline && !(args[0] == "products" && args[1] == "list"))
        {
            throw new InputException("Use --offline only with products list.");
        }
        string? program = options.GetValueOrDefault("--program");
        if (program is not null && program is not (ApiResources.StoreProgram or ApiResources.MarketplaceProgram))
        {
            throw new InputException("Unknown Microsoft program.");
        }
        PublisherConfiguration configuration = await stores.Create(command.ConfigurationPath).LoadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PublisherProfile> selected = PublisherSelection.Resolve(configuration,
            options.TryGetValue("--publisher", out string? ids) ? ids.Split(',') : [], all);
        if (configuration.SchemaVersion != 2) { throw new InputException("Explicit schema 2 migration is required before product discovery."); }
        foreach (PublisherProfile profile in selected) { InventoryCoordinator.Programs(profile, program); }
        InventoryStore store = new(options.GetValueOrDefault("--state") ?? InventoryStore.DefaultDirectory, privacy, writer);
        if (!offline)
        {
            var results = await new InventoryCoordinator(providers, credentials, store, clock).RefreshAsync(selected, program, cancellationToken).ConfigureAwait(false);
            return (results.All(result => result.Outcome == DiscoveryOutcome.Complete) ? 0 : 2,
                JsonSerializer.Serialize(results, InventoryStore.JsonOptions));
        }
        List<object> cached = [];
        bool missing = false;
        foreach (PublisherProfile profile in selected)
        {
            foreach (string scope in InventoryCoordinator.Programs(profile, program))
            {
                IReadOnlyList<AuthorizationContext> contexts = InventoryCoordinator.Contexts(profile, scope);
                if (contexts.Count == 0)
                {
                    cached.Add(new { publisherId = profile.Id, program = scope, status = "inventory-authorization-missing" });
                    missing = true;
                    continue;
                }
                InventoryBinding binding = InventoryBinding.Create(profile, scope, contexts);
                var state = await store.LoadOfflineAsync(binding, cancellationToken).ConfigureAwait(false);
                missing |= state.Snapshot is null;
                cached.Add(new
                {
                    publisherId = profile.Id,
                    program = scope,
                    status = state.Snapshot is null ? "no-matching-successful-snapshot" : "cached-offline",
                    authorizationStatus = "unverified-offline",
                    credentialGeneration = "not-checked-offline",
                    ageSeconds = state.Snapshot is null ? (double?)null : Math.Max(0, (clock.GetUtcNow() - state.Snapshot.ObservedAt).TotalSeconds),
                    products = state.Snapshot?.Products.Where(item => item.Returned && item.Publication == PublicationState.Published).ToArray(),
                    history = state.Snapshot?.Products.Where(item => !item.Returned).ToArray(),
                    coverage = state.Snapshot?.Coverage,
                    lastAttempt = state.Attempt
                });
            }
        }
        return (missing ? 2 : 0, JsonSerializer.Serialize(cached, InventoryStore.JsonOptions));
    }
}
