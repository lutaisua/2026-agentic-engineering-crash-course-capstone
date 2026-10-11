using System.Net;
using System.Text;
using System.Text.Json;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class StoreHubReportStageTests
{
    internal const string BaselineCsv = "Date,ProductId,ProductName,Market,SuccessfulInstallCount\r\n10/8/2026,APP-FICTIONAL,\"Fictional, App\",US,3\r\n" +
        "10/8/2026,APP-FICTIONAL,\"Fictional, App\",DE,4\r\n10/9/2026,APP-FICTIONAL,\"Fictional, App\",US,5\r\n";
    internal const string ReportCsv = "﻿ProductId,Date,SuccessfulInstallCount\nAPP-FICTIONAL,2026-10-08,7\nAPP-FICTIONAL,2026-10-09,5\n";

    [TestMethod]
    public void CsvParserHonoursQuotingAndRejectsRaggedStrayQuoteDuplicateHeadersAndInvalidEncoding()
    {
        StoreHubCsvTable table = StoreHubCsv.Parse("A,B\r\n\"x,\"\"y\"\"\",\"line\nbreak\"\r\n\r\n1,\n")!;
        Assert.AreEqual(2, table.Rows.Count);
        Assert.AreEqual("x,\"y\"", table.Rows[0][0]);
        Assert.AreEqual("line\nbreak", table.Rows[0][1]);
        Assert.AreEqual("", table.Rows[1][1]);
        foreach (string invalid in new[] { "A,B\n1\n", "A,B\n1,x\"y\n", "A,B\n\"open,2\n", "A,a\n1,2\n", "A,B\n\"1\"x,2\n", "" })
        { Assert.IsNull(StoreHubCsv.Parse(invalid), invalid); }
        Assert.AreEqual("A,B", StoreHubCsv.Decode([0xEF, 0xBB, 0xBF, (byte)'A', (byte)',', (byte)'B']));
        Assert.AreEqual("A", StoreHubCsv.Decode([0xFF, 0xFE, (byte)'A', 0]));
        Assert.IsNull(StoreHubCsv.Decode([0xC3, 0x28]));
        Assert.AreEqual(("2026-10-08", "iso-date"), Day("2026-10-08"));
        Assert.AreEqual(("2026-10-08", "utc-midnight"), Day("2026-10-08T00:00:00Z"));
        Assert.AreEqual(("2026-10-08", "slash-month-first"), Day("10/8/2026"));
        Assert.AreEqual(("2026-10-08", "slash-month-first"), Day("10/8/2026 12:00:00 AM"));
        foreach (string unrecognized in new[] { "2026-10-08T05:00:00Z", "2026-10-08T00:00:00", "08.10.2026", "PRIVATE-CANARY" })
        { Assert.IsNull(StoreHubCsv.ParseDay(unrecognized), unrecognized); }
    }

    private static (string, string) Day(string text)
    { (DateOnly day, string format) = StoreHubCsv.ParseDay(text)!.Value; return (day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), format); }

    [TestMethod]
    public async Task BaselineIdentityRequiresEveryRowToMatchTheParentAppAndRecordsNoValues()
    {
        StoreHubQueryPlan plan = Plan();
        using TemporaryDirectory temporary = new();
        WindowsPrivateStorageGuard privacy = new();
        string experiment = privacy.PrepareDirectory(Path.Combine(temporary.Path, Guid.NewGuid().ToString("D")));
        string path = Path.Combine(experiment, StoreHubBaseline.FileName);
        Assert.AreEqual("hub-baseline-missing", (await StoreHubBaseline.LoadAsync(experiment, plan, privacy, default)).Identity.Outcome);
        await File.WriteAllTextAsync(path, BaselineCsv);
        (StoreHubBaselineIdentity identity, StoreHubCsvSummary? summary) = await StoreHubBaseline.LoadAsync(experiment, plan, privacy, default);
        Assert.AreEqual("baseline-identity-matches", identity.Outcome);
        Assert.IsTrue(identity.ProductMatches);
        Assert.AreEqual(3, identity.RowCount);
        Assert.AreEqual(7m, summary!.Totals[new DateOnly(2026, 10, 8)]);
        CollectionAssert.AreEqual(new[] { "slash-month-first" }, identity.DateFormats.ToArray());
        string serialized = JsonSerializer.Serialize(identity);
        foreach (string canary in new[] { "APP-FICTIONAL", "Fictional", "10/8/2026" }) { Assert.IsFalse(serialized.Contains(canary, StringComparison.Ordinal)); }
        Assert.IsFalse(summary.ToString().Contains("APP", StringComparison.Ordinal));
        foreach ((string contents, string outcome) in new[]
        {
            (BaselineCsv + "10/9/2026,OTHER-FICTIONAL,Other,US,1\r\n", "hub-baseline-product-mismatch"),
            ("Date,Product,SuccessfulInstallCount\r\n10/8/2026,APP-FICTIONAL,1\r\n", "hub-baseline-columns-missing"),
            ("Date,ProductId,SuccessfulInstallCount\r\n", "hub-baseline-empty"),
            ("Date,ProductId,SuccessfulInstallCount\r\n\"10/8/2026,APP-FICTIONAL,1\r\n", "hub-baseline-unreadable")
        })
        {
            await File.WriteAllTextAsync(path, contents);
            (StoreHubBaselineIdentity rejected, StoreHubCsvSummary? none) = await StoreHubBaseline.LoadAsync(experiment, plan, privacy, default);
            Assert.AreEqual(outcome, rejected.Outcome);
            Assert.IsFalse(rejected.ProductMatches);
            Assert.IsNull(none);
        }
        await using (FileStream large = new(path, FileMode.Create)) { large.SetLength(StoreHubCsv.MaximumBytes + 1L); }
        Assert.AreEqual("hub-baseline-too-large", (await StoreHubBaseline.LoadAsync(experiment, plan, privacy, default)).Identity.Outcome);
        await File.WriteAllTextAsync(path, BaselineCsv);
        Assert.AreEqual("hub-baseline-unsafe", (await StoreHubBaseline.LoadAsync(experiment, plan, new RejectingGuard(privacy), default)).Identity.Outcome);
    }

    [TestMethod]
    public void BaselineBasisIsAcceptedOnlyForTheNarrowDateOnlyInstallsPlanAndThePreviewBasisIsUnchanged()
    {
        StoreHubQueryPlan plan = Plan();
        StoreHubScopeProof baseline = Proof(plan) with { PopulatedPreview = false, FixedDatesVerified = false, UtcBoundaryConventionVerified = false, Basis = "baseline-identity" };
        plan.RequireCreationProof(baseline);
        StoreHubQueryPlan utc = StoreHubQueryPlan.Prepare(StoreHubQueryTests.Metadata, Sample, "Installs", new StoreDiagnosticTransportTests.Clock(),
            plan.BindingFingerprint, "utc-timestamp");
        StoreHubQueryPlan addon = StoreHubQueryPlan.Prepare(StoreHubQueryTests.Metadata, Sample, "AddOnAcquisitions", new StoreDiagnosticTransportTests.Clock(), plan.BindingFingerprint);
        foreach ((StoreHubQueryPlan target, StoreHubScopeProof proof) in new[]
        {
            (utc, Proof(utc) with { PopulatedPreview = false, Basis = "baseline-identity" }),
            (addon, Proof(addon) with { PopulatedPreview = false, Basis = "baseline-identity" }),
            (plan, baseline with { ProductMatches = false }),
            (plan, baseline with { QuerySha256 = new string('0', 64) }),
            (plan, baseline with { Window = new("2026-10-07T00:00:00Z", "2026-10-09T00:00:00Z") }),
            (plan, baseline with { Basis = "PRIVATE-CANARY" }),
            (plan, Proof(plan) with { PopulatedPreview = false }),
            (plan, Proof(plan) with { UtcBoundaryConventionVerified = false })
        })
        { Assert.Throws<InventoryException>(() => target.RequireCreationProof(proof)); }
        plan.RequireCreationProof(Proof(plan));
    }

    [TestMethod]
    public async Task ReportPathCreatesOneQueryAndOneReportPollsDownloadsWithoutBearerAndComparesPerDayPrivately()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        StoreHubReportStageResult result = await fixture.RunAsync();
        StoreHubReportStagePacket packet = result.Packet;
        Assert.AreEqual(0, packet.ExitCode, packet.Outcome);
        Assert.AreEqual("report-recorded", packet.Outcome);
        Assert.AreEqual(1, fixture.Posts("ScheduledQueries"));
        Assert.AreEqual(1, fixture.Posts("ScheduledReport"));
        Assert.AreEqual(1, fixture.Downloads);
        Assert.AreEqual("completed", packet.Execution!.State);
        Assert.AreEqual(2, packet.Execution.Polls);
        Assert.AreEqual(1, packet.Execution.PendingObservations);
        CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(15) }, fixture.Delays.ToArray());
        StoreHubReportAnalysis analysis = packet.Analysis!;
        Assert.AreEqual("validated", analysis.Validation);
        Assert.AreEqual(2, analysis.RowCount);
        Assert.AreEqual(1, analysis.RowsInWindow);
        Assert.AreEqual(1, analysis.RowsOnEndDay);
        Assert.AreEqual("end-date-rows-observed", analysis.BoundaryState);
        Assert.AreEqual(2, analysis.Days.Count);
        Assert.AreEqual(2, analysis.MatchingDays);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(ReportCsv), File.ReadAllBytes(Path.Combine(result.Directory, "report.csv")));
        Assert.AreEqual(packet.Download!.Sha256, StoreDiagnosticPacketStore.Hash(File.ReadAllBytes(Path.Combine(result.Directory, "report.csv"))));
        StoreHubReceipt receipt = JsonSerializer.Deserialize<StoreHubReceipt>(File.ReadAllText(Path.Combine(result.Directory, "receipt.json")), StoreHubInput.JsonOptions)!;
        Assert.AreEqual(StoreDiagnosticPacketStore.Hash(File.ReadAllBytes(Path.Combine(result.Directory, "evidence.json"))), receipt.EvidenceSha256);
        Assert.AreEqual("InstallsReport", receipt.Stage);
        using JsonDocument report = JsonDocument.Parse(fixture.ReportBody!);
        Assert.AreEqual("2026-10-08", report.RootElement.GetProperty("queryStartTime").GetString());
        Assert.AreEqual("2026-10-09", report.RootElement.GetProperty("queryEndTime").GetString());
        foreach (string file in Directory.GetFiles(fixture.Root, "*-creation.json", SearchOption.AllDirectories))
        { StringAssert.Contains(File.ReadAllText(file), "baseline-identity"); }
        string projection = JsonSerializer.Serialize(packet.PublicProjection());
        string evidence = File.ReadAllText(Path.Combine(result.Directory, "evidence.json"));
        foreach (string canary in new[] { "APP-FICTIONAL", "FICTIONAL-TOKEN", "PRIVATE-SAS-CANARY", "SELECT", "2026-10-08", Fixture.ReportId, "pxanltx" })
        { Assert.IsFalse(projection.Contains(canary, StringComparison.Ordinal), canary); }
        foreach (string canary in new[] { "FICTIONAL-TOKEN", "PRIVATE-SAS-CANARY", "PRIVATE-MESSAGE-CANARY" })
        { Assert.IsFalse(evidence.Contains(canary, StringComparison.Ordinal), canary); }
        Assert.IsFalse(packet.ToString().Contains("APP", StringComparison.Ordinal));

        // A rerun reuses both recorded journals: no second POST, a new stage directory and the earlier evidence untouched.
        byte[] before = File.ReadAllBytes(Path.Combine(result.Directory, "evidence.json"));
        StoreHubReportStageResult again = await fixture.RunAsync();
        Assert.AreEqual(0, again.Packet.ExitCode);
        Assert.AreNotEqual(result.Directory, again.Directory);
        Assert.AreEqual(1, fixture.Posts("ScheduledQueries"));
        Assert.AreEqual(1, fixture.Posts("ScheduledReport"));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Path.Combine(result.Directory, "evidence.json")));
    }

    [TestMethod]
    [DataRow("baseline-missing", "hub-baseline-missing", 1)]
    [DataRow("baseline-mismatch", "hub-baseline-product-mismatch", 1)]
    public async Task BaselineFailureStopsBeforeCredentialsAndAnyRequest(string mode, string outcome, int exit)
    {
        using Fixture fixture = await Fixture.CreateAsync(mode);
        int credentials = fixture.Credentials.Contexts.Count;
        StoreHubReportStageResult result = await fixture.RunAsync();
        Assert.AreEqual(exit, result.Packet.ExitCode);
        Assert.AreEqual(outcome, result.Packet.Outcome);
        Assert.AreEqual(0, fixture.Api.Calls);
        Assert.AreEqual(0, fixture.Blob.Calls);
        Assert.AreEqual(credentials, fixture.Credentials.Contexts.Count);
        Assert.AreEqual(0, result.Packet.Creation.Count);
        Assert.IsTrue(File.Exists(Path.Combine(result.Directory, "receipt.json")));
    }

    [TestMethod]
    [DataRow("lost-report", "hub-creation-uncertain", 2)]
    [DataRow("never", "hub-report-execution-not-observed", 2)]
    [DataRow("pending-forever", "hub-report-execution-pending", 2)]
    [DataRow("failed", "hub-report-execution-failed", 2)]
    [DataRow("paused", "hub-report-execution-not-progressing", 2)]
    [DataRow("unknown-state", "hub-report-execution-state-unrecognized", 2)]
    [DataRow("unknown-field", "hub-execution-shape-unverified", 2)]
    [DataRow("other-report", "hub-execution-shape-unverified", 2)]
    [DataRow("other-host", "hub-download-host-unapproved", 2)]
    [DataRow("no-link", "hub-download-link-missing", 2)]
    [DataRow("execution-403", "forbidden-or-association-missing", 2)]
    [DataRow("execution-401-always", "unauthorized", 2)]
    [DataRow("html", "hub-download-error-body", 2)]
    [DataRow("json-200", "hub-download-error-body", 2)]
    [DataRow("redirect", "hub-download-redirect-rejected", 2)]
    [DataRow("oversize", "hub-download-too-large", 2)]
    [DataRow("expired-twice", "hub-download-link-expired", 2)]
    [DataRow("blob-500", "hub-download-failed", 2)]
    [DataRow("unsafe-blob", "hub-download-unsafe-transport", 2)]
    public async Task UnresolvedCreationExecutionOrDownloadStopsWithoutReplayOrUnsafeDownload(string mode, string outcome, int exit)
    {
        using Fixture fixture = await Fixture.CreateAsync(mode);
        StoreHubReportStageResult result = await fixture.RunAsync();
        Assert.AreEqual(exit, result.Packet.ExitCode, result.Packet.Outcome);
        Assert.AreEqual(outcome, result.Packet.Outcome);
        Assert.AreEqual(1, fixture.Posts("ScheduledQueries"));
        Assert.AreEqual(1, fixture.Posts("ScheduledReport"));
        Assert.IsFalse(File.Exists(Path.Combine(result.Directory, "report.csv")));
        if (mode is "lost-report") { Assert.AreEqual(0, fixture.Polls); }
        if (mode is "never" or "pending-forever")
        {
            Assert.AreEqual(40, result.Packet.Execution!.Polls);
            Assert.IsTrue(fixture.Delays.All(delay => delay == TimeSpan.FromSeconds(15)));
        }
        if (mode is "other-host" or "no-link" or "unsafe-blob" or "failed" or "never") { Assert.AreEqual(0, fixture.Blob.Calls); }
        if (mode == "other-host") { Assert.AreEqual("other.blob.core.windows.net", result.Packet.Execution!.ObservedDownloadHost); }
        if (mode == "expired-twice") { Assert.AreEqual(2, fixture.Blob.Calls); }
        string projection = JsonSerializer.Serialize(result.Packet.PublicProjection());
        foreach (string canary in new[] { "APP-FICTIONAL", "PRIVATE-SAS-CANARY", "PRIVATE-MESSAGE-CANARY", "other.blob" })
        { Assert.IsFalse(projection.Contains(canary, StringComparison.Ordinal), canary); }
        Assert.IsFalse(File.ReadAllText(Path.Combine(result.Directory, "evidence.json")).Contains("PRIVATE-SAS-CANARY", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("expired-once", 2)]
    [DataRow("execution-401-once", 1)]
    [DataRow("execution-429", 1)]
    public async Task OneExpiredLinkRereadOneTokenRenewalAndBoundedRetryAfterStillRecordTheReport(string mode, int downloads)
    {
        using Fixture fixture = await Fixture.CreateAsync(mode);
        StoreHubReportStageResult result = await fixture.RunAsync();
        Assert.AreEqual(0, result.Packet.ExitCode, result.Packet.Outcome);
        Assert.AreEqual(downloads, fixture.Blob.Calls);
        Assert.AreEqual(1, fixture.Posts("ScheduledReport"));
        if (mode == "execution-429") { Assert.IsTrue(fixture.Delays.Contains(TimeSpan.FromSeconds(2))); }
        if (mode == "execution-401-once") { Assert.AreEqual(2, fixture.Tokens); }
    }

    [TestMethod]
    [DataRow("APP-FICTIONAL,2026-10-08,7\nOTHER-FICTIONAL,2026-10-08,1\n", "hub-report-product-mismatch", 2, "scope-or-values-unverified")]
    [DataRow("APP-FICTIONAL,2026-10-07,7\n", "hub-report-date-scope-outside-window", 2, "scope-or-values-unverified")]
    [DataRow("APP-FICTIONAL,2026-10-10,7\n", "hub-report-date-scope-outside-window", 2, "scope-or-values-unverified")]
    [DataRow("APP-FICTIONAL,08.10.2026,7\n", "hub-report-values-unverified", 2, "scope-or-values-unverified")]
    [DataRow("APP-FICTIONAL,2026-10-08,1.5\n", "hub-report-values-unverified", 2, "scope-or-values-unverified")]
    [DataRow("", "report-recorded-empty", 0, "validated")]
    [DataRow("APP-FICTIONAL,2026-10-08,6\n", "report-recorded", 0, "validated")]
    public async Task DownloadedRowsAreCheckedForProductDateAndMetricAndKeptAsOriginalBytes(string rows, string outcome, int exit, string validation)
    {
        using Fixture fixture = await Fixture.CreateAsync(csv: "ProductId,Date,SuccessfulInstallCount\r\n" + rows);
        StoreHubReportStageResult result = await fixture.RunAsync();
        Assert.AreEqual(outcome, result.Packet.Outcome);
        Assert.AreEqual(exit, result.Packet.ExitCode);
        Assert.AreEqual(validation, result.Packet.Analysis!.Validation);
        Assert.IsTrue(File.Exists(Path.Combine(result.Directory, "report.csv")));
        if (outcome == "report-recorded")
        {
            Assert.AreEqual("end-date-rows-absent", result.Packet.Analysis.BoundaryState);
            Assert.AreEqual(0, result.Packet.Analysis.MatchingDays);
            Assert.IsFalse(result.Packet.Analysis.Days[0].TotalsEqual);
            Assert.IsTrue(result.Packet.Analysis.Days[1].BaselineRows);
            Assert.IsFalse(result.Packet.Analysis.Days[1].ReportRows);
        }
        if (outcome == "report-recorded-empty") { Assert.AreEqual("empty", result.Packet.Analysis.BoundaryState); }
        using Fixture headers = await Fixture.CreateAsync(csv: "Product,Day,Count\r\nAPP-FICTIONAL,2026-10-08,1\r\n");
        Assert.AreEqual("hub-report-schema-unverified", (await headers.RunAsync()).Packet.Outcome);
    }

    [TestMethod]
    public async Task CancellationDuringPollingIsRecordedAsCancelledWithoutDownload()
    {
        using CancellationTokenSource cancellation = new();
        using Fixture fixture = await Fixture.CreateAsync("pending-forever");
        fixture.OnPoll = () => cancellation.Cancel();
        StoreHubReportStageResult result = await fixture.RunAsync(cancellation.Token);
        Assert.AreEqual(130, result.Packet.ExitCode);
        Assert.AreEqual("operator-cancelled", result.Packet.Outcome);
        Assert.AreEqual(0, fixture.Blob.Calls);
    }

    private static StoreHubInput Sample { get; } = StoreHubDiscoveryTests.Sample;
    private static StoreHubQueryPlan Plan() => StoreHubQueryPlan.Prepare(StoreHubQueryTests.Metadata, Sample, "Installs",
        new StoreDiagnosticTransportTests.Clock(), new string('a', 64));
    private static StoreHubScopeProof Proof(StoreHubQueryPlan plan) => new(plan.Dataset, plan.MetadataSha256,
        StoreDiagnosticPacketStore.Hash(Encoding.UTF8.GetBytes(plan.Query)), plan.Window, true, plan.ParentId is null ? null : true,
        true, true, true, plan.SampleFingerprint, plan.BindingFingerprint, plan.DateWireFormat);

    private sealed class RejectingGuard(IPrivateStorageGuard inner) : IPrivateStorageGuard
    {
        public string PrepareDirectory(string path) => inner.PrepareDirectory(path);
        public void VerifyFile(string path)
        {
            if (Path.GetFileName(path) == StoreHubBaseline.FileName) { throw new Console.AnalyticsET.Cli.InputException("PRIVATE-ACL-CANARY"); }
            inner.VerifyFile(path);
        }
    }

    internal sealed class Fixture : IDisposable
    {
        internal const string ReportId = "66666666-7777-4888-8999-000000000000";
        private const string QueryId = "11111111-2222-4333-8444-555555555555";
        private const string ExecutionId = "28016f06-6bbf-459e-ba30-429da6910192";
        private readonly StoreHubDiscoveryTests.Fixture hub = new();
        private readonly List<string> posts = [];
        private readonly string mode;
        private readonly string csv;
        private string? query;
        private int completedLookups, pendingLookups, blobCalls;
        internal StoreHubDiscoveryResult Discovery { get; private set; } = null!;
        internal StoreDiagnosticTransportTests.Clock Clock { get; } = new();
        internal List<TimeSpan> Delays { get; } = [];
        internal InventoryHttpTests.Handler Api { get; }
        internal InventoryHttpTests.Handler Blob { get; }
        internal StoreSubscriptionDiagnosticTests.CountingCredentials Credentials => hub.Credentials;
        internal string Root => hub.Root;
        internal string Configuration => hub.Configuration;
        internal string Experiment => hub.Experiment;
        internal string? ReportBody { get; private set; }
        internal int Downloads => Blob.Calls;
        internal int Polls => completedLookups + pendingLookups;
        internal int Tokens { get; private set; }
        internal Action? OnPoll { get; set; }
        internal int Posts(string route) => posts.Count(item => item == route);

        private Fixture(string mode, string csv)
        {
            this.mode = mode;
            this.csv = csv;
            Api = new(RespondApi);
            Blob = new(RespondBlob);
        }

        internal static async Task<Fixture> CreateAsync(string mode = "valid", string csv = ReportCsv)
        {
            Fixture fixture = new(mode, csv);
            fixture.Discovery = await StoreHubQueryTests.DiscoverNativeAsync(fixture.hub);
            if (mode != "baseline-missing")
            {
                await File.WriteAllTextAsync(Path.Combine(fixture.hub.Experiment, StoreHubBaseline.FileName),
                    mode == "baseline-mismatch" ? BaselineCsv.Replace("DE,4", "DE,4\r\n10/9/2026,OTHER-FICTIONAL,Other,US,1", StringComparison.Ordinal) : BaselineCsv);
            }
            return fixture;
        }

        internal async Task<StoreHubReportStageResult> RunAsync(CancellationToken cancellationToken = default)
        {
            using HttpClientHandler? unsafeHandler = mode == "unsafe-blob" ? new() { AllowAutoRedirect = true } : null;
            HttpMessageHandler blob = unsafeHandler ?? (HttpMessageHandler)Blob;
            return await StoreHubReportStage.RunAsync(hub.Configuration, hub.Input, hub.Mapping, hub.Experiment, Discovery.Directory,
                new HashSet<string> { Discovery.Packet.SourceSha256 }, new WindowsPrivateStorageGuard(), hub.Credentials, Api, blob, Clock,
                new AtomicFileWriter(new NewFileCommitter()), new AtomicFileWriter(new FileCommitter()), cancellationToken,
                (duration, token) => { token.ThrowIfCancellationRequested(); Delays.Add(duration); Clock.Now += duration; return Task.CompletedTask; });
        }

        private async Task<HttpResponseMessage> RespondApi(HttpRequestMessage request)
        {
            Uri uri = request.RequestUri!;
            if (uri.Host == "login.microsoftonline.com")
            {
                Tokens++;
                return InventoryHttpTests.Json("{\"access_token\":\"FICTIONAL-TOKEN\",\"token_type\":\"Bearer\",\"expires_in\":3600}");
            }
            Assert.AreEqual("FICTIONAL-TOKEN", request.Headers.Authorization?.Parameter);
            if (request.Method == HttpMethod.Post)
            {
                string route = uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..];
                posts.Add(route);
                string body = await request.Content!.ReadAsStringAsync();
                using JsonDocument payload = JsonDocument.Parse(body);
                if (route == "ScheduledQueries")
                {
                    query = payload.RootElement.GetProperty("query").GetString();
                    return Envelope(new Dictionary<string, object?> { ["name"] = payload.RootElement.GetProperty("name").GetString(), ["queryId"] = QueryId, ["query"] = query });
                }
                ReportBody = body;
                if (mode == "lost-report") { throw new HttpRequestException("PRIVATE-TRANSPORT-CANARY"); }
                return Envelope(new Dictionary<string, object?>
                {
                    ["reportName"] = payload.RootElement.GetProperty("reportName").GetString(),
                    ["reportId"] = ReportId,
                    ["queryId"] = QueryId,
                    ["query"] = query,
                    ["executeNow"] = true,
                    ["format"] = "csv",
                    ["queryStartTime"] = null,
                    ["queryEndTime"] = null,
                    ["recurrenceInterval"] = -1,
                    ["recurrenceCount"] = 1,
                    ["callbackUrl"] = null
                });
            }
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("/consumer/insights/v1.1/ScheduledReport/execution/" + ReportId, uri.AbsolutePath);
            OnPoll?.Invoke();
            bool pendingLookup = uri.Query == "?executionStatus=Pending&scope=consumer";
            Assert.IsTrue(pendingLookup || uri.Query == "?scope=consumer");
            if (pendingLookup) { pendingLookups++; } else { completedLookups++; }
            if (mode == "execution-403") { return new(HttpStatusCode.Forbidden) { Content = new StringContent("PRIVATE-MESSAGE-CANARY") }; }
            if (mode == "execution-401-always" || (mode == "execution-401-once" && completedLookups == 1))
            { return new(HttpStatusCode.Unauthorized) { Content = new StringContent("PRIVATE-MESSAGE-CANARY") }; }
            if (mode == "execution-429" && completedLookups == 1)
            {
                HttpResponseMessage busy = new(HttpStatusCode.TooManyRequests) { Content = new StringContent("PRIVATE-MESSAGE-CANARY") };
                busy.Headers.RetryAfter = new(TimeSpan.FromSeconds(2));
                return busy;
            }
            bool ready = mode is not ("never" or "pending-forever" or "failed" or "paused" or "unknown-state") && completedLookups >= 2;
            if (!pendingLookup)
            {
                return ready ? Envelope(Execution("Completed")) : new(HttpStatusCode.NotFound) { Content = new StringContent("PRIVATE-MESSAGE-CANARY") };
            }
            return mode switch
            {
                "never" => new(HttpStatusCode.NotFound) { Content = new StringContent("PRIVATE-MESSAGE-CANARY") },
                "failed" => Envelope(Execution("Failed")),
                "paused" => Envelope(Execution("Pause")),
                "unknown-state" => Envelope(Execution("PRIVATE-STATE-CANARY")),
                _ => Envelope(Execution("Pending"))
            };
        }

        private Dictionary<string, object?> Execution(string status)
        {
            Dictionary<string, object?> item = new()
            {
                ["executionId"] = ExecutionId,
                ["reportId"] = mode == "other-report" ? "77777777-7777-4888-8999-000000000000" : ReportId,
                ["recurrenceInterval"] = -1,
                ["recurrenceCount"] = 1,
                ["callbackUrl"] = null,
                ["callbackMethod"] = null,
                ["format"] = "csv",
                ["executionStatus"] = status,
                ["reportLocation"] = "https://pxanltx.blob.core.windows.net/programmatic-export-pi/fictional.csv",
                ["reportAccessSecureLink"] = mode == "no-link" ? null : "https://" + (mode == "other-host" ? "other.blob.core.windows.net" : "pxanltx.blob.core.windows.net") +
                    "/programmatic-export-pi/fictional.csv?sv=1&sig=PRIVATE-SAS-CANARY",
                ["reportExpiryTime"] = null,
                ["reportGeneratedTime"] = "2026-10-10T00:00:00Z",
                ["endTime"] = "2026-10-10T00:00:00Z",
                ["totalRecurrenceCount"] = 1,
                ["nextExecutionStartTime"] = null
            };
            if (mode == "unknown-field") { item["unexpected"] = "PRIVATE-MESSAGE-CANARY"; }
            return item;
        }

        private static HttpResponseMessage Envelope(Dictionary<string, object?> item) => InventoryHttpTests.Json(JsonSerializer.Serialize(new
        { value = new[] { item }, totalCount = 1, message = "PRIVATE-MESSAGE-CANARY", statusCode = 200 }));

        private Task<HttpResponseMessage> RespondBlob(HttpRequestMessage request)
        {
            blobCalls++;
            Assert.IsNull(request.Headers.Authorization);
            Assert.IsFalse(request.Headers.Contains("Cookie"));
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("pxanltx.blob.core.windows.net", request.RequestUri!.Host);
            HttpResponseMessage response = mode switch
            {
                "html" => new(HttpStatusCode.OK) { Content = new StringContent("<html>PRIVATE-MESSAGE-CANARY</html>", Encoding.UTF8, "text/html") },
                "json-200" => new(HttpStatusCode.OK) { Content = new StringContent("{\"error\":\"PRIVATE-MESSAGE-CANARY\"}", Encoding.UTF8, "application/octet-stream") },
                "redirect" => new(HttpStatusCode.Redirect) { Headers = { Location = new("https://example.com/PRIVATE") } },
                "expired-twice" => new(HttpStatusCode.Forbidden),
                "expired-once" when blobCalls == 1 => new(HttpStatusCode.Forbidden),
                "blob-500" => new(HttpStatusCode.InternalServerError),
                _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(csv)) }
            };
            if (mode == "oversize") { response.Content.Headers.ContentLength = StoreHubCsv.MaximumBytes + 1L; }
            return Task.FromResult(response);
        }

        public void Dispose() { Api.Dispose(); Blob.Dispose(); hub.Dispose(); }
    }
}
