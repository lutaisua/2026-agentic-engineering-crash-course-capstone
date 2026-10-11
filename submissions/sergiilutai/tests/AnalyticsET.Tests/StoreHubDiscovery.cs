using System.Net;
using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

internal sealed class StoreHubDiscoveryTransport : DelegatingHandler
{
    private readonly Uri tokenEndpoint;
    private readonly TimeProvider clock;
    private readonly int maximumRequests;
    internal List<DiagnosticAttempt> Attempts { get; } = [];
    private int requests;

    internal StoreHubDiscoveryTransport(HttpMessageHandler inner, AuthorizationContext context, TimeProvider clock,
        int maximumRequests = 24) : base(inner)
    {
        if (context.ResourceKey != "store-analytics" || context.Program != "microsoft-store" ||
            context.Resource != "https://manage.devcenter.microsoft.com" || context.Flow != "client-credentials" ||
            !Guid.TryParseExact(context.TenantId, "D", out Guid tenant) || tenant == Guid.Empty || maximumRequests is < 1 or > 24)
        { throw new InventoryException("hub-invalid-binding"); }
        tokenEndpoint = new($"https://login.microsoftonline.com/{tenant:D}/oauth2/token");
        this.clock = clock;
        this.maximumRequests = maximumRequests;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        bool token = request.RequestUri == tokenEndpoint && request.Method == HttpMethod.Post && request.Headers.Authorization is null &&
            request.Content?.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded";
        bool dataset = request.RequestUri?.AbsoluteUri == StoreHubMetadata.Endpoint && request.Method == HttpMethod.Get && request.Content is null;
        if (!token && !dataset) { throw new InventoryException("hub-destination-rejected"); }
        if (Interlocked.Increment(ref requests) > maximumRequests) { throw new InventoryException("hub-request-budget-exhausted"); }
        string operation = token ? "token" : "dataset-discovery";
        int attempt = Attempts.Count(item => item.Operation == operation) + 1;
        DateTimeOffset started = clock.GetUtcNow();
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            StoreDiagnosticErrorSummary? error = response.IsSuccessStatusCode ? null :
                await StoreDiagnosticErrorSummary.ReadAsync(response.Content, cancellationToken).ConfigureAwait(false);
            Attempts.Add(new(operation, "consumer", attempt, started, clock.GetUtcNow(), (int)response.StatusCode, null,
                error, DiagnosticCorrelationCapture.Read(response, error?.BodyCorrelations)));
            return response;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
        {
            Attempts.Add(new(operation, "consumer", attempt, started, clock.GetUtcNow(), response is null ? null : (int)response.StatusCode,
                error is OperationCanceledException ? "transport-cancelled" : "transport-failed", null, []));
            response?.Dispose();
            throw;
        }
    }
}

internal sealed record StoreHubDiscoveryPacket(int SchemaVersion, string RunId, string Stage, DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt, string SourceSha256, string ContractSha256, string InputSha256, string BindingFingerprint,
    string Outcome, int ExitCode, StoreHubMetadata? Metadata, IReadOnlyList<DiagnosticAttempt> Attempts)
{
    public override string ToString() => "[protected Store reporting evidence]";
    internal object PublicProjection() => new
    {
        stage = "Discover",
        completion = ExitCode == 0 ? "recorded" : ExitCode == 130 ? "cancelled" : "partial",
        ExitCode,
        Outcome,
        transportAttempts = Attempts.Count,
        observations = Attempts.Select(attempt => new { attempt.Operation, attempt.Attempt, attempt.Status, attempt.Failure }),
        metadata = Metadata?.PublicProjection(),
        export = "unverified",
        comparison = "unverified",
        inventoryAcceptance = "unverified"
    };
}
internal sealed record StoreHubReceipt(int SchemaVersion, string RunId, string Stage, int ExitCode, string EvidenceSha256);
internal sealed record StoreHubDiscoveryResult(StoreHubDiscoveryPacket Packet, string Directory);

internal static class StoreHubDiscovery
{
    internal static async Task<StoreHubDiscoveryResult> RunAsync(string configurationPath, string inputPath, string mappingPath,
        string experimentDirectory, IPrivateStorageGuard privacy, IApplicationCredentialResolver credentials,
        HttpMessageHandler inner, TimeProvider clock, IAtomicFileWriter writer, CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null, TimeSpan? deadline = null, int maximumRequests = 24)
    {
        StoreHubPreflight preflight = await StoreHubPreflight.LoadAsync(configurationPath, inputPath, mappingPath, experimentDirectory,
            privacy, clock, cancellationToken).ConfigureAwait(false);
        string lockPath = Path.Combine(preflight.ExperimentDirectory, ".experiment.lock");
        if (File.Exists(lockPath)) { privacy.VerifyFile(lockPath); }
        using FileStream experimentLock = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        privacy.VerifyFile(lockPath);
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(deadline ?? TimeSpan.FromMinutes(2));
        DateTimeOffset started = clock.GetUtcNow();
        AuthorizationContext context = preflight.Context;
        using StoreHubDiscoveryTransport transport = new(inner, context, clock, maximumRequests);
        using HttpClient client = new(transport) { Timeout = TimeSpan.FromSeconds(60) };
        BoundedJsonHttp http = new(client, clock, delay);
        AuthorizedInventoryHttp authorized = new(http, new SingleDiagnosticRecoveryTokens(new ClientCredentialTokens(http, credentials, clock)));
        string outcome = "metadata-recorded";
        int exitCode = 0;
        StoreHubMetadata? metadata = null;
        try
        {
            ApplicationCredential credential = await credentials.ResolveAsync(context, bounded.Token).ConfigureAwait(false);
            if (preflight.PreviousBinding is not null && preflight.PreviousBinding.CredentialGeneration != credential.Generation)
            { throw new InventoryException("credential-generation-changed"); }
            context = context with { CredentialGeneration = credential.Generation };
            StoreHubBinding binding = new(1, preflight.InputSha256, context.ConfigurationKey, credential.Generation, StoreHubPreflight.ContractSha256);
            if (preflight.PreviousBinding is null)
            {
                await CheckedWriteAsync(Path.Combine(preflight.ExperimentDirectory, "binding.json"),
                    JsonSerializer.SerializeToUtf8Bytes(binding, StoreHubInput.JsonOptions), privacy, writer, bounded.Token).ConfigureAwait(false);
            }
            using JsonDocument? document = await authorized.GetAsync(context, new(StoreHubMetadata.Endpoint), new(StoreHubMetadata.Endpoint),
                false, bounded.Token).ConfigureAwait(false);
            metadata = StoreHubMetadata.Parse(document!.RootElement);
        }
        catch (OperationCanceledException)
        { exitCode = cancellationToken.IsCancellationRequested ? 130 : 2; outcome = exitCode == 130 ? "operator-cancelled" : "hub-deadline-exhausted"; }
        catch (InventoryException error) { exitCode = 2; outcome = SafeCategory(error.Category); }
        catch (Exception error) when (error is InputException or IOException or HttpRequestException or UnauthorizedAccessException or JsonException)
        { exitCode = 1; outcome = "hub-prerequisite-or-recording-failed"; }
        StoreHubDiscoveryPacket packet = new(1, Guid.NewGuid().ToString("D"), "Discover", started, clock.GetUtcNow(),
            StoreDiagnosticPacketStore.Hash(await File.ReadAllBytesAsync(typeof(StoreHubDiscovery).Assembly.Location, CancellationToken.None).ConfigureAwait(false)),
            StoreHubPreflight.ContractSha256, preflight.InputSha256, context.StorageKey, outcome, exitCode, metadata, transport.Attempts.ToArray());
        using CancellationTokenSource recording = new(TimeSpan.FromSeconds(10));
        string directory = Path.Combine(preflight.ExperimentDirectory, "Discover-" + packet.RunId);
        if (Directory.Exists(directory)) { throw StoreHubInput.Invalid(); }
        privacy.PrepareDirectory(directory);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(packet, StoreHubInput.JsonOptions);
        await CheckedWriteAsync(Path.Combine(directory, "evidence.json"), bytes, privacy, writer, recording.Token).ConfigureAwait(false);
        StoreHubReceipt receipt = new(1, packet.RunId, "Discover", packet.ExitCode, StoreDiagnosticPacketStore.Hash(bytes));
        await CheckedWriteAsync(Path.Combine(directory, "receipt.json"), JsonSerializer.SerializeToUtf8Bytes(receipt, StoreHubInput.JsonOptions),
            privacy, writer, recording.Token).ConfigureAwait(false);
        return new(packet, directory);
    }

    internal static async Task CheckedWriteAsync(string path, byte[] bytes, IPrivateStorageGuard privacy,
        IAtomicFileWriter writer, CancellationToken cancellationToken)
    {
        await writer.WriteAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        privacy.VerifyFile(path);
        if (StoreDiagnosticPacketStore.Hash(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)) != StoreDiagnosticPacketStore.Hash(bytes))
        { throw new InputException("Private reporting evidence verification failed."); }
    }

    private static string SafeCategory(string category) => category is "unauthorized" or "forbidden-or-association-missing" or
        "credential-generation-changed" or "invalid-token-binding" or "invalid-token-response" or "http-timeout" or
        "redirect-rejected" or "rejected-request-destination" or "resource-not-found" or "retry-budget-exhausted" or
        "retry-after-exceeds-budget" or "response-too-large" or "invalid-response-json" or "duplicate-response-property" or
        "hub-metadata-contract-unverified" or "hub-destination-rejected" or "hub-request-budget-exhausted" or "diagnostic-auth-recovery-exhausted" ?
        category : "hub-operation-failed";
}

[TestClass]
[DoNotParallelize]
public sealed class StoreHubLiveValidation
{
    /// <summary>Explicit private-path, discovery-only integration entry point.</summary>
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("Live")]
    public async Task DiscoverDatasets()
    {
        string? configuration = Environment.GetEnvironmentVariable("ANALYTICSET_HUB_CONFIG");
        string? input = Environment.GetEnvironmentVariable("ANALYTICSET_HUB_INPUT");
        string? directory = Environment.GetEnvironmentVariable("ANALYTICSET_HUB_EXPERIMENT");
        if (string.IsNullOrWhiteSpace(configuration) || string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(directory))
        { Assert.Inconclusive("Explicit protected Store reporting paths are required."); }
        try
        {
            WindowsPrivateStorageGuard privacy = new();
            ApplicationCredentialResolver credentials = new(ApplicationCredentialResolver.DefaultMappingPath, privacy, new WindowsGenericCredentialVault());
            using HttpClientHandler handler = new() { AllowAutoRedirect = false, UseCookies = false };
            StoreHubDiscoveryResult result = await StoreHubDiscovery.RunAsync(configuration, input, ApplicationCredentialResolver.DefaultMappingPath,
                directory, privacy, credentials, handler, TimeProvider.System, new AtomicFileWriter(new NewFileCommitter()), CancellationToken.None);
            TestContext.WriteLine("ANALYTICSET_HUB_RESULT " + JsonSerializer.Serialize(new
            { exitCode = result.Packet.ExitCode, packetDirectory = result.Directory, summary = result.Packet.PublicProjection() }));
        }
        catch (Exception error)
        {
            // Never expose private exception text or detailed test output to the operator's ordinary output.
            TestContext.WriteLine("ANALYTICSET_HUB_RESULT " + JsonSerializer.Serialize(new
            { exitCode = error is OperationCanceledException ? 130 : 1, category = "hub-prerequisite-or-recording-failed" }));
            Assert.Fail("Store reporting discovery did not record a verified packet. Inspect private prerequisites locally.");
        }
    }
}
