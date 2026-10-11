namespace AnalyticsET.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AnalyticsET-tests", Guid.NewGuid().ToString("N"));

    internal TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
