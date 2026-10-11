using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.AnalyticsET.Publishers;

internal sealed record PublisherConfiguration
{
    public int SchemaVersion { get; init; } = 1;
    public List<PublisherProfile> Publishers { get; init; } = [];
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

internal sealed record PublisherProfile
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string TenantId { get; init; } = "";
    public List<string> Programs { get; init; } = [];
    public Dictionary<string, ProgramScope> Accounts { get; init; } = [];
    public Dictionary<string, AuthorizationSettings> Authorization { get; init; } = [];
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

internal sealed record ProgramScope
{
    public string PublisherAccountId { get; init; } = "";
    public List<string> ProductIds { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiscoverySettings? Discovery { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ProductSelection>? ExportSelection { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

internal sealed record DiscoverySettings
{
    public bool Enabled { get; init; } = true;
    public CoverageSettings Coverage { get; init; } = new();
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

internal sealed record CoverageSettings
{
    public List<string> ExpectedOfferTypes { get; init; } = [];
    public DateTimeOffset? VerifiedAt { get; init; }
    public string? EvidenceReference { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

internal sealed record ProductSelection
{
    public string Kind { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string? ParentId { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

internal sealed record AuthorizationSettings
{
    public string ClientId { get; init; } = "";
    public string AuthorizedAccountId { get; init; } = "";
    public string CredentialReference { get; init; } = "";
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

internal static class ApiResources
{
    internal const string Store = "store-analytics";
    internal const string Marketplace = "marketplace-analytics";
    internal const string Financial = "financial";
    internal const string StoreInventory = "store-inventory";
    internal const string MarketplaceProductIngestion = "marketplace-product-ingestion";
    internal const string MarketplaceSubmission = "marketplace-submission";
    internal const string StoreProgram = "microsoft-store";
    internal const string MarketplaceProgram = "azure-marketplace";

    internal static readonly IReadOnlyDictionary<string, string> Audiences = new Dictionary<string, string>
    {
        [Store] = "https://manage.devcenter.microsoft.com",
        [Marketplace] = "https://api.partnercenter.microsoft.com",
        [Financial] = "https://api.partner.microsoft.com",
        [StoreInventory] = "https://manage.devcenter.microsoft.com",
        [MarketplaceProductIngestion] = "https://graph.microsoft.com",
        [MarketplaceSubmission] = "https://api.partner.microsoft.com"
    };

    internal static bool IsInventory(string resource) => resource is StoreInventory or
        MarketplaceProductIngestion or MarketplaceSubmission;

    internal static bool MatchesProgram(string resource, string program) => resource switch
    {
        Store or StoreInventory => program == StoreProgram,
        Marketplace or MarketplaceProductIngestion or MarketplaceSubmission => program == MarketplaceProgram,
        Financial => program is StoreProgram or MarketplaceProgram,
        _ => false
    };
}
