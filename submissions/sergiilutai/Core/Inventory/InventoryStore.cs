using System.Text.Json;
using System.Text.Json.Serialization;
using Core.AnalyticsET.Storage;

namespace Core.AnalyticsET.Inventory;

internal sealed class InventoryStore(string directory, IPrivateStorageGuard privacy, IAtomicFileWriter writer)
{
    private readonly string directory = Path.GetFullPath(directory);
    internal static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".analyticset", "inventory");
    internal static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    internal async Task<InventorySession> OpenAsync(InventoryBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        privacy.PrepareDirectory(directory);
        string lockPath = Path.Combine(directory, binding.FileKey + ".lock");
        if (File.Exists(lockPath)) { privacy.VerifyFile(lockPath); }
        FileStream lease = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            privacy.VerifyFile(lockPath);
            InventorySession session = new(this, binding, lease);
            await session.LoadAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<(InventorySnapshot? Snapshot, DiscoveryAttempt? Attempt)> LoadOfflineAsync(
        InventoryBinding binding, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) { return (null, null); }
        privacy.PrepareDirectory(directory);
        return (await LoadAsync<InventorySnapshot>(binding, "snapshot", false, cancellationToken).ConfigureAwait(false),
            await LoadAsync<DiscoveryAttempt>(binding, "attempt", false, cancellationToken).ConfigureAwait(false));
    }

    private async Task<T?> LoadAsync<T>(InventoryBinding binding, string suffix, bool requireGeneration, CancellationToken cancellationToken)
        where T : class
    {
        string path = Path.Combine(directory, binding.FileKey + "." + suffix + ".json");
        if (!File.Exists(path)) { return null; }
        privacy.VerifyFile(path);
        try
        {
            T value = JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), JsonOptions)
                ?? throw new InventoryException("corrupt-private-state");
            InventoryBinding loaded = value switch
            {
                InventorySnapshot snapshot => snapshot.Binding,
                DiscoveryAttempt attempt => attempt.Binding,
                _ => throw new InventoryException("corrupt-private-state")
            };
            if (loaded is null || loaded.PublisherId != binding.PublisherId || loaded.Program != binding.Program)
            {
                throw new InventoryException("wrong-state-binding");
            }
            if (loaded.ConfigurationKey != binding.ConfigurationKey ||
                (requireGeneration && loaded.AuthorizationKey != binding.AuthorizationKey)) { return null; }
            if (value is InventorySnapshot stored)
            {
                if (stored.SchemaVersion != 1 || stored.Products is null || stored.Coverage is null || stored.ObservedAt == default ||
                    !stored.Coverage.All(coverage => coverage is not null && coverage.State is CoverageState.Complete or CoverageState.NotApplicable))
                {
                    throw new InventoryException("corrupt-private-state");
                }
                InventoryValidation.Normalize(binding, stored.Products);
            }
            else if (value is DiscoveryAttempt attempt &&
                (attempt.Coverage is null || !Enum.IsDefined(attempt.Outcome) || attempt.AttemptedAt == default))
            {
                throw new InventoryException("corrupt-private-state");
            }
            return value;
        }
        catch (Exception error) when (error is JsonException or NullReferenceException or ArgumentException)
        {
            throw new InventoryException("corrupt-private-state");
        }
    }

    private async Task WriteAsync<T>(InventoryBinding binding, string suffix, T value, CancellationToken cancellationToken)
    {
        privacy.PrepareDirectory(directory);
        string path = Path.Combine(directory, binding.FileKey + "." + suffix + ".json");
        if (File.Exists(path)) { privacy.VerifyFile(path); }
        await writer.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), cancellationToken).ConfigureAwait(false);
        privacy.VerifyFile(path);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false));
        return options;
    }

    internal sealed class InventorySession(InventoryStore store, InventoryBinding binding, FileStream lease) : IAsyncDisposable
    {
        internal InventorySnapshot? Previous { get; private set; }
        internal async Task LoadAsync(CancellationToken cancellationToken) => Previous =
            await store.LoadAsync<InventorySnapshot>(binding, "snapshot", true, cancellationToken).ConfigureAwait(false);

        internal async Task CommitAsync(InventorySnapshot snapshot, CancellationToken cancellationToken)
        {
            if (snapshot.Binding != binding || snapshot.SchemaVersion != 1 || snapshot.ObservedAt == default ||
                snapshot.Coverage.Count == 0 || snapshot.Coverage.Any(coverage => coverage.State is not (CoverageState.Complete or CoverageState.NotApplicable)) ||
                snapshot.Products.Any(item => item.Returned && (item.Publication == PublicationState.Unknown || item.Availability == AvailabilityState.Unknown)))
            {
                throw new InventoryException("incomplete-snapshot");
            }
            InventoryValidation.Normalize(binding, snapshot.Products);
            await store.WriteAsync(binding, "snapshot", snapshot, cancellationToken).ConfigureAwait(false);
            Previous = snapshot;
        }

        internal Task RecordAttemptAsync(DiscoveryAttempt attempt, CancellationToken cancellationToken)
        {
            if (attempt.Binding != binding || !Enum.IsDefined(attempt.Outcome)) { throw new InventoryException("wrong-state-binding"); }
            return store.WriteAsync(binding, "attempt", attempt, cancellationToken);
        }

        public ValueTask DisposeAsync() => lease.DisposeAsync();
    }
}
