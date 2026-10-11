using System.Net;
using System.Text;
using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class StoreHubCreationTests
{
    [TestMethod]
    public async Task BothDatasetsCommitIntentBeforeOneSendAndRecordExactQueryAndOneTimeReportWithoutReplay()
    {
        foreach (string dataset in new[] { "Installs", "AddOnAcquisitions" })
        {
            using Fixture fixture = new(dataset);
            StoreHubCreationJournal query = await fixture.CreateAsync("query");
            Assert.AreEqual("recorded", query.State);
            Assert.AreEqual(fixture.QueryId, query.ResourceId);
            StoreHubCreationJournal report = await fixture.CreateAsync("report");
            Assert.AreEqual("recorded", report.State);
            Assert.AreEqual(fixture.ReportId, report.ResourceId);
            Assert.AreEqual(fixture.QueryId, report.QueryId);
            Assert.AreEqual(2, fixture.Handler.Calls);
            Assert.AreEqual(2, fixture.Tokens.Calls);
            await fixture.CreateAsync("query");
            await fixture.CreateAsync("report");
            Assert.AreEqual(2, fixture.Handler.Calls);
            Assert.AreEqual(2, fixture.Tokens.Calls);
            string projection = JsonSerializer.Serialize(report.PublicProjection());
            Assert.IsFalse(projection.Contains(fixture.ReportId, StringComparison.Ordinal));
            Assert.IsFalse(projection.Contains("FICTIONAL", StringComparison.Ordinal));
            Assert.IsFalse(report.ToString().Contains("FICTIONAL", StringComparison.Ordinal));
            foreach (string file in Directory.GetFiles(fixture.Experiment, "*-creation.json"))
            { Assert.IsFalse(File.ReadAllText(file).Contains("SECRET-POST-CANARY", StringComparison.Ordinal)); }
        }
    }

    [TestMethod]
    public async Task AuthQuotaTransientAndRedirectStatusesNeverReplayCreationOrRenewTheToken()
    {
        foreach (int status in new[] { 401, 403, 429, 500, 503, 302 })
        {
            using Fixture fixture = new() { Status = status };
            StoreHubCreationJournal result = await fixture.CreateAsync("query");
            Assert.AreEqual("creation-uncertain", result.State);
            Assert.AreEqual(status, result.HttpStatus);
            Assert.AreEqual(2, result.ExitCode);
            await fixture.CreateAsync("query");
            Assert.AreEqual(1, fixture.Handler.Calls);
            Assert.AreEqual(1, fixture.Tokens.Calls);
            Assert.IsFalse(File.ReadAllText(fixture.PathFor("query")).Contains("RAW-RESPONSE-CANARY", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task LostResponseMalformedIdsWrongScopeAndUnboundedBodiesRemainUncertain()
    {
        foreach (string mode in new[] { "lost", "bad-id", "wrong-name", "all-products", "duplicate", "count", "oversize", "response-uri" })
        {
            using Fixture fixture = new() { Mode = mode };
            StoreHubCreationJournal result = await fixture.CreateAsync("query");
            Assert.AreEqual("creation-uncertain", result.State, mode);
            Assert.IsNull(result.ResourceId, mode);
            await fixture.CreateAsync("query");
            Assert.AreEqual(1, fixture.Handler.Calls, mode);
            Assert.AreEqual(1, fixture.Tokens.Calls, mode);
        }
    }

    [TestMethod]
    public async Task ReportRequiresRecordedSameScopeQueryAndValidatedDatesOneTimeFieldsAndIdentity()
    {
        using (Fixture absent = new())
        {
            await Assert.ThrowsAsync<InventoryException>(() => absent.CreateAsync("report"));
            Assert.AreEqual(0, absent.Handler.Calls);
            Assert.AreEqual(0, absent.Tokens.Calls);
        }
        foreach (string mode in new[] { "wrong-query", "date", "recurring", "callback", "not-execute-now", "format", "bad-id", "all-products" })
        {
            using Fixture fixture = new();
            await fixture.CreateAsync("query");
            fixture.Mode = mode;
            StoreHubCreationJournal result = await fixture.CreateAsync("report");
            Assert.AreEqual("creation-uncertain", result.State, mode);
            await fixture.CreateAsync("report");
            Assert.AreEqual(2, fixture.Handler.Calls, mode);
            Assert.AreEqual(2, fixture.Tokens.Calls, mode);
        }
    }

    [TestMethod]
    [DataRow("date-only")]
    [DataRow("utc-timestamp")]
    public async Task ReportDateEchoMayBeOmittedOrUseTheOtherMidnightFormButNeverADifferentBoundary(string dateWireFormat)
    {
        foreach ((string mode, string outcome) in new[] { ("valid", "creation-recorded"), ("dates-other-form", "creation-recorded"),
            ("dates-null", "creation-recorded-dates-not-echoed"), ("dates-absent", "creation-recorded-dates-not-echoed") })
        {
            using Fixture fixture = new(dateWireFormat: dateWireFormat);
            await fixture.CreateAsync("query");
            fixture.Mode = mode;
            StoreHubCreationJournal report = await fixture.CreateAsync("report");
            Assert.AreEqual("recorded", report.State, mode);
            Assert.AreEqual(outcome, report.Outcome, mode);
            Assert.AreEqual(0, report.ExitCode, mode);
            Assert.AreEqual(fixture.ReportId, report.ResourceId, mode);
            StoreHubCreationJournal reread = await fixture.CreateAsync("report");
            Assert.AreEqual(outcome, reread.Outcome, mode);
            Assert.AreEqual(2, fixture.Handler.Calls, mode);
            Assert.AreEqual(2, fixture.Tokens.Calls, mode);
        }
        foreach (string mode in new[] { "date", "dates-non-midnight", "dates-number" })
        {
            using Fixture fixture = new(dateWireFormat: dateWireFormat);
            await fixture.CreateAsync("query");
            fixture.Mode = mode;
            StoreHubCreationJournal report = await fixture.CreateAsync("report");
            Assert.AreEqual("creation-uncertain", report.State, mode);
            Assert.IsNull(report.ResourceId, mode);
            await fixture.CreateAsync("report");
            Assert.AreEqual(2, fixture.Handler.Calls, mode);
        }
    }

    [TestMethod]
    public async Task UnverifiedScopeChangedBindingBroadQueryAndUnsafeTransportFailBeforeIntentOrToken()
    {
        using Fixture fixture = new();
        await Assert.ThrowsAsync<InventoryException>(() => fixture.CreateAsync("query", proof: fixture.Proof with { UtcBoundaryConventionVerified = false }));
        await Assert.ThrowsAsync<InventoryException>(() => fixture.CreateAsync("query", context: fixture.Context with { CredentialGeneration = "changed" }));
        StoreHubQueryPlan broad = fixture.Plan with { Query = fixture.Plan.Query.Replace("APP-FICTIONAL", "all", StringComparison.Ordinal) };
        await Assert.ThrowsAsync<InventoryException>(() => fixture.CreateAsync("query", plan: broad, proof: Proof(broad)));
        await Assert.ThrowsAsync<InventoryException>(() => fixture.CreateAsync("delete"));
        StoreHubQueryPlan invalidWire = fixture.Plan with { DateWireFormat = "PRIVATE-CANARY" };
        await Assert.ThrowsAsync<InventoryException>(() => fixture.CreateAsync("query", plan: invalidWire,
            proof: Proof(invalidWire) with { DateWireFormat = invalidWire.DateWireFormat }));
        StoreHubQueryPlan invalidAddonWire = StoreHubQueryPlan.Prepare(StoreHubQueryTests.Metadata, fixture.Sample, "AddOnAcquisitions",
            fixture.Clock, fixture.Context.StorageKey) with
        { DateWireFormat = "utc-timestamp" };
        await Assert.ThrowsAsync<InventoryException>(() => fixture.CreateAsync("query", plan: invalidAddonWire,
            proof: Proof(invalidAddonWire) with { DateWireFormat = invalidAddonWire.DateWireFormat }));
        Assert.AreEqual(0, fixture.Handler.Calls);
        Assert.AreEqual(0, fixture.Tokens.Calls);
        Assert.IsFalse(File.Exists(fixture.PathFor("query")));
        using HttpClientHandler unsafeHandler = new();
        Assert.Throws<InventoryException>(() => new StoreHubCreation(unsafeHandler, fixture.Tokens, fixture.Privacy,
            fixture.Writer, fixture.Clock));
        using HttpClientHandler safe = StoreHubCreation.CreateHandler();
        Assert.IsFalse(safe.AllowAutoRedirect);
        Assert.IsFalse(safe.UseCookies);
    }

    [TestMethod]
    public async Task WriteFailureBeforeIntentAndAfterIntentOrIdCommitNeverAllowsAnotherSend()
    {
        foreach (string mode in new[] { "before-intent", "after-intent", "after-id", "id-and-uncertain" })
        {
            using Fixture fixture = new();
            fixture.Writer = new FaultWriter(mode);
            if (mode is "before-intent" or "after-intent")
            {
                await Assert.ThrowsAsync<InventoryException>(() => fixture.CreateAsync("query"));
                Assert.AreEqual(0, fixture.Handler.Calls, mode);
                if (mode == "before-intent") { Assert.IsFalse(File.Exists(fixture.PathFor("query"))); }
            }
            else
            {
                StoreHubCreationJournal result = await fixture.CreateAsync("query");
                Assert.AreEqual("creation-uncertain", result.State, mode);
                Assert.AreEqual(1, fixture.Handler.Calls, mode);
            }
            if (mode != "before-intent")
            {
                fixture.Writer = new AtomicFileWriter(new FileCommitter());
                StoreHubCreationJournal resumed = await fixture.CreateAsync("query");
                Assert.AreEqual("creation-uncertain", resumed.State, mode);
                Assert.AreEqual(mode == "after-intent" ? 0 : 1, fixture.Handler.Calls, mode);
            }
        }
    }

    [TestMethod]
    public async Task CancellationBeforeIntentAfterIntentAndDuringSendPreservesTheCreationBarrier()
    {
        using (Fixture before = new())
        {
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => before.CreateAsync("query", cancellationToken: cancelled.Token));
            Assert.AreEqual(0, before.Tokens.Calls);
            Assert.IsFalse(File.Exists(before.PathFor("query")));
        }
        using (Fixture afterIntent = new())
        {
            using CancellationTokenSource cancel = new();
            afterIntent.Writer = new CancelWriter(cancel);
            await Assert.ThrowsAsync<OperationCanceledException>(() => afterIntent.CreateAsync("query", cancellationToken: cancel.Token));
            Assert.AreEqual(0, afterIntent.Handler.Calls);
            afterIntent.Writer = new AtomicFileWriter(new FileCommitter());
            Assert.AreEqual("creation-uncertain", (await afterIntent.CreateAsync("query")).State);
        }
        using Fixture during = new();
        using CancellationTokenSource cancellation = new();
        during.CancelDuringSend = cancellation;
        StoreHubCreationJournal result = await during.CreateAsync("query", cancellationToken: cancellation.Token);
        Assert.AreEqual(130, result.ExitCode);
        Assert.AreEqual("creation-uncertain", result.State);
        await during.CreateAsync("query");
        Assert.AreEqual(1, during.Handler.Calls);
    }

    [TestMethod]
    public async Task CorruptPartialOrChangedJournalIsRejectedAndOneUncertainCreationBlocksOtherOperations()
    {
        foreach (string corruption in new[] { "id", "partial", "duplicate", "unknown", "oversize" })
        {
            using Fixture fixture = new();
            await fixture.CreateAsync("query");
            string path = fixture.PathFor("query"), json = File.ReadAllText(path);
            await File.WriteAllTextAsync(path, corruption switch
            {
                "id" => json.Replace(fixture.QueryId, fixture.ReportId, StringComparison.Ordinal),
                "partial" => "{}",
                "duplicate" => json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1", StringComparison.Ordinal),
                "unknown" => json.Insert(1, "\"unknown\":\"PRIVATE-JOURNAL-CANARY\","),
                _ => new string('a', 16385)
            });
            await Assert.ThrowsAsync<InventoryException>(() => fixture.CreateAsync("report"));
            Assert.AreEqual(1, fixture.Handler.Calls, corruption);
            Assert.AreEqual(1, fixture.Tokens.Calls, corruption);
        }
        using Fixture uncertain = new() { Mode = "lost" };
        await uncertain.CreateAsync("query");
        StoreHubQueryPlan addon = StoreHubQueryPlan.Prepare(StoreHubQueryTests.Metadata, uncertain.Sample, "AddOnAcquisitions",
            uncertain.Clock, uncertain.Context.StorageKey);
        await Assert.ThrowsAsync<InventoryException>(() => uncertain.CreateAsync("query", plan: addon, proof: Proof(addon)));
        Assert.AreEqual(1, uncertain.Handler.Calls);
    }

    [TestMethod]
    public async Task ExclusiveExperimentLockPreventsConcurrentCreationBeforeCredentialAccess()
    {
        using Fixture fixture = new();
        fixture.Privacy.PrepareDirectory(fixture.Experiment);
        using FileStream held = new(Path.Combine(fixture.Experiment, ".experiment.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<InventoryException>(() => fixture.CreateAsync("query"));
        Assert.AreEqual(0, fixture.Handler.Calls);
        Assert.AreEqual(0, fixture.Tokens.Calls);
    }

    private static StoreHubScopeProof Proof(StoreHubQueryPlan plan) => new(plan.Dataset, plan.MetadataSha256,
        StoreDiagnosticPacketStore.Hash(Encoding.UTF8.GetBytes(plan.Query)), plan.Window, true, plan.ParentId is null ? null : true,
        true, true, true, plan.SampleFingerprint, plan.BindingFingerprint, plan.DateWireFormat);

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory temporary = new();
        internal StoreHubInput Sample { get; } = StoreHubDiscoveryTests.Sample;
        internal AuthorizationContext Context { get; } = AuthorizationContext.Create(InventoryModelTests.Profile(), ApiResources.StoreProgram,
            ApiResources.Store) with
        { CredentialGeneration = "fixture-generation" };
        internal TimeProvider Clock { get; } = new StoreDiagnosticTransportTests.Clock();
        internal WindowsPrivateStorageGuard Privacy { get; } = new();
        internal IAtomicFileWriter Writer { get; set; } = new AtomicFileWriter(new FileCommitter());
        internal Tokens Tokens { get; } = new();
        internal InventoryHttpTests.Handler Handler { get; }
        internal StoreHubQueryPlan Plan { get; }
        internal StoreHubScopeProof Proof => StoreHubCreationTests.Proof(Plan);
        internal string Experiment { get; }
        internal string QueryId { get; } = "11111111-2222-4333-8444-555555555555";
        internal string ReportId { get; } = "66666666-7777-4888-8999-000000000000";
        internal string Mode { get; set; } = "valid";
        internal int Status { get; set; } = 200;
        internal CancellationTokenSource? CancelDuringSend { get; set; }

        internal Fixture(string dataset = "Installs", string dateWireFormat = "date-only")
        {
            Experiment = Path.Combine(temporary.Path, Sample.ExperimentId);
            Plan = StoreHubQueryPlan.Prepare(StoreHubQueryTests.Metadata, Sample, dataset, Clock, Context.StorageKey, dateWireFormat);
            Handler = new(async request =>
            {
                bool query = request.RequestUri!.AbsolutePath.EndsWith("/ScheduledQueries", StringComparison.Ordinal);
                string kind = query ? "query" : "report";
                Assert.AreEqual(HttpMethod.Post, request.Method);
                Assert.AreEqual("https://manage.devcenter.microsoft.com", request.RequestUri.GetLeftPart(UriPartial.Authority));
                Assert.AreEqual("?scope=consumer", request.RequestUri.Query);
                Assert.AreEqual("SECRET-POST-CANARY", request.Headers.Authorization?.Parameter);
                StoreHubCreationEnvelope envelope = JsonSerializer.Deserialize<StoreHubCreationEnvelope>(File.ReadAllText(PathFor(kind)), StoreHubInput.JsonOptions)!;
                Assert.AreEqual("creation-intent", envelope.Journal.State);
                string body = await request.Content!.ReadAsStringAsync();
                Assert.AreEqual(envelope.Journal.RequestJson, body);
                using JsonDocument payload = JsonDocument.Parse(body);
                Assert.AreEqual(query ? 2 : 6, payload.RootElement.EnumerateObject().Count());
                if (query) { Assert.AreEqual(Plan.Query, payload.RootElement.GetProperty("query").GetString()); }
                else
                {
                    Assert.AreEqual(QueryId, payload.RootElement.GetProperty("queryId").GetString());
                    Assert.IsTrue(payload.RootElement.GetProperty("executeNow").GetBoolean());
                    Assert.AreEqual("csv", payload.RootElement.GetProperty("format").GetString());
                    Assert.AreEqual(Plan.QueryStartTime, payload.RootElement.GetProperty("queryStartTime").GetString());
                    Assert.AreEqual(Plan.QueryEndTime, payload.RootElement.GetProperty("queryEndTime").GetString());
                }
                Assert.IsFalse(envelope.Journal.Marker.Contains("FICTIONAL", StringComparison.Ordinal));
                if (Mode == "lost") { throw new HttpRequestException("PRIVATE-TRANSPORT-CANARY"); }
                if (CancelDuringSend is not null) { CancelDuringSend.Cancel(); throw new OperationCanceledException(CancelDuringSend.Token); }
                Dictionary<string, object?> resource = new()
                {
                    [query ? "name" : "reportName"] = Mode == "wrong-name" ? "PRIVATE-WRONG-NAME" : envelope.Journal.Marker,
                    ["queryId"] = Mode == "wrong-query" ? ReportId : QueryId,
                    ["query"] = Mode == "all-products" ? Plan.Query.Replace("APP-FICTIONAL", "all", StringComparison.Ordinal) : Plan.Query
                };
                if (query && Mode == "bad-id") { resource["queryId"] = "PRIVATE-ID-CANARY"; }
                if (!query)
                {
                    resource["reportId"] = Mode == "bad-id" ? "PRIVATE-ID-CANARY" : ReportId;
                    resource["executeNow"] = Mode != "not-execute-now";
                    resource["format"] = Mode == "format" ? "tsv" : "csv";
                    if (Mode != "dates-absent")
                    {
                        resource["queryStartTime"] = Mode switch
                        {
                            "date" => "2026-10-01",
                            "dates-null" => null,
                            "dates-other-form" => OtherMidnightForm(Plan.QueryStartTime),
                            "dates-non-midnight" => Plan.QueryStartTime[..10] + "T05:00:00Z",
                            "dates-number" => 20261008,
                            _ => Plan.QueryStartTime
                        };
                        resource["queryEndTime"] = Mode switch
                        {
                            "dates-null" => null,
                            "dates-other-form" => OtherMidnightForm(Plan.QueryEndTime),
                            _ => Plan.QueryEndTime
                        };
                    }
                    resource["recurrenceInterval"] = Mode == "recurring" ? 24 : -1;
                    resource["recurrenceCount"] = 1;
                    resource["callbackUrl"] = Mode == "callback" ? "https://example.com/PRIVATE" : null;
                }
                string json = JsonSerializer.Serialize(new
                {
                    value = new[] { resource },
                    totalCount = Mode == "count" ? 2 : 1,
                    statusCode = 200,
                    message = "RAW-RESPONSE-CANARY"
                });
                if (Mode == "duplicate") { json = json.Insert(1, "\"statusCode\":200,"); }
                HttpResponseMessage response = InventoryHttpTests.Json(json);
                response.StatusCode = (HttpStatusCode)Status;
                response.Headers.TryAddWithoutValidation("Retry-After", "1");
                if (Mode == "oversize") { response.Content.Headers.ContentLength = 4194305; }
                if (Mode == "response-uri") { response.RequestMessage = new(HttpMethod.Post, "https://example.com/PRIVATE"); }
                return response;
            });
        }
        internal string PathFor(string kind) => StoreHubCreation.JournalPath(Experiment, Plan.Dataset, kind);
        private static string OtherMidnightForm(string sent) => sent.Length == 10 ? sent + "T00:00:00Z" : sent[..10];
        internal async Task<StoreHubCreationJournal> CreateAsync(string kind, StoreHubQueryPlan? plan = null, StoreHubScopeProof? proof = null,
            AuthorizationContext? context = null, CancellationToken cancellationToken = default)
        {
            using StoreHubCreation creation = new(Handler, Tokens, Privacy, Writer, Clock);
            return await creation.CreateAsync(Experiment, kind, plan ?? Plan, proof ?? Proof, context ?? Context, cancellationToken);
        }
        public void Dispose() { Handler.Dispose(); temporary.Dispose(); }
    }
    private sealed class Tokens : IInventoryTokens
    {
        internal int Calls { get; private set; }
        public Task<AccessToken> GetAsync(AuthorizationContext context, bool forceRefresh, CancellationToken cancellationToken)
        { Assert.IsFalse(forceRefresh); Calls++; return Task.FromResult(new AccessToken("SECRET-POST-CANARY", DateTimeOffset.MaxValue)); }
    }
    private sealed class FaultWriter(string mode) : IAtomicFileWriter
    {
        private int count;
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken)
        {
            int write = ++count;
            if ((mode == "before-intent" && write == 1) || (mode == "id-and-uncertain" && write >= 2)) { throw new IOException("PRIVATE-WRITE-CANARY"); }
            await new AtomicFileWriter(new FileCommitter()).WriteAsync(path, contents, cancellationToken);
            if ((mode == "after-intent" && write == 1) || (mode == "after-id" && write == 2)) { throw new IOException("PRIVATE-WRITE-CANARY"); }
        }
    }
    private sealed class CancelWriter(CancellationTokenSource cancellation) : IAtomicFileWriter
    {
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken)
        { await new AtomicFileWriter(new FileCommitter()).WriteAsync(path, contents, cancellationToken); cancellation.Cancel(); }
    }
}
