using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Publishers;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class InventoryCoordinatorTests
{
    [TestMethod]
    public async Task LaterProductsRenamesRetirementAndNotReturnedHistoryUseStableIdentity()
    {
        using TemporaryDirectory directory = new();
        InventoryStore store = InventoryStoreTests.Store(directory.Path);
        FakeProvider provider = new();
        InventoryCoordinator coordinator = new([provider], new FakeCredentials(), store, TimeProvider.System);
        PublisherProfile profile = InventoryModelTests.Profile();
        provider.Products = [InventoryModelTests.Product("app-one"), InventoryModelTests.Product("missing-later")];
        (await coordinator.RefreshAsync([profile], ApiResources.StoreProgram, default))[0].Outcome.Should().Be(DiscoveryOutcome.Complete);
        provider.Products = [InventoryModelTests.Product("app-one") with { DisplayName = "Renamed" }, InventoryModelTests.Product("new-app"),
            InventoryModelTests.Product("retired-app") with { Availability = AvailabilityState.Retired }];
        InventoryResult result = (await coordinator.RefreshAsync([profile], ApiResources.StoreProgram, default))[0];
        result.Snapshot!.Products.Should().HaveCount(4);
        result.Snapshot.Products.Single(item => item.Identity.SourceId == "app-one").DisplayName.Should().Be("Renamed");
        result.Snapshot.Products.Single(item => item.Identity.SourceId == "retired-app").Availability.Should().Be(AvailabilityState.Retired);
        ProductRecord absent = result.Snapshot.Products.Single(item => item.Identity.SourceId == "missing-later");
        absent.Returned.Should().BeFalse();
        absent.Availability.Should().Be(AvailabilityState.NotReturned);
        absent.Publication.Should().Be(PublicationState.Published);
    }

    [TestMethod]
    public async Task PageChildAndCoverageFailuresNeverReplaceSuccessAndPublishersHaveIndependentOutcomes()
    {
        using TemporaryDirectory directory = new();
        FakeProvider provider = new();
        InventoryStore store = InventoryStoreTests.Store(directory.Path);
        InventoryCoordinator coordinator = new([provider], new FakeCredentials(), store, TimeProvider.System);
        PublisherProfile first = InventoryModelTests.Profile();
        PublisherProfile second = InventoryModelTests.Profile("publisher-two", "account-two");
        (await coordinator.RefreshAsync([first], ApiResources.StoreProgram, default))[0].Outcome.Should().Be(DiscoveryOutcome.Complete);
        string success = Directory.GetFiles(Path.Combine(directory.Path, "private"), "*.snapshot.json").Single();
        byte[] before = await File.ReadAllBytesAsync(success);
        provider.FailedPublisher = first.Id;
        var results = await coordinator.RefreshAsync([first, second], ApiResources.StoreProgram, default);
        results.Select(result => result.Outcome).Should().Equal(DiscoveryOutcome.Failed, DiscoveryOutcome.Complete);
        (await File.ReadAllBytesAsync(success)).Should().Equal(before);
        provider.FailedPublisher = null;
        provider.Products = [InventoryModelTests.Product("orphan") with { Identity = new("store-addon", "orphan", "missing") }];
        (await coordinator.RefreshAsync([first], ApiResources.StoreProgram, default))[0].Category.Should().Be("missing-parent");
        (await File.ReadAllBytesAsync(success)).Should().Equal(before);
        provider.Products = [InventoryModelTests.Product("changed")];
        provider.Partial = true;
        (await coordinator.RefreshAsync([first], ApiResources.StoreProgram, default))[0].Outcome.Should().Be(DiscoveryOutcome.Partial);
        (await File.ReadAllBytesAsync(success)).Should().Equal(before);
    }

    [TestMethod]
    public async Task CancellationPreservesSuccessAndRecordsAttemptWithoutContinuingBatch()
    {
        using TemporaryDirectory directory = new();
        FakeProvider provider = new();
        InventoryStore store = InventoryStoreTests.Store(directory.Path);
        InventoryCoordinator coordinator = new([provider], new FakeCredentials(), store, TimeProvider.System);
        PublisherProfile profile = InventoryModelTests.Profile();
        await coordinator.RefreshAsync([profile], ApiResources.StoreProgram, default);
        string success = Directory.GetFiles(Path.Combine(directory.Path, "private"), "*.snapshot.json").Single();
        byte[] before = await File.ReadAllBytesAsync(success);
        using CancellationTokenSource cancellation = new();
        provider.Cancel = cancellation;
        Func<Task> refresh = async () => await coordinator.RefreshAsync([profile, InventoryModelTests.Profile("later", "later-account")], ApiResources.StoreProgram, cancellation.Token);
        await refresh.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllBytesAsync(success)).Should().Equal(before);
        var cached = await store.LoadOfflineAsync(InventoryModelTests.Binding(profile), default);
        cached.Attempt!.Outcome.Should().Be(DiscoveryOutcome.Cancelled);
        provider.Calls.Should().Be(2);
    }

    internal sealed class FakeCredentials : IApplicationCredentialResolver
    {
        internal int Calls { get; private set; }
        public Task<ApplicationCredential> ResolveAsync(AuthorizationContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ApplicationCredential("FICTIONAL-SECRET", "fixture-v1"));
        }
    }

    internal sealed class FakeProvider : IProductInventoryProvider
    {
        public string Program => ApiResources.StoreProgram;
        internal IReadOnlyList<ProductRecord> Products { get; set; } = [InventoryModelTests.Product("app-one")];
        internal string? FailedPublisher { get; set; }
        internal bool Partial { get; set; }
        internal CancellationTokenSource? Cancel { get; set; }
        internal int Calls { get; private set; }
        public Task<DiscoveryCandidate> DiscoverAsync(DiscoveryRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Cancel?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Profile.Id == FailedPublisher) { throw new InventoryException("later-page-failed"); }
            return Task.FromResult(new DiscoveryCandidate(request.Binding, DateTimeOffset.UtcNow, Products,
                [new("store-app", "store-submission", null, Partial ? CoverageState.Failed : CoverageState.Complete, "fixture")], true, true));
        }
    }
}
