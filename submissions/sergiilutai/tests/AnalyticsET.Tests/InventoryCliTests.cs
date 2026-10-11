using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class InventoryCliTests
{
    [TestMethod]
    public async Task OfflineListDoesNotResolveCredentialsOrCallProviderAndReportsMissingCache()
    {
        using TemporaryDirectory directory = new();
        PublisherProfile profile = InventoryModelTests.Profile();
        string configuration = await SaveAsync(directory.Path, 2, [profile]);
        InventoryCoordinatorTests.FakeCredentials credentials = new();
        InventoryCoordinatorTests.FakeProvider provider = new();
        InventoryCommands commands = Commands(credentials, provider);
        string state = Path.Combine(directory.Path, "private");
        CommandLine command = new(configuration, ["products", "list", "--publisher", profile.Id, "--program", ApiResources.StoreProgram, "--offline", "--state", state]);
        var missing = await commands.ExecuteAsync(command, default);
        missing.ExitCode.Should().Be(2);
        missing.Output.Should().Contain("no-matching-successful-snapshot");
        Directory.Exists(state).Should().BeFalse();
        InventoryStore store = InventoryStoreTests.Store(directory.Path);
        await using (var session = await store.OpenAsync(InventoryModelTests.Binding(profile), default))
        { await session.CommitAsync(InventoryStoreTests.Snapshot(InventoryModelTests.Binding(profile)), default); }
        var cached = await commands.ExecuteAsync(command, default);
        cached.ExitCode.Should().Be(0);
        cached.Output.Should().Contain("cached-offline").And.Contain("ageSeconds").And.Contain("coverage").And.Contain("not-checked-offline");
        credentials.Calls.Should().Be(0);
        provider.Calls.Should().Be(0);
    }

    [TestMethod]
    public async Task OnboardingSelectsOnlyRequestedProgramAndPreservesIndependentOutcomes()
    {
        using TemporaryDirectory directory = new();
        PublisherProfile first = InventoryModelTests.Profile();
        PublisherProfile second = InventoryModelTests.Profile("publisher-two", "account-two");
        string configuration = await SaveAsync(directory.Path, 2, [first, second]);
        InventoryCoordinatorTests.FakeCredentials credentials = new();
        InventoryCoordinatorTests.FakeProvider provider = new() { FailedPublisher = second.Id };
        var result = await Commands(credentials, provider).ExecuteAsync(new(configuration,
            ["publisher", "onboard", "--all", "--program", ApiResources.StoreProgram, "--state", Path.Combine(directory.Path, "private")]), default);
        result.ExitCode.Should().Be(2);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        document.RootElement.EnumerateArray().Select(item => item.GetProperty("outcome").GetString()).Should().Equal("complete", "failed");
        document.RootElement.EnumerateArray().Select(item => item.GetProperty("program").GetString()).Should().OnlyContain(program => program == ApiResources.StoreProgram);
        credentials.Calls.Should().Be(2);
        provider.Calls.Should().Be(2);
    }

    [TestMethod]
    public async Task SchemaOneAndInvalidOptionsFailBeforeCredentialOrNetworkAccess()
    {
        using TemporaryDirectory directory = new();
        string configuration = await SaveAsync(directory.Path, 1, [ProfileFixtures.Create()]);
        InventoryCoordinatorTests.FakeCredentials credentials = new();
        InventoryCoordinatorTests.FakeProvider provider = new();
        InventoryCommands commands = Commands(credentials, provider);
        Func<Task> migration = async () => await commands.ExecuteAsync(new(configuration, ["products", "refresh", "--all"]), default);
        await migration.Should().ThrowAsync<InputException>().WithMessage("*migration*");
        foreach (string[] invalid in new[]
        {
            new[] { "products", "list", "--all", "--all" },
            new[] { "products", "refresh", "--all", "--offline" },
            new[] { "publisher", "onboard", "--program" }
        })
        {
            Func<Task> invoke = async () => await commands.ExecuteAsync(new(configuration, invalid), default);
            await invoke.Should().ThrowAsync<InputException>();
        }
        credentials.Calls.Should().Be(0);
        provider.Calls.Should().Be(0);
        (await CliProcess.RunAsync("--config", configuration, "products", "list", "--all", "--offline")).ExitCode.Should().Be(3);
    }

    private static InventoryCommands Commands(IApplicationCredentialResolver credentials, IProductInventoryProvider provider) => new(
        new PublisherStoreFactory(new AtomicFileWriter(new FileCommitter())), credentials, [provider],
        new WindowsPrivateStorageGuard(), new AtomicFileWriter(new FileCommitter()), TimeProvider.System);

    private static async Task<string> SaveAsync(string directory, int schema, List<PublisherProfile> profiles)
    {
        string path = Path.Combine(directory, "profiles.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new PublisherConfiguration { SchemaVersion = schema, Publishers = profiles }, ConfigurationJson.Options));
        return path;
    }
}
