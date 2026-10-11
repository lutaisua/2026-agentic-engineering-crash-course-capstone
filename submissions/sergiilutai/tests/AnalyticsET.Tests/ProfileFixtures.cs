using Core.AnalyticsET.Publishers;

namespace AnalyticsET.Tests;

internal static class ProfileFixtures
{
    internal static PublisherProfile Create(string id = "publisher-one", string account = "account-one") => new()
    {
        Id = id,
        DisplayName = "Example publisher",
        TenantId = "11111111-1111-1111-1111-111111111111",
        Programs = [ApiResources.StoreProgram, ApiResources.MarketplaceProgram],
        Accounts = new()
        {
            [ApiResources.StoreProgram] = new() { PublisherAccountId = account, ProductIds = ["9EXAMPLE00001"] },
            [ApiResources.MarketplaceProgram] = new() { PublisherAccountId = account, ProductIds = ["example-offer"] }
        },
        Authorization = new()
        {
            [ApiResources.Store] = Settings("store-reference", account),
            [ApiResources.Marketplace] = Settings("market-reference", account),
            [ApiResources.Financial] = Settings("financial-reference", account)
        }
    };

    private static AuthorizationSettings Settings(string reference, string account) => new()
    {
        ClientId = "22222222-2222-2222-2222-222222222222",
        AuthorizedAccountId = account,
        CredentialReference = reference
    };
}
