using System.Text.Json.Serialization;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;

namespace Core.AnalyticsET.Inventory;

internal interface IInventoryAccountProof
{
    Task<bool> VerifyAsync(DiscoveryRequest request, CancellationToken cancellationToken);
}

internal sealed class InventoryAccountProof(string directory, IPrivateStorageGuard privacy, TimeProvider clock) : IInventoryAccountProof
{
    internal static string DefaultDirectory => InventoryStore.DefaultDirectory;
    public async Task<bool> VerifyAsync(DiscoveryRequest request, CancellationToken cancellationToken)
    {
        CoverageSettings coverage = request.Profile.Accounts[request.Program].Discovery?.Coverage ?? new();
        if (coverage.VerifiedAt is not DateTimeOffset verified || coverage.EvidenceReference is null || verified > clock.GetUtcNow()) { return false; }
        string path = Path.Combine(directory, coverage.EvidenceReference + ".json");
        if (!File.Exists(path)) { return false; }
        privacy.PrepareDirectory(directory);
        privacy.VerifyFile(path);
        AccountProof proof = ConfigurationJson.Deserialize<AccountProof>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
        string[] kinds = request.Program == ApiResources.StoreProgram ? ["store-app", "store-addon"] : ["marketplace-offer", "marketplace-plan"];
        return proof.SchemaVersion == 1 && proof.PublisherId == request.Profile.Id && proof.Program == request.Program &&
            proof.ConfigurationKey == request.Binding.ConfigurationKey && proof.VerifiedAt == verified &&
            proof.Source == "partner-center-inventory-comparison" && proof.Kinds is not null &&
            proof.Kinds.Order(StringComparer.Ordinal).SequenceEqual(kinds.Order(StringComparer.Ordinal));
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record AccountProof
    {
        public int SchemaVersion { get; init; }
        public string PublisherId { get; init; } = "";
        public string Program { get; init; } = "";
        public string ConfigurationKey { get; init; } = "";
        public DateTimeOffset VerifiedAt { get; init; }
        public string Source { get; init; } = "";
        public List<string>? Kinds { get; init; }
    }
}
