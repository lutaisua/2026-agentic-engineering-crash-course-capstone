using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class StoreDiagnosticPacketTests
{
    internal static StoreDiagnosticPacket Packet() => new(1, Guid.NewGuid().ToString("D"), StoreDiagnosticInputTests.Sample.ConfirmedAt,
        StoreDiagnosticInputTests.Sample.ConfirmedAt, new string('a', 64), new string('b', 64), StoreDiagnosticInputTests.Sample,
        [new("addon-detail", "none", 1, StoreDiagnosticInputTests.Sample.ConfirmedAt, StoreDiagnosticInputTests.Sample.ConfirmedAt, 400, null,
            StoreDiagnosticErrorSummary.Parse(System.Text.Encoding.UTF8.GetBytes("{\"code\":\"NotAllowed\",\"target\":\"productType\"}")),
            [new("Request-ID", "present", "11111111-2222-3333-4444-555555555555")])],
        [new("addon-detail", "none", "http-request-failed", 400, new("not-read"))], ["partial-comparison"], 2);

    [TestMethod]
    public async Task AtomicPrivatePacketHasReceiptHashesAndKeepsIdentitiesAndCorrelationsOutOfSharedView()
    {
        using TemporaryDirectory temporary = new();
        WindowsPrivateStorageGuard guard = new();
        StoreDiagnosticPacket packet = Packet();
        StoreDiagnosticPacketStore store = new(Path.Combine(temporary.Path, "private"), guard, new AtomicFileWriter(new NewFileCommitter()));
        string run = await store.SaveAsync(packet, default);
        foreach (string file in new[] { "evidence.json", "support-attachment.md", "receipt.json" }) { guard.VerifyFile(Path.Combine(run, file)); }
        using JsonDocument receipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run, "receipt.json")));
        Assert.AreEqual("partial", receipt.RootElement.GetProperty("completion").GetString());
        Assert.AreEqual(StoreDiagnosticPacketStore.Hash(await File.ReadAllBytesAsync(Path.Combine(run, "evidence.json"))),
            receipt.RootElement.GetProperty("evidenceSha256").GetString());
        string shared = StoreDiagnosticSupport.SharedView(packet);
        Assert.IsFalse(shared.Contains("APP-FICTIONAL", StringComparison.Ordinal));
        Assert.IsFalse(shared.Contains("11111111-2222", StringComparison.Ordinal));
        Assert.IsTrue((await File.ReadAllTextAsync(Path.Combine(run, "support-attachment.md"))).Contains("SUB-FICTIONAL", StringComparison.Ordinal));
        Assert.IsFalse(JsonSerializer.Serialize(packet.PublicProjection()).Contains("11111111-2222", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InputException>(() => store.SaveAsync(packet, default));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailureBetweenFilesNeverCreatesCompletionReceiptAndPreservesPreviousPacket(bool commitFailure)
    {
        using TemporaryDirectory temporary = new();
        string root = Path.Combine(temporary.Path, "private");
        WindowsPrivateStorageGuard guard = new();
        StoreDiagnosticPacket earlier = Packet();
        string before = await new StoreDiagnosticPacketStore(root, guard, new AtomicFileWriter(new NewFileCommitter())).SaveAsync(earlier, default);
        byte[] saved = await File.ReadAllBytesAsync(Path.Combine(before, "evidence.json"));
        StoreDiagnosticPacket next = Packet();
        IAtomicFileWriter failing = commitFailure ? new AtomicFileWriter(new FailingCommitter()) : new FailingWriter();
        await Assert.ThrowsAsync<IOException>(() => new StoreDiagnosticPacketStore(root, guard, failing).SaveAsync(next, default));
        Assert.IsFalse(File.Exists(Path.Combine(root, next.RunId, "receipt.json")));
        CollectionAssert.AreEqual(saved, await File.ReadAllBytesAsync(Path.Combine(before, "evidence.json")));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        StoreDiagnosticPacket third = Packet();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new StoreDiagnosticPacketStore(root, guard, new AtomicFileWriter(new NewFileCommitter())).SaveAsync(third, cancelled.Token));
        Assert.IsFalse(File.Exists(Path.Combine(root, third.RunId, "receipt.json")));
    }
    [TestMethod]
    public async Task IncompleteSuccessAndCorruptedPublicationCannotAdvertiseCompletedPacket()
    {
        using TemporaryDirectory temporary = new();
        string root = Path.Combine(temporary.Path, "private");
        WindowsPrivateStorageGuard guard = new();
        StoreDiagnosticPacket incomplete = Packet() with { ExitCode = 0 };
        await Assert.ThrowsAsync<InputException>(() => new StoreDiagnosticPacketStore(root, guard,
            new AtomicFileWriter(new NewFileCommitter())).SaveAsync(incomplete, default));
        Assert.IsFalse(Directory.Exists(root));
        StoreDiagnosticPacket altered = Packet();
        await Assert.ThrowsAsync<InputException>(() => new StoreDiagnosticPacketStore(root, guard, new CorruptingWriter()).SaveAsync(altered, default));
        Assert.IsFalse(File.Exists(Path.Combine(root, altered.RunId, "receipt.json")));
    }
    private sealed class FailingCommitter : IFileCommitter
    {
        private int commits;
        public void Commit(string temporaryPath, string destination)
        {
            if (++commits == 2) { throw new IOException("FICTIONAL-COMMIT-FAILURE"); }
            new NewFileCommitter().Commit(temporaryPath, destination);
        }
    }
    private sealed class CorruptingWriter : IAtomicFileWriter
    {
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken)
        {
            await new AtomicFileWriter(new NewFileCommitter()).WriteAsync(path, contents, cancellationToken);
            await File.AppendAllTextAsync(path, "FICTIONAL-CORRUPTION", cancellationToken);
        }
    }
    private sealed class FailingWriter : IAtomicFileWriter
    {
        private int writes;
        public Task WriteAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken)
        {
            if (++writes == 2) { throw new IOException("FICTIONAL-WRITE-FAILURE"); }
            return new AtomicFileWriter(new NewFileCommitter()).WriteAsync(path, contents, cancellationToken);
        }
    }
}
