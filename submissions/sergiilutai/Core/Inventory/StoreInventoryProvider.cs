using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Publishers;

namespace Core.AnalyticsET.Inventory;

internal sealed class StoreInventoryProvider(StoreInventoryReader reader, IInventoryAccountProof proof, TimeProvider clock) : IProductInventoryProvider
{
    public string Program => ApiResources.StoreProgram;
    public async Task<DiscoveryCandidate> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        AuthorizationContext context = request.Contexts.SingleOrDefault(context => context.ResourceKey == ApiResources.StoreInventory)
            ?? throw new InventoryException("store-inventory-authorization-missing");
        DateTimeOffset observedAt = clock.GetUtcNow();
        bool verified = await proof.VerifyAsync(request, cancellationToken).ConfigureAwait(false);
        InventoryRequestBudget budget = new();
        List<ProductRecord> products = [];
        List<InventoryCoverage> coverage = [new("store-account", "partner-center-association", null,
            verified ? CoverageState.Complete : CoverageState.Unverified, verified ? "private-account-comparison" : "account-evidence-missing")];
        try
        {
            IReadOnlyList<ProductRecord> apps = await reader.AppsAsync(context, observedAt, cancellationToken, budget).ConfigureAwait(false);
            products.AddRange(apps);
            coverage.Add(PublicationCoverage("store-app", apps));
            List<ProductRecord> children = [];
            HashSet<InventoryCoverage> childFailures = [];
            foreach (ProductRecord app in apps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    children.AddRange(await reader.ChildrenAsync(context, app, observedAt, cancellationToken, budget).ConfigureAwait(false));
                }
                catch (InventoryException error)
                {
                    childFailures.Add(FailureCoverage("store-addon", error.Category));
                    if (error.Category is "inventory-request-budget-exhausted" or "unauthorized" or
                        "credential-generation-changed" or "invalid-token-response") { break; }
                }
            }
            products.AddRange(children);
            coverage.AddRange(childFailures.OrderBy(item => item.Reason, StringComparer.Ordinal));
            if (childFailures.Count == 0 || children.Any(child => child.Publication == PublicationState.Unknown))
            {
                coverage.Add(PublicationCoverage("store-addon", children));
            }
        }
        catch (InventoryException error)
        {
            coverage.Add(FailureCoverage("store-app", error.Category));
            coverage.Add(new("store-addon", "store-submission", null, CoverageState.Unverified, "parent-enumeration-incomplete"));
        }
        return new(request.Binding, observedAt, products, coverage, verified, verified);
    }

    private static InventoryCoverage PublicationCoverage(string kind, IEnumerable<ProductRecord> products) =>
        products.Any(product => product.Publication == PublicationState.Unknown) ?
            new(kind, "store-submission", null, CoverageState.Unverified, "publication-evidence-missing") :
            new(kind, "store-submission", null, CoverageState.Complete, "all-pages-and-publication-evidence");
    private static InventoryCoverage FailureCoverage(string kind, string category) => new(kind, "store-submission", null,
        category.StartsWith("unsupported-store", StringComparison.Ordinal) ? CoverageState.Unsupported : CoverageState.Failed, category);
}
