using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

internal sealed class StoreHubQueryReadTransport : DelegatingHandler
{
    private readonly Uri tokenEndpoint;
    private readonly Uri previewEndpoint;
    private readonly TimeProvider clock;
    private int count;
    internal List<DiagnosticAttempt> Attempts { get; } = [];
    internal StoreHubQueryReadTransport(HttpMessageHandler inner, AuthorizationContext context, StoreHubQueryPlan plan, TimeProvider clock) : base(inner)
    {
        if (context.ResourceKey != "store-analytics" || context.Program != "microsoft-store" || context.Flow != "client-credentials" ||
            context.Resource != "https://manage.devcenter.microsoft.com" || !Guid.TryParseExact(context.TenantId, "D", out Guid tenant) || tenant == Guid.Empty)
        { throw new InventoryException("hub-invalid-binding"); }
        tokenEndpoint = new($"https://login.microsoftonline.com/{tenant:D}/oauth2/token");
        previewEndpoint = plan.PreviewUri;
        this.clock = clock;
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        bool token = request.RequestUri == tokenEndpoint && request.Method == HttpMethod.Post && request.Headers.Authorization is null &&
            request.Content?.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded";
        if (!token && !(request.RequestUri == previewEndpoint && request.Method == HttpMethod.Get && request.Content is null))
        { throw new InventoryException("hub-destination-rejected"); }
        if (Interlocked.Increment(ref count) > 24) { throw new InventoryException("hub-request-budget-exhausted"); }
        string operation = token ? "token" : "installs-query-preview";
        int attempt = Attempts.Count(item => item.Operation == operation) + 1;
        DateTimeOffset start = clock.GetUtcNow();
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            StoreDiagnosticErrorSummary? error = response.IsSuccessStatusCode ? null :
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

internal sealed record StoreHubQueryProbePacket(int SchemaVersion, string RunId, string Stage, string SourceSha256,
    string ContractSha256, string InputSha256, string BindingFingerprint, string DiscoveryEvidenceSha256,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, StoreHubQueryPlan Plan, StoreHubPreview? Preview, StoreHubPreviewShape? Shape,
    IReadOnlyList<DiagnosticAttempt> Attempts, string Outcome, int ExitCode)
{
    public override string ToString() => "[protected query-validation evidence]";
    internal object PublicProjection() => new
    {
        stage = "Installs",
        phase = "query-preview",
        dateWireFormat = Plan.DateWireFormat,
        ExitCode,
        Outcome,
        previewRows = Preview?.RowCount,
        productMatches = Preview?.ProductMatches,
        observedDatesWithinBounds = Preview?.ObservedDatesWithinRequestedBounds,
        dateBoundary = "unverified",
        reportingResourcesCreated = 0,
        export = "unverified",
        comparison = "unverified",
        observations = Attempts.Select(item => new { item.Operation, item.Attempt, item.Status, item.Failure })
    };
}
internal sealed record StoreHubQueryProbeResult(StoreHubQueryProbePacket Packet, string Directory);

internal static class StoreHubQueryProbe
{
    internal static async Task<StoreHubQueryProbeResult> RunAsync(string configuration, string input, string mapping, string experiment,
        string discoveryDirectory, IReadOnlySet<string> approvedDiscoveryAssemblies, IPrivateStorageGuard privacy,
        IApplicationCredentialResolver credentials, HttpMessageHandler inner, TimeProvider clock, IAtomicFileWriter writer,
        CancellationToken cancellationToken, Func<TimeSpan, CancellationToken, Task>? delay = null, string dateWireFormat = "date-only")
    {
        StoreHubPreflight prepared = await StoreHubPreflight.LoadAsync(configuration, input, mapping, experiment, privacy, clock, cancellationToken).ConfigureAwait(false);
        string lockPath = Path.Combine(experiment, ".experiment.lock");
        if (File.Exists(lockPath)) { privacy.VerifyFile(lockPath); }
        using FileStream experimentLock = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        privacy.VerifyFile(lockPath);
        StoreHubDiscoveryPacket discovery = await StoreHubDiscoveryReader.LoadAsync(prepared, discoveryDirectory, approvedDiscoveryAssemblies, privacy,
            cancellationToken).ConfigureAwait(false);
        AuthorizationContext context = prepared.Context with { CredentialGeneration = prepared.PreviousBinding!.CredentialGeneration };
        StoreHubQueryPlan plan = StoreHubQueryPlan.Prepare(discovery.Metadata!, prepared.Input, "Installs", clock, context.StorageKey, dateWireFormat);
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromMinutes(2));
        DateTimeOffset started = clock.GetUtcNow();
        using StoreHubQueryReadTransport transport = new(inner, context, plan, clock);
        using HttpClient client = new(transport) { Timeout = TimeSpan.FromSeconds(60) };
        BoundedJsonHttp http = new(client, clock, delay);
        AuthorizedInventoryHttp authorized = new(http, new SingleDiagnosticRecoveryTokens(new ClientCredentialTokens(http, credentials, clock)));
        StoreHubPreview? preview = null;
        StoreHubPreviewShape? shape = null;
        string outcome = "hub-date-boundary-unverified";
        int exit = 2;
        try
        {
            using JsonDocument? document = await authorized.GetAsync(context, plan.PreviewUri, plan.PreviewUri, false, bounded.Token).ConfigureAwait(false);
            shape = StoreHubPreviewShape.Observe(document!.RootElement, plan);
            preview = StoreHubPreview.Observe(document!.RootElement, plan);
        }
        catch (InventoryException error) { outcome = SafeFailure(error.Category); }
        catch (OperationCanceledException) { exit = cancellationToken.IsCancellationRequested ? 130 : 2; outcome = exit == 130 ? "operator-cancelled" : "hub-deadline-exhausted"; }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or Console.AnalyticsET.Cli.InputException)
        { exit = 1; outcome = "hub-query-probe-failed"; }
        StoreHubQueryProbePacket packet = new(1, Guid.NewGuid().ToString("D"), "Installs", StoreDiagnosticPacketStore.Hash(
            await File.ReadAllBytesAsync(typeof(StoreHubQueryProbe).Assembly.Location, CancellationToken.None).ConfigureAwait(false)), StoreHubPreflight.ContractSha256,
            prepared.InputSha256, context.StorageKey, StoreDiagnosticPacketStore.Hash(await File.ReadAllBytesAsync(Path.Combine(discoveryDirectory, "evidence.json"),
                CancellationToken.None).ConfigureAwait(false)), started, clock.GetUtcNow(), plan, preview, shape, transport.Attempts.ToArray(), outcome, exit);
        using CancellationTokenSource recording = new(TimeSpan.FromSeconds(10));
        string directory = Path.Combine(prepared.ExperimentDirectory, "Installs-" + packet.RunId);
        if (Directory.Exists(directory)) { throw StoreHubInput.Invalid(); }
        privacy.PrepareDirectory(directory);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(packet, StoreHubInput.JsonOptions);
        await StoreHubDiscovery.CheckedWriteAsync(Path.Combine(directory, "evidence.json"), bytes, privacy, writer, recording.Token).ConfigureAwait(false);
        await StoreHubDiscovery.CheckedWriteAsync(Path.Combine(directory, "receipt.json"), JsonSerializer.SerializeToUtf8Bytes(
            new StoreHubReceipt(1, packet.RunId, "Installs", exit, StoreDiagnosticPacketStore.Hash(bytes)), StoreHubInput.JsonOptions), privacy, writer, recording.Token).ConfigureAwait(false);
        return new(packet, directory);
    }
    private static string SafeFailure(string category) => category is "hub-preview-shape-unverified" or "hub-preview-schema-unverified" or
        "hub-preview-product-mismatch" or "hub-preview-parent-mismatch" or "hub-preview-metric-invalid" or "hub-preview-date-scope-unverified" or
        "unauthorized" or "forbidden-or-association-missing" or "credential-generation-changed" or "resource-not-found" or "http-timeout" or
        "retry-budget-exhausted" or "retry-after-exceeds-budget" or "response-too-large" or "invalid-response-json" or "duplicate-response-property" or
        "redirect-rejected" or "hub-destination-rejected" or "hub-request-budget-exhausted" or "diagnostic-auth-recovery-exhausted" ? category : "hub-query-probe-failed";
}

[TestClass]
[DoNotParallelize]
public sealed class StoreHubQueryLiveValidation
{
    /// <summary>Receives only a sanitized marker; all preview rows are discarded.</summary>
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("Live")]
    public async Task RecordInstallsPreview()
        => await RecordPreviewAsync("date-only").ConfigureAwait(false);

    [TestMethod]
    [TestCategory("Live")]
    public async Task RecordInstallsUtcTimestampPreview()
        => await RecordPreviewAsync("utc-timestamp").ConfigureAwait(false);

    private async Task RecordPreviewAsync(string dateWireFormat)
    {
        string? configuration = Environment.GetEnvironmentVariable("ANALYTICSET_HUB_CONFIG");
        string? input = Environment.GetEnvironmentVariable("ANALYTICSET_HUB_INPUT");
        string? directory = Environment.GetEnvironmentVariable("ANALYTICSET_HUB_EXPERIMENT");
        string? discovery = Environment.GetEnvironmentVariable("ANALYTICSET_HUB_DISCOVERY");
        if (string.IsNullOrWhiteSpace(configuration) || string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(discovery))
        { Assert.Inconclusive("Explicit protected experiment/discovery paths are required."); }
        try
        {
            WindowsPrivateStorageGuard privacy = new();
            ApplicationCredentialResolver credentials = new(ApplicationCredentialResolver.DefaultMappingPath, privacy, new WindowsGenericCredentialVault());
            using HttpClientHandler handler = new() { AllowAutoRedirect = false, UseCookies = false };
            StoreHubQueryProbeResult result = await StoreHubQueryProbe.RunAsync(configuration, input, ApplicationCredentialResolver.DefaultMappingPath,
                directory, discovery, StoreHubDiscoveryReader.ReviewedDiscoveryAssemblies(), privacy, credentials, handler, TimeProvider.System,
                new AtomicFileWriter(new NewFileCommitter()), CancellationToken.None, dateWireFormat: dateWireFormat);
            TestContext.WriteLine("ANALYTICSET_HUB_RESULT " + JsonSerializer.Serialize(new
            { exitCode = result.Packet.ExitCode, packetDirectory = result.Directory, summary = result.Packet.PublicProjection() }));
        }
        catch (Exception error)
        {
            TestContext.WriteLine("ANALYTICSET_HUB_RESULT " + JsonSerializer.Serialize(new
            { exitCode = error is OperationCanceledException ? 130 : 1, category = "hub-query-probe-prerequisite-or-recording-failed" }));
            Assert.Fail("Installs query preview did not record a verified packet. Inspect private prerequisites locally.");
        }
    }
}
