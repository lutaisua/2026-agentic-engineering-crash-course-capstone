using System.Text;
using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

internal sealed record StoreHubCreationSummary(string Kind, string State, int? HttpStatus, string Outcome, int ExitCode, string? ResourceId)
{
    internal static StoreHubCreationSummary From(StoreHubCreationJournal journal) =>
        new(journal.Kind, journal.State, journal.HttpStatus, journal.Outcome, journal.ExitCode, journal.ResourceId);
}

internal sealed record StoreHubReportStagePacket(int SchemaVersion, string RunId, string Stage, string SourceSha256,
    string ContractSha256, string InputSha256, string BindingFingerprint, string DiscoveryEvidenceSha256,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, StoreHubQueryPlan Plan, string ProofBasis, StoreHubBaselineIdentity Baseline,
    IReadOnlyList<StoreHubCreationSummary> Creation, StoreHubExecutionObservation? Execution, StoreHubDownloadResult? Download,
    StoreHubReportAnalysis? Analysis, IReadOnlyList<DiagnosticAttempt> Attempts, string Outcome, int ExitCode)
{
    public override string ToString() => "[protected report-path evidence]";
    // Counts, fixed states and equality results only: no IDs, rows, totals, query text, dates or links.
    internal object PublicProjection() => new
    {
        stage = "InstallsReport",
        phase = "report-path",
        dateWireFormat = Plan.DateWireFormat,
        ExitCode,
        Outcome,
        proofBasis = ProofBasis,
        baselineRows = Baseline.RowCount,
        baselineIdentityMatches = Baseline.ProductMatches,
        reportingResourcesRecorded = Creation.Count(item => item.State == "recorded"),
        creation = Creation.Select(item => new { item.Kind, item.State, item.HttpStatus, item.Outcome }),
        execution = Execution?.State,
        executionPolls = Execution?.Polls,
        downloadHostApproved = Execution?.ObservedDownloadHost is null ? (bool?)null : Execution.ObservedDownloadHost == StoreHubExecution.DownloadHost,
        csvDownloaded = Download?.Sha256 is not null,
        csvBytes = Download?.Bytes,
        analysis = Analysis is null ? null : new
        {
            Analysis.Validation,
            Analysis.RowCount,
            Analysis.HeadersPresent,
            Analysis.ProductMatches,
            Analysis.RowsBeforeStart,
            Analysis.RowsInWindow,
            Analysis.RowsOnEndDay,
            Analysis.RowsAfterEndDay,
            Analysis.UnparsedDates,
            Analysis.InvalidMetrics,
            Analysis.DateFormats,
            Analysis.BoundaryState,
            Analysis.Days,
            Analysis.MatchingDays
        },
        export = Analysis?.ExitCode == 0 ? "recorded-for-review" : "unverified",
        comparison = Analysis is null ? "unverified" : "recorded-for-review-not-accepted",
        observations = Attempts.Select(item => new { item.Operation, item.Attempt, item.Status, item.Failure })
    };
}
internal sealed record StoreHubReportStageResult(StoreHubReportStagePacket Packet, string Directory);

internal static class StoreHubReportStage
{
    internal static async Task<StoreHubReportStageResult> RunAsync(string configuration, string input, string mapping, string experiment,
        string discoveryDirectory, IReadOnlySet<string> approvedDiscoveryAssemblies, IPrivateStorageGuard privacy,
        IApplicationCredentialResolver credentials, HttpMessageHandler apiHandler, HttpMessageHandler downloadHandler, TimeProvider clock,
        IAtomicFileWriter writer, IAtomicFileWriter journalWriter, CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        StoreHubPreflight prepared = await StoreHubPreflight.LoadAsync(configuration, input, mapping, experiment, privacy, clock, cancellationToken).ConfigureAwait(false);
        string experimentDirectory = Path.TrimEndingDirectorySeparator(prepared.ExperimentDirectory);
        string runId = Guid.NewGuid().ToString("D");
        string directory = Path.Combine(experimentDirectory, "InstallsReport-" + runId);
        StoreHubDiscoveryPacket discovery;
        StoreHubQueryPlan plan;
        StoreHubBaselineIdentity baseline;
        StoreHubCsvSummary? totals;
        AuthorizationContext context;
        // Offline checks under the experiment lock. Creation takes the same lock itself for each journal, so it is released here.
        using (FileStream experimentLock = OpenLock(experimentDirectory, privacy))
        {
            discovery = await StoreHubDiscoveryReader.LoadAsync(prepared, discoveryDirectory, approvedDiscoveryAssemblies, privacy,
                cancellationToken).ConfigureAwait(false);
            context = prepared.Context with { CredentialGeneration = prepared.PreviousBinding!.CredentialGeneration };
            plan = StoreHubQueryPlan.Prepare(discovery.Metadata!, prepared.Input, "Installs", clock, context.StorageKey);
            (baseline, totals) = await StoreHubBaseline.LoadAsync(experimentDirectory, plan, privacy, cancellationToken).ConfigureAwait(false);
            if (Directory.Exists(directory)) { throw StoreHubInput.Invalid(); }
            privacy.PrepareDirectory(directory);
        }
        DateTimeOffset started = clock.GetUtcNow();
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromMinutes(15));
        StoreHubRequestBudget budget = new();
        using StoreHubReportTransport transport = new(apiHandler, context, budget, clock);
        using HttpClient client = new(transport, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(60) };
        Func<TimeSpan, CancellationToken, Task> wait = delay ?? ((duration, token) => Task.Delay(duration, clock, token));
        BoundedJsonHttp http = new(client, clock, wait);
        SingleDiagnosticRecoveryTokens tokens = new(new ClientCredentialTokens(http, credentials, clock));
        AuthorizedInventoryHttp authorized = new(http, tokens);
        // Microsoft documents custom dates only on report creation; the preview is optional evidence, the baseline proves identity.
        StoreHubScopeProof proof = new(plan.Dataset, plan.MetadataSha256, StoreDiagnosticPacketStore.Hash(Encoding.UTF8.GetBytes(plan.Query)),
            plan.Window, baseline.ProductMatches, null, false, false, false, plan.SampleFingerprint, plan.BindingFingerprint, plan.DateWireFormat,
            "baseline-identity");
        List<StoreHubCreationSummary> creation = [];
        StoreHubExecutionObservation? execution = null;
        StoreHubDownloadResult? download = null;
        StoreHubReportAnalysis? analysis = null;
        string outcome = baseline.Outcome;
        int exit = 1;
        try
        {
            if (baseline.ProductMatches && totals is not null)
            {
                using StoreHubCreation creator = new(transport, tokens, privacy, journalWriter, clock);
                StoreHubCreationJournal query = await creator.CreateAsync(experimentDirectory, "query", plan, proof, context, bounded.Token).ConfigureAwait(false);
                creation.Add(StoreHubCreationSummary.From(query));
                StoreHubCreationJournal? report = null;
                if (query.State == "recorded")
                {
                    report = await creator.CreateAsync(experimentDirectory, "report", plan, proof, context, bounded.Token).ConfigureAwait(false);
                    creation.Add(StoreHubCreationSummary.From(report));
                }
                if (report?.State != "recorded") { outcome = report?.Outcome ?? query.Outcome; exit = report?.ExitCode ?? query.ExitCode; }
                else
                {
                    transport.PinReport(report.ResourceId!);
                    using FileStream experimentLock = OpenLock(experimentDirectory, privacy);
                    (execution, StoreHubCompletedExecution? completed) = await StoreHubExecution.PollAsync(authorized, transport, context,
                        report.ResourceId!, clock, wait, bounded.Token).ConfigureAwait(false);
                    if (completed is null) { outcome = execution.Outcome; exit = 2; }
                    else
                    {
                        using StoreHubReportDownload downloader = new(downloadHandler, budget);
                        (byte[]? bytes, int? status, string result) = await downloader.GetAsync(completed.Link, bounded.Token).ConfigureAwait(false);
                        if (result == "expired")
                        {
                            StoreHubCompletedExecution? again = await StoreHubExecution.RereadAsync(authorized, transport, context, report.ResourceId!,
                                completed.ExecutionId, bounded.Token).ConfigureAwait(false);
                            if (again is not null) { (bytes, status, result) = await downloader.GetAsync(again.Link, bounded.Token).ConfigureAwait(false); }
                            if (result == "expired" || again is null) { result = "hub-download-link-expired"; bytes = null; }
                        }
                        if (bytes is null) { download = new(downloader.Attempts, status, 0, null, result); outcome = result; exit = 2; }
                        else
                        {
                            // Original bytes are kept privately even when later scope checks fail; validation is recorded separately.
                            await StoreHubDiscovery.CheckedWriteAsync(Path.Combine(directory, "report.csv"), bytes, privacy, writer, bounded.Token).ConfigureAwait(false);
                            download = new(downloader.Attempts, status, bytes.Length, StoreDiagnosticPacketStore.Hash(bytes), "downloaded");
                            analysis = StoreHubReportAnalysis.Analyze(bytes, plan, totals);
                            outcome = analysis.Outcome;
                            exit = analysis.ExitCode;
                        }
                    }
                }
            }
        }
        catch (InventoryException error) { exit = 2; outcome = SafeFailure(error.Category); }
        catch (OperationCanceledException) { exit = cancellationToken.IsCancellationRequested ? 130 : 2; outcome = exit == 130 ? "operator-cancelled" : "hub-deadline-exhausted"; }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or UnauthorizedAccessException or Console.AnalyticsET.Cli.InputException)
        { exit = 1; outcome = "hub-report-stage-failed"; }
        StoreHubReportStagePacket packet = new(1, runId, "InstallsReport", StoreDiagnosticPacketStore.Hash(
            await File.ReadAllBytesAsync(typeof(StoreHubReportStage).Assembly.Location, CancellationToken.None).ConfigureAwait(false)),
            StoreHubPreflight.ContractSha256, prepared.InputSha256, context.StorageKey, StoreDiagnosticPacketStore.Hash(await File.ReadAllBytesAsync(
                Path.Combine(discoveryDirectory, "evidence.json"), CancellationToken.None).ConfigureAwait(false)), started, clock.GetUtcNow(), plan,
            proof.Basis, baseline, creation, execution, download, analysis, transport.Attempts.ToArray(), outcome, exit);
        using CancellationTokenSource recording = new(TimeSpan.FromSeconds(10));
        byte[] evidence = JsonSerializer.SerializeToUtf8Bytes(packet, StoreHubInput.JsonOptions);
        await StoreHubDiscovery.CheckedWriteAsync(Path.Combine(directory, "evidence.json"), evidence, privacy, writer, recording.Token).ConfigureAwait(false);
        await StoreHubDiscovery.CheckedWriteAsync(Path.Combine(directory, "receipt.json"), JsonSerializer.SerializeToUtf8Bytes(
            new StoreHubReceipt(1, runId, "InstallsReport", exit, StoreDiagnosticPacketStore.Hash(evidence)), StoreHubInput.JsonOptions),
            privacy, writer, recording.Token).ConfigureAwait(false);
        return new(packet, directory);
    }

    private static FileStream OpenLock(string experiment, IPrivateStorageGuard privacy)
    {
        string lockPath = Path.Combine(experiment, ".experiment.lock");
        if (File.Exists(lockPath)) { privacy.VerifyFile(lockPath); }
        FileStream experimentLock = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { privacy.VerifyFile(lockPath); return experimentLock; }
        catch { experimentLock.Dispose(); throw; }
    }

    private static string SafeFailure(string category) => category is "unauthorized" or "forbidden-or-association-missing" or
        "credential-generation-changed" or "resource-not-found" or "http-timeout" or "http-request-failed" or "retry-budget-exhausted" or
        "retry-after-exceeds-budget" or "response-too-large" or "invalid-response-json" or "duplicate-response-property" or "redirect-rejected" or
        "invalid-token-response" or "hub-destination-rejected" or "hub-request-budget-exhausted" or "diagnostic-auth-recovery-exhausted" or
        "hub-execution-shape-unverified" or "hub-download-unsafe-transport" or "hub-invalid-binding" or "hub-query-scope-unverified" or
        "hub-creation-scope-unverified" or "hub-date-wire-format-invalid" or "hub-creation-prerequisite-failed" or "hub-prior-creation-uncertain" or
        "hub-query-prerequisite-missing" or "hub-creation-scope-changed" or "hub-creation-journal-unverified" or "hub-creation-token-expiring" or
        "hub-creation-unsafe-transport" or "hub-creation-prerequisite-or-recording-failed" ? category : "hub-report-stage-failed";
}

[TestClass]
[DoNotParallelize]
public sealed class StoreHubReportLiveValidation
{
    /// <summary>Receives only a sanitized marker; report rows, IDs and the signed link stay private.</summary>
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("Live")]
    public async Task RecordInstallsReport()
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
            using HttpClientHandler api = new() { AllowAutoRedirect = false, UseCookies = false };
            using HttpClientHandler blob = new() { AllowAutoRedirect = false, UseCookies = false };
            StoreHubReportStageResult result = await StoreHubReportStage.RunAsync(configuration, input, ApplicationCredentialResolver.DefaultMappingPath,
                directory, discovery, StoreHubDiscoveryReader.ReviewedDiscoveryAssemblies(), privacy, credentials, api, blob, TimeProvider.System,
                new AtomicFileWriter(new NewFileCommitter()), new AtomicFileWriter(new FileCommitter()), CancellationToken.None);
            TestContext.WriteLine("ANALYTICSET_HUB_RESULT " + JsonSerializer.Serialize(new
            { exitCode = result.Packet.ExitCode, packetDirectory = result.Directory, summary = result.Packet.PublicProjection() }));
        }
        catch (Exception error)
        {
            TestContext.WriteLine("ANALYTICSET_HUB_RESULT " + JsonSerializer.Serialize(new
            { exitCode = error is OperationCanceledException ? 130 : 1, category = "hub-report-prerequisite-or-recording-failed" }));
            Assert.Fail("Installs report stage did not record a verified packet. Inspect private prerequisites locally.");
        }
    }
}
