using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class OfflineValidationTests
{
    [TestMethod]
    public async Task ValidConfigReportsReferencesAndAuthorizationAsUnverifiedWithoutCredentials()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "profiles.json");
        PublisherStore store = new(path, new AtomicFileWriter(new FileCommitter()));
        await store.AddAsync(ProfileFixtures.Create(), default);

        var result = await CliProcess.RunAsync("--config", path, "config", "validate");

        result.ExitCode.Should().Be(0);
        using JsonDocument output = JsonDocument.Parse(result.Output);
        output.RootElement.GetProperty("validationStatus").GetString().Should().Be("valid");
        output.RootElement.GetProperty("authorizationStatus").GetString().Should().Be("unverified");
        output.RootElement.GetProperty("credentialAvailability").GetString().Should().Be("not-checked-offline");
        result.Error.Should().BeEmpty();
        Directory.GetFiles(directory.Path).Should().HaveCount(2, "only the configuration and its write lock exist");
    }

    [TestMethod]
    public async Task SecretFieldsAndMalformedInputCannotLeakIntoCliOutput()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "profiles.json");
        string[] invalid =
        [
            "{\"schemaVersion\":1,\"publishers\":[],\"clientSecret\":\"DO-NOT-ECHO\"}",
            "{\"schemaVersion\":1,\"publishers\":\"DO-NOT-ECHO\"}",
            "{\"schemaVersion\":1,\"refreshToken\":\"DO-NOT-ECHO\""
        ];
        foreach (string json in invalid)
        {
            await File.WriteAllTextAsync(path, json);

            var result = await CliProcess.RunAsync("--config", path, "config", "validate");

            result.ExitCode.Should().Be(3);
            (result.Output + result.Error).Should().NotContain("DO-NOT-ECHO");
        }
    }

    [TestMethod]
    public async Task UnknownPublisherFailsBeforeCredentialReferenceInspection()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "profiles.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            new PublisherConfiguration { Publishers = [ProfileFixtures.Create()] }, ConfigurationJson.Options));
        FailingInspector inspector = new();
        ConfigurationCommands commands = new(new PublisherStoreFactory(new AtomicFileWriter(new FileCommitter())), inspector);

        Func<Task> validate = () => commands.ExecuteAsync(new(path, ["config", "validate", "--publisher", "unknown"]), default);

        await validate.Should().ThrowAsync<InputException>();
        inspector.Calls.Should().Be(0);
    }

    private sealed class FailingInspector : ICredentialReferenceInspector
    {
        internal int Calls { get; private set; }
        public CredentialReference Describe(AuthorizationContext context)
        {
            Calls++;
            throw new InvalidOperationException("Must not inspect an invalid selection.");
        }
    }
}
