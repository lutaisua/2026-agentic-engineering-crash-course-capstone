using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class ConfigurationMigrationTests
{
    [TestMethod]
    public async Task MigrationPreservesOriginalBytesReferencesExtensionsAndAmbiguousIds()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source.json");
        string destination = Path.Combine(directory.Path, "private", "version2.json");
        PublisherProfile profile = ProfileFixtures.Create();
        profile.Accounts[ApiResources.StoreProgram] = profile.Accounts[ApiResources.StoreProgram] with
        {
            ProductIds = ["could-be-app-or-addon"],
            Extensions = new() { ["region"] = JsonSerializer.SerializeToElement("preserve") }
        };
        PublisherConfiguration original = new()
        {
            Publishers = [profile, ProfileFixtures.Create("publisher-two", "account-two")],
            Extensions = new() { ["outputRoot"] = JsonSerializer.SerializeToElement("exports") }
        };
        await File.WriteAllTextAsync(source, JsonSerializer.Serialize(original, ConfigurationJson.Options));
        byte[] before = await File.ReadAllBytesAsync(source);
        ConfigurationMigrator migrator = CreateMigrator();

        await migrator.MigrateAsync(source, destination, default);

        (await File.ReadAllBytesAsync(source)).Should().Equal(before);
        PublisherConfiguration result = await new PublisherStoreFactory(new AtomicFileWriter(new FileCommitter()))
            .Create(destination).LoadAsync(default);
        result.SchemaVersion.Should().Be(2);
        result.Extensions!["outputRoot"].GetString().Should().Be(original.Extensions!["outputRoot"].GetString());
        result.Publishers[0].Authorization.Should().BeEquivalentTo(profile.Authorization);
        result.Publishers[0].Accounts[ApiResources.StoreProgram].Extensions!["region"].GetString().Should()
            .Be(profile.Accounts[ApiResources.StoreProgram].Extensions!["region"].GetString());
        result.Publishers[0].Accounts[ApiResources.StoreProgram].ProductIds.Should().BeEmpty();
        result.Publishers[0].Accounts[ApiResources.StoreProgram].ExportSelection.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ProductSelection { Kind = "legacy", SourceId = "could-be-app-or-addon" });
        new WindowsPrivateStorageGuard().VerifyFile(destination);
    }

    [TestMethod]
    public async Task SamePathExistingDestinationAndCommitFailureNeverChangeTheOriginal()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source.json");
        await File.WriteAllTextAsync(source, JsonSerializer.Serialize(new PublisherConfiguration
        {
            Publishers = [ProfileFixtures.Create()]
        }, ConfigurationJson.Options));
        byte[] before = await File.ReadAllBytesAsync(source);
        Func<Task> same = () => CreateMigrator().MigrateAsync(source, source, default);
        await same.Should().ThrowAsync<InputException>();

        string existing = Path.Combine(directory.Path, "existing.json");
        await File.WriteAllTextAsync(existing, "unchanged destination");
        Func<Task> occupied = () => CreateMigrator().MigrateAsync(source, existing, default);
        await occupied.Should().ThrowAsync<InputException>();
        (await File.ReadAllTextAsync(existing)).Should().Be("unchanged destination");

        string failedDestination = Path.Combine(directory.Path, "private", "failed.json");
        Func<Task> failed = () => CreateMigrator(new AtomicFileWriter(new FailingCommitter())).MigrateAsync(source, failedDestination, default);
        await failed.Should().ThrowAsync<IOException>();
        File.Exists(failedDestination).Should().BeFalse();
        Directory.GetFiles(Path.GetDirectoryName(failedDestination)!, "*.tmp").Should().BeEmpty();
        (await File.ReadAllBytesAsync(source)).Should().Equal(before);
    }

    [TestMethod]
    public async Task CliMigrationWritesASeparateOfflineConfiguration()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source.json");
        string destination = Path.Combine(directory.Path, "private", "version2.json");
        await File.WriteAllTextAsync(source, JsonSerializer.Serialize(new PublisherConfiguration
        {
            Publishers = [ProfileFixtures.Create()]
        }, ConfigurationJson.Options));

        var migrate = await CliProcess.RunAsync("--config", source, "config", "migrate", "--to-version", "2", "--output", destination);
        migrate.ExitCode.Should().Be(0, migrate.Error);
        migrate.Output.Should().Contain("Original preserved").And.Contain("unverified");
        (await CliProcess.RunAsync("--config", destination, "config", "validate")).ExitCode.Should().Be(0);
        (await CliProcess.RunAsync("--config", source, "config", "validate")).ExitCode.Should().Be(0);
    }

    [TestMethod]
    public void MigrationRejectsRepositoryDestinations()
    {
        using TemporaryDirectory directory = new();
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        Action repository = () => new WindowsPrivateStorageGuard().PrepareDirectory(Path.Combine(directory.Path, "private"));
        repository.Should().Throw<InputException>().WithMessage("*outside*");
    }

    [TestMethod]
    public void MigrationDoesNotRestrictABroadExistingParentDirectory()
    {
        using TemporaryDirectory directory = new();
        Action prepare = () => new WindowsPrivateStorageGuard().PrepareDirectory(directory.Path);
        prepare.Should().Throw<InputException>().WithMessage("*protected directory permissions*");
    }

    private static ConfigurationMigrator CreateMigrator(IAtomicFileWriter? writer = null) => new(
        new PublisherStoreFactory(new AtomicFileWriter(new FileCommitter())), new WindowsPrivateStorageGuard(),
        writer ?? new AtomicFileWriter(new NewFileCommitter()));

    private sealed class FailingCommitter : IFileCommitter
    {
        public void Commit(string temporaryPath, string destination) => throw new IOException("Simulated migration failure.");
    }
}
