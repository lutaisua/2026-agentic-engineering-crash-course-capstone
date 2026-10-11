using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class InventoryAccountProofTests
{
    [TestMethod]
    public async Task PrivateComparisonEvidenceMustMatchProfileProgramContextDateAndBothKinds()
    {
        using TemporaryDirectory directory = new();
        WindowsPrivateStorageGuard privacy = new();
        string evidenceDirectory = privacy.PrepareDirectory(Path.Combine(directory.Path, "evidence"));
        PublisherProfile profile = InventoryModelTests.Profile();
        DateTimeOffset verifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        profile.Accounts[ApiResources.StoreProgram] = profile.Accounts[ApiResources.StoreProgram] with
        { Discovery = new() { Coverage = new() { VerifiedAt = verifiedAt, EvidenceReference = "fictional-comparison" } } };
        InventoryBinding binding = InventoryModelTests.Binding(profile);
        DiscoveryRequest request = new(profile, ApiResources.StoreProgram, binding, [InventoryHttpTests.Context()]);
        InventoryAccountProof verifier = new(evidenceDirectory, privacy, TimeProvider.System);
        (await verifier.VerifyAsync(request, default)).Should().BeFalse();
        string path = Path.Combine(evidenceDirectory, "fictional-comparison.json");
        var evidence = new
        {
            schemaVersion = 1,
            publisherId = profile.Id,
            program = ApiResources.StoreProgram,
            configurationKey = binding.ConfigurationKey,
            verifiedAt,
            source = "partner-center-inventory-comparison",
            kinds = new[] { "store-app", "store-addon" }
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence));
        (await verifier.VerifyAsync(request, default)).Should().BeTrue();
        (await verifier.VerifyAsync(request with { Binding = binding with { ConfigurationKey = "changed-application" } }, default)).Should().BeFalse();
        (await verifier.VerifyAsync(request with { Profile = profile with { Id = "other-publisher" } }, default)).Should().BeFalse();
        string json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace("\"store-addon\"", "\"unverified-kind\"", StringComparison.Ordinal));
        (await verifier.VerifyAsync(request, default)).Should().BeFalse();
    }
}
