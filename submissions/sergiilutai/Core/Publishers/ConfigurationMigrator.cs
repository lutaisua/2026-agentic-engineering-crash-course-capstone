using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Storage;

namespace Core.AnalyticsET.Publishers;

internal sealed class ConfigurationMigrator(IPublisherStoreFactory stores, IPrivateStorageGuard privacy, IAtomicFileWriter writer)
{
    private readonly IPublisherStoreFactory stores = stores ?? throw new ArgumentNullException(nameof(stores));
    private readonly IPrivateStorageGuard privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
    private readonly IAtomicFileWriter writer = writer ?? throw new ArgumentNullException(nameof(writer));

    internal async Task MigrateAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        string source = Path.GetFullPath(sourcePath);
        string destination = Path.GetFullPath(destinationPath);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (source.Equals(destination, comparison))
        {
            throw new InputException("Migration output must be a different private file; the original is preserved.");
        }

        PublisherConfiguration original = await stores.Create(source).LoadAsync(cancellationToken).ConfigureAwait(false);
        if (original.SchemaVersion != 1)
        {
            throw new InputException("Only schemaVersion 1 configurations need migration to version 2.");
        }

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new InputException("Migration output already exists. Choose a new private file.");
        }

        privacy.PrepareDirectory(Path.GetDirectoryName(destination)!);
        await using FileStream writeLock = new(destination + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new InputException("Migration output already exists. Choose a new private file.");
        }

        PublisherConfiguration migrated = original with
        {
            SchemaVersion = 2,
            Publishers = original.Publishers.Select(profile => profile with
            {
                Accounts = profile.Accounts.ToDictionary(pair => pair.Key, pair => pair.Value with
                {
                    ProductIds = [],
                    Discovery = new(),
                    ExportSelection = pair.Value.ProductIds.Select(id => new ProductSelection { Kind = "legacy", SourceId = id }).ToList()
                })
            }).ToList()
        };
        ConfigurationValidator.Validate(migrated);
        await writer.WriteAsync(destination, JsonSerializer.SerializeToUtf8Bytes(migrated, ConfigurationJson.Options),
            cancellationToken).ConfigureAwait(false);
        privacy.VerifyFile(destination);
    }
}
