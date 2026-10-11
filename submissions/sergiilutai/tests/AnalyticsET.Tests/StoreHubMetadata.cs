using System.Text.Json;
using Core.AnalyticsET.Inventory;

namespace AnalyticsET.Tests;

internal sealed record StoreHubDataset(string DatasetName, IReadOnlyList<string>? SelectableColumns, IReadOnlyList<string>? AvailableMetrics,
    IReadOnlyList<string>? AvailableDateRanges, int? MinimumRecurrenceInterval, string? ModifiedTime,
    IReadOnlyDictionary<string, string?>? ColumnFilters, IReadOnlyDictionary<string, string?>? AggregationToDateRangeMapping)
{
    internal bool SchemaPresent => SelectableColumns?.Count > 0 && AvailableMetrics?.Count > 0 &&
        ColumnFilters is not null && AggregationToDateRangeMapping?.Count > 0;
}

internal sealed record StoreHubMetadata(IReadOnlyList<StoreHubDataset> Datasets, int AdvertisedCount)
{
    internal const string Endpoint = "https://manage.devcenter.microsoft.com/consumer/insights/v1.1/ScheduledDataset?scope=consumer";
    internal object PublicProjection() => new
    {
        metadata = "recorded",
        datasetCount = Datasets.Count,
        targets = new[] { "Installs", "AddOnAcquisitions" }.Select(name => new
        {
            dataset = name,
            availability = Datasets.Any(dataset => dataset.DatasetName == name) ? "discovered" : "unavailable",
            schema = Datasets.SingleOrDefault(dataset => dataset.DatasetName == name)?.SchemaPresent == true ? "present" : "incomplete",
            export = "unverified",
            comparison = "unverified"
        })
    };

    internal static StoreHubMetadata Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(field =>
                field.Name is not ("value" or "totalCount" or "statusCode" or "message")) ||
            !root.TryGetProperty("value", out JsonElement values) || values.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("totalCount", out JsonElement count) || !count.TryGetInt32(out int total) || total < 0 || total > 256 ||
            total != values.GetArrayLength() || !root.TryGetProperty("statusCode", out JsonElement status) ||
            !status.TryGetInt32(out int code) || code != 200) { throw Invalid(); }
        Unique(root);
        List<StoreHubDataset> datasets = [];
        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(field => field.Name is not
                ("datasetName" or "selectableColumns" or "availableMetrics" or "availableDateRanges" or "minimumRecurrenceInterval" or
                "modifiedTime" or "columnFilters" or "aggregationToDateRangeMapping")) ||
                !value.TryGetProperty("datasetName", out JsonElement name) || name.ValueKind != JsonValueKind.String ||
                !Identifier(name.GetString()!) || datasets.Any(dataset => string.Equals(dataset.DatasetName, name.GetString(), StringComparison.OrdinalIgnoreCase)))
            { throw Invalid(); }
            int? recurrence = null;
            if (value.TryGetProperty("minimumRecurrenceInterval", out JsonElement interval) && interval.ValueKind != JsonValueKind.Null)
            {
                if (!interval.TryGetInt32(out int number) || number < 0) { throw Invalid(); }
                recurrence = number;
            }
            datasets.Add(new(name.GetString()!, Array(value, "selectableColumns", true), Array(value, "availableMetrics", true),
                Array(value, "availableDateRanges", false), recurrence, Text(value, "modifiedTime", 128),
                Map(value, "columnFilters"), Map(value, "aggregationToDateRangeMapping")));
        }
        return new(datasets, total);
    }

    private static IReadOnlyList<string>? Array(JsonElement root, string field, bool identifiers)
    {
        if (!root.TryGetProperty(field, out JsonElement value) || value.ValueKind == JsonValueKind.Null) { return null; }
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 256) { throw Invalid(); }
        List<string> entries = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !Bounded(item.GetString()!, 256) ||
                (identifiers && !Identifier(item.GetString()!)) || entries.Contains(item.GetString()!, StringComparer.OrdinalIgnoreCase)) { throw Invalid(); }
            entries.Add(item.GetString()!);
        }
        return entries;
    }
    private static IReadOnlyDictionary<string, string?>? Map(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out JsonElement value) || value.ValueKind == JsonValueKind.Null) { return null; }
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() > 256) { throw Invalid(); }
        Dictionary<string, string?> entries = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty item in value.EnumerateObject())
        {
            if (!Identifier(item.Name) || !entries.TryAdd(item.Name, Text(value, item.Name, 1024))) { throw Invalid(); }
        }
        return entries;
    }
    private static string? Text(JsonElement root, string field, int maximum)
    {
        if (!root.TryGetProperty(field, out JsonElement value) || value.ValueKind == JsonValueKind.Null) { return null; }
        if (value.ValueKind != JsonValueKind.String || !Bounded(value.GetString()!, maximum)) { throw Invalid(); }
        return value.GetString();
    }
    private static bool Bounded(string value, int maximum) => value.Length is > 0 && value.Length <= maximum && !value.Any(char.IsControl);
    private static bool Identifier(string value) => Bounded(value, 128) && char.IsAsciiLetter(value[0]) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty field in value.EnumerateObject())
            {
                if (!names.Add(field.Name)) { throw Invalid(); }
                Unique(field.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) { foreach (JsonElement item in value.EnumerateArray()) { Unique(item); } }
    }
    private static InventoryException Invalid() => new("hub-metadata-contract-unverified");
}
