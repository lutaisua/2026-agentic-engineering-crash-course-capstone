using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

internal static class StoreHubDiscoveryReader
{
    internal static async Task<StoreHubDiscoveryPacket> LoadAsync(StoreHubPreflight prepared, string directory,
        IReadOnlySet<string> approvedAssemblyHashes, IPrivateStorageGuard privacy, CancellationToken cancellationToken)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string leaf = Path.GetFileName(full);
        if (Path.GetDirectoryName(full) != prepared.ExperimentDirectory || !leaf.StartsWith("Discover-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(leaf[9..], "D", out Guid id) || id == Guid.Empty || prepared.PreviousBinding is null)
        { throw StoreHubInput.Invalid(); }
        privacy.PrepareDirectory(full);
        string receiptPath = Path.Combine(full, "receipt.json"), evidencePath = Path.Combine(full, "evidence.json");
        foreach (string file in new[] { receiptPath, evidencePath })
        {
            privacy.VerifyFile(file);
            if (new FileInfo(file).Length > (file == receiptPath ? 8192 : 4 * 1024 * 1024)) { throw StoreHubInput.Invalid(); }
        }
        byte[] receiptBytes = await File.ReadAllBytesAsync(receiptPath, cancellationToken).ConfigureAwait(false);
        byte[] evidenceBytes = await File.ReadAllBytesAsync(evidencePath, cancellationToken).ConfigureAwait(false);
        using JsonDocument receiptJson = JsonDocument.Parse(receiptBytes), evidenceJson = JsonDocument.Parse(evidenceBytes);
        if (!StoreHubQueryPlan.Unique(receiptJson.RootElement) || !StoreHubQueryPlan.Unique(evidenceJson.RootElement)) { throw StoreHubInput.Invalid(); }
        StoreHubReceipt receipt = JsonSerializer.Deserialize<StoreHubReceipt>(receiptBytes, StoreHubInput.JsonOptions) ?? throw StoreHubInput.Invalid();
        StoreHubDiscoveryPacket packet = JsonSerializer.Deserialize<StoreHubDiscoveryPacket>(evidenceBytes, StoreHubInput.JsonOptions) ?? throw StoreHubInput.Invalid();
        if (receipt.SchemaVersion != 1 || packet.SchemaVersion != 1 || receipt.RunId != id.ToString("D") || receipt.RunId != packet.RunId ||
            receipt.Stage != "Discover" || packet.Stage != "Discover" || receipt.ExitCode != 0 || packet.ExitCode != 0 || packet.Outcome != "metadata-recorded" ||
            receipt.EvidenceSha256 != StoreDiagnosticPacketStore.Hash(evidenceBytes) || packet.ContractSha256 != StoreHubPreflight.ContractSha256 ||
            packet.InputSha256 != prepared.InputSha256 || packet.BindingFingerprint != AuthorizationContext.Hash(
                [prepared.Context.ConfigurationKey, prepared.PreviousBinding.CredentialGeneration]) ||
            !approvedAssemblyHashes.Contains(packet.SourceSha256) || packet.Metadata is null || packet.Attempts is null ||
            packet.Metadata.Datasets is null || packet.StartedAt.Offset != TimeSpan.Zero || packet.CompletedAt.Offset != TimeSpan.Zero ||
            packet.CompletedAt < packet.StartedAt) { throw StoreHubInput.Invalid(); }
        using JsonDocument normalized = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new
        { value = packet.Metadata.Datasets, totalCount = packet.Metadata.AdvertisedCount, statusCode = 200 }, StoreHubInput.JsonOptions));
        StoreHubMetadata.Parse(normalized.RootElement);
        return packet;
    }

    internal static IReadOnlySet<string> ReviewedDiscoveryAssemblies()
    {
        string app = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        HashSet<string> approved = new(StringComparer.Ordinal)
        {
            StoreDiagnosticPacketStore.Hash(File.ReadAllBytes(typeof(StoreHubDiscoveryReader).Assembly.Location))
        };
        // Historical source identities remain valid for already recorded discovery, under the same contract/binding.
        foreach (string filename in new[] { "store-downloads-hub-source-hashes.json", "store-downloads-hub-setup-diagnostics-source-hashes.json" })
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(app, "docs", "verification", filename)));
            if (manifest.RootElement.GetProperty("contractSha256").GetString() != StoreHubPreflight.ContractSha256) { throw StoreHubInput.Invalid(); }
            string hash = manifest.RootElement.GetProperty("testAssemblySha256").GetString() ?? "";
            if (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)) { throw StoreHubInput.Invalid(); }
            approved.Add(hash);
        }
        return approved;
    }
}
