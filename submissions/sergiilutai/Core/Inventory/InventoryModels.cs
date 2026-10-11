using System.Text.Json.Serialization;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Publishers;

namespace Core.AnalyticsET.Inventory;

internal enum PublicationState { Published, DraftOnly, PreviewOnly, Unknown }
internal enum AvailabilityState { Available, Retired, NotReturned, Unknown }
internal enum CoverageState { Complete, NotApplicable, Unsupported, Unverified, Failed }
internal enum DiscoveryOutcome { Complete, Partial, Failed, Cancelled, Unconfigured }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ProductIdentity(string Kind, string SourceId, string? ParentId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ProductRecord(ProductIdentity Identity, string DisplayName, string? ExternalId,
    string? OfferType, PublicationState Publication, AvailabilityState Availability, string SourceApi,
    string SchemaVersion, string? PublicationEvidence, DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt, bool Returned = true);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InventoryCoverage(string Kind, string SourceApi, string? OfferType,
    CoverageState State, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InventoryBinding(string PublisherId, string Program, string ConfigurationKey, string AuthorizationKey)
{
    internal static InventoryBinding Create(PublisherProfile profile, string program, IReadOnlyList<AuthorizationContext> contexts)
    {
        if (contexts.Count == 0 || contexts.Any(context => context.PublisherId != profile.Id || context.Program != program ||
            !ApiResources.IsInventory(context.ResourceKey) || !ApiResources.MatchesProgram(context.ResourceKey, program) ||
            context.ConfigurationKey != AuthorizationContext.Create(profile, program, context.ResourceKey).ConfigurationKey))
        {
            throw new InputException("Inventory authorization does not match the selected publisher/program binding.");
        }

        if (contexts.Select(context => context.ResourceKey).Distinct(StringComparer.Ordinal).Count() != contexts.Count)
        {
            throw new InputException("Inventory authorization contains duplicate resource bindings.");
        }

        CoverageSettings scope = profile.Accounts[program].Discovery?.Coverage ?? new();
        string[] scopeParts = [.. scope.ExpectedOfferTypes.Order(StringComparer.Ordinal), scope.VerifiedAt?.ToString("O") ?? "",
            scope.EvidenceReference ?? ""];
        string configuration = AuthorizationContext.Hash([.. contexts.Select(context => context.ConfigurationKey).Order(StringComparer.Ordinal),
            AuthorizationContext.Hash(scopeParts)]);
        return new(profile.Id, program, configuration,
            AuthorizationContext.Hash([configuration, .. contexts.Select(context => context.StorageKey).Order(StringComparer.Ordinal)]));
    }

    internal string FileKey => AuthorizationContext.Hash([PublisherId, Program]);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InventorySnapshot(int SchemaVersion, InventoryBinding Binding, DateTimeOffset ObservedAt,
    IReadOnlyList<ProductRecord> Products, IReadOnlyList<InventoryCoverage> Coverage);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record DiscoveryAttempt(InventoryBinding Binding, DateTimeOffset AttemptedAt,
    DiscoveryOutcome Outcome, string Category, IReadOnlyList<InventoryCoverage> Coverage);

internal sealed record DiscoveryRequest(PublisherProfile Profile, string Program, InventoryBinding Binding,
    IReadOnlyList<AuthorizationContext> Contexts);

internal sealed record DiscoveryCandidate(InventoryBinding Binding, DateTimeOffset ObservedAt,
    IReadOnlyList<ProductRecord> Products, IReadOnlyList<InventoryCoverage> Coverage,
    bool AccountBindingVerified, bool AccountScopeVerified);

internal interface IProductInventoryProvider
{
    string Program { get; }
    Task<DiscoveryCandidate> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken);
}

internal sealed class InventoryException(string category) : Exception("Product discovery failed; see the categorized outcome.")
{
    internal string Category { get; } = category;
}

internal static class InventoryValidation
{
    internal static IReadOnlyList<ProductRecord> Normalize(InventoryBinding binding, IEnumerable<ProductRecord> products)
    {
        Dictionary<ProductIdentity, ProductRecord> unique = [];
        Dictionary<(string Kind, string Source), string?> parents = [];
        foreach (ProductRecord item in products)
        {
            bool child = item.Identity.Kind is "store-addon" or "marketplace-plan";
            bool validKind = binding.Program == ApiResources.StoreProgram ?
                item.Identity.Kind is "store-app" or "store-addon" : item.Identity.Kind is "marketplace-offer" or "marketplace-plan";
            if (!validKind || !Text(item.Identity.SourceId) || child != (item.Identity.ParentId is not null) ||
                (child && !Text(item.Identity.ParentId)) || !Text(item.DisplayName) || !Text(item.SourceApi) ||
                !Text(item.SchemaVersion) || !Enum.IsDefined(item.Publication) || !Enum.IsDefined(item.Availability) ||
                (item.Publication == PublicationState.Published && !Text(item.PublicationEvidence)) ||
                item.LastObservedAt < item.FirstObservedAt || item.FirstObservedAt == default)
            {
                throw new InventoryException("invalid-product-metadata");
            }

            var parentKey = (item.Identity.Kind, item.Identity.SourceId);
            if (parents.TryGetValue(parentKey, out string? previousParent) && previousParent != item.Identity.ParentId)
            {
                throw new InventoryException("conflicting-parent");
            }
            parents[parentKey] = item.Identity.ParentId;
            if (unique.TryGetValue(item.Identity, out ProductRecord? previous) && previous != item)
            {
                throw new InventoryException("conflicting-identity");
            }
            unique[item.Identity] = item;
        }

        foreach (ProductRecord child in unique.Values.Where(item => item.Identity.ParentId is not null))
        {
            string parentKind = child.Identity.Kind == "store-addon" ? "store-app" : "marketplace-offer";
            if (!unique.ContainsKey(new(parentKind, child.Identity.ParentId!)))
            {
                throw new InventoryException("missing-parent");
            }
        }

        return unique.Values.OrderBy(item => item.Identity.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Identity.SourceId, StringComparer.Ordinal).ToArray();
    }

    internal static bool Complete(DiscoveryCandidate candidate) => candidate.AccountBindingVerified && candidate.AccountScopeVerified &&
        candidate.Coverage.Count > 0 && candidate.Coverage.All(coverage => coverage.State is CoverageState.Complete or CoverageState.NotApplicable) &&
        candidate.Products.All(item => item.Publication != PublicationState.Unknown && item.Availability != AvailabilityState.Unknown);

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096 &&
        value == value.Trim() && !value.Any(char.IsControl);
}
