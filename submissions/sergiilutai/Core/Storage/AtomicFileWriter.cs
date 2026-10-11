namespace Core.AnalyticsET.Storage;

internal interface IAtomicFileWriter
{
    Task WriteAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken);
}

internal interface IFileCommitter
{
    void Commit(string temporaryPath, string destination);
}

internal sealed class FileCommitter : IFileCommitter
{
    public void Commit(string temporaryPath, string destination) => File.Move(temporaryPath, destination, overwrite: true);
}

internal sealed class NewFileCommitter : IFileCommitter
{
    public void Commit(string temporaryPath, string destination) => File.Move(temporaryPath, destination, overwrite: false);
}

internal sealed class AtomicFileWriter(IFileCommitter committer) : IAtomicFileWriter
{
    private readonly IFileCommitter committer = committer ?? throw new ArgumentNullException(nameof(committer));

    public async Task WriteAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            committer.Commit(temporaryPath, fullPath);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
