using System.Diagnostics;
using System.Text.Json;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

[TestClass]
[DoNotParallelize]
public sealed class StoreDiagnosticHelperTests
{
    [TestMethod]
    public async Task OfflineMaskedPreparationAndHelpNeverStartDotnetAndPreserveExistingInput()
    {
        using StoreSubscriptionDiagnosticTests.Fixture fixture = new();
        string input = Path.Combine(fixture.Root, "helper", "sample.json");
        string output = Path.Combine(fixture.Root, "helper", "runs");
        var help = await RunAsync(fixture.Configuration, input, output, help: true);
        Assert.AreEqual(0, help.Exit);
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(input)));
        var prepared = await RunAsync(fixture.Configuration, input, output);
        Assert.AreEqual(0, prepared.Exit, prepared.Error);
        Assert.IsTrue(prepared.Output.Contains("No credential was read", StringComparison.Ordinal));
        StoreDiagnosticInput sample = StoreDiagnosticInput.Parse(await File.ReadAllTextAsync(input), TimeProvider.System);
        Assert.AreEqual("publisher-one", sample.PublisherId);
        new WindowsPrivateStorageGuard().VerifyFile(input);
        byte[] before = await File.ReadAllBytesAsync(input);
        var preserved = await RunAsync(fixture.Configuration, input, output, rejectPrompts: true);
        Assert.AreEqual(0, preserved.Exit, preserved.Error);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(input));
        foreach (var result in new[] { help, prepared, preserved })
        {
            Assert.IsFalse((result.Output + result.Error).Contains("FICTIONAL", StringComparison.Ordinal));
            Assert.IsTrue(result.Output.Contains("environment-restored", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    [DataRow(130)]
    [DataRow(1)]
    [DataRow(99)]
    public async Task LiveWrapperValidatesReceiptSanitizesNoiseAndRestoresEnvironment(int code)
    {
        using StoreSubscriptionDiagnosticTests.Fixture fixture = new();
        WindowsPrivateStorageGuard privacy = new();
        string output = Path.Combine(fixture.Root, "helper-runs");
        StoreDiagnosticPacket packet = StoreDiagnosticPacketTests.Packet() with
        {
            ExitCode = code is 0 or 2 or 130 ? code : 2,
            Probes = StoreSubscriptionDiagnostic.Plan(StoreDiagnosticInputTests.Sample).Select(item =>
                new DiagnosticProbe(item.Route.Operation, item.Route.Query, code == 0 ? "http-request-failed" : "not-attempted",
                    code == 0 ? 400 : null, new("not-read"))).ToArray()
        };
        string directory = await new StoreDiagnosticPacketStore(output, privacy, new AtomicFileWriter(new NewFileCommitter()))
            .SaveAsync(packet, default);
        string fake = Path.Combine(fixture.Root, "fake-dotnet.ps1");
        string marker = JsonSerializer.Serialize(new { exitCode = code, packetDirectory = directory });
        await File.WriteAllTextAsync(fake, """
            if (-not $env:ANALYTICSET_LIVE_CONFIG -or -not $env:ANALYTICSET_DIAGNOSTIC_INPUT -or -not $env:ANALYTICSET_DIAGNOSTIC_OUTPUT) { throw 'Missing paths.' }
            Write-Output 'PRIVATE-NATIVE-NOISE-FICTIONAL'
            """ + "\nWrite-Output " + Quote("ANALYTICSET_DIAGNOSTIC_RESULT " + marker) + "\n$global:LASTEXITCODE = 0\n");
        var result = await RunAsync(fixture.Configuration, fixture.Input, output, live: true, dotnet: fake, rejectPrompts: true);
        Assert.AreEqual(code == 99 ? 1 : code, result.Exit, result.Error);
        Assert.IsTrue(result.Output.Contains("environment-restored", StringComparison.Ordinal));
        Assert.IsFalse((result.Output + result.Error).Contains("PRIVATE-NATIVE-NOISE", StringComparison.Ordinal));
        Assert.IsFalse((result.Output + result.Error).Contains("SUB-FICTIONAL", StringComparison.Ordinal));
        if (code is 0 or 2 or 130) { Assert.IsTrue(result.Output.Contains("Diagnostic packet path", StringComparison.Ordinal)); }
        else { Assert.IsFalse(result.Output.Contains("Diagnostic packet path", StringComparison.Ordinal)); }

        if (code == 0)
        {
            await File.AppendAllTextAsync(Path.Combine(directory, "evidence.json"), "CORRUPTED-PRIVATE-CANARY");
            var corrupted = await RunAsync(fixture.Configuration, fixture.Input, output, live: true, dotnet: fake, rejectPrompts: true);
            Assert.AreEqual(1, corrupted.Exit);
            Assert.IsFalse(corrupted.Output.Contains("Diagnostic packet path", StringComparison.Ordinal));
            Assert.IsFalse((corrupted.Output + corrupted.Error).Contains("CANARY", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task MissingMarkerUnsafeStorageAndInterruptedRunnerFailWithoutExposingInput()
    {
        using StoreSubscriptionDiagnosticTests.Fixture fixture = new();
        string fake = Path.Combine(fixture.Root, "failed-dotnet.ps1");
        await File.WriteAllTextAsync(fake, "Write-Output 'PRIVATE-NATIVE-CANARY'; $global:LASTEXITCODE = 1");
        var missing = await RunAsync(fixture.Configuration, fixture.Input, Path.Combine(fixture.Root, "runs"), live: true, dotnet: fake);
        Assert.AreEqual(1, missing.Exit);
        Assert.IsFalse((missing.Output + missing.Error).Contains("CANARY", StringComparison.Ordinal));
        Assert.IsTrue(missing.Output.Contains("environment-restored", StringComparison.Ordinal));

        await File.WriteAllTextAsync(fake, "$global:LASTEXITCODE = 130");
        var cancelled = await RunAsync(fixture.Configuration, fixture.Input, Path.Combine(fixture.Root, "runs"), live: true, dotnet: fake);
        Assert.AreEqual(130, cancelled.Exit, cancelled.Error);
        Assert.IsTrue(cancelled.Output.Contains("environment-restored", StringComparison.Ordinal));

        string shared = Path.Combine(fixture.Root, "shared");
        Directory.CreateDirectory(shared);
        // Replace inherited private rules with ordinary inherited temporary-directory permissions.
        using TemporaryDirectory unprotected = new();
        string unsafeInput = Path.Combine(unprotected.Path, "input.json");
        await File.WriteAllTextAsync(unsafeInput, "PRIVATE-INPUT-CANARY");
        var unsafeResult = await RunAsync(fixture.Configuration, unsafeInput, shared, live: true, dotnet: fake);
        Assert.AreEqual(1, unsafeResult.Exit);
        Assert.IsFalse((unsafeResult.Output + unsafeResult.Error).Contains("CANARY", StringComparison.Ordinal));
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private static async Task<(int Exit, string Output, string Error)> RunAsync(string config, string input, string output,
        bool live = false, bool help = false, bool rejectPrompts = false, string? dotnet = null)
    {
        ProcessStartInfo start = new("powershell.exe")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        string prompts = rejectPrompts ? "function global:Read-Host { throw 'Unexpected prompt.' }" : """
            function global:Read-Host {
                param([string]$Prompt, [switch]$AsSecureString)
                if ($Prompt.StartsWith('Confirm')) { return 'CONFIRM' }
                if (-not $AsSecureString) { throw 'Sample prompts must be masked.' }
                $value = if ($Prompt.StartsWith('Existing')) { 'publisher-one' } elseif ($Prompt.StartsWith('Parent')) { 'APP-FICTIONAL' } else { 'SUB-FICTIONAL' }
                $secure = [Security.SecureString]::new()
                foreach ($character in $value.ToCharArray()) { $secure.AppendChar($character) }
                return $secure
            }
            """;
        string script = Path.Combine(CliProcess.ProjectDirectory, "scripts", "Invoke-StoreSubscriptionDiagnostic.ps1");
        string invocation = "& " + Quote(script) + " -ConfigurationPath " + Quote(config) + " -InputPath " + Quote(input) +
            " -OutputRoot " + Quote(output) + " -DotnetPath " + Quote(dotnet ?? "must-never-be-invoked") +
            (live ? " -RunLive" : "") + (help ? " -Help" : "");
        string restoration = """
            $helperExit = $LASTEXITCODE
            if ($env:ANALYTICSET_LIVE_CONFIG -ne 'before-config' -or $env:ANALYTICSET_DIAGNOSTIC_INPUT -ne 'before-input' -or
                $env:ANALYTICSET_DIAGNOSTIC_OUTPUT -ne 'before-output') { exit 98 }
            Write-Output 'environment-restored'
            exit $helperExit
            """;
        start.ArgumentList.Add(prompts + "\n" + invocation + "\n" + restoration);
        start.Environment["ANALYTICSET_LIVE_CONFIG"] = "before-config";
        start.Environment["ANALYTICSET_DIAGNOSTIC_INPUT"] = "before-input";
        start.Environment["ANALYTICSET_DIAGNOSTIC_OUTPUT"] = "before-output";
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

