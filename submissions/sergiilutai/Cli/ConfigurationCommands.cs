using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Publishers;

namespace Console.AnalyticsET.Cli;

internal sealed class ConfigurationCommands(IPublisherStoreFactory stores, ICredentialReferenceInspector references,
    ConfigurationMigrator? migrator = null)
{
    private readonly IPublisherStoreFactory stores = stores ?? throw new ArgumentNullException(nameof(stores));
    private readonly ICredentialReferenceInspector references = references ?? throw new ArgumentNullException(nameof(references));

    internal async Task<string> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        if (command.Arguments is ["config", "migrate", "--to-version", "2", "--output", string destination])
        {
            if (migrator is null)
            {
                throw new InputException("Configuration migration is not configured.");
            }

            await migrator.MigrateAsync(command.ConfigurationPath, destination, cancellationToken).ConfigureAwait(false);
            return "Migrated to schemaVersion 2. Original preserved; legacy export selections require inventory resolution. Microsoft access remains unverified.";
        }

        bool all;
        string[] ids;
        switch (command.Arguments)
        {
            case ["config", "validate"]:
            case ["config", "validate", "--all"]:
                all = true;
                ids = [];
                break;
            case ["config", "validate", "--publisher", string selection]:
                all = false;
                ids = selection.Split(',');
                break;
            default:
                throw new InputException("Invalid configuration command. Use config path, config validate, or config migrate --to-version 2 --output <new-private-file>.");
        }

        PublisherConfiguration configuration = await stores.Create(command.ConfigurationPath).LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<PublisherProfile> selected = all && configuration.Publishers.Count == 0 ? [] :
            PublisherSelection.Resolve(configuration, ids, all);
        return JsonSerializer.Serialize(new
        {
            configuration.SchemaVersion,
            validationStatus = "valid",
            authorizationStatus = "unverified",
            credentialAvailability = "not-checked-offline",
            publisherCount = selected.Count,
            publishers = selected.Select(profile => new
            {
                publisherId = profile.Id,
                profile.Programs,
                contexts = AuthorizationContext.ForProfile(profile).Select(context => new
                {
                    context.Program,
                    context.Resource,
                    credentialReference = references.Describe(context),
                    authorizationStatus = "unverified"
                })
            })
        }, ConfigurationJson.Options);
    }
}
