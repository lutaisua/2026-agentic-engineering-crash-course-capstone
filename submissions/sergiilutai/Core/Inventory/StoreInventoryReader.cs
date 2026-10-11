using System.Globalization;
using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Networking;

namespace Core.AnalyticsET.Inventory;

internal sealed class StoreInventoryReader(AuthorizedInventoryHttp http)
{
    internal static readonly Uri BaseUri = new("https://manage.devcenter.microsoft.com/v1.0/my/");

    internal async Task<IReadOnlyList<ProductRecord>> AppsAsync(AuthorizationContext context, DateTimeOffset observedAt, CancellationToken cancellationToken,
        InventoryRequestBudget? budget = null)
    {
        IReadOnlyList<JsonElement> apps = await CollectionAsync(context, "applications", "id", cancellationToken, budget ?? new()).ConfigureAwait(false);
        List<ProductRecord> products = [];
        foreach (JsonElement app in apps)
        {
            string id = SourceId(app, "id");
            var publication = StorePublication.Classify(app, id, false);
            products.Add(new(new("store-app", id), Text(app, "primaryName"), null, null, publication.State,
                AvailabilityState.Available, "store-submission", "v1.0", publication.Evidence, observedAt, observedAt));
        }
        InventoryBinding shape = new("shape-validation", "microsoft-store", "", "");
        return InventoryValidation.Normalize(shape, products);
    }

    internal async Task<IReadOnlyList<ProductRecord>> ChildrenAsync(AuthorizationContext context, ProductRecord parent,
        DateTimeOffset observedAt, CancellationToken cancellationToken, InventoryRequestBudget? budget = null)
    {
        budget ??= new();
        string collection = "applications/" + Uri.EscapeDataString(parent.Identity.SourceId) + "/listinappproducts";
        IReadOnlyList<JsonElement> children = await CollectionAsync(context, collection, "inAppProductId", cancellationToken, budget).ConfigureAwait(false);
        List<ProductRecord> products = [];
        foreach (string id in children.Select(child => SourceId(child, "inAppProductId")).Distinct(StringComparer.Ordinal))
        {
            budget.Charge();
            using JsonDocument detail = await http.GetAsync(context, new(BaseUri, "inappproducts/" + Uri.EscapeDataString(id)), BaseUri,
                false, cancellationToken).ConfigureAwait(false) ?? throw new InventoryException("missing-child-detail");
            JsonElement child = detail.RootElement;
            if (child.ValueKind != JsonValueKind.Object || SourceId(child, "id") != id ||
                !child.TryGetProperty("applications", out JsonElement applications) || applications.ValueKind != JsonValueKind.Object ||
                !applications.TryGetProperty("value", out JsonElement parents) || parents.ValueKind != JsonValueKind.Array || parents.GetArrayLength() != 1 ||
                !applications.TryGetProperty("totalCount", out JsonElement count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out int total) || total != 1 ||
                SourceId(parents[0], "id") != parent.Identity.SourceId ||
                Text(parents[0], "resourceLocation") != "applications/" + parent.Identity.SourceId)
            { throw new InventoryException("conflicting-parent"); }
            if (!SupportsProductType(Text(child, "productType"))) { throw new InventoryException("unsupported-store-product-type"); }
            var publication = StorePublication.Classify(child, id, true);
            products.Add(new(new("store-addon", id, parent.Identity.SourceId), Text(child, "productId"), Text(child, "productId"),
                null, publication.State, AvailabilityState.Available, "store-submission", "v1.0", publication.Evidence, observedAt, observedAt));
        }
        return products;
    }

    internal static bool SupportsProductType(string value) => value is "Durable" or "Consumable";

    private async Task<IReadOnlyList<JsonElement>> CollectionAsync(AuthorizationContext context, string collection, string identityField,
        CancellationToken cancellationToken, InventoryRequestBudget budget)
    {
        Uri current = new(BaseUri, collection + "?top=10");
        string expectedPath = current.AbsolutePath;
        HashSet<string> visited = new(StringComparer.Ordinal);
        List<JsonElement> values = [];
        int? expectedTotal = null;
        for (int page = 0; page < 10000; page++)
        {
            BoundedJsonHttp.ValidateDestination(current, BaseUri);
            if (current.AbsolutePath.TrimEnd('/') != expectedPath || !visited.Add(PageKey(current)))
            { throw new InventoryException("continuation-cycle-or-scope-change"); }
            budget.Charge();
            using JsonDocument? document = await http.GetAsync(context, current, BaseUri, page == 0, cancellationToken).ConfigureAwait(false);
            if (document is null) { return []; }
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("value", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("totalCount", out JsonElement count) || count.ValueKind != JsonValueKind.Number ||
                !count.TryGetInt32(out int total) || total < 0 || (expectedTotal is not null && expectedTotal != total))
            { throw new InventoryException("malformed-or-changing-collection"); }
            expectedTotal = total;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                SourceId(row, identityField);
                values.Add(row.Clone());
                if (values.Count > 100000) { throw new InventoryException("inventory-item-budget-exhausted"); }
            }
            if (!root.TryGetProperty("@nextLink", out JsonElement next) || next.ValueKind == JsonValueKind.Null)
            {
                if (values.Select(value => SourceId(value, identityField)).Distinct(StringComparer.Ordinal).Count() != expectedTotal)
                { throw new InventoryException("collection-count-mismatch"); }
                return values;
            }
            if (next.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(next.GetString()) || !Uri.TryCreate(BaseUri, next.GetString(), out Uri? destination))
            { throw new InventoryException("malformed-continuation"); }
            BoundedJsonHttp.ValidateDestination(destination, BaseUri);
            current = destination;
        }
        throw new InventoryException("pagination-budget-exhausted");
    }

    private static string PageKey(Uri uri)
    {
        Dictionary<string, int> parameters = new(StringComparer.Ordinal);
        foreach (string part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=');
            if (pair.Length != 2 || pair[0] is not ("top" or "skip") ||
                !int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
                !parameters.TryAdd(pair[0], value)) { throw new InventoryException("unsupported-continuation-query"); }
        }
        return uri.AbsolutePath.TrimEnd('/') + "?top=" + parameters.GetValueOrDefault("top", 10) + "&skip=" + parameters.GetValueOrDefault("skip", 0);
    }

    private static string SourceId(JsonElement value, string field)
    {
        string id = Text(value, field);
        if (id.Length > 128 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        { throw new InventoryException("invalid-store-identity"); }
        return id;
    }

    private static string Text(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(field, out JsonElement property) || property.ValueKind != JsonValueKind.String)
        { throw new InventoryException("missing-store-metadata"); }
        string text = property.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() || text.Length > 4096 || text.Any(char.IsControl))
        { throw new InventoryException("invalid-store-metadata"); }
        return text;
    }
}

internal sealed class InventoryRequestBudget(int maximum = 20000)
{
    private int remaining = maximum;
    internal void Charge()
    {
        if (Interlocked.Decrement(ref remaining) < 0) { throw new InventoryException("inventory-request-budget-exhausted"); }
    }
}
