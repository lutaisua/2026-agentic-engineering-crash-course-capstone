using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Core.AnalyticsET.Inventory;

namespace AnalyticsET.Tests;

internal sealed record StoreHubQueryPlan(string Dataset, string ProductId, string? ParentId, StoreHubWindow Window,
    string Metric, IReadOnlyList<string> Columns, string Query, string NormalizedQuery, string MetadataSha256,
    string SampleFingerprint, string BindingFingerprint, string DateWireFormat = "date-only")
{
    internal const string QuerySpecification = "https://learn.microsoft.com/en-us/partner-center/insights/apps-games-custom-query-specification";
    internal const string Base = "https://manage.devcenter.microsoft.com/consumer/insights/v1.1/";
    public override string ToString() => "[protected narrow Store query]";
    internal string QueryStartTime => WireDate(Window.StartUtc);
    internal string QueryEndTime => WireDate(Window.EndUtc);
    private string WireDate(string boundary) => DateWireFormat switch
    {
        "date-only" => boundary[..10],
        "utc-timestamp" => boundary,
        _ => throw Invalid("hub-date-wire-format-invalid")
    };
    internal Uri PreviewUri => new(Base + "ScheduledQueries/testQueryResult?exportQuery=" + Uri.EscapeDataString(Query) +
        "&queryStartTime=" + Uri.EscapeDataString(QueryStartTime) + "&queryEndTime=" + Uri.EscapeDataString(QueryEndTime));

    internal static StoreHubQueryPlan Prepare(StoreHubMetadata metadata, StoreHubInput input, string dataset, TimeProvider clock,
        string bindingFingerprint, string dateWireFormat = "date-only")
    {
        // Revalidate typed input, even if a caller constructed records rather than parsing the private file.
        StoreHubInput.Parse(JsonSerializer.Serialize(input, StoreHubInput.JsonOptions), clock);
        if (dataset is not ("Installs" or "AddOnAcquisitions")) { throw Invalid("hub-dataset-not-permitted"); }
        if (dateWireFormat is not ("date-only" or "utc-timestamp") || (dateWireFormat == "utc-timestamp" && dataset != "Installs"))
        { throw Invalid("hub-date-wire-format-invalid"); }
        StoreHubDataset definition = metadata.Datasets.SingleOrDefault(item => item.DatasetName == dataset) ?? throw Invalid("hub-dataset-unavailable");
        if (bindingFingerprint.Length != 64 || !bindingFingerprint.All(char.IsAsciiHexDigit) || !definition.SchemaPresent ||
            definition.AvailableDateRanges?.Count is not > 0 || !definition.AggregationToDateRangeMapping!.TryGetValue("Daily", out string? daily) || daily is null)
        { throw Invalid("hub-query-schema-incomplete"); }
        string[] ranges = daily.Split(',', StringSplitOptions.TrimEntries);
        if (ranges.Length == 0 || ranges.Any(range => range is not ("LAST_30_DAYS" or "LAST_3_MONTHS")) ||
            ranges.Distinct(StringComparer.Ordinal).Count() != ranges.Length || ranges.Any(range => !definition.AvailableDateRanges!.Contains(range, StringComparer.Ordinal)))
        { throw Invalid("hub-daily-date-contract-unverified"); }
        string metric = dataset == "Installs" ? "SuccessfulInstallCount" : "PurchaseQuantity";
        string[] columns = dataset == "Installs" ? ["ProductId", "Date", metric] : ["ProductId", "ParentProductId", metric];
        if (definition.SelectableColumns is null || definition.AvailableMetrics is null ||
            !definition.AvailableMetrics.Contains(metric, StringComparer.Ordinal) ||
            columns.Where(column => column != metric).Any(column => !definition.SelectableColumns.Contains(column, StringComparer.Ordinal)))
        { throw Invalid("hub-query-schema-drift"); }
        StoreHubWindow window = dataset == "Installs" ? input.InstallsWindow : input.AddOnAcquisitionsWindow;
        string product = dataset == "Installs" ? input.ParentApplicationStoreId : input.SubscriptionStoreId;
        string? parent = dataset == "Installs" ? null : input.ParentApplicationStoreId;
        string select = "SELECT " + string.Join(", ", columns) + " FROM " + dataset;
        string parentFilter = parent is null ? "" : "ParentProductId = '" + parent + "'";
        string query = select + " WHERE ProductId IN ('" + product + "')" + (parent is null ? "" : " AND " + parentFilter) + " AGGREGATED Daily";
        string normalized = select + (parent is null ? "" : " WHERE " + parentFilter) + " AGGREGATED Daily";
        // TIMESPAN is optional in Microsoft's consumer grammar. Custom dates travel in their documented fields.
        // Empty columnFilters is a documented shape, not evidence that WHERE is forbidden.
        return new(dataset, product, parent, window, metric, columns, query, normalized,
            StoreDiagnosticPacketStore.Hash(JsonSerializer.SerializeToUtf8Bytes(definition, StoreHubInput.JsonOptions)),
            StoreDiagnosticPacketStore.Hash(JsonSerializer.SerializeToUtf8Bytes(input, StoreHubInput.JsonOptions)), bindingFingerprint, dateWireFormat);
    }

    internal bool CompatibleQuery(JsonElement resource)
    {
        if (resource.ValueKind != JsonValueKind.Object || !Unique(resource)) { return false; }
        JsonElement query = Field(resource, "query"), id = Field(resource, "queryId");
        if (query.ValueKind != JsonValueKind.String || id.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(id.GetString(), "D", out Guid queryId) || queryId == Guid.Empty) { return false; }
        string? actual = query.GetString();
        if (actual is null || actual.Length > 4096) { return false; }
        bool literalScope = Normalize(actual) == Normalize(Query);
        bool extractedScope = Normalize(actual) == Normalize(NormalizedQuery);
        if (!literalScope && !extractedScope) { return false; }
        JsonElement productInfo = Field(resource, "productInfo");
        if (productInfo.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) { return literalScope; }
        if (productInfo.ValueKind != JsonValueKind.Object || !productInfo.TryGetProperty("productId", out JsonElement product) ||
            product.ValueKind != JsonValueKind.String || !string.Equals(product.GetString(), ProductId, StringComparison.OrdinalIgnoreCase) ||
            !productInfo.TryGetProperty("productIdDbColumnName", out JsonElement column) || column.ValueKind != JsonValueKind.String ||
            !string.Equals(column.GetString(), "ProductId", StringComparison.OrdinalIgnoreCase) ||
            productInfo.EnumerateObject().Any(property => property.Name is not ("productId" or "productIdDbColumnName" or "productGroupId")) ||
            (productInfo.TryGetProperty("productGroupId", out JsonElement group) && group.ValueKind != JsonValueKind.Null &&
                (group.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(group.GetString())))) { return false; }
        return true;
    }

    internal void RequireCreationProof(StoreHubScopeProof proof)
    {
        // "baseline-identity": Microsoft documents custom dates only on report creation, so the fixed dates are proven by the
        // downloaded CSV instead of a populated preview. It is limited to the narrow Installs plan with date-only boundaries.
        bool basis = proof.Basis switch
        {
            "preview" => proof.PopulatedPreview && proof.FixedDatesVerified && proof.UtcBoundaryConventionVerified,
            "baseline-identity" => Dataset == "Installs" && ParentId is null && DateWireFormat == "date-only",
            _ => false
        };
        if (proof.SampleFingerprint != SampleFingerprint || proof.BindingFingerprint != BindingFingerprint ||
            proof.Dataset != Dataset || proof.MetadataSha256 != MetadataSha256 || proof.QuerySha256 !=
            StoreDiagnosticPacketStore.Hash(System.Text.Encoding.UTF8.GetBytes(Query)) || proof.Window != Window ||
            !proof.ProductMatches || (ParentId is not null && proof.ParentMatches != true) || !basis || proof.DateWireFormat != DateWireFormat)
        { throw Invalid("hub-query-scope-unverified"); }
    }

    private static string Normalize(string query) => Regex.Replace(query.Trim(), @"\s+", " ", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100)).ToUpperInvariant();
    internal static JsonElement Field(JsonElement row, string name) => row.ValueKind == JsonValueKind.Object ?
        row.EnumerateObject().SingleOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)).Value : default;
    internal static bool Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty field in value.EnumerateObject()) { if (!names.Add(field.Name) || !Unique(field.Value)) { return false; } }
        }
        else if (value.ValueKind == JsonValueKind.Array) { foreach (JsonElement item in value.EnumerateArray()) { if (!Unique(item)) { return false; } } }
        return true;
    }
    internal static InventoryException Invalid(string category) => new(category);
}

internal sealed record StoreHubScopeProof(string Dataset, string MetadataSha256, string QuerySha256, StoreHubWindow Window,
    bool ProductMatches, bool? ParentMatches, bool PopulatedPreview, bool FixedDatesVerified, bool UtcBoundaryConventionVerified,
    string SampleFingerprint, string BindingFingerprint, string DateWireFormat = "date-only", string Basis = "preview");

internal sealed record StoreHubPreview(int RowCount, bool? ProductMatches, bool? ParentMatches, bool? ObservedDatesWithinRequestedBounds,
    string DataState, string BoundaryState)
{
    internal static StoreHubPreview Observe(JsonElement root, StoreHubQueryPlan plan)
    {
        if (root.ValueKind != JsonValueKind.Object || !StoreHubQueryPlan.Unique(root) ||
            !root.TryGetProperty("statusCode", out JsonElement status) || !status.TryGetInt32(out int code) || code != 200 ||
            !root.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 10000 ||
            !root.TryGetProperty("totalCount", out JsonElement count) || !count.TryGetInt32(out int total) || total != value.GetArrayLength() ||
            root.EnumerateObject().Any(field => field.Name is not ("value" or "totalCount" or "statusCode" or "message")))
        { throw StoreHubQueryPlan.Invalid("hub-preview-shape-unverified"); }
        foreach (JsonElement row in value.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Count() != plan.Columns.Count ||
                row.EnumerateObject().Any(field => !plan.Columns.Contains(field.Name, StringComparer.OrdinalIgnoreCase)))
            { throw StoreHubQueryPlan.Invalid("hub-preview-schema-unverified"); }
            JsonElement Field(string name) => row.EnumerateObject().SingleOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
            JsonElement product = Field("ProductId");
            if (product.ValueKind != JsonValueKind.String || !string.Equals(product.GetString(), plan.ProductId, StringComparison.OrdinalIgnoreCase))
            { throw StoreHubQueryPlan.Invalid("hub-preview-product-mismatch"); }
            if (plan.ParentId is not null)
            {
                JsonElement parent = Field("ParentProductId");
                if (parent.ValueKind != JsonValueKind.String || !string.Equals(parent.GetString(), plan.ParentId, StringComparison.OrdinalIgnoreCase))
                { throw StoreHubQueryPlan.Invalid("hub-preview-parent-mismatch"); }
            }
            JsonElement metric = Field(plan.Metric);
            bool number = metric.ValueKind == JsonValueKind.Number ? metric.TryGetDecimal(out _) : metric.ValueKind == JsonValueKind.String &&
                decimal.TryParse(metric.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
            if (!number) { throw StoreHubQueryPlan.Invalid("hub-preview-metric-invalid"); }
            decimal quantity = metric.ValueKind == JsonValueKind.Number ? metric.GetDecimal() : decimal.Parse(metric.GetString()!, CultureInfo.InvariantCulture);
            if (decimal.Truncate(quantity) != quantity || (plan.Dataset == "Installs" && quantity < 0)) { throw StoreHubQueryPlan.Invalid("hub-preview-metric-invalid"); }
            if (plan.Dataset == "Installs")
            {
                JsonElement date = Field("Date");
                if (date.ValueKind != JsonValueKind.String || !TryObservedDay(date.GetString(), out DateOnly observed) ||
                    observed < DateOnly.ParseExact(plan.Window.StartUtc[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture) ||
                    observed >= DateOnly.ParseExact(plan.Window.EndUtc[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture))
                { throw StoreHubQueryPlan.Invalid("hub-preview-date-scope-unverified"); }
            }
        }
        // Do not keep preview rows, totals or private identifiers; empty/inside observations do not establish date inclusivity.
        return new(total, total > 0 ? true : null, plan.ParentId is null || total == 0 ? null : true,
            plan.Dataset == "Installs" && total > 0 ? true : null, total == 0 ? "empty" : "populated-observed", "unverified");
    }

    private static bool TryObservedDay(string? text, out DateOnly day)
    {
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) { return true; }
        if (DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset timestamp) &&
            timestamp.TimeOfDay == TimeSpan.Zero)
        { day = DateOnly.FromDateTime(timestamp.UtcDateTime); return true; }
        // A locale-dependent date or timestamp without an explicit zone is not interpreted as UTC.
        return false;
    }
}

internal sealed record StoreHubPreviewFieldShape(string Field, string Kind);
internal sealed record StoreHubPreviewShape(string RootKind, string ValueKind, string FirstRowKind, int? ReturnedArrayCount,
    int? FirstRowFieldCount, bool UniqueProperties, IReadOnlyList<StoreHubPreviewFieldShape> ExpectedFields)
{
    internal static StoreHubPreviewShape Observe(JsonElement root, StoreHubQueryPlan plan)
    {
        bool unique = StoreHubQueryPlan.Unique(root);
        if (!unique) { return new(root.ValueKind.ToString(), "unverified", "unverified", null, null, false, []); }
        JsonElement value = StoreHubQueryPlan.Field(root, "value");
        JsonElement first = value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 ? value[0] : default;
        return new(root.ValueKind.ToString(), value.ValueKind.ToString(), first.ValueKind.ToString(),
            value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : null,
            first.ValueKind == JsonValueKind.Object ? first.EnumerateObject().Count() : null, true,
            plan.Columns.Select(field => new StoreHubPreviewFieldShape(field, StoreHubQueryPlan.Field(first, field).ValueKind.ToString())).ToArray());
    }
}
