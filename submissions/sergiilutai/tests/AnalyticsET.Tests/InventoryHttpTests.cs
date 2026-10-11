using System.Net;
using System.Text;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;
using Core.AnalyticsET.Publishers;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class InventoryHttpTests
{
    [TestMethod]
    public async Task NetworkTimeoutIsFailureRatherThanOperatorCancellation()
    {
        using HttpClient client = new(new Handler(request => throw new TaskCanceledException("FICTIONAL-PRIVATE-ERROR")));
        BoundedJsonHttp http = new(client, TimeProvider.System);
        Uri uri = new("https://manage.devcenter.microsoft.com/v1.0/my/applications");
        Func<Task> call = async () => await http.SendAsync(() => new(HttpMethod.Get, uri), uri, false, default);
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "http-timeout" && !error.Message.Contains("PRIVATE-ERROR"));
    }

    [TestMethod]
    public async Task StoreTokensUseValueResourceFlowAndSeparateCacheBindingsWithExpiryRenewal()
    {
        Handler handler = new(async request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().EndWith("/oauth2/token");
            string body = await request.Content!.ReadAsStringAsync();
            body.Should().Contain("client_secret=FICTIONAL-SECRET").And.Contain("resource=https%3A%2F%2Fmanage.devcenter.microsoft.com");
            body.Should().NotContain("secret_id").And.NotContain("redirect_uri");
            return Json("{\"access_token\":\"FICTIONAL-TOKEN\",\"token_type\":\"Bearer\",\"expires_in\":\"3600\"}");
        });
        using HttpClient client = new(handler);
        MutableClock clock = new();
        ClientCredentialTokens tokens = new(new(client, clock), new InventoryCoordinatorTests.FakeCredentials(), clock);
        AuthorizationContext context = AuthorizationContext.Create(InventoryModelTests.Profile(), ApiResources.StoreProgram, ApiResources.StoreInventory)
            with
        { CredentialGeneration = "fixture-v1" };
        AccessToken token = await tokens.GetAsync(context, false, default);
        token.ToString().Should().NotContain("FICTIONAL-TOKEN");
        await tokens.GetAsync(context, false, default);
        handler.Calls.Should().Be(1);
        await tokens.GetAsync(context with { ResourceKey = ApiResources.Store }, false, default);
        handler.Calls.Should().Be(2);
        clock.Now = clock.Now.AddHours(1);
        await tokens.GetAsync(context, false, default);
        handler.Calls.Should().Be(3);
        Func<Task> flow = () => tokens.GetAsync(context with { Flow = "delegated" }, false, default);
        await flow.Should().ThrowAsync<InventoryException>();
        Func<Task> rotated = () => tokens.GetAsync(context with { CredentialGeneration = "rotated" }, false, default);
        await rotated.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "credential-generation-changed");
    }

    [TestMethod]
    public async Task UnauthorizedRenewsOnceAndForbiddenDoesNotPretendAssociationOrEmptyInventory()
    {
        Handler handler = new(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        using HttpClient client = new(handler);
        FakeTokens tokens = new();
        AuthorizedInventoryHttp http = new(new(client, TimeProvider.System), tokens);
        Uri uri = new("https://manage.devcenter.microsoft.com/v1.0/my/applications");
        Func<Task> call = async () => await http.GetAsync(Context(), uri, uri, false, default);
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "unauthorized");
        tokens.Forced.Should().Equal(false, true);
        handler.Respond = request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        { Content = new StringContent("SECRET-ERROR-DETAIL") });
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "forbidden-or-association-missing" && !error.Message.Contains("SECRET-ERROR-DETAIL"));
        handler.Calls.Should().Be(3);
    }

    [TestMethod]
    public async Task RetryAfterBudgetsAndCancellationAreBounded()
    {
        Handler handler = new(request =>
        {
            HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(7));
            return Task.FromResult(response);
        });
        using HttpClient client = new(handler);
        List<TimeSpan> waits = [];
        BoundedJsonHttp http = new(client, TimeProvider.System, (duration, cancellation) => { waits.Add(duration); return Task.CompletedTask; });
        Uri uri = new("https://manage.devcenter.microsoft.com/v1.0/my/applications");
        Func<Task> call = async () => await http.SendAsync(() => new(HttpMethod.Get, uri), uri, false, default);
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "retry-budget-exhausted");
        handler.Calls.Should().Be(3);
        waits.Should().Equal(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7));
        using CancellationTokenSource cancellation = new();
        BoundedJsonHttp cancelled = new(client, TimeProvider.System, (duration, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        Func<Task> cancel = async () => await cancelled.SendAsync(() => new(HttpMethod.Get, uri), uri, false, cancellation.Token);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        handler.Respond = request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "retry-budget-exhausted");
    }

    [TestMethod]
    public async Task OffOriginLinksAndRedirectsCannotReceiveBearerHeaders()
    {
        Handler handler = new(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)
        { Headers = { Location = new("https://fictional-other-origin.example/") } }));
        using HttpClient client = new(handler);
        FakeTokens tokens = new();
        AuthorizedInventoryHttp http = new(new(client, TimeProvider.System), tokens);
        Uri approved = new("https://manage.devcenter.microsoft.com/v1.0/my/");
        Func<Task> rejected = async () => await http.GetAsync(Context(), new("https://fictional-other-origin.example/"), approved, false, default);
        await rejected.Should().ThrowAsync<InventoryException>();
        handler.Calls.Should().Be(0);
        tokens.Forced.Should().BeEmpty();
        Func<Task> redirect = async () => await http.GetAsync(Context(), new(approved, "applications"), approved, false, default);
        await redirect.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "redirect-rejected");
        handler.Calls.Should().Be(1);
    }

    internal static AuthorizationContext Context() => AuthorizationContext.Create(InventoryModelTests.Profile(), ApiResources.StoreProgram, ApiResources.StoreInventory)
        with
    { CredentialGeneration = "fixture-v1" };
    internal static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    internal sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Respond(request); }
    }
    internal sealed class FakeTokens : IInventoryTokens
    {
        internal List<bool> Forced { get; } = [];
        public Task<AccessToken> GetAsync(AuthorizationContext context, bool forceRefresh, CancellationToken cancellationToken)
        { Forced.Add(forceRefresh); return Task.FromResult(new AccessToken("FICTIONAL-TOKEN", DateTimeOffset.UtcNow.AddHours(1))); }
    }
    private sealed class MutableClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
