using System.Net;
using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class StoreHubQueryTests
{
    private static readonly StoreHubInput Sample = StoreHubDiscoveryTests.Sample;
    private static readonly string Binding = new('a', 64);
    internal static StoreHubMetadata Metadata => new([
        new("Installs", ["ProductId", "Date"], ["SuccessfulInstallCount"], ["LAST_30_DAYS", "LAST_3_MONTHS"], 24, null,
            new Dictionary<string, string?>(), new Dictionary<string, string?> { ["Daily"] = "LAST_30_DAYS,LAST_3_MONTHS" }),
        new("AddOnAcquisitions", ["ProductId", "ParentProductId", "DateStamp"], ["PurchaseQuantity"], ["LAST_30_DAYS"], 24, null,
            new Dictionary<string, string?>(), new Dictionary<string, string?> { ["Daily"] = "LAST_30_DAYS" })], 2);
    private static StoreHubQueryPlan Plan(string dataset = "Installs") => StoreHubQueryPlan.Prepare(Metadata, Sample, dataset,
        new StoreDiagnosticTransportTests.Clock(), Binding);

    [TestMethod]
    public void DiscoverySourceAuthorityIncludesExecutingAndReviewedHistoricalAssemblies()
    {
        IReadOnlySet<string> approved = StoreHubDiscoveryReader.ReviewedDiscoveryAssemblies();
        Assert.IsTrue(approved.Contains(StoreDiagnosticPacketStore.Hash(File.ReadAllBytes(typeof(StoreHubDiscoveryReader).Assembly.Location))));
        Assert.IsTrue(approved.Count >= 2);
        Assert.IsTrue(approved.All(hash => hash.Length == 64 && hash.All(char.IsAsciiHexDigit)));
    }

    [TestMethod]
    public void LiveSpellingEmptyFilterMapMinimalScopeAndFixedDatesProduceNoRollingFallback()
    {
        StoreHubQueryPlan installs = Plan(), addons = Plan("AddOnAcquisitions");
        Assert.IsTrue(Metadata.Datasets[0].SchemaPresent);
        Assert.AreEqual("SELECT ProductId, Date, SuccessfulInstallCount FROM Installs WHERE ProductId IN ('APP-FICTIONAL') AGGREGATED Daily", installs.Query);
        Assert.AreEqual("SELECT ProductId, ParentProductId, PurchaseQuantity FROM AddOnAcquisitions WHERE ProductId IN ('SUB-FICTIONAL') AND ParentProductId = 'APP-FICTIONAL' AGGREGATED Daily", addons.Query);
        Assert.IsFalse(addons.Query.Contains("DateStamp", StringComparison.Ordinal));
        Assert.IsFalse(installs.Query.Contains("TIMESPAN", StringComparison.Ordinal));
        Assert.IsFalse(installs.Query.Contains("'all'", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(installs.PreviewUri.Query, "queryStartTime=2026-10-08");
        StringAssert.Contains(installs.PreviewUri.Query, "queryEndTime=2026-10-09");
        Assert.IsFalse(installs.ToString().Contains("FICTIONAL", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MissingDatesDriftedMetricUnknownFilterContractUnsafeIdentifiersAndAllScopeAreBlocked()
    {
        StoreHubDataset original = Metadata.Datasets[0];
        foreach (StoreHubDataset changed in new[] { original with { AvailableDateRanges = null }, original with { ColumnFilters = null },
            original with { AvailableMetrics = ["SuccessfullInstallCount"] }, original with { SelectableColumns = ["ProductId"] },
            original with { AggregationToDateRangeMapping = new Dictionary<string, string?> { ["Daily"] = "UNKNOWN" } } })
        {
            Assert.Throws<InventoryException>(() => StoreHubQueryPlan.Prepare(new([changed], 1), Sample, "Installs", new StoreDiagnosticTransportTests.Clock(), Binding));
        }
        foreach (string id in new[] { "all", "ALL", "../PRIVATE-ID-CANARY", "APP' OR '1'='1" })
        {
            Assert.Throws<Console.AnalyticsET.Cli.InputException>(() => StoreHubQueryPlan.Prepare(Metadata, Sample with { ParentApplicationStoreId = id },
                "Installs", new StoreDiagnosticTransportTests.Clock(), Binding));
        }
        Assert.Throws<InventoryException>(() => StoreHubQueryPlan.Prepare(Metadata, Sample, "Acquisitions", new StoreDiagnosticTransportTests.Clock(), Binding));
    }

    [TestMethod]
    public void CompatibleOwnedOrSystemQueryRequiresLiteralOrExtractedEffectiveProductAndParentScope()
    {
        foreach (string dataset in new[] { "Installs", "AddOnAcquisitions" })
        {
            StoreHubQueryPlan plan = Plan(dataset);
            foreach (string kind in new[] { "system", "userDefined" })
            {
                using JsonDocument literal = JsonDocument.Parse(JsonSerializer.Serialize(new
                { queryId = "11111111-2222-4333-8444-555555555555", type = kind, query = plan.Query.ToLowerInvariant() }));
                Assert.IsTrue(plan.CompatibleQuery(literal.RootElement));
                using JsonDocument normalized = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    queryId = "11111111-2222-4333-8444-555555555555",
                    type = kind,
                    query = plan.NormalizedQuery,
                    ProductInfo = new { productId = plan.ProductId, productIdDbColumnName = "ProductId", productGroupId = "" }
                }));
                Assert.IsTrue(plan.CompatibleQuery(normalized.RootElement));
            }
            foreach (string incorrect in new[] { "all", "OTHER-FICTIONAL" })
            {
                using JsonDocument wrong = JsonDocument.Parse(JsonSerializer.Serialize(new
                { queryId = "11111111-2222-4333-8444-555555555555", query = plan.NormalizedQuery, productInfo = new { productId = incorrect, productIdDbColumnName = "ProductId" } }));
                Assert.IsFalse(plan.CompatibleQuery(wrong.RootElement));
            }
            foreach (string wrongQuery in new[] { plan.Query + " TIMESPAN LAST_30_DAYS", plan.NormalizedQuery,
                plan.Query.Replace("APP-FICTIONAL", "OTHER-FICTIONAL", StringComparison.Ordinal), plan.Query.Replace("Daily", "Monthly", StringComparison.Ordinal) })
            {
                using JsonDocument wrong = JsonDocument.Parse(JsonSerializer.Serialize(new { queryId = "11111111-2222-4333-8444-555555555555", query = wrongQuery }));
                Assert.IsFalse(plan.CompatibleQuery(wrong.RootElement));
            }
        }
    }

    [TestMethod]
    public void CreationRequiresBoundSampleSchemaProductParentPopulatedAndKnownFixedUtcConvention()
    {
        StoreHubQueryPlan plan = Plan("AddOnAcquisitions");
        StoreHubScopeProof proof = new(plan.Dataset, plan.MetadataSha256, StoreDiagnosticPacketStore.Hash(System.Text.Encoding.UTF8.GetBytes(plan.Query)),
            plan.Window, true, true, true, true, true, plan.SampleFingerprint, plan.BindingFingerprint);
        plan.RequireCreationProof(proof);
        foreach (StoreHubScopeProof unverified in new[] { proof with { UtcBoundaryConventionVerified = false }, proof with { FixedDatesVerified = false },
            proof with { ParentMatches = false }, proof with { ProductMatches = false }, proof with { PopulatedPreview = false },
            proof with { BindingFingerprint = new string('b', 64) }, proof with { SampleFingerprint = "other-experiment" },
            proof with { MetadataSha256 = "old-schema" }, proof with { Window = new("2026-10-07T00:00:00Z", "2026-10-10T00:00:00Z") } })
        { Assert.Throws<InventoryException>(() => plan.RequireCreationProof(unverified)); }
    }

    [TestMethod]
    public void PreviewRecordsMinimalObservationsWithoutRowsAndKeepsEmptyAndBoundaryUnverified()
    {
        using JsonDocument observed = JsonDocument.Parse(PreviewJson());
        StoreHubPreview preview = StoreHubPreview.Observe(observed.RootElement, Plan());
        Assert.AreEqual(1, preview.RowCount);
        Assert.AreEqual(true, preview.ProductMatches);
        Assert.AreEqual(true, preview.ObservedDatesWithinRequestedBounds);
        Assert.AreEqual("unverified", preview.BoundaryState);
        StoreHubPreviewShape shape = StoreHubPreviewShape.Observe(observed.RootElement, Plan());
        Assert.AreEqual("Array", shape.ValueKind);
        Assert.AreEqual(3, shape.FirstRowFieldCount);
        Assert.IsFalse(JsonSerializer.Serialize(shape).Contains("APP-FICTIONAL", StringComparison.Ordinal));
        string serialized = JsonSerializer.Serialize(preview);
        Assert.IsFalse(serialized.Contains("APP-FICTIONAL", StringComparison.Ordinal));
        Assert.IsFalse(serialized.Contains("PRIVATE-MESSAGE-CANARY", StringComparison.Ordinal));
        using JsonDocument empty = JsonDocument.Parse("{\"value\":[],\"totalCount\":0,\"statusCode\":200}");
        StoreHubPreview noRows = StoreHubPreview.Observe(empty.RootElement, Plan());
        Assert.AreEqual("empty", noRows.DataState);
        Assert.IsNull(noRows.ProductMatches);
    }

    [TestMethod]
    public void WrongProductsParentsDatesMalformedMetricsUnexpectedColumnsAndIncompleteCountsRejectPreview()
    {
        foreach (string invalid in new[] { PreviewJson().Replace("APP-FICTIONAL", "OTHER-FICTIONAL"),
            PreviewJson().Replace("2026-10-08", "2026-10-09"), PreviewJson().Replace("2026-10-08", "2026-10-07"),
            PreviewJson().Replace("\"SuccessfulInstallCount\":1", "\"SuccessfulInstallCount\":\"PRIVATE-METRIC-CANARY\""),
            PreviewJson().Replace("\"SuccessfulInstallCount\":1", "\"SuccessfulInstallCount\":1.2"),
            PreviewJson().Replace("\"SuccessfulInstallCount\":1", "\"SuccessfulInstallCount\":-1"),
            PreviewJson().Replace("\"totalCount\":1", "\"totalCount\":2"),
            PreviewJson().Replace("\"Date\":", "\"UnexpectedColumn\":\"PRIVATE-METRIC-CANARY\",\"Date\":") })
        {
            using JsonDocument wrong = JsonDocument.Parse(invalid);
            InventoryException error = Assert.Throws<InventoryException>(() => StoreHubPreview.Observe(wrong.RootElement, Plan()));
            Assert.IsFalse(error.Message.Contains("PRIVATE-METRIC-CANARY", StringComparison.Ordinal));
        }
        using JsonDocument wrongParent = JsonDocument.Parse("{\"value\":[{\"ProductId\":\"SUB-FICTIONAL\",\"ParentProductId\":\"OTHER-FICTIONAL\",\"PurchaseQuantity\":1}],\"totalCount\":1,\"statusCode\":200}");
        Assert.Throws<InventoryException>(() => StoreHubPreview.Observe(wrongParent.RootElement, Plan("AddOnAcquisitions")));
    }

    [TestMethod]
    public async Task VerifiedDiscoveryAndReadOnlyProbePreserveOriginalEvidenceAndNeverCreateResources()
    {
        using StoreHubDiscoveryTests.Fixture fixture = new();
        StoreHubDiscoveryResult discovery = await DiscoverNativeAsync(fixture);
        byte[] original = File.ReadAllBytes(Path.Combine(discovery.Directory, "evidence.json"));
        int calls = 0;
        InventoryHttpTests.Handler handler = new(request =>
        {
            calls++;
            if (request.Method == HttpMethod.Post)
            {
                Assert.AreEqual("login.microsoftonline.com", request.RequestUri!.Host);
                return Task.FromResult(InventoryHttpTests.Json("{\"access_token\":\"FICTIONAL-TOKEN\",\"token_type\":\"Bearer\",\"expires_in\":3600}"));
            }
            Assert.AreEqual("/consumer/insights/v1.1/ScheduledQueries/testQueryResult", request.RequestUri!.AbsolutePath);
            return Task.FromResult(InventoryHttpTests.Json(PreviewJson()));
        });
        StoreHubQueryProbeResult result = await StoreHubQueryProbe.RunAsync(fixture.Configuration, fixture.Input, fixture.Mapping, fixture.Experiment,
            discovery.Directory, new HashSet<string> { discovery.Packet.SourceSha256 }, new WindowsPrivateStorageGuard(), fixture.Credentials,
            handler, new StoreDiagnosticTransportTests.Clock(), new AtomicFileWriter(new NewFileCommitter()), default, (_, _) => Task.CompletedTask);
        Assert.AreEqual(2, result.Packet.ExitCode);
        Assert.AreEqual("hub-date-boundary-unverified", result.Packet.Outcome);
        Assert.AreEqual(2, calls);
        Assert.AreEqual(true, result.Packet.Preview?.ProductMatches);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(discovery.Directory, "evidence.json")));
        string projection = JsonSerializer.Serialize(result.Packet.PublicProjection());
        foreach (string canary in new[] { "APP-FICTIONAL", "FICTIONAL-TOKEN", "PRIVATE-MESSAGE-CANARY", "SELECT", "2026-10-08" })
        { Assert.IsFalse(projection.Contains(canary, StringComparison.Ordinal)); }
        Assert.IsTrue(File.Exists(Path.Combine(result.Directory, "receipt.json")));
    }

    [TestMethod]
    public async Task MissingCorruptCrossScopeOrUnapprovedDiscoveryStopsBeforeCredentialAndHttp()
    {
        foreach (string invalid in new[] { "corrupt", "assembly", "scope", "partial" })
        {
            using StoreHubDiscoveryTests.Fixture fixture = new();
            StoreHubDiscoveryResult discovery = await DiscoverNativeAsync(fixture);
            int credentialCalls = fixture.Credentials.Contexts.Count;
            if (invalid == "corrupt") { await File.AppendAllTextAsync(Path.Combine(discovery.Directory, "evidence.json"), "PRIVATE-CANARY"); }
            if (invalid == "partial") { File.Delete(Path.Combine(discovery.Directory, "receipt.json")); }
            if (invalid == "scope") { await File.WriteAllTextAsync(fixture.Input, JsonSerializer.Serialize(fixture.Sample with { ParentApplicationStoreId = "OTHER-FICTIONAL" }, StoreHubInput.JsonOptions)); }
            await Assert.ThrowsExactlyAsync<Console.AnalyticsET.Cli.InputException>(async () =>
            {
                try
                {
                    await StoreHubQueryProbe.RunAsync(fixture.Configuration, fixture.Input, fixture.Mapping, fixture.Experiment,
                        discovery.Directory, new HashSet<string> { invalid == "assembly" ? new string('f', 64) : discovery.Packet.SourceSha256 },
                        new WindowsPrivateStorageGuard(), fixture.Credentials, new InventoryHttpTests.Handler(_ => throw new AssertFailedException("Unexpected HTTP")),
                        new StoreDiagnosticTransportTests.Clock(), new AtomicFileWriter(new NewFileCommitter()), default);
                }
                catch (Exception error) when (error is JsonException or FileNotFoundException) { throw StoreHubInput.Invalid(); }
            });
            Assert.AreEqual(credentialCalls, fixture.Credentials.Contexts.Count);
        }
    }

    [TestMethod]
    public void ExplicitUtcWireControlKeepsScopeAndInstantsAndCannotReuseDateOnlyCreationProof()
    {
        StoreHubQueryPlan dateOnly = Plan();
        StoreHubQueryPlan utc = StoreHubQueryPlan.Prepare(Metadata, Sample, "Installs", new StoreDiagnosticTransportTests.Clock(), Binding, "utc-timestamp");
        Assert.AreEqual(dateOnly.Query, utc.Query);
        Assert.AreEqual(dateOnly.Window, utc.Window);
        Assert.AreEqual(dateOnly.SampleFingerprint, utc.SampleFingerprint);
        Assert.AreEqual(dateOnly.MetadataSha256, utc.MetadataSha256);
        Assert.AreEqual(Sample.InstallsWindow.StartUtc, utc.QueryStartTime);
        Assert.AreEqual(Sample.InstallsWindow.EndUtc, utc.QueryEndTime);
        StringAssert.Contains(utc.PreviewUri.Query, "queryStartTime=2026-10-08T00%3A00%3A00Z");
        StringAssert.Contains(utc.PreviewUri.Query, "queryEndTime=2026-10-09T00%3A00%3A00Z");
        StoreHubScopeProof proof = new(utc.Dataset, utc.MetadataSha256, StoreDiagnosticPacketStore.Hash(System.Text.Encoding.UTF8.GetBytes(utc.Query)),
            utc.Window, true, null, true, true, true, utc.SampleFingerprint, utc.BindingFingerprint);
        Assert.Throws<InventoryException>(() => utc.RequireCreationProof(proof));
        utc.RequireCreationProof(proof with { DateWireFormat = "utc-timestamp" });
        Assert.Throws<InventoryException>(() => StoreHubQueryPlan.Prepare(Metadata, Sample, "Installs", new StoreDiagnosticTransportTests.Clock(), Binding, "PRIVATE-CANARY"));
        Assert.Throws<InventoryException>(() => StoreHubQueryPlan.Prepare(Metadata, Sample, "AddOnAcquisitions", new StoreDiagnosticTransportTests.Clock(), Binding, "utc-timestamp"));
    }

    [TestMethod]
    [DataRow("2026-10-08T00:00:00Z", true)]
    [DataRow("2026-10-08T00:00:00.000Z", true)]
    [DataRow("2026-10-08T01:00:00Z", false)]
    [DataRow("2026-10-08T00:00:00", false)]
    [DataRow("10/8/2026", false)]
    [DataRow("2026-10-09T00:00:00Z", false)]
    public void UtcMidnightPreviewDatesAreObservedWithoutAcceptingLocaleZoneOrBoundaryGuesses(string date, bool accepted)
    {
        StoreHubQueryPlan plan = Plan() with { DateWireFormat = "utc-timestamp" };
        using JsonDocument document = JsonDocument.Parse(PreviewJson().Replace("2026-10-08", date, StringComparison.Ordinal));
        if (accepted)
        {
            StoreHubPreview observation = StoreHubPreview.Observe(document.RootElement, plan);
            Assert.AreEqual(true, observation.ObservedDatesWithinRequestedBounds);
            Assert.AreEqual("unverified", observation.BoundaryState);
        }
        else { Assert.Throws<InventoryException>(() => StoreHubPreview.Observe(document.RootElement, plan)); }
    }

    [TestMethod]
    public async Task ExplicitUtcControlMakesOnlyTheBoundTokenAndPreviewCallsAndPreservesInputAndEarlierPacket()
    {
        using StoreHubDiscoveryTests.Fixture fixture = new();
        StoreHubDiscoveryResult discovery = await DiscoverNativeAsync(fixture);
        byte[] inputBefore = File.ReadAllBytes(fixture.Input), evidenceBefore = File.ReadAllBytes(Path.Combine(discovery.Directory, "evidence.json"));
        int calls = 0;
        using InventoryHttpTests.Handler handler = new(request =>
        {
            calls++;
            if (request.Method == HttpMethod.Post)
            {
                Assert.AreEqual("login.microsoftonline.com", request.RequestUri!.Host);
                return Task.FromResult(InventoryHttpTests.Json("{\"access_token\":\"FICTIONAL-TOKEN\",\"token_type\":\"Bearer\",\"expires_in\":3600}"));
            }
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("/consumer/insights/v1.1/ScheduledQueries/testQueryResult", request.RequestUri!.AbsolutePath);
            StringAssert.Contains(request.RequestUri.Query, "T00%3A00%3A00Z");
            return Task.FromResult(InventoryHttpTests.Json(PreviewJson().Replace("2026-10-08", "2026-10-08T00:00:00.000Z", StringComparison.Ordinal)));
        });
        StoreHubQueryProbeResult result = await StoreHubQueryProbe.RunAsync(fixture.Configuration, fixture.Input, fixture.Mapping, fixture.Experiment,
            discovery.Directory, new HashSet<string> { discovery.Packet.SourceSha256 }, new WindowsPrivateStorageGuard(), fixture.Credentials,
            handler, new StoreDiagnosticTransportTests.Clock(), new AtomicFileWriter(new NewFileCommitter()), default,
            (_, _) => Task.CompletedTask, "utc-timestamp");
        Assert.AreEqual(2, calls);
        Assert.AreEqual(2, result.Packet.ExitCode);
        Assert.AreEqual(1, result.Packet.Preview?.RowCount);
        Assert.AreEqual("utc-timestamp", result.Packet.Plan.DateWireFormat);
        CollectionAssert.AreEqual(inputBefore, File.ReadAllBytes(fixture.Input));
        CollectionAssert.AreEqual(evidenceBefore, File.ReadAllBytes(Path.Combine(discovery.Directory, "evidence.json")));
        string projection = JsonSerializer.Serialize(result.Packet.PublicProjection());
        foreach (string canary in new[] { "APP-FICTIONAL", "FICTIONAL-TOKEN", "SELECT", "2026-10-08" })
        { Assert.IsFalse(projection.Contains(canary, StringComparison.Ordinal)); }
    }

    internal static string PreviewJson() => "{\"value\":[{\"ProductId\":\"APP-FICTIONAL\",\"Date\":\"2026-10-08\",\"SuccessfulInstallCount\":1}],\"totalCount\":1,\"statusCode\":200,\"message\":\"PRIVATE-MESSAGE-CANARY\"}";
    internal static Task<StoreHubDiscoveryResult> DiscoverNativeAsync(StoreHubDiscoveryTests.Fixture fixture) => StoreHubDiscovery.RunAsync(
        fixture.Configuration, fixture.Input, fixture.Mapping, fixture.Experiment, new WindowsPrivateStorageGuard(), fixture.Credentials,
        new InventoryHttpTests.Handler(request => Task.FromResult(request.Method == HttpMethod.Post ?
            InventoryHttpTests.Json("{\"access_token\":\"FICTIONAL-TOKEN\",\"token_type\":\"Bearer\",\"expires_in\":3600}") :
            InventoryHttpTests.Json(JsonSerializer.Serialize(new { value = Metadata.Datasets, totalCount = 2, statusCode = 200 }, StoreHubInput.JsonOptions)))),
        new StoreDiagnosticTransportTests.Clock(), new AtomicFileWriter(new NewFileCommitter()), default);
}
