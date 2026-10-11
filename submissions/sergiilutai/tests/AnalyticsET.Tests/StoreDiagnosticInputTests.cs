using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class StoreDiagnosticInputTests
{
    internal static readonly StoreDiagnosticInput Sample = new(1, "publisher-one", "APP-FICTIONAL", "SUB-FICTIONAL", "Subscription",
        new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [TestMethod]
    public void ExplicitSelectionSeparatesPublishersSharingTenantAndRejectsWrongOrMissingScope()
    {
        PublisherProfile one = InventoryModelTests.Profile(), two = InventoryModelTests.Profile("publisher-two", "account-two");
        PublisherConfiguration configuration = new() { SchemaVersion = 2, Publishers = [one, two] };
        var selected = StoreDiagnosticPreflight.Select(configuration, Sample);
        var other = StoreDiagnosticPreflight.Select(configuration, Sample with { PublisherId = "publisher-two" });
        Assert.AreEqual(selected.TenantId, other.TenantId);
        Assert.AreNotEqual(selected.ConfigurationKey, other.ConfigurationKey);
        Assert.AreEqual("account-one", selected.PublisherAccountId);
        Assert.Throws<InputException>(() => StoreDiagnosticPreflight.Select(configuration, Sample with { PublisherId = "missing" }));
        one.Authorization.Remove(ApiResources.StoreInventory);
        Assert.Throws<InputException>(() => StoreDiagnosticPreflight.Select(configuration, Sample));
    }

    [TestMethod]
    public void StrictSampleRejectsAmbiguityUrisUnknownFieldsAndInvalidDates()
    {
        string valid = JsonSerializer.Serialize(Sample, JsonOptions);
        Assert.AreEqual(Sample, StoreDiagnosticInput.Parse(valid, new StoreDiagnosticTransportTests.Clock()));
        string[] invalid = [valid.Replace("APP-FICTIONAL", "../private"), valid.Replace("Subscription", "Consumable"),
            valid.Replace("publisher-one", "Publisher-One"), valid.Replace("2026-10-10", "2030-10-10"),
            valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1,\"schemaVersion\": 1"),
            valid.Replace("\"schemaVersion\": 1", "\"unexpected\": true,\"schemaVersion\": 1"), "PRIVATE-CANARY"];
        foreach (string value in invalid)
        {
            InputException error = Assert.Throws<InputException>(() => StoreDiagnosticInput.Parse(value, new StoreDiagnosticTransportTests.Clock()));
            Assert.IsFalse(error.Message.Contains("PRIVATE-CANARY", StringComparison.Ordinal));
        }
        Assert.IsFalse(Sample.ToString().Contains("FICTIONAL", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RealPrivateStoragePreflightPreservesInputsAndRejectsSharedDirectory()
    {
        using TemporaryDirectory temporary = new();
        WindowsPrivateStorageGuard guard = new();
        string root = guard.PrepareDirectory(Path.Combine(temporary.Path, "private"));
        string config = Path.Combine(root, "publishers.json"), input = Path.Combine(root, "input.json"), mapping = Path.Combine(root, "references.json");
        PublisherProfile profile = InventoryModelTests.Profile();
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new PublisherConfiguration { SchemaVersion = 2, Publishers = [profile] }, JsonOptions));
        await File.WriteAllTextAsync(input, JsonSerializer.Serialize(Sample, JsonOptions));
        await File.WriteAllTextAsync(mapping, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            references = new Dictionary<string, object> { [profile.Authorization[ApiResources.StoreInventory].CredentialReference] = new { target = "FICTIONAL-CREDENTIAL", generation = "1" } }
        }));
        byte[] before = await File.ReadAllBytesAsync(config);
        DiagnosticPreflight prepared = await StoreDiagnosticPreflight.LoadAsync(config, input, mapping, Path.Combine(root, "output"), guard,
            new StoreDiagnosticTransportTests.Clock(), default);
        Assert.AreEqual(Sample, prepared.Input);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(config));
        string shared = Path.Combine(temporary.Path, "shared");
        Directory.CreateDirectory(shared);
        await Assert.ThrowsAsync<InputException>(() => StoreDiagnosticPreflight.LoadAsync(config, input, mapping, shared, guard,
            new StoreDiagnosticTransportTests.Clock(), default));
    }

    [TestMethod]
    public async Task RepositoryReparseAndBroadAclInputsStopBeforeVaultOrHttp()
    {
        using StoreSubscriptionDiagnosticTests.Fixture repository = new();
        Directory.CreateDirectory(Path.Combine(repository.Root, ".git"));
        await Assert.ThrowsAsync<InputException>(() => repository.RunAsync());
        Assert.AreEqual(0, repository.Calls);
        Assert.AreEqual(0, repository.Credentials.Contexts.Count);

        using StoreSubscriptionDiagnosticTests.Fixture broad = new();
        if (!OperatingSystem.IsWindows()) { Assert.Fail("Windows diagnostic ACL fixtures require Windows."); return; }
        FileInfo sample = new(broad.Input);
        FileSecurity acl = sample.GetAccessControl();
        Assert.Throws<InputException>(() =>
        {
            if (OperatingSystem.IsWindows())
            { WindowsPrivateStorageGuard.VerifyWindowsAccess(acl, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), false); }
        });
        acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        sample.SetAccessControl(acl);
        await Assert.ThrowsAsync<InputException>(() => broad.RunAsync());
        Assert.AreEqual(0, broad.Calls);
        Assert.AreEqual(0, broad.Credentials.Contexts.Count);

        using StoreSubscriptionDiagnosticTests.Fixture redirected = new();
        string link = Path.Combine(redirected.Root, "fixture-junction");
        ProcessStartInfo start = new("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "/d", "/c", "mklink", "/J", link, redirected.Root }) { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start)!;
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, "Fictional junction fixture could not be created.");
        try
        {
            await Assert.ThrowsAsync<InputException>(() => StoreSubscriptionDiagnostic.RunAsync(redirected.Configuration,
                Path.Combine(link, "sample.json"), redirected.Mapping, Path.Combine(redirected.Root, "runs"), new WindowsPrivateStorageGuard(),
                redirected.Credentials, new InventoryHttpTests.Handler(_ => throw new AssertFailedException("Unexpected HTTP.")),
                new StoreDiagnosticTransportTests.Clock(), new AtomicFileWriter(new NewFileCommitter()), default));
            Assert.AreEqual(0, redirected.Credentials.Contexts.Count);
        }
        finally
        {
            // Remove only the created junction, without recursion or following its target.
            Directory.Delete(link, recursive: false);
        }
    }

    [TestMethod]
    public async Task AmbiguousCredentialMappingIsRejectedBeforeResolverAccess()
    {
        using StoreSubscriptionDiagnosticTests.Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.Mapping, "{\"schemaVersion\":1,\"schemaVersion\":1,\"references\":{}}");
        await Assert.ThrowsAsync<InputException>(() => fixture.RunAsync());
        Assert.AreEqual(0, fixture.Calls);
        Assert.AreEqual(0, fixture.Credentials.Contexts.Count);
    }
}
