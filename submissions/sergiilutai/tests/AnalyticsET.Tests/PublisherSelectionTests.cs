using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Publishers;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class PublisherSelectionTests
{
    [TestMethod]
    public void SameTenantAndClientDoNotSharePublisherAuthorizationContext()
    {
        PublisherProfile first = ProfileFixtures.Create();
        PublisherProfile second = ProfileFixtures.Create("publisher-two", "account-two");
        PublisherConfiguration configuration = new() { Publishers = [first, second] };

        IReadOnlyList<PublisherProfile> selected = PublisherSelection.Resolve(configuration, [first.Id, second.Id], false);
        AuthorizationContext firstContext = AuthorizationContext.Create(selected[0], ApiResources.StoreProgram, ApiResources.Store);
        AuthorizationContext secondContext = AuthorizationContext.Create(selected[1], ApiResources.StoreProgram, ApiResources.Store);

        firstContext.TenantId.Should().Be(secondContext.TenantId);
        firstContext.ClientId.Should().Be(secondContext.ClientId);
        firstContext.StorageKey.Should().NotBe(secondContext.StorageKey);
        firstContext.PublisherAccountId.Should().NotBe(secondContext.PublisherAccountId);
        AuthorizationContext.ForProfile(first).Select(context => context.StorageKey).Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void EveryAuthorizationIdentityFieldAffectsItsStorageKey()
    {
        AuthorizationContext baseline = AuthorizationContext.Create(ProfileFixtures.Create(), ApiResources.StoreProgram, ApiResources.Store);
        AuthorizationContext[] changed =
        [
            baseline with { PublisherId = "other" }, baseline with { TenantId = "other" },
            baseline with { ClientId = "other" }, baseline with { AuthorizedAccountId = "other" },
            baseline with { PublisherAccountId = "other" }, baseline with { Program = "other" },
            baseline with { Resource = "other" }, baseline with { CredentialReference = "other" }
        ];

        changed.Select(context => context.StorageKey).Should().OnlyHaveUniqueItems().And.NotContain(baseline.StorageKey);
    }

    [TestMethod]
    public void SelectionRejectsUnknownDuplicateAndImplicitPublishers()
    {
        PublisherConfiguration configuration = new() { Publishers = [ProfileFixtures.Create()] };
        string[][] invalid = [[], ["unknown"], ["publisher-one", "publisher-one"], ["../unsafe"]];
        foreach (string[] ids in invalid)
        {
            Action select = () => PublisherSelection.Resolve(configuration, ids, false);
            select.Should().Throw<InputException>();
        }

        PublisherSelection.Resolve(configuration, [], true).Should().ContainSingle();
    }

    [TestMethod]
    public async Task UnknownCliSelectionFailsWithoutChangingConfigurationOrWritingExports()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "profiles.json");
        string before = JsonSerializer.Serialize(new PublisherConfiguration { Publishers = [ProfileFixtures.Create()] }, ConfigurationJson.Options);
        await File.WriteAllTextAsync(path, before);

        var result = await CliProcess.RunAsync("--config", path, "publisher", "show", "--publisher", "unknown");

        result.ExitCode.Should().Be(3);
        result.Output.Should().BeEmpty();
        result.Error.Should().Contain("Unknown publisher");
        (await File.ReadAllTextAsync(path)).Should().Be(before);
        Directory.GetFiles(directory.Path).Should().ContainSingle();
        Directory.GetDirectories(directory.Path).Should().BeEmpty();
    }
}
