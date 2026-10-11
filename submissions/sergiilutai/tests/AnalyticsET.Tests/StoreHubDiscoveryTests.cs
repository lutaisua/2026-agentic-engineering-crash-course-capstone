using System.Diagnostics;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class StoreHubDiscoveryTests
{
    internal static StoreHubInput Sample => new(1, Guid.NewGuid().ToString("D"), "publisher-one", "APP-FICTIONAL", "SUB-FICTIONAL",
        "Subscription", true, true, "2026-10-10T00:00:00Z", new("2026-10-08T00:00:00Z", "2026-10-09T00:00:00Z"),
        new("2026-10-07T00:00:00Z", "2026-10-09T00:00:00Z"));
    internal static string MetadataJson => JsonSerializer.Serialize(new
    {
        value = new[] { Dataset("Installs"), Dataset("AddOnAcquisitions") },
        totalCount = 2,
        statusCode = 200,
        message = "RAW-PRIVATE-CANARY"
    });
    internal static object Dataset(string name) => new
    {
        datasetName = name,
        selectableColumns = new[] { "ProductID", "Date" },
        availableMetrics = new[] { "Quantity" },
        availableDateRanges = new[] { "LAST_30_DAYS" },
        minimumRecurrenceInterval = 24,
        modifiedTime = "2026-10-01T00:00:00Z",
        columnFilters = new { ProductID = "IN" },
        aggregationToDateRangeMapping = new { Daily = "LAST_30_DAYS" }
    };

    [TestMethod]
    public void PrivateInputRejectsAmbiguousIdsUnconfirmedRelationshipsAndNonCompletedUtcWindows()
    {
        StoreHubInput sample = Sample;
        string valid = JsonSerializer.Serialize(sample, StoreHubInput.JsonOptions);
        Assert.AreEqual(sample, StoreHubInput.Parse(valid, new StoreDiagnosticTransportTests.Clock()));
        foreach (string invalid in new[]
        {
            valid.Replace("APP-FICTIONAL", "SUB-FICTIONAL"), valid.Replace("APP-FICTIONAL", "../PRIVATE-CANARY"),
            valid.Replace("Subscription", "Durable"), valid.Replace("publisher-one", "Publisher-One"),
            valid.Replace("\"appPublishedConfirmed\": true", "\"appPublishedConfirmed\": false"),
            valid.Replace("\"subscriptionParentConfirmed\": true", "\"subscriptionParentConfirmed\": false"),
            valid.Replace("2026-10-08T00:00:00Z", "2026-10-08T00:00:00+02:00"),
            valid.Replace("2026-10-08T00:00:00Z", "2026-10-08T12:00:00Z"),
            valid.Replace("2026-10-08T00:00:00Z", "2026-10-09T00:00:00Z"),
            valid.Replace("2026-10-08T00:00:00Z", "2026-10-10T00:00:00Z"),
            valid.Replace("2026-10-08T00:00:00Z", "2026-09-01T00:00:00Z"),
            valid.Replace("2026-10-09T00:00:00Z", "2026-10-11T00:00:00Z"),
            valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"),
            valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"extra\": \"PRIVATE-CANARY\""),
            valid.Replace("\"startUtc\":", "\"StartUtc\": \"PRIVATE-CANARY\", \"startUtc\":"),
            valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"),
            valid.Replace("\"expectedProductType\": \"Subscription\"", "\"expectedProductType\": null"),
            "PRIVATE-CANARY"
        })
        {
            InputException error = Assert.Throws<InputException>(() => StoreHubInput.Parse(invalid, new StoreDiagnosticTransportTests.Clock()));
            Assert.IsFalse(error.Message.Contains("PRIVATE-CANARY", StringComparison.Ordinal));
        }
        StoreHubWindow maximum = new("2026-10-02T00:00:00Z", "2026-10-09T00:00:00Z");
        maximum.Validate(new StoreDiagnosticTransportTests.Clock());
        Assert.IsFalse(sample.ToString().Contains("FICTIONAL", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AnalyticsSelectionKeepsSameTenantPublishersIsolatedAndRequiresCorrectConfiguredResource()
    {
        PublisherProfile one = InventoryModelTests.Profile(), two = InventoryModelTests.Profile("publisher-two", "account-two");
        PublisherConfiguration configuration = new() { SchemaVersion = 2, Publishers = [one, two] };
        AuthorizationContext first = StoreHubPreflight.Select(configuration, Sample);
        AuthorizationContext second = StoreHubPreflight.Select(configuration, Sample with { PublisherId = "publisher-two" });
        Assert.AreEqual(first.TenantId, second.TenantId);
        Assert.AreNotEqual(first.ConfigurationKey, second.ConfigurationKey);
        Assert.AreEqual(ApiResources.Store, first.ResourceKey);
        Assert.AreEqual("account-one", first.PublisherAccountId);
        Assert.AreNotEqual(first.ConfigurationKey, AuthorizationContext.Create(one, ApiResources.StoreProgram, ApiResources.StoreInventory).ConfigurationKey);
        Assert.Throws<InputException>(() => StoreHubPreflight.Select(configuration, Sample with { PublisherId = "missing" }));
        one.Authorization.Remove(ApiResources.Store);
        Assert.Throws<InputException>(() => StoreHubPreflight.Select(configuration, Sample));
    }

    [TestMethod]
    public async Task InvalidSamplesMappingsMissingResourcesAndChangedBindingsRejectBeforeCredentialOrHttp()
    {
        foreach (string invalid in new[] { "input", "mapping", "resource", "binding", "wrong-experiment", "wrong-client" })
        {
            using Fixture fixture = new();
            switch (invalid)
            {
                case "input": await File.WriteAllTextAsync(fixture.Input, "PRIVATE-CANARY"); break;
                case "mapping": await File.WriteAllTextAsync(fixture.Mapping, "{\"schemaVersion\":1,\"schemaVersion\":1,\"references\":{}}"); break;
                case "resource":
                    PublisherProfile profile = InventoryModelTests.Profile();
                    profile.Authorization.Remove(ApiResources.Store);
                    await File.WriteAllTextAsync(fixture.Configuration, JsonSerializer.Serialize(new PublisherConfiguration
                    { SchemaVersion = 2, Publishers = [profile] }, StoreHubInput.JsonOptions)); break;
                case "binding":
                    await File.WriteAllTextAsync(Path.Combine(fixture.Experiment, "binding.json"), JsonSerializer.Serialize(new StoreHubBinding(
                        1, "wrong-input", "wrong-publisher", "old-generation", StoreHubPreflight.ContractSha256), StoreHubInput.JsonOptions)); break;
                case "wrong-experiment":
                    await File.WriteAllTextAsync(fixture.Input, JsonSerializer.Serialize(fixture.Sample with { ExperimentId = Guid.NewGuid().ToString("D") }, StoreHubInput.JsonOptions)); break;
                case "wrong-client":
                    await File.WriteAllTextAsync(Path.Combine(fixture.Experiment, "binding.json"), JsonSerializer.Serialize(new StoreHubBinding(
                        1, StoreDiagnosticPacketStore.Hash(File.ReadAllBytes(fixture.Input)), "previous-client", "old-generation", StoreHubPreflight.ContractSha256), StoreHubInput.JsonOptions)); break;
            }
            await Assert.ThrowsAsync<InputException>(() => fixture.RunAsync());
            Assert.AreEqual(0, fixture.Calls);
            Assert.AreEqual(0, fixture.Credentials.Contexts.Count);
        }
    }

    [TestMethod]
    public async Task RepositoryBroadPermissionsWrongOwnerAndReparsePathsBlockBeforeCredentials()
    {
        using (Fixture repository = new())
        {
            Directory.CreateDirectory(Path.Combine(repository.Root, ".git"));
            await Assert.ThrowsAsync<InputException>(() => repository.RunAsync());
            Assert.AreEqual(0, repository.Calls);
            Assert.AreEqual(0, repository.Credentials.Contexts.Count);
        }
        using (Fixture broad = new())
        {
            if (!OperatingSystem.IsWindows()) { Assert.Fail("Windows ACL fixtures require Windows."); return; }
            FileInfo input = new(broad.Input);
            FileSecurity acl = input.GetAccessControl();
            Assert.Throws<InputException>(() =>
            {
                if (OperatingSystem.IsWindows())
                { WindowsPrivateStorageGuard.VerifyWindowsAccess(acl, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), false); }
            });
            acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
            input.SetAccessControl(acl);
            await Assert.ThrowsAsync<InputException>(() => broad.RunAsync());
            Assert.AreEqual(0, broad.Calls);
            Assert.AreEqual(0, broad.Credentials.Contexts.Count);
        }
        using Fixture redirected = new();
        string link = Path.Combine(redirected.Root, "fixture-junction");
        ProcessStartInfo start = new("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "/d", "/c", "mklink", "/J", link, redirected.Experiment }) { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start)!;
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode);
        try
        {
            await Assert.ThrowsAsync<InputException>(() => StoreHubDiscovery.RunAsync(redirected.Configuration, Path.Combine(link, "input.json"),
                redirected.Mapping, link, new WindowsPrivateStorageGuard(), redirected.Credentials, new InventoryHttpTests.Handler(_ =>
                    throw new AssertFailedException("Unexpected HTTP")), new StoreDiagnosticTransportTests.Clock(), new AtomicFileWriter(new NewFileCommitter()), default));
            Assert.AreEqual(0, redirected.Credentials.Contexts.Count);
        }
        finally { Directory.Delete(link, recursive: false); }
    }

    [TestMethod]
    public void MetadataAvailabilityOmissionsAndMalformedContractsRemainDistinctFromExportSupport()
    {
        using JsonDocument valid = JsonDocument.Parse(MetadataJson);
        StoreHubMetadata metadata = StoreHubMetadata.Parse(valid.RootElement);
        Assert.AreEqual(2, metadata.Datasets.Count);
        Assert.AreEqual("IN", metadata.Datasets[0].ColumnFilters!["ProductID"]);
        Assert.IsTrue(metadata.Datasets.All(dataset => dataset.SchemaPresent));
        foreach (string malformed in new[]
        {
            MetadataJson.Replace("\"totalCount\":2", "\"totalCount\":3"),
            MetadataJson.Replace("AddOnAcquisitions", "Installs"),
            MetadataJson.Replace("\"statusCode\":200", "\"statusCode\":400"),
            MetadataJson.Replace("\"totalCount\":2", "\"totalCount\":2,\"nextLink\":\"PRIVATE-CANARY\""),
            MetadataJson.Replace("\"totalCount\":2", "\"totalCount\":2,\"totalCount\":2"),
            MetadataJson.Replace("\"ProductID\",\"Date\"", "\"ProductID\",\"productid\""),
            MetadataJson.Replace("\"Quantity\"", "33"), MetadataJson.Replace("\"IN\"", "[]"),
            MetadataJson.Replace("\"datasetName\":\"Installs\"", "\"datasetName\":\"https://PRIVATE-CANARY\""), "{}"
        })
        {
            using JsonDocument invalid = JsonDocument.Parse(malformed);
            Assert.Throws<InventoryException>(() => StoreHubMetadata.Parse(invalid.RootElement));
        }
        using JsonDocument absent = JsonDocument.Parse("{\"value\":[],\"totalCount\":0,\"statusCode\":200}");
        string projection = JsonSerializer.Serialize(StoreHubMetadata.Parse(absent.RootElement).PublicProjection());
        StringAssert.Contains(projection, "unavailable");
        StringAssert.Contains(projection, "unverified");
        using JsonDocument omitted = JsonDocument.Parse("{\"value\":[{\"datasetName\":\"Installs\"}],\"totalCount\":1,\"statusCode\":200}");
        Assert.IsFalse(StoreHubMetadata.Parse(omitted.RootElement).Datasets[0].SchemaPresent);
    }

    [TestMethod]
    public void DocumentedFictionalInputAndDiscoveryProjectionMatchTheOfflineContract()
    {
        string app = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string example = File.ReadAllText(Path.Combine(app, "docs", "examples", "store-downloads-hub-input.fictional.json"));
        StoreHubInput input = StoreHubInput.Parse(example, new StoreDiagnosticTransportTests.Clock());
        Assert.AreEqual("publisher-one", input.PublisherId);
        Assert.AreEqual("Subscription", input.ExpectedProductType);
        using JsonDocument metadata = JsonDocument.Parse(MetadataJson);
        using JsonDocument projection = JsonDocument.Parse(JsonSerializer.Serialize(StoreHubMetadata.Parse(metadata.RootElement).PublicProjection()));
        JsonElement root = projection.RootElement;
        Assert.AreEqual("recorded", root.GetProperty("metadata").GetString());
        Assert.AreEqual(2, root.GetProperty("datasetCount").GetInt32());
        foreach (JsonElement target in root.GetProperty("targets").EnumerateArray())
        {
            Assert.AreEqual("discovered", target.GetProperty("availability").GetString());
            Assert.AreEqual("present", target.GetProperty("schema").GetString());
            Assert.AreEqual("unverified", target.GetProperty("export").GetString());
            Assert.AreEqual("unverified", target.GetProperty("comparison").GetString());
        }
    }

    [TestMethod]
    public async Task DiscoveryRecordsOnlyNormalizedMetadataWithVerifiedReceiptAndPreservesProductionBytes()
    {
        using Fixture fixture = new();
        byte[] configuration = File.ReadAllBytes(fixture.Configuration), mapping = File.ReadAllBytes(fixture.Mapping), input = File.ReadAllBytes(fixture.Input);
        string inventory = Path.Combine(fixture.Root, "inventory.json");
        await File.WriteAllTextAsync(inventory, "FICTIONAL-INVENTORY");
        StoreHubDiscoveryResult result = await fixture.RunAsync();
        Assert.AreEqual(0, result.Packet.ExitCode);
        Assert.AreEqual(2, fixture.Calls);
        Assert.IsTrue(fixture.Credentials.Contexts.All(context => context.ResourceKey == ApiResources.Store));
        CollectionAssert.AreEqual(configuration, File.ReadAllBytes(fixture.Configuration));
        CollectionAssert.AreEqual(mapping, File.ReadAllBytes(fixture.Mapping));
        CollectionAssert.AreEqual(input, File.ReadAllBytes(fixture.Input));
        Assert.AreEqual("FICTIONAL-INVENTORY", File.ReadAllText(inventory));
        string evidence = File.ReadAllText(Path.Combine(result.Directory, "evidence.json"));
        foreach (string canary in new[] { "RAW-PRIVATE-CANARY", "FICTIONAL-TOKEN", "FICTIONAL-SECRET", "ProductId IN ('all')" })
        { Assert.IsFalse(evidence.Contains(canary, StringComparison.Ordinal)); }
        string projection = JsonSerializer.Serialize(result.Packet.PublicProjection());
        foreach (string canary in new[] { "APP-FICTIONAL", "SUB-FICTIONAL", "publisher-one", "ProductID", "RAW-PRIVATE-CANARY", fixture.Sample.ExperimentId })
        { Assert.IsFalse(projection.Contains(canary, StringComparison.Ordinal)); }
        Assert.IsFalse(result.Packet.ToString().Contains("FICTIONAL", StringComparison.Ordinal));
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.Directory, "receipt.json")));
        Assert.AreEqual(StoreDiagnosticPacketStore.Hash(File.ReadAllBytes(Path.Combine(result.Directory, "evidence.json"))),
            receipt.RootElement.GetProperty("evidenceSha256").GetString());
        Assert.AreEqual(StoreHubPreflight.ContractSha256, result.Packet.ContractSha256);
    }

    [TestMethod]
    public async Task SafeGet401RecoveryForbiddenTransientQuotaAndMalformedMetadataHaveBoundedOutcomes()
    {
        foreach (string kind in new[] { "401-once", "401-always", "403", "429", "500", "503-once", "redirect", "count", "retry-after", "oversize", "budget", "generation" })
        {
            using Fixture fixture = new();
            fixture.Kind = kind;
            StoreHubDiscoveryResult result = await fixture.RunAsync(maximumRequests: kind == "budget" ? 1 : 24);
            Assert.AreEqual(kind is "401-once" or "503-once" ? 0 : 2, result.Packet.ExitCode, kind);
            Assert.IsTrue(fixture.Calls <= 4, kind);
            if (kind == "401-once") { Assert.AreEqual(4, fixture.Calls); }
            if (kind == "403") { Assert.AreEqual("forbidden-or-association-missing", result.Packet.Outcome); }
            if (kind == "generation") { Assert.AreEqual(0, fixture.Calls); }
        }
    }

    [TestMethod]
    public async Task RepeatedDiscoveryPreservesPreviousPacketsAndRejectsCredentialRotation()
    {
        using Fixture fixture = new();
        StoreHubDiscoveryResult first = await fixture.RunAsync();
        byte[] before = File.ReadAllBytes(Path.Combine(first.Directory, "evidence.json"));
        StoreHubDiscoveryResult second = await fixture.RunAsync();
        Assert.AreNotEqual(first.Directory, second.Directory);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Path.Combine(first.Directory, "evidence.json")));
        int calls = fixture.Calls;
        fixture.Credentials.Rotate = true;
        StoreHubDiscoveryResult changed = await fixture.RunAsync();
        Assert.AreEqual("credential-generation-changed", changed.Packet.Outcome);
        Assert.AreEqual(calls, fixture.Calls);
    }

    [TestMethod]
    public async Task CancellationAndRecordingFailureCannotAdvertiseMetadataSuccess()
    {
        using Fixture cancelled = new();
        using CancellationTokenSource cancellation = new();
        cancelled.Cancel = cancellation;
        StoreHubDiscoveryResult result = await cancelled.RunAsync(cancellation.Token);
        Assert.AreEqual(130, result.Packet.ExitCode);
        Assert.IsNull(result.Packet.Metadata);
        using Fixture failed = new();
        await Assert.ThrowsAsync<IOException>(() => failed.RunAsync(writer: new FailingWriter()));
        Assert.AreEqual(0, Directory.GetFiles(failed.Experiment, "receipt.json", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task DiscoveryBoundaryRejectsCreationSubmissionAndOffOriginBeforeTransmission()
    {
        AuthorizationContext context = StoreHubPreflight.Select(new() { SchemaVersion = 2, Publishers = [InventoryModelTests.Profile()] }, Sample);
        InventoryHttpTests.Handler inner = new(_ => throw new AssertFailedException("Unexpected network."));
        using StoreHubDiscoveryTransport boundary = new(inner, context, TimeProvider.System);
        using HttpClient client = new(boundary);
        foreach (string uri in new[] { StoreHubMetadata.Endpoint.Replace("ScheduledDataset", "ScheduledQueries"),
            "https://manage.devcenter.microsoft.com/v1.0/my/applications", "https://example.com/consumer/insights/v1.1/ScheduledDataset?scope=consumer" })
        { await Assert.ThrowsAsync<InventoryException>(() => client.GetAsync(uri)); }
        await Assert.ThrowsAsync<InventoryException>(() => client.PostAsync(StoreHubMetadata.Endpoint, new StringContent("PRIVATE-CANARY")));
        Assert.AreEqual(0, inner.Calls);
    }

    internal sealed class FailingWriter : IAtomicFileWriter
    { public Task WriteAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken) => throw new IOException("PRIVATE-CANARY"); }

    internal sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory temporary = new();
        internal StoreHubInput Sample { get; } = StoreHubDiscoveryTests.Sample;
        internal string Root { get; }
        internal string Configuration => Path.Combine(Root, "publishers.json");
        internal string Mapping => Path.Combine(Root, "references.json");
        internal string Experiment => Path.Combine(Root, Sample.ExperimentId);
        internal string Input => Path.Combine(Experiment, "input.json");
        internal StoreSubscriptionDiagnosticTests.CountingCredentials Credentials { get; } = new();
        internal int Calls { get; private set; }
        internal string Kind { get; set; } = "normal";
        internal CancellationTokenSource? Cancel { get; set; }
        private int datasetCalls;
        internal Fixture()
        {
            WindowsPrivateStorageGuard guard = new();
            Root = guard.PrepareDirectory(Path.Combine(temporary.Path, "private"));
            guard.PrepareDirectory(Experiment);
            PublisherProfile profile = InventoryModelTests.Profile();
            File.WriteAllText(Configuration, JsonSerializer.Serialize(new PublisherConfiguration { SchemaVersion = 2, Publishers = [profile] }, StoreHubInput.JsonOptions));
            File.WriteAllText(Input, JsonSerializer.Serialize(Sample, StoreHubInput.JsonOptions));
            File.WriteAllText(Mapping, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                references = new Dictionary<string, object> { [profile.Authorization[ApiResources.Store].CredentialReference] = new { target = "FICTIONAL-CREDENTIAL", generation = "1" } }
            }));
        }
        internal Task<StoreHubDiscoveryResult> RunAsync(CancellationToken cancellation = default, int maximumRequests = 24, IAtomicFileWriter? writer = null)
        {
            if (Kind == "generation") { Credentials.Rotate = true; }
            return StoreHubDiscovery.RunAsync(Configuration, Input, Mapping, Experiment, new WindowsPrivateStorageGuard(), Credentials,
                new InventoryHttpTests.Handler(Respond), new StoreDiagnosticTransportTests.Clock(), writer ?? new AtomicFileWriter(new NewFileCommitter()),
                cancellation, (_, _) => Task.CompletedTask, maximumRequests: maximumRequests);
        }
        private Task<HttpResponseMessage> Respond(HttpRequestMessage request)
        {
            Calls++;
            if (request.Method == HttpMethod.Post)
            {
                Assert.AreEqual("login.microsoftonline.com", request.RequestUri!.Host);
                return Task.FromResult(InventoryHttpTests.Json("{\"access_token\":\"FICTIONAL-TOKEN\",\"token_type\":\"Bearer\",\"expires_in\":3600}"));
            }
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual(StoreHubMetadata.Endpoint, request.RequestUri!.AbsoluteUri);
            Assert.IsNull(request.Content);
            datasetCalls++;
            Cancel?.Cancel();
            HttpStatusCode status = Kind switch
            {
                "401-always" => HttpStatusCode.Unauthorized,
                "401-once" when datasetCalls == 1 => HttpStatusCode.Unauthorized,
                "403" => HttpStatusCode.Forbidden,
                "429" or "retry-after" => HttpStatusCode.TooManyRequests,
                "500" => HttpStatusCode.InternalServerError,
                "503-once" when datasetCalls == 1 => HttpStatusCode.ServiceUnavailable,
                "redirect" => HttpStatusCode.Redirect,
                _ => HttpStatusCode.OK
            };
            HttpResponseMessage response = status == HttpStatusCode.OK ? InventoryHttpTests.Json(Kind == "count" ?
                MetadataJson.Replace("\"totalCount\":2", "\"totalCount\":3") : MetadataJson) : new(status) { Content = new StringContent("RAW-PRIVATE-CANARY") };
            if (Kind == "retry-after") { response.Headers.RetryAfter = new(TimeSpan.FromSeconds(31)); }
            if (Kind == "oversize") { response.Content.Headers.ContentLength = 4 * 1024 * 1024 + 1; }
            return Task.FromResult(response);
        }
        public void Dispose() => temporary.Dispose();
    }
}
