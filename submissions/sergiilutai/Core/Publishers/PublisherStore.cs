using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Storage;

namespace Core.AnalyticsET.Publishers;

internal interface IPublisherStore
{
    Task<PublisherConfiguration> LoadAsync(CancellationToken cancellationToken);
    Task AddAsync(PublisherProfile profile, CancellationToken cancellationToken);
    Task UpdateAsync(string id, PublisherProfile profile, CancellationToken cancellationToken);
    Task RemoveAsync(string id, CancellationToken cancellationToken);
}

internal interface IPublisherStoreFactory
{
    IPublisherStore Create(string path);
}

internal sealed class PublisherStoreFactory(IAtomicFileWriter writer) : IPublisherStoreFactory
{
    private readonly IAtomicFileWriter writer = writer ?? throw new ArgumentNullException(nameof(writer));
    public IPublisherStore Create(string path) => new PublisherStore(path, writer);
}

internal sealed class PublisherStore(string path, IAtomicFileWriter writer) : IPublisherStore
{
    private readonly string path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
    private readonly IAtomicFileWriter writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public Task<PublisherConfiguration> LoadAsync(CancellationToken cancellationToken) => LoadAsync(false, cancellationToken);

    private async Task<PublisherConfiguration> LoadAsync(bool allowMissing, CancellationToken cancellationToken, int missingSchemaVersion = 1)
    {
        if (!File.Exists(path))
        {
            if (allowMissing && !Directory.Exists(path))
            {
                return new() { SchemaVersion = missingSchemaVersion };
            }

            throw new InputException("Configuration file is missing or is a directory. Add a publisher or select --config <file>.");
        }

        string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        PublisherConfiguration configuration = ConfigurationJson.Deserialize<PublisherConfiguration>(json);
        ConfigurationValidator.Validate(configuration);
        return configuration;
    }

    public async Task AddAsync(PublisherProfile profile, CancellationToken cancellationToken)
    {
        int profileSchema = ConfigurationValidator.ProfileSchema(profile);
        ConfigurationValidator.Validate(profile, profileSchema);
        await MutateAsync(configuration =>
        {
            if (configuration.Publishers.Any(existing => existing.Id == profile.Id))
            {
                throw new InputException("Publisher ID already exists. Use publisher update with the same stable ID.");
            }

            configuration.Publishers.Add(profile);
        }, allowMissing: true, cancellationToken, profileSchema).ConfigureAwait(false);
    }

    public async Task UpdateAsync(string id, PublisherProfile profile, CancellationToken cancellationToken)
    {
        ConfigurationValidator.Validate(profile, ConfigurationValidator.ProfileSchema(profile));
        if (profile.Id != id)
        {
            throw new InputException("Updated profile must retain the selected stable publisher ID.");
        }

        await MutateAsync(configuration =>
        {
            int index = FindIndex(configuration, id);
            PublisherProfile previous = configuration.Publishers[index];
            configuration.Publishers[index] = profile with
            {
                Extensions = Merge(previous.Extensions, profile.Extensions),
                Accounts = profile.Accounts.ToDictionary(pair => pair.Key, pair => pair.Value with
                {
                    Extensions = Merge(previous.Accounts.GetValueOrDefault(pair.Key)?.Extensions, pair.Value.Extensions)
                }),
                Authorization = profile.Authorization.ToDictionary(pair => pair.Key, pair => pair.Value with
                {
                    Extensions = Merge(previous.Authorization.GetValueOrDefault(pair.Key)?.Extensions, pair.Value.Extensions)
                })
            };
        }, allowMissing: false, cancellationToken).ConfigureAwait(false);
    }

    public Task RemoveAsync(string id, CancellationToken cancellationToken) =>
        MutateAsync(configuration => configuration.Publishers.RemoveAt(FindIndex(configuration, id)), false, cancellationToken);

    private async Task MutateAsync(Action<PublisherConfiguration> mutate, bool allowMissing, CancellationToken cancellationToken,
        int missingSchemaVersion = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // A sidecar lock serializes read-modify-write operations across processes.
        // Keep the lock file after closing it, so another process cannot acquire a different file inode.
        await using FileStream writeLock = new(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        PublisherConfiguration configuration = await LoadAsync(allowMissing, cancellationToken, missingSchemaVersion).ConfigureAwait(false);
        mutate(configuration);
        ConfigurationValidator.Validate(configuration);
        await writer.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(configuration, ConfigurationJson.Options),
            cancellationToken).ConfigureAwait(false);
    }

    private static int FindIndex(PublisherConfiguration configuration, string id)
    {
        int index = configuration.Publishers.FindIndex(profile => profile.Id == id);
        return index >= 0 ? index : throw new InputException("Unknown publisher ID. Run publisher list to see configured IDs.");
    }

    private static Dictionary<string, JsonElement>? Merge(Dictionary<string, JsonElement>? previous,
        Dictionary<string, JsonElement>? replacement)
    {
        if (previous is null && replacement is null)
        {
            return null;
        }

        Dictionary<string, JsonElement> merged = previous is null ? [] : new(previous, StringComparer.Ordinal);
        foreach ((string key, JsonElement value) in replacement ?? [])
        {
            merged[key] = value;
        }

        return merged;
    }
}
