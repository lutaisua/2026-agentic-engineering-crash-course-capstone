using System.Net;
using System.Text.Json;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Publishers;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class StoreInventoryProviderTests
{
    [TestMethod]
    public async Task PublishedParentDoesNotPublishPendingChildAndAbsentProofBlocksCompleteness()
    {
        using HttpClient client = new(new InventoryHttpTests.Handler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            string json = path.EndsWith("applications", StringComparison.Ordinal) ? StoreInventoryReaderTests.Page([StoreInventoryReaderTests.App("app-one")], 1) :
                path.Contains("listinappproducts", StringComparison.Ordinal) ? StoreInventoryReaderTests.Page([new { inAppProductId = "child-one" }], 1) :
                StoreInventoryReaderTests.Child("child-one", "app-one").Replace("lastPublishedInAppProductSubmission", "pendingInAppProductSubmission", StringComparison.Ordinal);
            return Task.FromResult(InventoryHttpTests.Json(json));
        }));
        StoreInventoryProvider provider = new(StoreInventoryReaderTests.Reader(client), new FakeProof(true), TimeProvider.System);
        DiscoveryCandidate result = await provider.DiscoverAsync(Request(), default);
        result.Products.Single(product => product.Identity.Kind == "store-app").Publication.Should().Be(PublicationState.Published);
        result.Products.Single(product => product.Identity.Kind == "store-addon").Publication.Should().Be(PublicationState.Unknown);
        InventoryValidation.Complete(result).Should().BeFalse();
        result.Coverage.Single(coverage => coverage.Kind == "store-addon").State.Should().Be(CoverageState.Unverified);
        DiscoveryCandidate unverified = await new StoreInventoryProvider(StoreInventoryReaderTests.Reader(client), new FakeProof(false), TimeProvider.System).DiscoverAsync(Request(), default);
        unverified.AccountBindingVerified.Should().BeFalse();
    }

    [TestMethod]
    public async Task EmptyCollectionsCompleteOnlyWithAccountProofAnd409IsUnsupportedCoverage()
    {
        InventoryHttpTests.Handler handler = new(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using HttpClient client = new(handler);
        StoreInventoryProvider provider = new(StoreInventoryReaderTests.Reader(client), new FakeProof(true), TimeProvider.System);
        DiscoveryCandidate empty = await provider.DiscoverAsync(Request(), default);
        empty.Products.Should().BeEmpty();
        InventoryValidation.Complete(empty).Should().BeTrue();
        handler.Respond = request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
        DiscoveryCandidate unsupported = await provider.DiscoverAsync(Request(), default);
        unsupported.Coverage.Should().Contain(coverage => coverage.Kind == "store-app" && coverage.State == CoverageState.Unsupported);
        InventoryValidation.Complete(unsupported).Should().BeFalse();
    }

    [TestMethod]
    public async Task LaterPageAndChildFailureKeepLastSuccessfulSnapshotBytes()
    {
        using TemporaryDirectory directory = new();
        InventoryHttpTests.Handler handler = new(request => Task.FromResult(InventoryHttpTests.Json(
            request.RequestUri!.AbsolutePath.EndsWith("applications", StringComparison.Ordinal) ?
                StoreInventoryReaderTests.Page([StoreInventoryReaderTests.App("app-one")], 1) : StoreInventoryReaderTests.Page([], 0))));
        using HttpClient client = new(handler);
        InventoryStore store = InventoryStoreTests.Store(directory.Path);
        StoreInventoryProvider provider = new(StoreInventoryReaderTests.Reader(client), new FakeProof(true), TimeProvider.System);
        InventoryCoordinator coordinator = new([provider], new InventoryCoordinatorTests.FakeCredentials(), store, TimeProvider.System);
        PublisherProfile profile = InventoryModelTests.Profile();
        (await coordinator.RefreshAsync([profile], ApiResources.StoreProgram, default))[0].Outcome.Should().Be(DiscoveryOutcome.Complete);
        string path = Directory.GetFiles(Path.Combine(directory.Path, "private"), "*.snapshot.json").Single();
        byte[] before = await File.ReadAllBytesAsync(path);
        handler.Respond = request => Task.FromResult(InventoryHttpTests.Json(request.RequestUri!.AbsolutePath.EndsWith("applications", StringComparison.Ordinal) ?
            StoreInventoryReaderTests.Page([StoreInventoryReaderTests.App("new-app")], 2, "applications?skip=1&top=1") : StoreInventoryReaderTests.Page([], 0)));
        (await coordinator.RefreshAsync([profile], ApiResources.StoreProgram, default))[0].Outcome.Should().Be(DiscoveryOutcome.Partial);
        (await File.ReadAllBytesAsync(path)).Should().Equal(before);
        handler.Respond = request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("applications", StringComparison.Ordinal) ?
            InventoryHttpTests.Json(StoreInventoryReaderTests.Page([StoreInventoryReaderTests.App("new-app")], 1)) : new HttpResponseMessage(HttpStatusCode.Conflict));
        InventoryResult result = (await coordinator.RefreshAsync([profile], ApiResources.StoreProgram, default))[0];
        result.Outcome.Should().Be(DiscoveryOutcome.Partial);
        result.PartialProducts.Should().ContainSingle();
        result.Coverage.Should().Contain(coverage => coverage.Kind == "store-addon" && coverage.State == CoverageState.Unsupported);
        (await File.ReadAllBytesAsync(path)).Should().Equal(before);
    }

    [TestMethod]
    public async Task FailedFirstParentDoesNotHideLaterAddonsOrTurnFailureIntoEmptySuccess()
    {
        List<string> childCollections = [];
        using HttpClient client = new(new InventoryHttpTests.Handler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("applications", StringComparison.Ordinal))
            {
                return Task.FromResult(InventoryHttpTests.Json(StoreInventoryReaderTests.Page(
                    [StoreInventoryReaderTests.App("app-one"), StoreInventoryReaderTests.App("app-two")], 2)));
            }
            if (path.EndsWith("listinappproducts", StringComparison.Ordinal))
            {
                childCollections.Add(path);
                return Task.FromResult(path.Contains("app-one", StringComparison.Ordinal) ?
                    new HttpResponseMessage(HttpStatusCode.BadRequest) :
                    InventoryHttpTests.Json(StoreInventoryReaderTests.Page([new { inAppProductId = "child-two" }], 1)));
            }
            return Task.FromResult(InventoryHttpTests.Json(StoreInventoryReaderTests.Child("child-two", "app-two")));
        }));
        DiscoveryCandidate result = await new StoreInventoryProvider(StoreInventoryReaderTests.Reader(client),
            new FakeProof(true), TimeProvider.System).DiscoverAsync(Request(), default);

        childCollections.Should().HaveCount(2);
        result.Products.Should().Contain(product => product.Identity == new ProductIdentity("store-addon", "child-two", "app-two"));
        result.Coverage.Should().Contain(coverage => coverage.Kind == "store-addon" &&
            coverage.State == CoverageState.Failed && coverage.Reason == "http-request-failed");
        result.Coverage.Should().NotContain(coverage => coverage.Kind == "store-addon" && coverage.State == CoverageState.Complete);
        InventoryValidation.Complete(result).Should().BeFalse();
    }

    [TestMethod]
    public async Task ParentFailuresAreDeduplicatedAndUnknownSuccessfulChildrenRemainExplicit()
    {
        using HttpClient client = new(new InventoryHttpTests.Handler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("applications", StringComparison.Ordinal))
            {
                return Task.FromResult(InventoryHttpTests.Json(StoreInventoryReaderTests.Page(
                    [StoreInventoryReaderTests.App("app-one"), StoreInventoryReaderTests.App("app-two"),
                     StoreInventoryReaderTests.App("app-three")], 3)));
            }
            if (path.EndsWith("listinappproducts", StringComparison.Ordinal))
            {
                return Task.FromResult(path.Contains("app-three", StringComparison.Ordinal) ?
                    InventoryHttpTests.Json(StoreInventoryReaderTests.Page([new { inAppProductId = "child-three" }], 1)) :
                    new HttpResponseMessage(HttpStatusCode.BadRequest));
            }
            return Task.FromResult(InventoryHttpTests.Json(StoreInventoryReaderTests.Child("child-three", "app-three")
                .Replace("lastPublishedInAppProductSubmission", "pendingInAppProductSubmission", StringComparison.Ordinal)));
        }));
        DiscoveryCandidate result = await new StoreInventoryProvider(StoreInventoryReaderTests.Reader(client),
            new FakeProof(true), TimeProvider.System).DiscoverAsync(Request(), default);

        result.Coverage.Count(coverage => coverage.Kind == "store-addon" && coverage.State == CoverageState.Failed).Should().Be(1);
        result.Coverage.Should().Contain(coverage => coverage.Kind == "store-addon" && coverage.State == CoverageState.Unverified);
        result.Products.Single(product => product.Identity.Kind == "store-addon").Publication.Should().Be(PublicationState.Unknown);
        InventoryValidation.Complete(result).Should().BeFalse();
    }

    internal static DiscoveryRequest Request() => new(InventoryModelTests.Profile(), ApiResources.StoreProgram,
        InventoryModelTests.Binding(InventoryModelTests.Profile()), [InventoryHttpTests.Context()]);
    private sealed class FakeProof(bool verified) : IInventoryAccountProof
    {
        public Task<bool> VerifyAsync(DiscoveryRequest request, CancellationToken cancellationToken) => Task.FromResult(verified);
    }
}
