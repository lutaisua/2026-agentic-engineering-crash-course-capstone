using System.Diagnostics;
using System.Text.Json;
using Core.AnalyticsET.Publishers;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class DocumentationExamplesTests
{
    [TestMethod]
    public async Task DiscoveryExampleAddsWithoutProductIdsAndOfflineHelpAndCacheStatusWork()
    {
        using TemporaryDirectory directory = new();
        string configuration = Path.Combine(directory.Path, "publishers.json");
        string example = Path.Combine(CliProcess.ProjectDirectory, "docs", "examples", "publisher-discovery.json");
        (await CliProcess.RunAsync("--config", configuration, "publisher", "add", "--file", example)).ExitCode.Should().Be(0);
        PublisherConfiguration saved = ConfigurationJson.Deserialize<PublisherConfiguration>(await File.ReadAllTextAsync(configuration));
        saved.SchemaVersion.Should().Be(2);
        saved.Publishers.Single().Accounts[ApiResources.StoreProgram].ProductIds.Should().BeEmpty();
        (await CliProcess.RunAsync("--config", configuration, "config", "validate")).ExitCode.Should().Be(0);
        var cached = await CliProcess.RunAsync("--config", configuration, "products", "list", "--all", "--offline", "--state", Path.Combine(directory.Path, "absent-cache"));
        cached.ExitCode.Should().Be(2);
        cached.Output.Should().Contain("no-matching-successful-snapshot");
        var help = await CliProcess.RunAsync("--help");
        help.Output.Should().Contain("publisher onboard").And.Contain("products refresh").And.Contain("--offline");
    }

    [TestMethod]
    public async Task EveryV01ExampleRunsAgainstTemporaryConfiguration()
    {
        using TemporaryDirectory directory = new();
        string configuration = Path.Combine(directory.Path, "publishers.json");
        ProcessStartInfo start = new("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(CliProcess.ProjectDirectory, "docs", "examples", "v0.1-smoke.ps1"),
            "-ConfigurationPath", configuration })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(40));
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            string stdout = await output;
            string stderr = await error;

            process.ExitCode.Should().Be(0, stderr);
            stdout.Should().Contain("Example commands passed (offline)").And.Contain("unverified");
            PublisherConfiguration result = ConfigurationJson.Deserialize<PublisherConfiguration>(await File.ReadAllTextAsync(configuration));
            result.Publishers.Should().ContainSingle().Which.Id.Should().Be("publisher-two");
            result.Publishers[0].Accounts[ApiResources.StoreProgram].PublisherAccountId.Should().Be("account-two");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
