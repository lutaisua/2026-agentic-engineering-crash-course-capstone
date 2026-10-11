using System.Globalization;
using System.Text;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

internal sealed record StoreHubCsvTable(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows);

// Private per-day totals stay in memory; only counts and equality results are recorded.
internal sealed record StoreHubCsvSummary(int Rows, int ProductMismatches, int UnparsedDates, int InvalidMetrics,
    IReadOnlyDictionary<DateOnly, decimal> Totals, IReadOnlyDictionary<DateOnly, int> RowsPerDay, IReadOnlyList<string> DateFormats)
{
    public override string ToString() => "[protected report totals]";
}

internal static class StoreHubCsv
{
    internal const int MaximumBytes = 32 * 1024 * 1024;
    private const int MaximumRows = 1_000_000;

    internal static string? Decode(byte[] bytes)
    {
        try
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { return new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2); }
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException) { return null; }
    }

    // RFC 4180 quoting; a stray quote, unterminated field, ragged row or duplicate header makes the table unverified.
    internal static StoreHubCsvTable? Parse(string text)
    {
        List<string[]> rows = [];
        List<string> fields = [];
        StringBuilder field = new();
        int state = 0; // 0 field start, 1 unquoted, 2 quoted, 3 after closing quote
        void EndField() { fields.Add(field.ToString()); field.Clear(); state = 0; }
        bool EndRow()
        {
            EndField();
            if (!(fields.Count == 1 && fields[0].Length == 0)) { rows.Add([.. fields]); }
            fields.Clear();
            return rows.Count <= MaximumRows + 1;
        }
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (state == 2)
            {
                if (current != '"') { field.Append(current); }
                else if (index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index++; }
                else { state = 3; }
                continue;
            }
            if (current == ',') { EndField(); continue; }
            if (current is '\r' or '\n')
            {
                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n') { index++; }
                if (!EndRow()) { return null; }
                continue;
            }
            if (state == 3 || (current == '"' && state == 1)) { return null; }
            if (current == '"') { state = 2; continue; }
            field.Append(current);
            state = 1;
        }
        if (state == 2) { return null; }
        if (state != 0 || fields.Count > 0) { if (!EndRow()) { return null; } }
        if (rows.Count == 0) { return null; }
        string[] headers = rows[0].Select(header => header.Trim()).ToArray();
        if (headers.Any(header => header.Length == 0) || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length ||
            rows.Skip(1).Any(row => row.Length != headers.Length)) { return null; }
        return new(headers, rows.Skip(1).ToArray());
    }

    internal static int Column(StoreHubCsvTable table, string name)
    {
        for (int index = 0; index < table.Headers.Count; index++)
        { if (string.Equals(table.Headers[index], name, StringComparison.OrdinalIgnoreCase)) { return index; } }
        return -1;
    }

    internal static StoreHubCsvSummary Summarize(StoreHubCsvTable table, int product, int date, int metric, string productId)
    {
        Dictionary<DateOnly, decimal> totals = [];
        Dictionary<DateOnly, int> rowsPerDay = [];
        SortedSet<string> formats = new(StringComparer.Ordinal);
        int mismatches = 0, unparsed = 0, invalid = 0;
        foreach (string[] row in table.Rows)
        {
            if (!string.Equals(row[product].Trim(), productId, StringComparison.OrdinalIgnoreCase)) { mismatches++; }
            (DateOnly Day, string Format)? day = ParseDay(row[date]);
            if (day is null) { unparsed++; formats.Add("unrecognized"); continue; }
            formats.Add(day.Value.Format);
            rowsPerDay[day.Value.Day] = rowsPerDay.GetValueOrDefault(day.Value.Day) + 1;
            if (!decimal.TryParse(row[metric].Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal quantity) ||
                decimal.Truncate(quantity) != quantity) { invalid++; continue; }
            totals[day.Value.Day] = totals.GetValueOrDefault(day.Value.Day) + quantity;
        }
        return new(table.Rows.Count, mismatches, unparsed, invalid, totals, rowsPerDay, formats.ToArray());
    }

    // Slash dates are read month-first, as the operator declared for the Partner Center baseline; the format is recorded.
    internal static (DateOnly Day, string Format)? ParseDay(string? value)
    {
        string? text = value?.Trim();
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly iso)) { return (iso, "iso-date"); }
        if (DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset utc) && utc.TimeOfDay == TimeSpan.Zero)
        { return (DateOnly.FromDateTime(utc.UtcDateTime), "utc-midnight"); }
        if (DateTime.TryParseExact(text, ["M/d/yyyy", "M/d/yyyy h:mm:ss tt", "M/d/yyyy H:mm:ss"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime slash) && slash.TimeOfDay == TimeSpan.Zero)
        { return (DateOnly.FromDateTime(slash), "slash-month-first"); }
        return null;
    }
}

internal sealed record StoreHubBaselineIdentity(int RowCount, bool HeadersPresent, bool ProductMatches, int UnparsedDates,
    int InvalidMetrics, IReadOnlyList<string> DateFormats, string Outcome);

internal static class StoreHubBaseline
{
    internal const string FileName = "installs-partner-center-small.csv";

    // Reads the operator's private Partner Center export; returns the identity proof and in-memory totals, never values.
    internal static async Task<(StoreHubBaselineIdentity Identity, StoreHubCsvSummary? Summary)> LoadAsync(string experiment,
        StoreHubQueryPlan plan, IPrivateStorageGuard privacy, CancellationToken cancellationToken)
    {
        string path = Path.Combine(experiment, FileName);
        static StoreHubBaselineIdentity Failed(string outcome) => new(0, false, false, 0, 0, [], outcome);
        if (!File.Exists(path)) { return (Failed("hub-baseline-missing"), null); }
        try { privacy.VerifyFile(path); }
        catch (Exception error) when (error is Console.AnalyticsET.Cli.InputException or IOException or UnauthorizedAccessException)
        { return (Failed("hub-baseline-unsafe"), null); }
        if (new FileInfo(path).Length > StoreHubCsv.MaximumBytes) { return (Failed("hub-baseline-too-large"), null); }
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        string? text = StoreHubCsv.Decode(bytes);
        StoreHubCsvTable? table = text is null ? null : StoreHubCsv.Parse(text);
        if (table is null) { return (Failed("hub-baseline-unreadable"), null); }
        int product = StoreHubCsv.Column(table, "ProductId"), date = StoreHubCsv.Column(table, "Date"), metric = StoreHubCsv.Column(table, plan.Metric);
        if (product < 0 || date < 0 || metric < 0) { return (Failed("hub-baseline-columns-missing") with { RowCount = table.Rows.Count }, null); }
        StoreHubCsvSummary summary = StoreHubCsv.Summarize(table, product, date, metric, plan.ProductId);
        bool matches = summary.Rows > 0 && summary.ProductMismatches == 0;
        string outcome = summary.Rows == 0 ? "hub-baseline-empty" : matches ? "baseline-identity-matches" : "hub-baseline-product-mismatch";
        return (new(summary.Rows, true, matches, summary.UnparsedDates, summary.InvalidMetrics, summary.DateFormats, outcome), matches ? summary : null);
    }
}
