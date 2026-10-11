using System.Net;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;
using Core.AnalyticsET.Publishers;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class MarketplaceAuthenticationTests
{
    [TestMethod]
    [DataRow(ApiResources.MarketplaceProductIngestion, "/oauth2/v2.0/token", "scope=https%3A%2F%2Fgraph.microsoft.com%2F.default")]
    [DataRow(ApiResources.MarketplaceSubmission, "/oauth2/token", "resource=https%3A%2F%2Fapi.partner.microsoft.com")]
    public async Task InventoryTokenRequestUsesTheDocumentedFlowAndSecretValue(string resource, string endpoint, string field)
    {
        InventoryHttpTests.Handler handler = new(async request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().Be("/11111111-1111-1111-1111-111111111111" + endpoint);
            string body = await request.Content!.ReadAsStringAsync();
            body.Should().Contain(field).And.Contain("client_secret=FICTIONAL-SECRET").And.Contain("grant_type=client_credentials");
            body.Should().NotContain("secret_id").And.NotContain("redirect_uri");
            body.Contains("scope=", StringComparison.Ordinal).Should().Be(resource == ApiResources.MarketplaceProductIngestion);
            body.Contains("resource=", StringComparison.Ordinal).Should().Be(resource == ApiResources.MarketplaceSubmission);
            return Token();
        });
        using HttpClient client = new(handler);
        ClientCredentialTokens tokens = new(new(client, TimeProvider.System), new InventoryCoordinatorTests.FakeCredentials(), TimeProvider.System);
        AccessToken token = await tokens.GetAsync(Context(resource), false, default);
        token.ToString().Should().NotContain("FICTIONAL-TOKEN");
        handler.Calls.Should().Be(1);
    }

    [TestMethod]
    public async Task SameClientUsesSeparateCachesForResourcesPublishersAndAccounts()
    {
        InventoryHttpTests.Handler handler = new(_ => Task.FromResult(Token()));
        using HttpClient client = new(handler);
        ClientCredentialTokens tokens = new(new(client, TimeProvider.System), new InventoryCoordinatorTests.FakeCredentials(), TimeProvider.System);
        AuthorizationContext graph = Context(ApiResources.MarketplaceProductIngestion);
        AuthorizationContext submission = Context(ApiResources.MarketplaceSubmission);
        AccessToken first = await tokens.GetAsync(graph, false, default);
        (await tokens.GetAsync(graph, false, default)).Should().BeSameAs(first);
        (await tokens.GetAsync(submission, false, default)).Should().NotBeSameAs(first);
        (await tokens.GetAsync(graph with { PublisherId = "publisher-two", PublisherAccountId = "account-two", AuthorizedAccountId = "account-two" }, false, default))
            .Should().NotBeSameAs(first);
        (await tokens.GetAsync(graph with { AuthorizedAccountId = "another-association" }, false, default)).Should().NotBeSameAs(first);
        handler.Calls.Should().Be(4);
    }

    [TestMethod]
    public async Task AnalyticsFinancialWrongAudienceAndCrossProgramBindingsAreRejectedBeforeCredentialsOrHttp()
    {
        InventoryHttpTests.Handler handler = new(_ => Task.FromResult(Token()));
        using HttpClient client = new(handler);
        InventoryCoordinatorTests.FakeCredentials credentials = new();
        ClientCredentialTokens tokens = new(new(client, TimeProvider.System), credentials, TimeProvider.System);
        PublisherProfile profile = Profile();
        AuthorizationContext submission = Context(ApiResources.MarketplaceSubmission);
        AuthorizationContext[] rejected =
        [
            AuthorizationContext.Create(profile, ApiResources.MarketplaceProgram, ApiResources.Marketplace) with { CredentialGeneration = "fixture-v1" },
            AuthorizationContext.Create(profile, ApiResources.MarketplaceProgram, ApiResources.Financial) with { CredentialGeneration = "fixture-v1" },
            submission with { Flow = "delegated" },
            submission with { Resource = ApiResources.Audiences[ApiResources.Marketplace] },
            submission with { Program = ApiResources.StoreProgram },
            submission with { ResourceKey = ApiResources.Financial },
            submission with { TenantId = Guid.Empty.ToString() }
        ];
        foreach (AuthorizationContext context in rejected)
        {
            Func<Task> call = () => tokens.GetAsync(context, false, default);
            await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "invalid-token-binding");
        }
        credentials.Calls.Should().Be(0);
        handler.Calls.Should().Be(0);
    }

    [TestMethod]
    [DataRow(ApiResources.MarketplaceProductIngestion, "https://graph.microsoft.com/rp/product-ingestion/product")]
    [DataRow(ApiResources.MarketplaceSubmission, "https://api.partner.microsoft.com/v1.0/ingestion/products")]
    public async Task MissingAssociationIsAnIndependentFailureRatherThanEmptyInventory(string resource, string destination)
    {
        InventoryHttpTests.Handler handler = new(request => Task.FromResult(request.Method == HttpMethod.Post ? Token() :
            new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("FICTIONAL-PRIVATE-ERROR") }));
        using HttpClient client = new(handler);
        BoundedJsonHttp http = new(client, TimeProvider.System);
        ClientCredentialTokens tokens = new(http, new InventoryCoordinatorTests.FakeCredentials(), TimeProvider.System);
        AuthorizedInventoryHttp authorized = new(http, tokens);
        Uri uri = new(destination);
        Func<Task> call = async () => await authorized.GetAsync(Context(resource), uri, uri, true, default);
        await call.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "forbidden-or-association-missing" &&
            !error.Message.Contains("PRIVATE-ERROR"));
        handler.Calls.Should().Be(2);
    }

    [TestMethod]
    public async Task CredentialRotationAndExplicitRenewalCannotReuseOldMarketplaceTokens()
    {
        InventoryHttpTests.Handler handler = new(_ => Task.FromResult(Token()));
        using HttpClient client = new(handler);
        RotatingCredentials credentials = new();
        ClientCredentialTokens tokens = new(new(client, TimeProvider.System), credentials, TimeProvider.System);
        AuthorizationContext context = Context(ApiResources.MarketplaceProductIngestion);
        AccessToken old = await tokens.GetAsync(context, false, default);
        credentials.Generation = "fixture-v2";
        Func<Task> stale = () => tokens.GetAsync(context, false, default);
        await stale.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "credential-generation-changed");
        handler.Calls.Should().Be(1);
        context = context with { CredentialGeneration = credentials.Generation };
        AccessToken current = await tokens.GetAsync(context, false, default);
        current.Should().NotBeSameAs(old);
        (await tokens.GetAsync(context, true, default)).Should().NotBeSameAs(current);
        handler.Calls.Should().Be(3);
    }

    private static HttpResponseMessage Token() => InventoryHttpTests.Json("""
        {"access_token":"FICTIONAL-TOKEN","token_type":"Bearer","expires_in":3600}
        """);

    private static AuthorizationContext Context(string resource) => AuthorizationContext.Create(Profile(), ApiResources.MarketplaceProgram, resource)
        with
    { CredentialGeneration = "fixture-v1" };

    private static PublisherProfile Profile()
    {
        PublisherProfile profile = ProfileFixtures.Create();
        foreach (ProgramScope scope in profile.Accounts.Values) { scope.ProductIds.Clear(); }
        profile.Authorization[ApiResources.MarketplaceProductIngestion] = profile.Authorization[ApiResources.Marketplace];
        profile.Authorization[ApiResources.MarketplaceSubmission] = profile.Authorization[ApiResources.Marketplace];
        return profile;
    }

    private sealed class RotatingCredentials : IApplicationCredentialResolver
    {
        internal string Generation { get; set; } = "fixture-v1";
        public Task<ApplicationCredential> ResolveAsync(AuthorizationContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ApplicationCredential("FICTIONAL-SECRET", Generation));
    }
}
