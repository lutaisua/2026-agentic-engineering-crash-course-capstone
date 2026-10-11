using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;

namespace AnalyticsET.Tests;

// One budget per stage across authentication, creation, polling and download (design §6: 100 attempts).
internal sealed class StoreHubRequestBudget(int maximum = 100)
{
    private int used;
    internal int Used => Volatile.Read(ref used);
    internal void Take() { if (Interlocked.Increment(ref used) > maximum) { throw new InventoryException("hub-request-budget-exhausted"); } }
}

internal sealed class StoreHubReportTransport : DelegatingHandler
{
    private readonly Uri tokenEndpoint;
    private readonly Uri queryEndpoint = new(StoreHubQueryPlan.Base + "ScheduledQueries?scope=consumer");
    private readonly Uri reportEndpoint = new(StoreHubQueryPlan.Base + "ScheduledReport?scope=consumer");
    private readonly StoreHubRequestBudget budget;
    private readonly TimeProvider clock;
    private string? reportId;
    internal List<DiagnosticAttempt> Attempts { get; } = [];

    internal StoreHubReportTransport(HttpMessageHandler inner, AuthorizationContext context, StoreHubRequestBudget budget, TimeProvider clock) : base(inner)
    {
        if (context.ResourceKey != "store-analytics" || context.Program != "microsoft-store" || context.Flow != "client-credentials" ||
            context.Resource != "https://manage.devcenter.microsoft.com" || !Guid.TryParseExact(context.TenantId, "D", out Guid tenant) || tenant == Guid.Empty)
        { throw new InventoryException("hub-invalid-binding"); }
        tokenEndpoint = new($"https://login.microsoftonline.com/{tenant:D}/oauth2/token");
        this.budget = budget;
        this.clock = clock;
    }

    // Execution routes open only for the report ID that creation recorded.
    internal void PinReport(string id)
    {
        if (!Guid.TryParseExact(id, "D", out Guid value) || value == Guid.Empty || reportId is not null) { throw new InventoryException("hub-invalid-binding"); }
        reportId = value.ToString("D");
    }
    internal Uri CompletedLookup => new(StoreHubQueryPlan.Base + "ScheduledReport/execution/" + (reportId ?? throw new InventoryException("hub-invalid-binding")) + "?scope=consumer");
    internal Uri PendingLookup => new(StoreHubQueryPlan.Base + "ScheduledReport/execution/" + (reportId ?? throw new InventoryException("hub-invalid-binding")) +
        "?executionStatus=Pending&scope=consumer");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri? destination = request.RequestUri;
        bool post = request.Method == HttpMethod.Post;
        string? operation = destination is null ? null :
            post && destination == tokenEndpoint && request.Headers.Authorization is null &&
                request.Content?.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded" ? "token" :
            post && destination == queryEndpoint && request.Content?.Headers.ContentType?.MediaType == "application/json" ? "query-create" :
            post && destination == reportEndpoint && request.Content?.Headers.ContentType?.MediaType == "application/json" ? "report-create" :
            request.Method == HttpMethod.Get && request.Content is null && reportId is not null && destination == CompletedLookup ? "execution-completed" :
            request.Method == HttpMethod.Get && request.Content is null && reportId is not null && destination == PendingLookup ? "execution-pending" : null;
        if (operation is null) { throw new InventoryException("hub-destination-rejected"); }
        budget.Take();
        int attempt = Attempts.Count(item => item.Operation == operation) + 1;
        DateTimeOffset start = clock.GetUtcNow();
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            // A creation response body is read once by the single-send boundary, so it is never summarized here.
            StoreDiagnosticErrorSummary? error = response.IsSuccessStatusCode || operation.EndsWith("-create", StringComparison.Ordinal) ? null :
                await StoreDiagnosticErrorSummary.ReadAsync(response.Content, cancellationToken).ConfigureAwait(false);
            Attempts.Add(new(operation, "fixed-dates", attempt, start, clock.GetUtcNow(), (int)response.StatusCode, null,
                error, DiagnosticCorrelationCapture.Read(response, error?.BodyCorrelations)));
            return response;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
        {
            Attempts.Add(new(operation, "fixed-dates", attempt, start, clock.GetUtcNow(), response is null ? null : (int)response.StatusCode,
                error is OperationCanceledException ? "transport-cancelled" : "transport-failed", null, []));
            response?.Dispose();
            throw;
        }
    }
}

// Private: the execution ID and observed host stay in protected evidence; the signed link is never part of this record.
internal sealed record StoreHubExecutionObservation(string State, string? ExecutionId, int Polls, int CompletedNotFound,
    int PendingObservations, string? ObservedDownloadHost, string Outcome)
{
    public override string ToString() => "[protected report execution]";
}

internal sealed class StoreHubCompletedExecution(string executionId, Uri link)
{
    internal string ExecutionId { get; } = executionId;
    internal Uri Link { get; } = link;
    public override string ToString() => "[protected signed report link]";
}

internal static class StoreHubExecution
{
    internal const string DownloadHost = "pxanltx.blob.core.windows.net";
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);
    private static readonly string[] ItemFields = ["executionId", "reportId", "recurrenceInterval", "recurrenceCount", "callbackUrl",
        "callbackMethod", "format", "executionStatus", "reportLocation", "reportAccessSecureLink", "reportExpiryTime", "reportGeneratedTime",
        "endTime", "totalRecurrenceCount", "nextExecutionStartTime"];

    // Microsoft documents a Completed/latest default (404 before completion) and executionStatus=Pending.
    // The consumer OpenAPI describes the states pause, started, inactive, completed and failed.
    internal static async Task<(StoreHubExecutionObservation Observation, StoreHubCompletedExecution? Completed)> PollAsync(
        AuthorizedInventoryHttp http, StoreHubReportTransport transport, AuthorizationContext context, string reportId, TimeProvider clock,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
    {
        DateTimeOffset started = clock.GetUtcNow();
        int polls = 0, notFound = 0, pending = 0;
        StoreHubExecutionObservation Observe(string state, string outcome, string? execution = null, string? host = null) =>
            new(state, execution, polls, notFound, pending, host, outcome);
        while (true)
        {
            polls++;
            using (JsonDocument? completed = await http.GetAsync(context, transport.CompletedLookup, transport.CompletedLookup, true,
                cancellationToken).ConfigureAwait(false))
            {
                if (completed is not null)
                {
                    IReadOnlyList<(string Status, string? ExecutionId, JsonElement Item)> items = Read(completed.RootElement, reportId);
                    if (items.Count != 1 || items[0].Status != "completed") { return (Observe("unrecognized", "hub-execution-shape-unverified"), null); }
                    (string status, string? executionId, JsonElement item) = items[0];
                    if (executionId is null) { return (Observe(status, "hub-execution-shape-unverified"), null); }
                    JsonElement linkField = StoreHubQueryPlan.Field(item, "reportAccessSecureLink");
                    if (linkField.ValueKind != JsonValueKind.String || !Uri.TryCreate(linkField.GetString(), UriKind.Absolute, out Uri? link))
                    { return (Observe(status, "hub-download-link-missing", executionId), null); }
                    if (link.Scheme != "https" || !link.IsDefaultPort || link.UserInfo.Length != 0 || link.Fragment.Length != 0 ||
                        !string.Equals(link.Host, DownloadHost, StringComparison.OrdinalIgnoreCase))
                    { return (Observe(status, "hub-download-host-unapproved", executionId, link.Host.Length <= 255 ? link.Host.ToLowerInvariant() : "oversized"), null); }
                    return (Observe(status, "execution-completed", executionId, DownloadHost), new(executionId, link));
                }
            }
            notFound++;
            using (JsonDocument? waiting = await http.GetAsync(context, transport.PendingLookup, transport.PendingLookup, true,
                cancellationToken).ConfigureAwait(false))
            {
                if (waiting is not null)
                {
                    IReadOnlyList<(string Status, string? ExecutionId, JsonElement Item)> items = Read(waiting.RootElement, reportId);
                    if (items.Count == 0) { return (Observe("unrecognized", "hub-execution-shape-unverified"), null); }
                    if (items.Any(item => item.Status == "failed")) { return (Observe("failed", "hub-report-execution-failed"), null); }
                    if (items.Any(item => item.Status is "pause" or "inactive")) { return (Observe("not-progressing", "hub-report-execution-not-progressing"), null); }
                    if (items.Any(item => item.Status is not ("pending" or "started" or "completed")))
                    { return (Observe("unrecognized", "hub-report-execution-state-unrecognized"), null); }
                    pending++;
                }
            }
            // No completed execution yet: never treated as empty data.
            if (clock.GetUtcNow() - started + Interval >= Limit)
            { return (Observe(pending > 0 ? "pending" : "not-observed", pending > 0 ? "hub-report-execution-pending" : "hub-report-execution-not-observed"), null); }
            await delay(Interval, cancellationToken).ConfigureAwait(false);
        }
    }

    // Re-reads the same completed execution once, for an expired link; it never creates another report.
    internal static async Task<StoreHubCompletedExecution?> RereadAsync(AuthorizedInventoryHttp http, StoreHubReportTransport transport,
        AuthorizationContext context, string reportId, string executionId, CancellationToken cancellationToken)
    {
        using JsonDocument? completed = await http.GetAsync(context, transport.CompletedLookup, transport.CompletedLookup, true,
            cancellationToken).ConfigureAwait(false);
        if (completed is null) { return null; }
        IReadOnlyList<(string Status, string? ExecutionId, JsonElement Item)> items = Read(completed.RootElement, reportId);
        if (items.Count != 1 || items[0].Status != "completed" || items[0].ExecutionId != executionId) { return null; }
        JsonElement linkField = StoreHubQueryPlan.Field(items[0].Item, "reportAccessSecureLink");
        return linkField.ValueKind == JsonValueKind.String && Uri.TryCreate(linkField.GetString(), UriKind.Absolute, out Uri? link) &&
            link.Scheme == "https" && link.IsDefaultPort && link.UserInfo.Length == 0 && link.Fragment.Length == 0 &&
            string.Equals(link.Host, DownloadHost, StringComparison.OrdinalIgnoreCase) ? new(executionId, link) : null;
    }

    private static List<(string Status, string? ExecutionId, JsonElement Item)> Read(JsonElement root, string reportId)
    {
        JsonElement value = StoreHubQueryPlan.Field(root, "value"), status = StoreHubQueryPlan.Field(root, "statusCode"), count = StoreHubQueryPlan.Field(root, "totalCount");
        if (root.ValueKind != JsonValueKind.Object || !StoreHubQueryPlan.Unique(root) ||
            root.EnumerateObject().Any(field => field.Name is not ("value" or "totalCount" or "message" or "statusCode")) ||
            status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out int code) || code != 200 ||
            value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 100 ||
            count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out int total) || total != value.GetArrayLength())
        { throw new InventoryException("hub-execution-shape-unverified"); }
        List<(string, string?, JsonElement)> items = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            string? Text(string name) { JsonElement field = StoreHubQueryPlan.Field(item, name); return field.ValueKind == JsonValueKind.String ? field.GetString() : null; }
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Any(field => !ItemFields.Contains(field.Name, StringComparer.OrdinalIgnoreCase)) ||
                !Guid.TryParseExact(Text("reportId"), "D", out Guid observed) || observed.ToString("D") != reportId ||
                (Text("format") is string format && !string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)) ||
                StoreHubQueryPlan.Field(item, "callbackUrl").ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            { throw new InventoryException("hub-execution-shape-unverified"); }
            foreach ((string name, int expected) in new[] { ("recurrenceInterval", -1), ("recurrenceCount", 1), ("totalRecurrenceCount", 1) })
            {
                JsonElement field = StoreHubQueryPlan.Field(item, name);
                if (field.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) &&
                    (field.ValueKind != JsonValueKind.Number || !field.TryGetInt32(out int observedValue) || observedValue != expected))
                { throw new InventoryException("hub-execution-shape-unverified"); }
            }
            string state = Text("executionStatus")?.Trim().ToLowerInvariant() switch
            {
                "completed" => "completed",
                "pending" => "pending",
                "started" or "running" => "started",
                "failed" => "failed",
                "pause" or "paused" => "pause",
                "inactive" => "inactive",
                _ => "unrecognized"
            };
            string? execution = Guid.TryParseExact(Text("executionId"), "D", out Guid id) && id != Guid.Empty ? id.ToString("D") : null;
            items.Add((state, execution, item));
        }
        return items;
    }
}
