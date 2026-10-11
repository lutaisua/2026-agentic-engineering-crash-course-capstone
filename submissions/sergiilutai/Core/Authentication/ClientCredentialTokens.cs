using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;
using Core.AnalyticsET.Publishers;

namespace Core.AnalyticsET.Authentication;

internal sealed class AccessToken(string value, DateTimeOffset expiresAt)
{
    [JsonIgnore]
    internal string Value { get; } = value;
    internal DateTimeOffset ExpiresAt { get; } = expiresAt;
    public override string ToString() => "[protected access token]";
}

internal interface IInventoryTokens
{
    Task<AccessToken> GetAsync(AuthorizationContext context, bool forceRefresh, CancellationToken cancellationToken);
}

internal sealed class ClientCredentialTokens(BoundedJsonHttp http, IApplicationCredentialResolver credentials, TimeProvider clock) : IInventoryTokens
{
    private readonly ConcurrentDictionary<string, AccessToken> cache = new(StringComparer.Ordinal);
    public async Task<AccessToken> GetAsync(AuthorizationContext context, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (context.Flow != "client-credentials" || context.ResourceKey is not (ApiResources.StoreInventory or ApiResources.Store or
            ApiResources.MarketplaceProductIngestion or ApiResources.MarketplaceSubmission) ||
            !ApiResources.MatchesProgram(context.ResourceKey, context.Program) ||
            context.Resource != ApiResources.Audiences[context.ResourceKey] || !Guid.TryParse(context.TenantId, out Guid tenant) ||
            tenant == Guid.Empty || !Guid.TryParse(context.ClientId, out Guid client) || client == Guid.Empty ||
            context.CredentialGeneration == "unresolved")
        { throw new InventoryException("invalid-token-binding"); }
        ApplicationCredential credential = await credentials.ResolveAsync(context, cancellationToken).ConfigureAwait(false);
        if (credential.Generation != context.CredentialGeneration) { throw new InventoryException("credential-generation-changed"); }
        if (!forceRefresh && cache.TryGetValue(context.StorageKey, out AccessToken? token) && token.ExpiresAt > clock.GetUtcNow().AddMinutes(2))
        { return token; }
        bool graphScope = context.ResourceKey == ApiResources.MarketplaceProductIngestion;
        Uri endpoint = new($"https://login.microsoftonline.com/{tenant:D}/oauth2/{(graphScope ? "v2.0/" : "")}token");
        using JsonDocument document = await http.SendAsync(() => new(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = context.ClientId,
                ["client_secret"] = credential.SecretValue,
                [graphScope ? "scope" : "resource"] = graphScope ? context.Resource + "/.default" : context.Resource
            })
        }, endpoint, false, cancellationToken).ConfigureAwait(false) ?? throw new InventoryException("invalid-token-response");
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("access_token", out JsonElement value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Any(character => !char.IsAsciiLetterOrDigit(character) && !"-._~+/=".Contains(character, StringComparison.Ordinal)) ||
            !root.TryGetProperty("token_type", out JsonElement type) ||
            type.ValueKind != JsonValueKind.String || !string.Equals(type.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase) ||
            !root.TryGetProperty("expires_in", out JsonElement expiry) ||
            !int.TryParse(expiry.ValueKind == JsonValueKind.String ? expiry.GetString() : expiry.GetRawText(), NumberStyles.None,
                CultureInfo.InvariantCulture, out int seconds) || seconds is < 1 or > 86400 ||
            (root.TryGetProperty("resource", out JsonElement resource) && (resource.ValueKind != JsonValueKind.String || resource.GetString() != context.Resource)))
        { throw new InventoryException("invalid-token-response"); }
        token = new(value.GetString()!, clock.GetUtcNow().AddSeconds(seconds));
        cache[context.StorageKey] = token;
        return token;
    }
}

internal sealed class AuthorizedInventoryHttp(BoundedJsonHttp http, IInventoryTokens tokens)
{
    internal async Task<JsonDocument?> GetAsync(AuthorizationContext context, Uri destination, Uri approvedBase,
        bool emptyOnNotFound, CancellationToken cancellationToken)
    {
        // Check the destination before credential resolution or attaching a bearer header.
        BoundedJsonHttp.ValidateDestination(destination, approvedBase);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            AccessToken token = await tokens.GetAsync(context, attempt != 0, cancellationToken).ConfigureAwait(false);
            try
            {
                return await http.SendAsync(() =>
                {
                    HttpRequestMessage request = new(HttpMethod.Get, destination);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
                    return request;
                }, approvedBase, emptyOnNotFound, cancellationToken).ConfigureAwait(false);
            }
            catch (InventoryException error) when (error.Category == "unauthorized" && attempt == 0) { /* One renewed-token retry only. */ }
        }
        throw new InventoryException("unauthorized");
    }
}
