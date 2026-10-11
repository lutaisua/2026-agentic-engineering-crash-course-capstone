using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Storage;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class InventoryStoreTests
{
    [TestMethod]
    public async Task SnapshotIsPrivateAllowlistedAndExclusiveDuringRefresh()
    {
        using TemporaryDirectory directory = new();
        InventoryStore store = Store(directory.Path);
        InventoryBinding binding = InventoryModelTests.Binding(InventoryModelTests.Profile());
        InventorySnapshot snapshot = Snapshot(binding);
        await using (InventoryStore.InventorySession session = await store.OpenAsync(binding, default))
        {
            Func<Task> concurrent = async () => { await using var duplicate = await store.OpenAsync(binding, default); };
            await concurrent.Should().ThrowAsync<IOException>();
            await session.CommitAsync(snapshot, default);
        }
        (await store.LoadOfflineAsync(binding, default)).Snapshot.Should().BeEquivalentTo(snapshot);
        string path = Path.Combine(directory.Path, "private", binding.FileKey + ".snapshot.json");
        new WindowsPrivateStorageGuard().VerifyFile(path);
        string json = await File.ReadAllTextAsync(path);
        json.Should().NotContain("clientSecret").And.NotContain("accessToken").And.NotContain("rawPayload");
        // Unknown raw-provider fields must not survive a reload as trusted inventory.
        await File.WriteAllTextAsync(path, json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"rawPayload\": {}", StringComparison.Ordinal));
        Func<Task> corrupt = async () => await store.LoadOfflineAsync(binding, default);
        await corrupt.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "corrupt-private-state");
    }

    [TestMethod]
    public async Task InterruptedCommitKeepsLastSuccessAndChangedBindingDoesNotReuseIt()
    {
        using TemporaryDirectory directory = new();
        InventoryBinding binding = InventoryModelTests.Binding(InventoryModelTests.Profile());
        InventoryStore store = Store(directory.Path);
        await using (var session = await store.OpenAsync(binding, default)) { await session.CommitAsync(Snapshot(binding), default); }
        string path = Path.Combine(directory.Path, "private", binding.FileKey + ".snapshot.json");
        byte[] original = await File.ReadAllBytesAsync(path);
        InventoryStore failing = Store(directory.Path, new AtomicFileWriter(new FailingCommitter()));
        await using (var session = await failing.OpenAsync(binding, default))
        {
            Func<Task> commit = () => session.CommitAsync(Snapshot(binding) with { Products = [InventoryModelTests.Product("new-app")] }, default);
            await commit.Should().ThrowAsync<IOException>();
        }
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
        Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Should().BeEmpty();
        (await store.LoadOfflineAsync(binding with { ConfigurationKey = "changed" }, default)).Snapshot.Should().BeNull();
        await using (var changed = await store.OpenAsync(binding with { AuthorizationKey = "rotated" }, default))
        { changed.Previous.Should().BeNull(); }
    }

    [TestMethod]
    public async Task WrongProfileBroadAclAndWrongOwnerAreRejected()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows ACL verification requires Windows."); return; }
        using TemporaryDirectory directory = new();
        InventoryBinding binding = InventoryModelTests.Binding(InventoryModelTests.Profile());
        InventoryStore store = Store(directory.Path);
        await using (var session = await store.OpenAsync(binding, default)) { await session.CommitAsync(Snapshot(binding), default); }
        string path = Path.Combine(directory.Path, "private", binding.FileKey + ".snapshot.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(Snapshot(binding with { PublisherId = "other" }), InventoryStore.JsonOptions));
        Func<Task> wrong = async () => await store.LoadOfflineAsync(binding, default);
        await wrong.Should().ThrowAsync<InventoryException>().Where(error => error.Category == "wrong-state-binding");
        FileInfo file = new(path);
        FileSecurity acl = file.GetAccessControl();
        Action wrongOwner = () =>
        {
            if (OperatingSystem.IsWindows())
            {
                WindowsPrivateStorageGuard.VerifyWindowsAccess(acl,
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), requireProtected: false);
            }
        };
        wrongOwner.Should().Throw<Console.AnalyticsET.Cli.InputException>().WithMessage("*owned by the current operator*");
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(acl);
        await wrong.Should().ThrowAsync<Console.AnalyticsET.Cli.InputException>();
        InventoryStore denied = new(Path.Combine(directory.Path, "private"), new DeniedGuard(), new AtomicFileWriter(new FileCommitter()));
        Func<Task> deniedRead = async () => await denied.LoadOfflineAsync(binding, default);
        await deniedRead.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task RepositoryDestinationAndIncompleteSnapshotAreRejected()
    {
        using TemporaryDirectory directory = new();
        InventoryBinding binding = InventoryModelTests.Binding(InventoryModelTests.Profile());
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        Func<Task> repository = async () => { await using var session = await Store(directory.Path).OpenAsync(binding, default); };
        await repository.Should().ThrowAsync<Console.AnalyticsET.Cli.InputException>();
        using TemporaryDirectory valid = new();
        await using var session = await Store(valid.Path).OpenAsync(binding, default);
        Func<Task> incomplete = () => session.CommitAsync(Snapshot(binding) with
        { Coverage = [new("store-app", "store-submission", null, CoverageState.Unverified, "missing-page")] }, default);
        await incomplete.Should().ThrowAsync<InventoryException>();
        session.Previous.Should().BeNull();
    }

    internal static InventoryStore Store(string path, IAtomicFileWriter? writer = null) => new(Path.Combine(path, "private"),
        new WindowsPrivateStorageGuard(), writer ?? new AtomicFileWriter(new FileCommitter()));
    internal static InventorySnapshot Snapshot(InventoryBinding binding) => new(1, binding, DateTimeOffset.UtcNow,
        [InventoryModelTests.Product("app-one")], [new("store-app", "store-submission", null, CoverageState.Complete, "all-pages")]);
    private sealed class FailingCommitter : IFileCommitter
    {
        public void Commit(string temporaryPath, string destination) => throw new IOException("Fixture commit failure.");
    }
    private sealed class DeniedGuard : IPrivateStorageGuard
    {
        public string PrepareDirectory(string path) => throw new UnauthorizedAccessException();
        public void VerifyFile(string path) => throw new UnauthorizedAccessException();
    }
}
