using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class PublisherStoreTests
{
    [TestMethod]
    public async Task TwoProfileCrudPreservesUnrelatedConfigurationAndSettings()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "profiles.json");
        PublisherProfile first = ProfileFixtures.Create() with
        {
            Extensions = new() { ["notes"] = JsonSerializer.SerializeToElement("preserve me") }
        };
        first.Accounts[ApiResources.StoreProgram] = first.Accounts[ApiResources.StoreProgram] with
        {
            Extensions = new() { ["region"] = JsonSerializer.SerializeToElement("preserve account setting") }
        };
        first.Authorization[ApiResources.Store] = first.Authorization[ApiResources.Store] with
        {
            Extensions = new() { ["label"] = JsonSerializer.SerializeToElement("preserve resource setting") }
        };
        PublisherConfiguration source = new()
        {
            Publishers = [first],
            Extensions = new() { ["outputRoot"] = JsonSerializer.SerializeToElement("exports") }
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(source, ConfigurationJson.Options));
        PublisherStore store = new(path, new AtomicFileWriter(new FileCommitter()));
        PublisherProfile second = ProfileFixtures.Create("publisher-two", "account-two");

        await store.AddAsync(second, default);
        await store.UpdateAsync(first.Id, ProfileFixtures.Create() with { DisplayName = "Updated" }, default);
        PublisherConfiguration updated = await store.LoadAsync(default);

        updated.Publishers.Should().HaveCount(2);
        updated.Publishers[1].Should().BeEquivalentTo(second);
        updated.Extensions!["outputRoot"].GetString().Should().Be("exports");
        updated.Publishers[0].Extensions!["notes"].GetString().Should().Be("preserve me");
        updated.Publishers[0].Accounts[ApiResources.StoreProgram].Extensions!["region"].GetString()
            .Should().Be("preserve account setting");
        updated.Publishers[0].Authorization[ApiResources.Store].Extensions!["label"].GetString()
            .Should().Be("preserve resource setting");
        await store.RemoveAsync(first.Id, default);
        (await store.LoadAsync(default)).Publishers.Should().ContainSingle().Which.Should().BeEquivalentTo(second);
    }

    [TestMethod]
    public async Task FailureAfterTemporaryWritePreservesOriginalBytesAndCleansTemporaryFile()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "profiles.json");
        PublisherStore initial = new(path, new AtomicFileWriter(new FileCommitter()));
        await initial.AddAsync(ProfileFixtures.Create(), default);
        byte[] before = await File.ReadAllBytesAsync(path);
        PublisherStore failing = new(path, new AtomicFileWriter(new FailingCommitter()));

        Func<Task> update = () => failing.UpdateAsync("publisher-one", ProfileFixtures.Create() with { DisplayName = "Changed" }, default);

        await update.Should().ThrowAsync<IOException>();
        (await File.ReadAllBytesAsync(path)).Should().Equal(before);
        Directory.GetFiles(directory.Path, "*.tmp").Should().BeEmpty();
    }

    [TestMethod]
    public async Task DuplicateAddAndIdChangeDoNotAlterConfiguration()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "profiles.json");
        PublisherStore store = new(path, new AtomicFileWriter(new FileCommitter()));
        await store.AddAsync(ProfileFixtures.Create(), default);
        string before = await File.ReadAllTextAsync(path);

        Func<Task> duplicate = () => store.AddAsync(ProfileFixtures.Create(), default);
        Func<Task> rename = () => store.UpdateAsync("publisher-one", ProfileFixtures.Create("publisher-two"), default);

        await duplicate.Should().ThrowAsync<InputException>();
        await rename.Should().ThrowAsync<InputException>();
        (await File.ReadAllTextAsync(path)).Should().Be(before);
    }

    [TestMethod]
    public async Task CliCrudCreatesTwoProfilesAndKeepsOtherAccountUnchanged()
    {
        using TemporaryDirectory directory = new();
        string configuration = Path.Combine(directory.Path, "profiles.json");
        string file = Path.Combine(directory.Path, "input.json");
        foreach (string id in new[] { "publisher-one", "publisher-two" })
        {
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(ProfileFixtures.Create(id, id), ConfigurationJson.Options));
            (await CliProcess.RunAsync("--config", configuration, "publisher", "add", "--file", file)).ExitCode.Should().Be(0);
        }

        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(
            ProfileFixtures.Create() with { DisplayName = "Changed" }, ConfigurationJson.Options));
        (await CliProcess.RunAsync("--config", configuration, "publisher", "update", "publisher-one", "--file", file)).ExitCode.Should().Be(0);
        var list = await CliProcess.RunAsync("--config", configuration, "publisher", "list");
        List<PublisherProfile> profiles = JsonSerializer.Deserialize<List<PublisherProfile>>(list.Output, ConfigurationJson.Options)!;

        profiles.Should().HaveCount(2);
        profiles[1].Accounts[ApiResources.StoreProgram].PublisherAccountId.Should().Be("publisher-two");
        (await CliProcess.RunAsync("--config", configuration, "publisher", "remove", "publisher-one")).ExitCode.Should().Be(0);
        var final = await CliProcess.RunAsync("--config", configuration, "publisher", "list");
        JsonSerializer.Deserialize<List<PublisherProfile>>(final.Output, ConfigurationJson.Options)!.Should().ContainSingle();
    }

    private sealed class FailingCommitter : IFileCommitter
    {
        public void Commit(string temporaryPath, string destination)
        {
            File.Exists(temporaryPath).Should().BeTrue();
            throw new IOException("Simulated publication failure.");
        }
    }
}
