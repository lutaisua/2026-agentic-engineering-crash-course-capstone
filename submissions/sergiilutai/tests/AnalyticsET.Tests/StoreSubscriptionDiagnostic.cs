using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

internal sealed record DiagnosticExecution(StoreDiagnosticPacket Packet, string Directory);

internal static class StoreSubscriptionDiagnostic
{
    internal static async Task<DiagnosticExecution> RunAsync(string configurationPath, string inputPath, string mappingPath, string outputRoot,
        IPrivateStorageGuard privacy, IApplicationCredentialResolver credentials, HttpMessageHandler inner,
        TimeProvider clock, IAtomicFileWriter writer, CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null, TimeSpan? deadline = null, int maximumRequests = 24)
    {
        DiagnosticPreflight prepared = await StoreDiagnosticPreflight.LoadAsync(configurationPath, inputPath, mappingPath, outputRoot,
            privacy, clock, cancellationToken).ConfigureAwait(false);
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(deadline ?? TimeSpan.FromMinutes(3));
        DateTimeOffset started = clock.GetUtcNow();
        AuthorizationContext context = prepared.Context;
        var plan = Plan(prepared.Input);
        using StoreDiagnosticTransport transport = new(inner, new($"https://login.microsoftonline.com/{context.TenantId}/oauth2/token"),
            plan.ToDictionary(item => item.Uri.AbsoluteUri, item => item.Route, StringComparer.Ordinal), clock, maximumRequests);
        using HttpClient client = new(transport) { Timeout = TimeSpan.FromSeconds(60) };
        BoundedJsonHttp http = new(client, clock, delay);
        SingleDiagnosticRecoveryTokens tokens = new(new ClientCredentialTokens(http, credentials, clock));
        AuthorizedInventoryHttp authorized = new(http, tokens);
        List<DiagnosticProbe> probes = [];
        string? stop = null;
        bool cancelled = false;
        try
        {
            ApplicationCredential credential = await credentials.ResolveAsync(context, bounded.Token).ConfigureAwait(false);
            context = context with { CredentialGeneration = credential.Generation };
            foreach (var item in plan)
            {
                if (stop is not null) { probes.Add(new(item.Route.Operation, item.Route.Query, "not-attempted", null, new("not-read"))); continue; }
                string outcome = "observed";
                DiagnosticMetadata metadata = new("not-read");
                try
                {
                    using JsonDocument? document = await authorized.GetAsync(context, item.Uri, StoreInventoryReader.BaseUri, false, bounded.Token).ConfigureAwait(false);
                    metadata = Observe(document!.RootElement, item.Route.Operation, prepared.Input);
                }
                catch (InventoryException error)
                {
                    outcome = SafeFailure(error.Category);
                    if (outcome is "unauthorized" or "forbidden-or-association-missing" or "credential-generation-changed" or
                        "diagnostic-auth-recovery-exhausted" or "diagnostic-request-budget-exhausted" or "diagnostic-destination-rejected" or
                        "redirect-rejected" or "rejected-request-destination" or "invalid-token-response" or "invalid-token-binding" or
                        "retry-budget-exhausted" or "retry-after-exceeds-budget" or "http-timeout") { stop = outcome; }
                }
                catch (OperationCanceledException)
                {
                    cancelled = cancellationToken.IsCancellationRequested;
                    outcome = cancelled ? "operator-cancelled" : "diagnostic-deadline-exhausted";
                    stop = outcome;
                }
                catch (Exception error) when (error is HttpRequestException or IOException or InputException or JsonException)
                { outcome = "diagnostic-operation-failed"; stop = outcome; }
                int? status = transport.Observations.LastOrDefault(attempt => attempt.Operation == item.Route.Operation && attempt.Query == item.Route.Query)?.Status;
                probes.Add(new(item.Route.Operation, item.Route.Query, outcome, status, metadata));
            }
        }
        catch (Exception error) when (error is InputException or InventoryException or OperationCanceledException or IOException or HttpRequestException)
        {
            cancelled = error is OperationCanceledException && cancellationToken.IsCancellationRequested;
            stop = cancelled ? "operator-cancelled" : "diagnostic-authorization-failed";
        }
        foreach (var item in plan.Skip(probes.Count)) { probes.Add(new(item.Route.Operation, item.Route.Query, "not-attempted", null, new("not-read"))); }
        int code = cancelled ? 130 : probes.All(probe => probe.Status is not null) && stop is null ? 0 : 2;
        StoreDiagnosticPacket packet = new(1, Guid.NewGuid().ToString("D"), started, clock.GetUtcNow(),
            StoreDiagnosticPacketStore.Hash(await File.ReadAllBytesAsync(typeof(StoreSubscriptionDiagnostic).Assembly.Location, CancellationToken.None).ConfigureAwait(false)),
            context.StorageKey, prepared.Input, transport.Observations.ToArray(), probes, Interpret(probes, transport.Observations), code);
        // A bounded fresh token permits an atomic partial packet after operator cancellation.
        using CancellationTokenSource recording = new(TimeSpan.FromSeconds(10));
        string directory = await new StoreDiagnosticPacketStore(prepared.OutputRoot, privacy, writer).SaveAsync(packet, recording.Token).ConfigureAwait(false);
        return new(packet, directory);
    }

    internal static IReadOnlyList<(Uri Uri, DiagnosticRoute Route)> Plan(StoreDiagnosticInput sample)
    {
        string parent = Uri.EscapeDataString(sample.ParentApplicationStoreId), child = Uri.EscapeDataString(sample.SubscriptionStoreId);
        return [
            (new(StoreInventoryReader.BaseUri, "applications/" + parent), new("parent-control", "none")),
            (new(StoreInventoryReader.BaseUri, "inappproducts/" + child), new("addon-detail", "none")),
            (new(StoreInventoryReader.BaseUri, "applications/" + parent + "/listinappproducts?top=10"), new("parent-collection", "top=10")),
            (new(StoreInventoryReader.BaseUri, "applications/" + parent + "/listinappproducts"), new("parent-collection", "none")),
            (new(StoreInventoryReader.BaseUri, "inappproducts?top=10"), new("account-collection", "top=10")),
            (new(StoreInventoryReader.BaseUri, "inappproducts"), new("account-collection", "none"))
        ];
    }

    private static DiagnosticMetadata Observe(JsonElement root, string operation, StoreDiagnosticInput sample)
    {
        if (root.ValueKind != JsonValueKind.Object) { return new("malformed"); }
        if (operation is "parent-collection" or "account-collection")
        {
            return root.TryGetProperty("value", out JsonElement rows) && rows.ValueKind == JsonValueKind.Array &&
                root.TryGetProperty("totalCount", out JsonElement count) && count.TryGetInt32(out int total) && total >= 0 ?
                new("collection-observed", TotalCount: total) : new("malformed");
        }
        string? id = Id(StoreDiagnosticErrorSummary.Property(root, "id"));
        string expected = operation == "parent-control" ? sample.ParentApplicationStoreId : sample.SubscriptionStoreId;
        if (id is null) { return new("malformed"); }
        var publication = StorePublication.Classify(root, id, operation == "addon-detail");
        if (operation == "parent-control") { return new(id == expected ? "matched" : "mismatch", id == expected, Publication: publication.State.ToString(), ObservedStoreId: id); }
        JsonElement applications = StoreDiagnosticErrorSummary.Property(root, "applications");
        JsonElement parents = StoreDiagnosticErrorSummary.Property(applications, "value");
        JsonElement parentCount = StoreDiagnosticErrorSummary.Property(applications, "totalCount");
        string? parent = parents.ValueKind == JsonValueKind.Array && parents.GetArrayLength() == 1 ?
            Id(StoreDiagnosticErrorSummary.Property(parents[0], "id")) : null;
        string? location = parents.ValueKind == JsonValueKind.Array && parents.GetArrayLength() == 1 ?
            StoreDiagnosticErrorSummary.Property(parents[0], "resourceLocation").ValueKind == JsonValueKind.String ?
                StoreDiagnosticErrorSummary.Property(parents[0], "resourceLocation").GetString() : null : null;
        bool? parentMatches = parent is null || parentCount.ValueKind != JsonValueKind.Number || !parentCount.TryGetInt32(out int number) || number != 1 ?
            null : parent == sample.ParentApplicationStoreId && location == "applications/" + sample.ParentApplicationStoreId;
        DiagnosticField wireType = StoreDiagnosticErrorSummary.Allowed(StoreDiagnosticErrorSummary.Property(root, "productType"), StoreDiagnosticErrorSummary.Types);
        bool? compatible = wireType.Value is null ? null : StoreInventoryReader.SupportsProductType(wireType.Value);
        string state = id != expected || parentMatches == false ? "mismatch" : parentMatches is null || wireType.State != "present" ? "malformed" : "matched";
        return new(state, id == expected, parentMatches, wireType, compatible, publication.State.ToString(), id, parent);
    }

    private static string? Id(JsonElement field) => field.ValueKind == JsonValueKind.String && StoreDiagnosticInput.IsStoreId(field.GetString()!) ? field.GetString() : null;

    internal static IReadOnlyList<string> Interpret(IReadOnlyList<DiagnosticProbe> probes, IReadOnlyList<DiagnosticAttempt> attempts)
    {
        List<string> found = [];
        DiagnosticProbe? parent = probes.FirstOrDefault(probe => probe.Operation == "parent-control");
        DiagnosticProbe? detail = probes.FirstOrDefault(probe => probe.Operation == "addon-detail");
        if (probes.Any(probe => probe.Status is null)) { found.Add("partial-comparison"); }
        if (parent?.Metadata.IdentityMatches != true) { found.Add("parent-control-unverified"); }
        if (detail?.Metadata.State == "mismatch") { found.Add("sample-identity-or-parent-mismatch"); }
        bool matched = parent?.Metadata.IdentityMatches == true && detail?.Metadata.State == "matched";
        if (matched && probes.Any(probe => probe.Operation.EndsWith("collection", StringComparison.Ordinal) && probe.Status is >= 400))
        { found.Add("collection-versus-detail-difference"); }
        if (matched && string.Equals(detail!.Metadata.WireProductType?.Value, "Subscription", StringComparison.OrdinalIgnoreCase) && detail.Metadata.ParserCompatible == false)
        { found.Add("local-parser-gap"); }
        string[] Rejections(string operation) => attempts.Where(attempt => attempt.Operation == operation && attempt.Status is >= 400)
            .SelectMany(attempt => attempt.Error?.Records ?? []).Where(record => string.Equals(record.Target.Value, "productType", StringComparison.OrdinalIgnoreCase) &&
                record.ProductType.Value is not null && new[] { "NotAllowed", "InvalidIapProductType", "NotSupported", "Unsupported", "InAppProductsNotSupported" }
                    .Contains(record.Code.Value, StringComparer.OrdinalIgnoreCase))
            .Select(record => record.Code.Value + ":" + record.Target.Value + ":" + record.ProductType.Value).ToArray();
        if (parent?.Metadata.IdentityMatches == true && Rejections("addon-detail").Intersect(Rejections("parent-collection").Concat(Rejections("account-collection")), StringComparer.OrdinalIgnoreCase).Any())
        { found.Add("matching-product-type-rejection-hypothesis"); }
        if (matched && probes.Count == 6 && probes.All(probe => probe.Status is >= 200 and < 300 && probe.Metadata.State is "matched" or "collection-observed"))
        { found.Add("failure-not-reproduced"); }
        if (found.Count == 0 || (detail?.Metadata.State is "malformed" or "not-read" && !found.Contains("matching-product-type-rejection-hypothesis")))
        { found.Add("unresolved"); }
        return found;
    }

    private static string SafeFailure(string category) => category is "unauthorized" or "forbidden-or-association-missing" or
        "credential-generation-changed" or "diagnostic-auth-recovery-exhausted" or "diagnostic-request-budget-exhausted" or
        "diagnostic-destination-rejected" or "redirect-rejected" or "rejected-request-destination" or "invalid-token-response" or
        "invalid-token-binding" or "http-request-failed" or "resource-not-found" or "unsupported-store-feature" or "invalid-response-json" or
        "duplicate-response-property" or "response-too-large" or "retry-budget-exhausted" or "retry-after-exceeds-budget" or "http-timeout" ? category : "diagnostic-operation-failed";
}
