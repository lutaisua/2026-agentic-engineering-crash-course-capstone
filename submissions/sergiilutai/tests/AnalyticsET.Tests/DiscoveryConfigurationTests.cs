using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class DiscoveryConfigurationTests
{
    [TestMethod]
    public void DiscoveryProfileRoundTripsWithoutProductIdsOrInventoryCredentials()
    {
        PublisherConfiguration configuration = new() { SchemaVersion = 2, Publishers = [Create()] };
        PublisherConfiguration copy = ConfigurationJson.Deserialize<PublisherConfiguration>(
            JsonSerializer.Serialize(configuration, ConfigurationJson.Options));

        ConfigurationValidator.Validate(copy);

        copy.Should().BeEquivalentTo(configuration);
        copy.Publishers[0].Accounts.Values.Should().OnlyContain(scope => scope.ProductIds.Count == 0);
        AuthorizationContext.ForProfile(copy.Publishers[0]).Should().HaveCount(4);
    }

    [TestMethod]
    public async Task NoProductIdCrudAndInvalidUpdatePreserveTheOtherPublisherAndOriginalBytes()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "publishers.json");
        PublisherStore store = new(path, new AtomicFileWriter(new FileCommitter()));
        await store.AddAsync(Create(), default);
        await store.AddAsync(Create("publisher-two", "account-two"), default);
        (await store.LoadAsync(default)).SchemaVersion.Should().Be(2);
        byte[] before = await File.ReadAllBytesAsync(path);
        PublisherProfile invalid = Create() with { Accounts = new() { [ApiResources.StoreProgram] = new() } };

        Func<Task> invalidUpdate = () => store.UpdateAsync("publisher-one", invalid, default);
        await invalidUpdate.Should().ThrowAsync<InputException>();
        (await File.ReadAllBytesAsync(path)).Should().Equal(before);

        await store.UpdateAsync("publisher-one", Create() with { DisplayName = "Renamed" }, default);
        (await store.LoadAsync(default)).Publishers[1].Should().BeEquivalentTo(Create("publisher-two", "account-two"));
    }

    [TestMethod]
    public void RejectsWrongProgramChildrenAmbiguousLegacyMixAndDuplicateSelections()
    {
        ProgramScope[] invalidScopes =
        [
            new() { PublisherAccountId = "account-one", ProductIds = ["old-id"] },
            new() { PublisherAccountId = "account-one", ExportSelection = [new() { Kind = "marketplace-offer", SourceId = "offer" }] },
            new() { PublisherAccountId = "account-one", ExportSelection = [new() { Kind = "store-addon", SourceId = "child" }] },
            new() { PublisherAccountId = "account-one", ExportSelection = [new() { Kind = "store-app", SourceId = "app", ParentId = "parent" }] },
            new() { PublisherAccountId = "account-one", ExportSelection = [new() { Kind = "store-app", SourceId = "app" }, new() { Kind = "store-app", SourceId = "app" }] }
        ];
        foreach (ProgramScope scope in invalidScopes)
        {
            PublisherProfile profile = Create();
            profile.Accounts[ApiResources.StoreProgram] = scope;
            Action validate = () => ConfigurationValidator.Validate(new PublisherConfiguration { SchemaVersion = 2, Publishers = [profile] });
            validate.Should().Throw<InputException>();
        }
    }

    [TestMethod]
    public void InventoryResourcesCannotCrossProgramsAndAreNotSchemaOneResources()
    {
        PublisherProfile profile = Create() with
        {
            Programs = [ApiResources.StoreProgram],
            Accounts = new() { [ApiResources.StoreProgram] = new() { PublisherAccountId = "account-one" } },
            Authorization = new() { [ApiResources.Store] = ProfileFixtures.Create().Authorization[ApiResources.Store] }
        };
        profile.Authorization.Add(ApiResources.MarketplaceProductIngestion, profile.Authorization[ApiResources.Store]);
        Action validate = () => ConfigurationValidator.Validate(profile, 2);
        validate.Should().Throw<InputException>();

        PublisherProfile old = ProfileFixtures.Create();
        old.Authorization.Add(ApiResources.StoreInventory, old.Authorization[ApiResources.Store]);
        Action validateOld = () => ConfigurationValidator.Validate(old);
        validateOld.Should().Throw<InputException>();
    }

    [TestMethod]
    public void CoverageAttestationRequiresPairedEvidenceAndDoesNotAllowSecretFields()
    {
        PublisherProfile profile = Create();
        profile.Accounts[ApiResources.MarketplaceProgram] = new()
        {
            PublisherAccountId = "account-one",
            Discovery = new() { Coverage = new() { ExpectedOfferTypes = ["softwareAsAService"], VerifiedAt = DateTimeOffset.UtcNow } }
        };
        Action validate = () => ConfigurationValidator.Validate(profile, 2);
        validate.Should().Throw<InputException>().WithMessage("*both*");
        Action deserialize = () => ConfigurationJson.Deserialize<PublisherConfiguration>(
            """{"schemaVersion":2,"publishers":[],"discovery":{"client_secret":"DO-NOT-ECHO"}}""");
        deserialize.Should().Throw<InputException>().Which.Message.Should().NotContain("DO-NOT-ECHO");
    }

    internal static PublisherProfile Create(string id = "publisher-one", string account = "account-one")
    {
        PublisherProfile profile = ProfileFixtures.Create(id, account);
        return profile with
        {
            Accounts = profile.Accounts.ToDictionary(pair => pair.Key, pair => pair.Value with
            {
                ProductIds = [],
                Discovery = new()
            })
        };
    }
}
