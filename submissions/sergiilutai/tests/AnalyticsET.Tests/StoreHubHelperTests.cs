using System.Diagnostics;
using System.Text.Json;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

[TestClass]
[DoNotParallelize]
public sealed class StoreHubHelperTests
{
    [TestMethod]
    public async Task OfflineMaskedPreparationHelpAndPreservationNeverInvokeVaultOrRunner()
    {
        using StoreHubDiscoveryTests.Fixture fixture = new();
        string directory = Path.Combine(fixture.Root, Guid.NewGuid().ToString("D"));
        var help = await RunAsync(fixture.Configuration, directory, help: true);
        Assert.AreEqual(0, help.Exit, help.Error);
        Assert.IsFalse(Directory.Exists(directory));
        var prepared = await RunAsync(fixture.Configuration, directory);
        Assert.AreEqual(0, prepared.Exit, prepared.Error);
        StringAssert.Contains(prepared.Output, "No credential was read");
        string input = Path.Combine(directory, "input.json");
        StoreHubInput sample = StoreHubInput.Parse(await File.ReadAllTextAsync(input), TimeProvider.System);
        Assert.AreEqual("publisher-one", sample.PublisherId);
        new WindowsPrivateStorageGuard().VerifyFile(input);
        byte[] before = File.ReadAllBytes(input);
        var preserved = await RunAsync(fixture.Configuration, directory, rejectPrompts: true);
        Assert.AreEqual(0, preserved.Exit, preserved.Error);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(input));
        foreach (var result in new[] { help, prepared, preserved })
        {
            Assert.IsFalse((result.Output + result.Error).Contains("FICTIONAL", StringComparison.Ordinal));
            StringAssert.Contains(result.Output, "environment-restored");
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    [DataRow(130)]
    [DataRow(1)]
    [DataRow(99)]
    public async Task LiveWrapperRequiresMatchingReceiptAndKeepsNativeNoisePrivate(int code)
    {
        using StoreHubDiscoveryTests.Fixture fixture = new();
        if (code == 2) { fixture.Kind = "403"; }
        using CancellationTokenSource cancellation = new();
        if (code == 130) { fixture.Cancel = cancellation; }
        StoreHubDiscoveryResult packet = await fixture.RunAsync(cancellation.Token);
        string fake = Path.Combine(fixture.Root, "fake-dotnet.ps1");
        string marker = JsonSerializer.Serialize(code is 1 or 99 ?
            (object)new { exitCode = code, category = "private-canary-must-not-print" } : new { exitCode = code, packetDirectory = packet.Directory });
        await File.WriteAllTextAsync(fake, """
            if (-not $env:ANALYTICSET_HUB_CONFIG -or -not $env:ANALYTICSET_HUB_INPUT -or -not $env:ANALYTICSET_HUB_EXPERIMENT) { throw 'Missing paths.' }
            Write-Output 'PRIVATE-NATIVE-NOISE-FICTIONAL'
            """ + "\nWrite-Output " + Quote("ANALYTICSET_HUB_RESULT " + marker) + "\n$global:LASTEXITCODE = 0\n");
        var result = await RunAsync(fixture.Configuration, fixture.Experiment, live: true, dotnet: fake, rejectPrompts: true);
        Assert.AreEqual(code == 99 ? 1 : code, result.Exit, result.Error);
        StringAssert.Contains(result.Output, "environment-restored");
        foreach (string canary in new[] { "PRIVATE-NATIVE-NOISE", "SUB-FICTIONAL", "FICTIONAL-SECRET", "private-canary-must-not-print" })
        { Assert.IsFalse((result.Output + result.Error).Contains(canary, StringComparison.Ordinal)); }
        if (code is 0 or 2 or 130) { StringAssert.Contains(result.Output, "Discovery packet path"); }
        if (code == 0)
        {
            await File.AppendAllTextAsync(Path.Combine(packet.Directory, "evidence.json"), "CORRUPTED-PRIVATE-CANARY");
            var invalid = await RunAsync(fixture.Configuration, fixture.Experiment, live: true, dotnet: fake, rejectPrompts: true);
            Assert.AreEqual(1, invalid.Exit);
            Assert.IsFalse(invalid.Output.Contains("Discovery packet path", StringComparison.Ordinal));
            Assert.IsFalse((invalid.Output + invalid.Error).Contains("CANARY", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task MissingMarkerInterruptedProcessUnsafeStorageAndPrematureExportAreSanitized()
    {
        using StoreHubDiscoveryTests.Fixture fixture = new();
        string fake = Path.Combine(fixture.Root, "failed-runner.ps1");
        await File.WriteAllTextAsync(fake, "Write-Output 'PRIVATE-NATIVE-CANARY'; $global:LASTEXITCODE = 1");
        var missing = await RunAsync(fixture.Configuration, fixture.Experiment, live: true, dotnet: fake, rejectPrompts: true);
        Assert.AreEqual(1, missing.Exit);
        Assert.IsFalse((missing.Output + missing.Error).Contains("CANARY", StringComparison.Ordinal));
        await File.WriteAllTextAsync(fake, "$global:LASTEXITCODE = 130");
        var interrupted = await RunAsync(fixture.Configuration, fixture.Experiment, live: true, dotnet: fake, rejectPrompts: true);
        Assert.AreEqual(130, interrupted.Exit);
        using TemporaryDirectory temporary = new();
        string unsafeDirectory = Path.Combine(temporary.Path, Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(unsafeDirectory);
        var unsafeResult = await RunAsync(fixture.Configuration, unsafeDirectory, rejectPrompts: true);
        Assert.AreEqual(1, unsafeResult.Exit);
        foreach (string stage in new[] { "Installs", "InstallsReport", "AddOnAcquisitions" })
        {
            string directory = Path.Combine(fixture.Root, Guid.NewGuid().ToString("D"));
            var rejected = await RunAsync(fixture.Configuration, directory, stage: stage, live: true, rejectPrompts: true);
            Assert.AreEqual(1, rejected.Exit);
            Assert.IsFalse(Directory.Exists(directory));
            StringAssert.Contains(rejected.Error, stage == "AddOnAcquisitions" ? "export-mapping-unverified" : "discovery-prerequisite-required");
        }
        foreach (var result in new[] { missing, interrupted, unsafeResult }) { StringAssert.Contains(result.Output, "environment-restored"); }
        var invalidControl = await RunAsync(fixture.Configuration, fixture.Experiment, live: true, dotnet: fake, rejectPrompts: true, useUtc: true);
        Assert.AreEqual(1, invalidControl.Exit);
        StringAssert.Contains(invalidControl.Error, "date-wire-control-stage-invalid");
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    [TestMethod]
    public async Task NativeStderrNeitherHidesTheMarkerNorReachesOrdinaryOutput()
    {
        using StoreHubDiscoveryTests.Fixture fixture = new();
        StoreHubDiscoveryResult packet = await fixture.RunAsync();
        // A batch file is a native process, so its stderr reaches the wrapper the way dotnet test output does.
        string fake = Path.Combine(fixture.Root, "stderr-runner.cmd");
        string marker = JsonSerializer.Serialize(new { exitCode = 0, packetDirectory = packet.Directory });
        await File.WriteAllTextAsync(fake, "@echo off\r\necho PRIVATE-NATIVE-STDERR-CANARY 1>&2\r\necho ANALYTICSET_HUB_RESULT " + marker +
            "\r\necho PRIVATE-NATIVE-STDERR-CANARY 1>&2\r\nexit /b 0\r\n");
        var recorded = await RunAsync(fixture.Configuration, fixture.Experiment, live: true, dotnet: fake, rejectPrompts: true);
        Assert.AreEqual(0, recorded.Exit, recorded.Error);
        StringAssert.Contains(recorded.Output, "Discovery packet path");
        await File.WriteAllTextAsync(fake, "@echo off\r\necho PRIVATE-NATIVE-STDERR-CANARY 1>&2\r\nexit /b 1\r\n");
        var missing = await RunAsync(fixture.Configuration, fixture.Experiment, live: true, dotnet: fake, rejectPrompts: true);
        Assert.AreEqual(1, missing.Exit);
        StringAssert.Contains(missing.Error, "result-validation");
        foreach (var result in new[] { recorded, missing })
        {
            Assert.IsFalse((result.Output + result.Error).Contains("PRIVATE-NATIVE-STDERR-CANARY", StringComparison.Ordinal));
            StringAssert.Contains(result.Output, "environment-restored");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InstallsPreviewWrapperRequiresRecordedDiscoveryAndKeepsPartialResultPrivate(bool useUtc)
    {
        using StoreHubDiscoveryTests.Fixture fixture = new();
        StoreHubDiscoveryResult discovery = await StoreHubQueryTests.DiscoverNativeAsync(fixture);
        StoreHubQueryProbeResult probe = await StoreHubQueryProbe.RunAsync(fixture.Configuration, fixture.Input, fixture.Mapping,
            fixture.Experiment, discovery.Directory, new HashSet<string> { discovery.Packet.SourceSha256 }, new WindowsPrivateStorageGuard(),
            fixture.Credentials, new InventoryHttpTests.Handler(request => Task.FromResult(request.Method == HttpMethod.Post ?
                InventoryHttpTests.Json("{\"access_token\":\"FICTIONAL-TOKEN\",\"token_type\":\"Bearer\",\"expires_in\":3600}") :
                InventoryHttpTests.Json(StoreHubQueryTests.PreviewJson()))), new StoreDiagnosticTransportTests.Clock(),
            new AtomicFileWriter(new NewFileCommitter()), default, dateWireFormat: useUtc ? "utc-timestamp" : "date-only");
        string fake = Path.Combine(fixture.Root, "fake-query-runner.ps1");
        string marker = JsonSerializer.Serialize(new { exitCode = 2, packetDirectory = probe.Directory });
        await File.WriteAllTextAsync(fake, """
            if (-not $env:ANALYTICSET_HUB_DISCOVERY) { throw 'Discovery path is missing.' }
            if ($global:helperUseUtc -and ($args -notcontains 'FullyQualifiedName=AnalyticsET.Tests.StoreHubQueryLiveValidation.RecordInstallsUtcTimestampPreview')) { throw 'Wrong timestamp test selector.' }
            Write-Output 'PRIVATE-NATIVE-QUERY-CANARY'
            """ + "\nWrite-Output " + Quote("ANALYTICSET_HUB_RESULT " + marker) + "\n$global:LASTEXITCODE = 0\n");
        var result = await RunAsync(fixture.Configuration, fixture.Experiment, live: true, dotnet: fake, rejectPrompts: true,
            stage: "Installs", discoveryPath: discovery.Directory, useUtc: useUtc);
        Assert.AreEqual(2, result.Exit, result.Error);
        StringAssert.Contains(result.Output, "Installs query packet path");
        StringAssert.Contains(result.Output, "No query/report resource was created");
        StringAssert.Contains(result.Output, "environment-restored");
        foreach (string canary in new[] { "PRIVATE-NATIVE-QUERY-CANARY", "SELECT", "APP-FICTIONAL", "FICTIONAL-TOKEN" })
        { Assert.IsFalse((result.Output + result.Error).Contains(canary, StringComparison.Ordinal)); }
        await File.AppendAllTextAsync(Path.Combine(probe.Directory, "evidence.json"), "CORRUPTED-PRIVATE-CANARY");
        var corrupted = await RunAsync(fixture.Configuration, fixture.Experiment, live: true, dotnet: fake, rejectPrompts: true,
            stage: "Installs", discoveryPath: discovery.Directory, useUtc: useUtc);
        Assert.AreEqual(1, corrupted.Exit);
        Assert.IsFalse(corrupted.Output.Contains("Installs query packet path", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("valid", 0)]
    [DataRow("never", 2)]
    public async Task InstallsReportWrapperVerifiesReceiptAndReportBytesAndPrintsOnlyCounts(string mode, int code)
    {
        using StoreHubReportStageTests.Fixture fixture = await StoreHubReportStageTests.Fixture.CreateAsync(mode);
        StoreHubReportStageResult stage = await fixture.RunAsync();
        Assert.AreEqual(code, stage.Packet.ExitCode);
        string fake = Path.Combine(fixture.Root, "fake-report-runner.ps1");
        string marker = JsonSerializer.Serialize(new { exitCode = code, packetDirectory = stage.Directory });
        await File.WriteAllTextAsync(fake, """
            if (-not $env:ANALYTICSET_HUB_DISCOVERY) { throw 'Discovery path is missing.' }
            if ($args -notcontains 'FullyQualifiedName=AnalyticsET.Tests.StoreHubReportLiveValidation.RecordInstallsReport') { throw 'Wrong report test selector.' }
            Write-Output 'PRIVATE-NATIVE-REPORT-CANARY'
            """ + "\nWrite-Output " + Quote("ANALYTICSET_HUB_RESULT " + marker) + "\n$global:LASTEXITCODE = 0\n");
        var result = await RunAsync(fixture.Configuration, fixture.Experiment, live: true, dotnet: fake, rejectPrompts: true,
            stage: "InstallsReport", discoveryPath: fixture.Discovery.Directory);
        Assert.AreEqual(code, result.Exit, result.Error);
        StringAssert.Contains(result.Output, "Installs report packet path");
        StringAssert.Contains(result.Output, "Reporting resources recorded in Partner Center: 2 of 2");
        StringAssert.Contains(result.Output, "CSV downloaded: " + (code == 0 ? "True" : "False"));
        if (code == 0) { StringAssert.Contains(result.Output, "Days with equal totals to the baseline: 2 of 2"); }
        StringAssert.Contains(result.Output, "environment-restored");
        foreach (string canary in new[] { "PRIVATE-NATIVE-REPORT-CANARY", "SELECT", "APP-FICTIONAL", "FICTIONAL-TOKEN", "PRIVATE-SAS-CANARY", "2026-10-08" })
        { Assert.IsFalse((result.Output + result.Error).Contains(canary, StringComparison.Ordinal), canary); }
        string tampered = code == 0 ? Path.Combine(stage.Directory, "report.csv") : Path.Combine(stage.Directory, "evidence.json");
        await File.AppendAllTextAsync(tampered, "CORRUPTED-PRIVATE-CANARY");
        var corrupted = await RunAsync(fixture.Configuration, fixture.Experiment, live: true, dotnet: fake, rejectPrompts: true,
            stage: "InstallsReport", discoveryPath: fixture.Discovery.Directory);
        Assert.AreEqual(1, corrupted.Exit);
        Assert.IsFalse(corrupted.Output.Contains("Installs report packet path", StringComparison.Ordinal));
        var timestamp = await RunAsync(fixture.Configuration, fixture.Experiment, live: true, dotnet: fake, rejectPrompts: true,
            stage: "InstallsReport", discoveryPath: fixture.Discovery.Directory, useUtc: true);
        Assert.AreEqual(1, timestamp.Exit);
        StringAssert.Contains(timestamp.Error, "date-wire-control-stage-invalid");
    }

    [TestMethod]
    [DataRow("alias-invalid", "publisher-alias-input", "publisher-alias-invalid")]
    [DataRow("alias-missing", "publisher-selection", "publisher-alias-not-found")]
    [DataRow("alias-ambiguous", "publisher-selection", "publisher-alias-ambiguous")]
    [DataRow("binding-missing", "publisher-selection", "store-analytics-binding-missing")]
    [DataRow("account-missing", "publisher-selection", "store-account-scope-missing")]
    [DataRow("parent-invalid", "parent-store-id-input", "parent-store-id-invalid")]
    [DataRow("subscription-invalid", "subscription-store-id-input", "subscription-store-id-invalid")]
    [DataRow("parent-equals-subscription", "subscription-store-id-input", "parent-child-id-equal")]
    [DataRow("confirmation", "sample-confirmation", "sample-confirmation-required")]
    [DataRow("installs-format", "installs-window", "utc-date-format-invalid")]
    [DataRow("installs-zero", "installs-window", "utc-window-end-not-after-start")]
    [DataRow("installs-too-long", "installs-window", "utc-window-longer-than-seven-days")]
    [DataRow("installs-future", "installs-window", "utc-end-date-not-completed")]
    [DataRow("addon-format", "addon-acquisitions-window", "utc-date-format-invalid")]
    public async Task SetupFailuresReportSpecificFixedCategoriesBeforeSavingOrInvokingRunner(string scenario, string step, string category)
    {
        using StoreHubDiscoveryTests.Fixture fixture = new();
        if (scenario is "alias-ambiguous" or "binding-missing" or "account-missing")
        {
            var profile = InventoryModelTests.Profile();
            if (scenario == "binding-missing") { profile.Authorization.Remove(Core.AnalyticsET.Publishers.ApiResources.Store); }
            if (scenario == "account-missing") { profile.Accounts.Remove(Core.AnalyticsET.Publishers.ApiResources.StoreProgram); }
            var publishers = scenario == "alias-ambiguous" ? new[] { profile, profile } : new[] { profile };
            await File.WriteAllTextAsync(fixture.Configuration, JsonSerializer.Serialize(new Core.AnalyticsET.Publishers.PublisherConfiguration
            { SchemaVersion = 2, Publishers = [.. publishers] }, StoreHubInput.JsonOptions));
        }
        byte[] before = File.ReadAllBytes(fixture.Configuration);
        string directory = Path.Combine(fixture.Root, Guid.NewGuid().ToString("D"));
        var result = await RunAsync(fixture.Configuration, directory, live: true, scenario: scenario);
        Assert.AreEqual(1, result.Exit, result.Error);
        StringAssert.Contains(result.Error, "at " + step + " (" + category + ")");
        Assert.IsFalse(File.Exists(Path.Combine(directory, "input.json")));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Configuration));
        Assert.IsFalse((result.Output + result.Error).Contains("PRIVATE-INPUT-CANARY", StringComparison.Ordinal));
        Assert.IsFalse((result.Output + result.Error).Contains("APP-FICTIONAL", StringComparison.Ordinal));
        Assert.IsFalse((result.Output + result.Error).Contains("SUB-FICTIONAL", StringComparison.Ordinal));
        StringAssert.Contains(result.Output, "environment-restored");
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(string config, string directory,
        bool live = false, bool help = false, bool rejectPrompts = false, string? dotnet = null, string stage = "Discover", string scenario = "normal",
        string? discoveryPath = null, bool useUtc = false)
    {
        ProcessStartInfo start = new("powershell.exe")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        string prompts = rejectPrompts ? "function global:Read-Host { throw 'Unexpected prompt.' }" : """
            function global:Read-Host {
                param([string]$Prompt, [switch]$AsSecureString)
                if ($Prompt.StartsWith('Confirm')) { if ($global:helperScenario -eq 'confirmation') { return 'yes' }; return 'CONFIRM' }
                if ($Prompt.Contains('start boundary')) {
                    if ($global:helperScenario -eq 'installs-format' -and $Prompt.StartsWith('Installs')) { return 'PRIVATE-INPUT-CANARY' }
                    if ($global:helperScenario -eq 'addon-format' -and $Prompt.StartsWith('AddOnAcquisitions')) { return 'PRIVATE-INPUT-CANARY' }
                    if ($global:helperScenario -eq 'installs-too-long') { return '2026-09-01' }
                    return '2026-10-08'
                }
                if ($Prompt.Contains('end boundary')) {
                    if ($global:helperScenario -eq 'installs-zero' -and $Prompt.StartsWith('Installs')) { return '2026-10-08' }
                    if ($global:helperScenario -eq 'installs-future') { return [DateTime]::UtcNow.AddDays(1).ToString('yyyy-MM-dd') }
                    return '2026-10-09'
                }
                if (-not $AsSecureString) { throw 'Identifier prompts must be masked.' }
                $value = if ($Prompt.StartsWith('Existing')) { 'publisher-one' } elseif ($Prompt.StartsWith('Parent')) { 'APP-FICTIONAL' } else { 'SUB-FICTIONAL' }
                if ($global:helperScenario -eq 'alias-invalid' -and $Prompt.StartsWith('Existing')) { $value = 'PRIVATE-INPUT-CANARY' }
                if ($global:helperScenario -eq 'alias-missing' -and $Prompt.StartsWith('Existing')) { $value = 'publisher-absent' }
                if ($global:helperScenario -eq 'parent-invalid' -and $Prompt.StartsWith('Parent')) { $value = '../PRIVATE-INPUT-CANARY' }
                if ($global:helperScenario -eq 'subscription-invalid' -and $Prompt.StartsWith('Known')) { $value = '../PRIVATE-INPUT-CANARY' }
                if ($global:helperScenario -eq 'parent-equals-subscription' -and $Prompt.StartsWith('Known')) { $value = 'APP-FICTIONAL' }
                $secure = [Security.SecureString]::new()
                foreach ($character in $value.ToCharArray()) { $secure.AppendChar($character) }
                return $secure
            }
            """;
        string script = Path.Combine(CliProcess.ProjectDirectory, "scripts", "Invoke-StoreDownloadsHubValidation.ps1");
        string invocation = "& " + Quote(script) + " -ConfigurationPath " + Quote(config) + " -ExperimentDirectory " + Quote(directory) +
            " -Stage " + Quote(stage) + " -DotnetPath " + Quote(dotnet ?? "must-never-be-invoked") +
            (discoveryPath is null ? "" : " -DiscoveryPacketDirectory " + Quote(discoveryPath)) +
            (live ? " -RunLive" : "") + (help ? " -Help" : "") + (useUtc ? " -UseUtcTimestampPreview" : "");
        string restoration = """
            $helperExit = $LASTEXITCODE
            if ($env:ANALYTICSET_HUB_CONFIG -ne 'before-config' -or $env:ANALYTICSET_HUB_INPUT -ne 'before-input' -or
                $env:ANALYTICSET_HUB_EXPERIMENT -ne 'before-experiment' -or $env:ANALYTICSET_HUB_DISCOVERY -ne 'before-discovery') { exit 98 }
            Write-Output 'environment-restored'
            exit $helperExit
            """;
        start.ArgumentList.Add("$global:helperUseUtc = " + (useUtc ? "$true" : "$false") + "\n$global:helperScenario = " + Quote(scenario) + "\n" + prompts + "\n" + invocation + "\n" + restoration);
        start.Environment["ANALYTICSET_HUB_CONFIG"] = "before-config";
        start.Environment["ANALYTICSET_HUB_INPUT"] = "before-input";
        start.Environment["ANALYTICSET_HUB_EXPERIMENT"] = "before-experiment";
        start.Environment["ANALYTICSET_HUB_DISCOVERY"] = "before-discovery";
        using Process process = Process.Start(start)!;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(40));
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token), stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }
}
