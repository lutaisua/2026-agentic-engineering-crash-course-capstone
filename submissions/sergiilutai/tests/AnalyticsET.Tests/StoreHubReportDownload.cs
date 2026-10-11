using System.Globalization;
using System.Net;
using Core.AnalyticsET.Inventory;

namespace AnalyticsET.Tests;

internal sealed record StoreHubDownloadResult(int Attempts, int? HttpStatus, long Bytes, string? Sha256, string Outcome);

// A separate client with no API bearer, cookies or redirects; only the reviewed blob host is reachable.
internal sealed class StoreHubReportDownload : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);
    private readonly HttpClient client;
    private readonly StoreHubRequestBudget budget;
    internal int Attempts { get; private set; }

    internal StoreHubReportDownload(HttpMessageHandler handler, StoreHubRequestBudget budget)
    {
        HttpMessageHandler? current = handler;
        while (current is not null)
        {
            if ((current is HttpClientHandler classic && (classic.AllowAutoRedirect || classic.UseCookies)) ||
                (current is SocketsHttpHandler sockets && (sockets.AllowAutoRedirect || sockets.UseCookies)))
            { throw new InventoryException("hub-download-unsafe-transport"); }
            current = (current as DelegatingHandler)?.InnerHandler;
        }
        client = new(handler, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        this.budget = budget;
    }
    public void Dispose() => client.Dispose();

    // Returns original bytes only for an HTTP 200 body that is not an HTML/JSON/XML error page; "expired" lets the caller reread once.
    internal async Task<(byte[]? Bytes, int? Status, string Outcome)> GetAsync(Uri link, CancellationToken cancellationToken)
    {
        if (link.Scheme != "https" || !link.IsDefaultPort || link.UserInfo.Length != 0 || link.Fragment.Length != 0 ||
            !string.Equals(link.Host, StoreHubExecution.DownloadHost, StringComparison.OrdinalIgnoreCase))
        { return (null, null, "hub-download-host-unapproved"); }
        budget.Take();
        Attempts++;
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(Timeout);
        int? status = null;
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, link);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            if (status is >= 300 and < 400 || (response.RequestMessage?.RequestUri is Uri returned && returned != link))
            { return (null, status, "hub-download-redirect-rejected"); }
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) { return (null, status, "expired"); }
            if (response.StatusCode != HttpStatusCode.OK) { return (null, status, "hub-download-failed"); }
            string? media = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (media is "text/html" or "application/json" or "application/xml" or "text/xml" ||
                (media is not null && (media.EndsWith("+json", StringComparison.Ordinal) || media.EndsWith("+xml", StringComparison.Ordinal))))
            { return (null, status, "hub-download-error-body"); }
            if (response.Content.Headers.ContentLength > StoreHubCsv.MaximumBytes) { return (null, status, "hub-download-too-large"); }
            await using Stream stream = await response.Content.ReadAsStreamAsync(bounded.Token).ConfigureAwait(false);
            using MemoryStream contents = new();
            byte[] buffer = new byte[81920];
            int length;
            while ((length = await stream.ReadAsync(buffer, bounded.Token).ConfigureAwait(false)) != 0)
            {
                if (contents.Length + length > StoreHubCsv.MaximumBytes) { return (null, status, "hub-download-too-large"); }
                contents.Write(buffer, 0, length);
            }
            byte[] bytes = contents.ToArray();
            string? text = StoreHubCsv.Decode(bytes);
            string start = text?.TrimStart() ?? "";
            if (start.StartsWith('<') || start.StartsWith('{') || start.StartsWith('[')) { return (null, status, "hub-download-error-body"); }
            return (bytes, status, "downloaded");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return (null, status, "hub-download-timeout"); }
        catch (Exception error) when (error is HttpRequestException or IOException) { return (null, status, "hub-download-failed"); }
    }
}

internal sealed record StoreHubReportDay(int DayOffset, bool ReportRows, bool BaselineRows, bool TotalsEqual);

internal sealed record StoreHubReportAnalysis(string Validation, int RowCount, bool HeadersPresent, bool? ProductMatches,
    int RowsBeforeStart, int RowsInWindow, int RowsOnEndDay, int RowsAfterEndDay, int UnparsedDates, int InvalidMetrics,
    IReadOnlyList<string> DateFormats, string BoundaryState, IReadOnlyList<StoreHubReportDay> Days, int MatchingDays, string Outcome, int ExitCode)
{
    // Days run from the start boundary through the end boundary day, so rows on the end day are compared, not discarded.
    internal static StoreHubReportAnalysis Analyze(byte[] bytes, StoreHubQueryPlan plan, StoreHubCsvSummary baseline)
    {
        string? text = StoreHubCsv.Decode(bytes);
        StoreHubCsvTable? table = text is null || text.Trim().Length == 0 ? null : StoreHubCsv.Parse(text);
        if (table is null) { return Unverified("malformed", 0, false, "hub-report-csv-unverified"); }
        int product = StoreHubCsv.Column(table, "ProductId"), date = StoreHubCsv.Column(table, "Date"), metric = StoreHubCsv.Column(table, plan.Metric);
        if (product < 0 || date < 0 || metric < 0) { return Unverified("headers-missing", table.Rows.Count, false, "hub-report-schema-unverified"); }
        StoreHubCsvSummary summary = StoreHubCsv.Summarize(table, product, date, metric, plan.ProductId);
        DateOnly start = DateOnly.ParseExact(plan.Window.StartUtc[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateOnly end = DateOnly.ParseExact(plan.Window.EndUtc[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        int before = summary.RowsPerDay.Where(day => day.Key < start).Sum(day => day.Value);
        int inside = summary.RowsPerDay.Where(day => day.Key >= start && day.Key < end).Sum(day => day.Value);
        int onEnd = summary.RowsPerDay.GetValueOrDefault(end);
        int after = summary.RowsPerDay.Where(day => day.Key > end).Sum(day => day.Value);
        List<StoreHubReportDay> days = [];
        for (DateOnly day = start; day <= end; day = day.AddDays(1))
        {
            days.Add(new(day.DayNumber - start.DayNumber, summary.RowsPerDay.ContainsKey(day), baseline.RowsPerDay.ContainsKey(day),
                summary.Totals.GetValueOrDefault(day) == baseline.Totals.GetValueOrDefault(day)));
        }
        string boundary = summary.Rows == 0 ? "empty" : onEnd > 0 ? "end-date-rows-observed" : "end-date-rows-absent";
        (string outcome, int exit) = summary.ProductMismatches > 0 ? ("hub-report-product-mismatch", 2) :
            before > 0 || after > 0 ? ("hub-report-date-scope-outside-window", 2) :
            summary.UnparsedDates > 0 || summary.InvalidMetrics > 0 ? ("hub-report-values-unverified", 2) :
            summary.Rows == 0 ? ("report-recorded-empty", 0) : ("report-recorded", 0);
        return new(exit == 0 ? "validated" : "scope-or-values-unverified", summary.Rows, true, summary.Rows == 0 ? null : summary.ProductMismatches == 0,
            before, inside, onEnd, after, summary.UnparsedDates, summary.InvalidMetrics, summary.DateFormats, boundary, days,
            days.Count(day => day.TotalsEqual), outcome, exit);
    }

    private static StoreHubReportAnalysis Unverified(string validation, int rows, bool headers, string outcome) =>
        new(validation, rows, headers, null, 0, 0, 0, 0, 0, 0, [], "unverified", [], 0, outcome, 2);
}
