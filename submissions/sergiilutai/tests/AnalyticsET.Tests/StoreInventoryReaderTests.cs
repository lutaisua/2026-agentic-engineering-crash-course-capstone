using System.Net;
using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class StoreInventoryReaderTests
{
    [TestMethod]
    public async Task AppsFollowEveryRelativePageBeyondTenAndNormalizeConsistentDuplicates()
    {
        int calls = 0;
        using HttpClient client = new(new InventoryHttpTests.Handler(request =>
        {
            calls++;
            bool later = request.RequestUri!.Query.Contains("skip=10", StringComparison.Ordinal);
            return Task.FromResult(InventoryHttpTests.Json(Page(later ? [App("app10"), App("app00")] :
                Enumerable.Range(0, 10).Select(index => App("app" + index.ToString("D2"))).ToArray(), 11,
                later ? null : "applications?skip=10&top=10")));
        }));
        var result = await Reader(client).AppsAsync(InventoryHttpTests.Context(), DateTimeOffset.UtcNow, default);
        result.Should().HaveCount(11);
        calls.Should().Be(2);
    }

    [TestMethod]
    public async Task ConflictsCyclesMalformedPagesEmptyAndLater404AreDistinct()
    {
        InventoryHttpTests.Handler handler = new(request => Task.FromResult(InventoryHttpTests.Json(Page([App("one"), App("one", "Conflict")], 1))));
        using HttpClient client = new(handler);
        Func<Task> call = async () => await Reader(client).AppsAsync(InventoryHttpTests.Context(), DateTimeOffset.UtcNow, default);
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "conflicting-identity");
        handler.Respond = request => Task.FromResult(InventoryHttpTests.Json(Page([App("one")], 2, "applications?skip=0&top=10")));
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "continuation-cycle-or-scope-change");
        handler.Respond = request => Task.FromResult(InventoryHttpTests.Json("{\"value\":{},\"totalCount\":0}"));
        await call.Should().ThrowAsync<InventoryException>();
        handler.Respond = request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        (await Reader(client).AppsAsync(InventoryHttpTests.Context(), DateTimeOffset.UtcNow, default)).Should().BeEmpty();
        handler.Respond = request => Task.FromResult(InventoryHttpTests.Json(Page([], 0)));
        (await Reader(client).AppsAsync(InventoryHttpTests.Context(), DateTimeOffset.UtcNow, default)).Should().BeEmpty();
        handler.Respond = request => Task.FromResult(request.RequestUri!.Query.Contains("skip=10", StringComparison.Ordinal) ?
            new HttpResponseMessage(HttpStatusCode.NotFound) : InventoryHttpTests.Json(Page([App("one")], 2, "applications?skip=10&top=10")));
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "resource-not-found");
    }

    [TestMethod]
    public async Task ChildrenPageAndResolveStoreDeveloperAndParentIdsSeparately()
    {
        using HttpClient client = new(new InventoryHttpTests.Handler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("listinappproducts", StringComparison.Ordinal) || path.EndsWith("listinappproducts/", StringComparison.Ordinal))
            {
                bool later = request.RequestUri.Query.Contains("skip=1", StringComparison.Ordinal);
                return Task.FromResult(InventoryHttpTests.Json(Page([new { inAppProductId = later ? "child-two" : "child-one" }], 2,
                    later ? null : "applications/app-one/listinappproducts/?skip=1&top=1")));
            }
            return Task.FromResult(InventoryHttpTests.Json(Child(path.Split('/')[^1], "app-one")));
        }));
        ProductRecord parent = InventoryModelTests.Product("app-one");
        var children = await Reader(client).ChildrenAsync(InventoryHttpTests.Context(), parent, DateTimeOffset.UtcNow, default);
        children.Should().HaveCount(2);
        children.Select(child => child.Identity.ParentId).Should().OnlyContain(id => id == "app-one");
        children[0].Identity.SourceId.Should().Be("child-one");
        children[0].ExternalId.Should().Be("developer-child-one");
        children[0].Publication.Should().Be(PublicationState.Published);
    }

    [TestMethod]
    public async Task EmptyChildrenWrongParentAndMissingDetailDoNotBecomePublishedChildren()
    {
        InventoryHttpTests.Handler handler = new(request => Task.FromResult(InventoryHttpTests.Json(Page([], 0))));
        using HttpClient client = new(handler);
        ProductRecord parent = InventoryModelTests.Product("app-one");
        (await Reader(client).ChildrenAsync(InventoryHttpTests.Context(), parent, DateTimeOffset.UtcNow, default)).Should().BeEmpty();
        handler.Respond = request => Task.FromResult(InventoryHttpTests.Json(request.RequestUri!.AbsolutePath.Contains("listinappproducts", StringComparison.Ordinal)
            ? Page([new { inAppProductId = "child-one" }], 1) : Child("child-one", "wrong-parent")));
        Func<Task> call = async () => await Reader(client).ChildrenAsync(InventoryHttpTests.Context(), parent, DateTimeOffset.UtcNow, default);
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "conflicting-parent");
        handler.Respond = request => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("listinappproducts", StringComparison.Ordinal)
            ? InventoryHttpTests.Json(Page([new { inAppProductId = "child-one" }], 1)) : new HttpResponseMessage(HttpStatusCode.NotFound));
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "resource-not-found");
    }

    [TestMethod]
    public async Task OffOriginOrCrossCollectionContinuationIsRejectedBeforeAnotherBearerRequest()
    {
        InventoryHttpTests.Handler handler = new(request => Task.FromResult(InventoryHttpTests.Json(Page([App("one")], 2,
            "https://fictional-other-origin.example/applications?skip=1"))));
        using HttpClient client = new(handler);
        Func<Task> call = async () => await Reader(client).AppsAsync(InventoryHttpTests.Context(), DateTimeOffset.UtcNow, default);
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "rejected-request-destination");
        handler.Calls.Should().Be(1);
        handler.Respond = request => Task.FromResult(InventoryHttpTests.Json(Page([App("one")], 2, "inappproducts?skip=1")));
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "continuation-cycle-or-scope-change");
        handler.Calls.Should().Be(2);
    }

    [TestMethod]
    public async Task ExhaustedScopeRequestBudgetStopsBeforeAnotherRequest()
    {
        InventoryHttpTests.Handler handler = new(request => Task.FromResult(InventoryHttpTests.Json(Page([App("one")], 2, "applications?skip=1&top=1"))));
        using HttpClient client = new(handler);
        Func<Task> call = async () => await Reader(client).AppsAsync(InventoryHttpTests.Context(), DateTimeOffset.UtcNow, default, new InventoryRequestBudget(1));
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "inventory-request-budget-exhausted");
        handler.Calls.Should().Be(1);
    }

    internal static StoreInventoryReader Reader(HttpClient client) => new(new AuthorizedInventoryHttp(new BoundedJsonHttp(client, TimeProvider.System), new InventoryHttpTests.FakeTokens()));
    internal static object App(string id, string name = "Fictional app") => new
    {
        id,
        primaryName = name,
        lastPublishedApplicationSubmission = new { id = "123", resourceLocation = "applications/" + id + "/submissions/123" }
    };
    internal static string Page(IEnumerable<object> values, int count, string? next = null) => JsonSerializer.Serialize(
        new Dictionary<string, object?> { ["value"] = values, ["totalCount"] = count, ["@nextLink"] = next });
    internal static string Child(string id, string parent) => JsonSerializer.Serialize(new
    {
        id,
        productId = "developer-" + id,
        productType = "Durable",
        applications = new { value = new[] { new { id = parent, resourceLocation = "applications/" + parent } }, totalCount = 1 },
        lastPublishedInAppProductSubmission = new { id = "456", resourceLocation = "inappproducts/" + id + "/submissions/456" }
    });
}
