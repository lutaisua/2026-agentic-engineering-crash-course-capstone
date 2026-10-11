using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Publishers;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class InventoryModelTests
{
    [TestMethod]
    public void BindingSeparatesPublisherProgramLogicalResourceFlowAndCredentialGeneration()
    {
        PublisherProfile first = Profile();
        PublisherProfile second = Profile("publisher-two", "account-two");
        AuthorizationContext store = AuthorizationContext.Create(first, ApiResources.StoreProgram, ApiResources.StoreInventory);
        AuthorizationContext analytics = AuthorizationContext.Create(first, ApiResources.StoreProgram, ApiResources.Store);
        store.Resource.Should().Be(analytics.Resource);
        store.StorageKey.Should().NotBe(analytics.StorageKey);
        (store with { Flow = "delegated" }).StorageKey.Should().NotBe(store.StorageKey);
        (store with { CredentialGeneration = "rotated" }).StorageKey.Should().NotBe(store.StorageKey);
        InventoryBinding firstBinding = InventoryBinding.Create(first, store.Program, [store]);
        InventoryBinding.Create(second, store.Program,
            [AuthorizationContext.Create(second, store.Program, store.ResourceKey)]).FileKey.Should().NotBe(firstBinding.FileKey);
        InventoryBinding.Create(first, store.Program, [store with { CredentialGeneration = "rotated" }])
            .AuthorizationKey.Should().NotBe(firstBinding.AuthorizationKey);
        Action crossProgram = () => InventoryBinding.Create(first, ApiResources.MarketplaceProgram, [store]);
        crossProgram.Should().Throw<Console.AnalyticsET.Cli.InputException>();
        Action wrongAccount = () => InventoryBinding.Create(first, store.Program, [store with { AuthorizedAccountId = "wrong" }]);
        wrongAccount.Should().Throw<Console.AnalyticsET.Cli.InputException>();
    }

    [TestMethod]
    public void NormalizationDeduplicatesConsistentProductsAndRejectsConflictsOrMissingParents()
    {
        PublisherProfile profile = Profile();
        InventoryBinding binding = Binding(profile);
        ProductRecord app = Product("app-one");
        InventoryValidation.Normalize(binding, [app, app]).Should().ContainSingle();
        Action conflict = () => InventoryValidation.Normalize(binding, [app, app with { DisplayName = "Conflicting" }]);
        conflict.Should().Throw<InventoryException>().Which.Category.Should().Be("conflicting-identity");
        ProductRecord child = app with { Identity = new("store-addon", "child-one", "app-one") };
        Action orphan = () => InventoryValidation.Normalize(binding, [child]);
        orphan.Should().Throw<InventoryException>().Which.Category.Should().Be("missing-parent");
        Action parents = () => InventoryValidation.Normalize(binding,
            [app, child, child with { Identity = new("store-addon", "child-one", "other-parent") }]);
        parents.Should().Throw<InventoryException>().Which.Category.Should().Be("conflicting-parent");
    }

    [TestMethod]
    public void PublicationAndScopeEvidenceAreRequiredForCompleteness()
    {
        DiscoveryCandidate complete = new(Binding(Profile()), DateTimeOffset.UtcNow, [Product("app-one")],
            [new("store-app", "store-submission", null, CoverageState.Complete, "all-pages")], true, true);
        InventoryValidation.Complete(complete).Should().BeTrue();
        InventoryValidation.Complete(complete with { AccountBindingVerified = false }).Should().BeFalse();
        InventoryValidation.Complete(complete with { AccountScopeVerified = false }).Should().BeFalse();
        InventoryValidation.Complete(complete with { Products = [Product("app-one") with { Publication = PublicationState.Unknown }] })
            .Should().BeFalse();
        Action noProof = () => InventoryValidation.Normalize(complete.Binding,
            [Product("app-one") with { PublicationEvidence = null }]);
        noProof.Should().Throw<InventoryException>();
    }

    internal static PublisherProfile Profile(string id = "publisher-one", string account = "account-one")
    {
        PublisherProfile profile = DiscoveryConfigurationTests.Create(id, account);
        Dictionary<string, AuthorizationSettings> authorization = new(profile.Authorization)
        {
            [ApiResources.StoreInventory] = profile.Authorization[ApiResources.Store]
        };
        return profile with { Authorization = authorization };
    }

    internal static InventoryBinding Binding(PublisherProfile profile) => InventoryBinding.Create(profile, ApiResources.StoreProgram,
        [AuthorizationContext.Create(profile, ApiResources.StoreProgram, ApiResources.StoreInventory) with { CredentialGeneration = "fixture-v1" }]);

    internal static ProductRecord Product(string id) => new(new("store-app", id), "Fictional app", null, null,
        PublicationState.Published, AvailabilityState.Available, "store-submission", "v1.0", "last-published-submission",
        DateTimeOffset.Parse("2026-10-05T00:00:00Z"), DateTimeOffset.Parse("2026-10-05T00:00:00Z"));
}
