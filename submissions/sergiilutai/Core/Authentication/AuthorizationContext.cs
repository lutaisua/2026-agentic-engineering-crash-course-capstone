using System.Security.Cryptography;
using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Publishers;

namespace Core.AnalyticsET.Authentication;

internal sealed record AuthorizationContext(string PublisherId, string TenantId, string ClientId,
    string AuthorizedAccountId, string PublisherAccountId, string Program, string Resource, string CredentialReference,
    string ResourceKey = "", string Flow = "client-credentials", string CredentialGeneration = "unresolved")
{
    internal string ConfigurationKey => Hash([PublisherId, TenantId, ClientId, AuthorizedAccountId,
        PublisherAccountId, Program, Resource, ResourceKey, Flow, CredentialReference]);
    internal string StorageKey => Hash([ConfigurationKey, CredentialGeneration]);
    internal static string Hash(string[] parts) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(parts)));

    internal static AuthorizationContext Create(PublisherProfile profile, string program, string resource)
    {
        ConfigurationValidator.Validate(profile, ConfigurationValidator.ProfileSchema(profile));
        if (!profile.Programs.Contains(program, StringComparer.Ordinal) ||
            !ApiResources.Audiences.TryGetValue(resource, out string? audience) ||
            !ApiResources.MatchesProgram(resource, program) ||
            !profile.Authorization.TryGetValue(resource, out AuthorizationSettings? settings))
        {
            throw new InputException("Selected program/resource is not configured for this publisher.");
        }

        return new(profile.Id, Guid.Parse(profile.TenantId).ToString("D"), Guid.Parse(settings.ClientId).ToString("D"),
            settings.AuthorizedAccountId, profile.Accounts[program].PublisherAccountId, program, audience, settings.CredentialReference,
            resource, resource == ApiResources.Financial ? "delegated" : "client-credentials");
    }

    internal static IReadOnlyList<AuthorizationContext> ForProfile(PublisherProfile profile)
    {
        List<AuthorizationContext> contexts = [];
        foreach (string program in profile.Programs)
        {
            contexts.Add(Create(profile, program, program == ApiResources.StoreProgram ? ApiResources.Store : ApiResources.Marketplace));
            if (profile.Authorization.ContainsKey(ApiResources.Financial))
            {
                contexts.Add(Create(profile, program, ApiResources.Financial));
            }

            foreach (string resource in profile.Authorization.Keys.Where(resource =>
                ApiResources.IsInventory(resource) && ApiResources.MatchesProgram(resource, program)))
            {
                contexts.Add(Create(profile, program, resource));
            }
        }

        return contexts;
    }
}
