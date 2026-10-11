using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

internal sealed record StoreHubCreationJournal(int SchemaVersion, string Dataset, string Kind, string BindingFingerprint,
    string SampleFingerprint, string PlanFingerprint, string Marker, string RequestJson, string RequestSha256,
    string? QueryId, string State, string? ResourceId, int? HttpStatus, string Outcome, int ExitCode, DateTimeOffset RecordedAt,
    string? ProofBasis = null)
{
    public override string ToString() => "[protected reporting creation journal]";
    internal object PublicProjection() => new { Dataset, Kind, State, HttpStatus, Outcome, ExitCode };
}

internal sealed record StoreHubCreationEnvelope(int SchemaVersion, StoreHubCreationJournal Journal, string JournalSha256);

// Separate from BoundedJsonHttp: a creation request must never be replayed on an HTTP/transport failure.
internal sealed class StoreHubCreation(HttpMessageHandler handler, IInventoryTokens tokens, IPrivateStorageGuard privacy,
    IAtomicFileWriter writer, TimeProvider clock) : IDisposable
{
    private readonly HttpClient client = CreateClient(handler);
    internal static HttpClientHandler CreateHandler() => new() { AllowAutoRedirect = false, UseCookies = false };
    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        HttpMessageHandler? current = handler;
        while (current is not null)
        {
            if ((current is HttpClientHandler classic && (classic.AllowAutoRedirect || classic.UseCookies)) ||
                (current is SocketsHttpHandler sockets && (sockets.AllowAutoRedirect || sockets.UseCookies)))
            { throw Failure("hub-creation-unsafe-transport"); }
            current = (current as DelegatingHandler)?.InnerHandler;
        }
        return new(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(60) };
    }
    public void Dispose() => client.Dispose();

    internal async Task<StoreHubCreationJournal> CreateAsync(string experiment, string kind, StoreHubQueryPlan plan,
        StoreHubScopeProof proof, AuthorizationContext context, CancellationToken cancellationToken)
    {
        plan.RequireCreationProof(proof);
        ValidatePlan(plan, context, clock);
        if (kind is not ("query" or "report") || !Guid.TryParseExact(Path.GetFileName(Path.GetFullPath(experiment)), "D", out Guid experimentId) ||
            experimentId == Guid.Empty) { throw Failure("hub-creation-prerequisite-failed"); }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            privacy.PrepareDirectory(experiment);
            string lockPath = Path.Combine(experiment, ".experiment.lock");
            if (File.Exists(lockPath)) { privacy.VerifyFile(lockPath); }
            using FileStream experimentLock = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            privacy.VerifyFile(lockPath);
            string path = JournalPath(experiment, plan.Dataset, kind);
            StoreHubCreationJournal? existing = await ReadAsync(path, context, plan.SampleFingerprint, cancellationToken).ConfigureAwait(false);
            string planFingerprint = Hash(JsonSerializer.Serialize(plan, StoreHubInput.JsonOptions));
            if (existing is not null)
            {
                if (existing.PlanFingerprint != planFingerprint) { throw Failure("hub-creation-scope-changed"); }
                // An intent alone may have outlived its process/response. Only reconciliation may resolve it.
                return existing.State == "recorded" ? existing : existing with { State = "creation-uncertain", Outcome = "hub-creation-uncertain", ExitCode = 2 };
            }
            foreach (string dataset in new[] { "Installs", "AddOnAcquisitions" })
            {
                foreach (string operation in new[] { "query", "report" })
                {
                    StoreHubCreationJournal? previous = await ReadAsync(JournalPath(experiment, dataset, operation), context,
                        plan.SampleFingerprint, cancellationToken).ConfigureAwait(false);
                    if (previous is not null && previous.State != "recorded") { throw Failure("hub-prior-creation-uncertain"); }
                }
            }
            string? queryId = null;
            if (kind == "report")
            {
                StoreHubCreationJournal query = await ReadAsync(JournalPath(experiment, plan.Dataset, "query"), context,
                    plan.SampleFingerprint, cancellationToken).ConfigureAwait(false) ?? throw Failure("hub-query-prerequisite-missing");
                if (query.State != "recorded" || query.PlanFingerprint != planFingerprint) { throw Failure("hub-query-prerequisite-missing"); }
                queryId = query.ResourceId;
            }
            // Authentication/generation validation occurs before durable creation intent; no reporting POST has been sent yet.
            AccessToken token = await tokens.GetAsync(context, false, cancellationToken).ConfigureAwait(false);
            if (token.ExpiresAt <= clock.GetUtcNow().AddSeconds(60)) { throw Failure("hub-creation-token-expiring"); }
            string marker = $"AnalyticsET-{experimentId:N}-{plan.Dataset}-{kind}";
            string body = kind == "query" ? JsonSerializer.Serialize(new { name = marker, query = plan.Query }) :
                JsonSerializer.Serialize(new
                {
                    reportName = marker,
                    queryId,
                    executeNow = true,
                    format = "csv",
                    queryStartTime = plan.QueryStartTime,
                    queryEndTime = plan.QueryEndTime
                });
            StoreHubCreationJournal intent = new(1, plan.Dataset, kind, context.StorageKey, plan.SampleFingerprint,
                planFingerprint, marker, body, Hash(body), queryId, "creation-intent", null, null, "intent-recorded", 2, clock.GetUtcNow(), proof.Basis);
            await WriteAsync(path, intent, cancellationToken).ConfigureAwait(false);
            return await SendOnceAsync(path, intent, plan, token, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { throw Failure("hub-creation-prerequisite-or-recording-failed"); }
    }

    private async Task<StoreHubCreationJournal> SendOnceAsync(string path, StoreHubCreationJournal intent, StoreHubQueryPlan plan,
        AccessToken token, CancellationToken cancellationToken)
    {
        int? status = null;
        string outcome = "hub-creation-uncertain";
        int exit = 2;
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            bounded.Token.ThrowIfCancellationRequested();
            Uri endpoint = new(StoreHubQueryPlan.Base + (intent.Kind == "query" ? "ScheduledQueries" : "ScheduledReport") + "?scope=consumer");
            using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
            { Content = new StringContent(intent.RequestJson, Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            if (status != 200 || (response.RequestMessage?.RequestUri is Uri actual && actual != endpoint))
            { throw Failure("hub-creation-response-unverified"); }
            if (response.Content.Headers.ContentLength > 4194304) { throw Failure("hub-creation-response-unverified"); }
            await using Stream stream = await response.Content.ReadAsStreamAsync(bounded.Token).ConfigureAwait(false);
            using MemoryStream contents = new();
            byte[] buffer = new byte[8192];
            int length;
            while ((length = await stream.ReadAsync(buffer, bounded.Token).ConfigureAwait(false)) != 0)
            {
                if (contents.Length + length > 4194304) { throw Failure("hub-creation-response-unverified"); }
                contents.Write(buffer, 0, length);
            }
            using JsonDocument document = JsonDocument.Parse(contents.ToArray());
            (string id, bool datesEchoed) = ValidateResponse(document.RootElement, intent, plan);
            StoreHubCreationJournal completed = intent with
            {
                State = "recorded",
                ResourceId = id,
                HttpStatus = status,
                // An omitted date echo cannot confirm scope; the downloaded report's dates must prove it later.
                Outcome = datesEchoed ? "creation-recorded" : "creation-recorded-dates-not-echoed",
                ExitCode = 0,
                RecordedAt = clock.GetUtcNow()
            };
            await WriteAsync(path, completed, bounded.Token).ConfigureAwait(false);
            return completed;
        }
        catch (OperationCanceledException) { exit = cancellationToken.IsCancellationRequested ? 130 : 2; outcome = "hub-creation-uncertain"; }
        catch (Exception error) when (error is InventoryException or HttpRequestException or IOException or JsonException or UnauthorizedAccessException or
            InvalidOperationException or Console.AnalyticsET.Cli.InputException)
        { /* The service may have created a resource; do not replay or expose the response/exception. */ }
        StoreHubCreationJournal uncertain = intent with
        {
            State = "creation-uncertain",
            HttpStatus = status,
            Outcome = outcome,
            ExitCode = exit,
            RecordedAt = clock.GetUtcNow()
        };
        using CancellationTokenSource recording = new(TimeSpan.FromSeconds(10));
        try { await WriteAsync(path, uncertain, recording.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException or Console.AnalyticsET.Cli.InputException)
        { /* The durable intent still blocks subsequent creation even if this second recording fails. */ }
        return uncertain;
    }

    private async Task<StoreHubCreationJournal?> ReadAsync(string path, AuthorizationContext context, string sample, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) { return null; }
        privacy.VerifyFile(path);
        if (new FileInfo(path).Length > 16384) { throw Failure("hub-creation-journal-unverified"); }
        string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(json);
        if (!StoreHubQueryPlan.Unique(document.RootElement)) { throw Failure("hub-creation-journal-unverified"); }
        StoreHubCreationEnvelope envelope = JsonSerializer.Deserialize<StoreHubCreationEnvelope>(json, StoreHubInput.JsonOptions) ??
            throw Failure("hub-creation-journal-unverified");
        StoreHubCreationJournal journal = envelope.Journal;
        if (envelope.SchemaVersion != 1 || journal is null || envelope.JournalSha256 != Hash(JsonSerializer.Serialize(journal, StoreHubInput.JsonOptions)))
        { throw Failure("hub-creation-journal-unverified"); }
        if (journal.SchemaVersion != 1 || journal.BindingFingerprint != context.StorageKey || journal.SampleFingerprint != sample ||
            journal.Dataset is not ("Installs" or "AddOnAcquisitions") || journal.Kind is not ("query" or "report") ||
            Path.GetFileName(path) != Path.GetFileName(JournalPath("", journal.Dataset, journal.Kind)) ||
            journal.State is not ("creation-intent" or "creation-uncertain" or "recorded") ||
            journal.ProofBasis is not (null or "preview" or "baseline-identity") ||
            string.IsNullOrWhiteSpace(journal.RequestJson) || journal.RequestSha256 != Hash(journal.RequestJson) ||
            !IsHash(journal.PlanFingerprint) || string.IsNullOrWhiteSpace(journal.Marker) ||
            (journal.Kind == "report" && !IsId(journal.QueryId)) || (journal.Kind == "query" && journal.QueryId is not null) ||
            (journal.State == "recorded" ? !IsId(journal.ResourceId) || journal.ExitCode != 0 : journal.ResourceId is not null))
        { throw Failure("hub-creation-journal-unverified"); }
        return journal;
    }

    private async Task WriteAsync(string path, StoreHubCreationJournal journal, CancellationToken cancellationToken)
    {
        if (File.Exists(path)) { privacy.VerifyFile(path); }
        StoreHubCreationEnvelope envelope = new(1, journal, Hash(JsonSerializer.Serialize(journal, StoreHubInput.JsonOptions)));
        await StoreHubDiscovery.CheckedWriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(envelope, StoreHubInput.JsonOptions),
            privacy, writer, cancellationToken).ConfigureAwait(false);
    }

    private static (string Id, bool DatesEchoed) ValidateResponse(JsonElement root, StoreHubCreationJournal intent, StoreHubQueryPlan plan)
    {
        if (root.ValueKind != JsonValueKind.Object || !StoreHubQueryPlan.Unique(root)) { throw Failure("hub-creation-response-unverified"); }
        JsonElement value = StoreHubQueryPlan.Field(root, "value"), status = StoreHubQueryPlan.Field(root, "statusCode"), count = StoreHubQueryPlan.Field(root, "totalCount");
        if (status.ValueKind != JsonValueKind.Number ||
            !status.TryGetInt32(out int code) || code != 200 || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out int total) || total != 1 ||
            value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 1 || value[0].ValueKind != JsonValueKind.Object)
        { throw Failure("hub-creation-response-unverified"); }
        JsonElement resource = value[0];
        string? Text(string name) { JsonElement field = StoreHubQueryPlan.Field(resource, name); return field.ValueKind == JsonValueKind.String ? field.GetString() : null; }
        string? id = Text(intent.Kind == "query" ? "queryId" : "reportId");
        if (!IsId(id) || !plan.CompatibleQuery(resource) || Text(intent.Kind == "query" ? "name" : "reportName") != intent.Marker)
        { throw Failure("hub-creation-response-unverified"); }
        bool datesEchoed = true;
        if (intent.Kind == "report")
        {
            JsonElement executeNow = StoreHubQueryPlan.Field(resource, "executeNow");
            bool? start = DateEcho(StoreHubQueryPlan.Field(resource, "queryStartTime"), plan.QueryStartTime);
            bool? end = DateEcho(StoreHubQueryPlan.Field(resource, "queryEndTime"), plan.QueryEndTime);
            if (Text("queryId") != intent.QueryId || executeNow.ValueKind != JsonValueKind.True || Text("format") != "csv" ||
                start == false || end == false)
            { throw Failure("hub-creation-response-unverified"); }
            datesEchoed = start == true && end == true;
            foreach (string name in new[] { "callbackUrl", "callbackMethod", "nextExecutionStartTime" })
            { if (StoreHubQueryPlan.Field(resource, name).ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) { throw Failure("hub-creation-response-unverified"); } }
            foreach ((string name, int expected) in new[] { ("recurrenceInterval", -1), ("recurrenceCount", 1), ("totalRecurrenceCount", 1) })
            {
                JsonElement field = StoreHubQueryPlan.Field(resource, name);
                if (field.ValueKind != JsonValueKind.Undefined && (field.ValueKind != JsonValueKind.Number || !field.TryGetInt32(out int observed) || observed != expected))
                { throw Failure("hub-creation-response-unverified"); }
            }
        }
        return (Guid.Parse(id!).ToString("D"), datesEchoed);
    }

    // Microsoft's instant-report example echoes null dates. Null or absent is "not echoed"; any other boundary is a mismatch.
    private static bool? DateEcho(JsonElement field, string sent)
    {
        if (field.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) { return null; }
        return field.ValueKind == JsonValueKind.String && MidnightDay(field.GetString()) is DateOnly echoed && MidnightDay(sent) == echoed;
    }
    private static DateOnly? MidnightDay(string? text)
    {
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)) { return day; }
        return DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset value) && value.TimeOfDay == TimeSpan.Zero ?
            DateOnly.FromDateTime(value.UtcDateTime) : null;
    }

    private static void ValidatePlan(StoreHubQueryPlan plan, AuthorizationContext context, TimeProvider clock)
    {
        plan.Window.Validate(clock);
        if (context.ResourceKey != "store-analytics" || context.Program != "microsoft-store" || context.Flow != "client-credentials" ||
            context.Resource != "https://manage.devcenter.microsoft.com" || context.CredentialGeneration == "unresolved" ||
            !IsId(context.TenantId) || !IsId(context.ClientId) || plan.BindingFingerprint != context.StorageKey ||
            !IsHash(plan.SampleFingerprint) || !IsHash(plan.MetadataSha256) || !StoreDiagnosticInput.IsStoreId(plan.ProductId) ||
            string.Equals(plan.ProductId, "all", StringComparison.OrdinalIgnoreCase)) { throw Failure("hub-invalid-binding"); }
        if (plan.DateWireFormat is not ("date-only" or "utc-timestamp") || (plan.DateWireFormat == "utc-timestamp" && plan.Dataset != "Installs"))
        { throw Failure("hub-date-wire-format-invalid"); }
        string[] columns;
        string where = " WHERE ProductId IN ('" + plan.ProductId + "')";
        if (plan.Dataset == "Installs" && plan.ParentId is null && plan.Metric == "SuccessfulInstallCount") { columns = ["ProductId", "Date", plan.Metric]; }
        else if (plan.Dataset == "AddOnAcquisitions" && plan.ParentId is not null && StoreDiagnosticInput.IsStoreId(plan.ParentId) &&
            !string.Equals(plan.ParentId, "all", StringComparison.OrdinalIgnoreCase) && plan.ParentId != plan.ProductId && plan.Metric == "PurchaseQuantity")
        { columns = ["ProductId", "ParentProductId", plan.Metric]; where += " AND ParentProductId = '" + plan.ParentId + "'"; }
        else { throw Failure("hub-creation-scope-unverified"); }
        string expected = "SELECT " + string.Join(", ", columns) + " FROM " + plan.Dataset + where + " AGGREGATED Daily";
        if (!columns.SequenceEqual(plan.Columns) || plan.Query != expected) { throw Failure("hub-creation-scope-unverified"); }
    }

    internal static string JournalPath(string experiment, string dataset, string kind) => Path.Combine(experiment, dataset + "-" + kind + "-creation.json");
    private static bool IsId(string? id) => Guid.TryParseExact(id, "D", out Guid value) && value != Guid.Empty;
    private static bool IsHash(string? value) => value?.Length == 64 && value.All(char.IsAsciiHexDigit);
    private static string Hash(string value) => StoreDiagnosticPacketStore.Hash(Encoding.UTF8.GetBytes(value));
    private static InventoryException Failure(string category) => new(category);
}
