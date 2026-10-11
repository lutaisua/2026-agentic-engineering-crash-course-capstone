using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Publishers;

namespace Console.AnalyticsET.Cli;

internal sealed class PublisherCommands(IPublisherStoreFactory stores)
{
    private readonly IPublisherStoreFactory stores = stores ?? throw new ArgumentNullException(nameof(stores));

    internal async Task<string> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        IPublisherStore store = stores.Create(command.ConfigurationPath);
        switch (command.Arguments)
        {
            case ["publisher", "list"]:
                PublisherConfiguration configuration = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Serialize(configuration.Publishers, ConfigurationJson.Options);
            case ["publisher", "show", "--publisher", string ids]:
                return await ShowAsync(store, ids.Split(','), false, cancellationToken).ConfigureAwait(false);
            case ["publisher", "show", "--all"]:
                return await ShowAsync(store, [], true, cancellationToken).ConfigureAwait(false);
            case ["publisher", "add", "--file", string file]:
                await store.AddAsync(await ReadProfileAsync(file, cancellationToken).ConfigureAwait(false), cancellationToken)
                    .ConfigureAwait(false);
                return "Publisher added. Microsoft authorization is unverified.";
            case ["publisher", "update", string id, "--file", string file]:
                await store.UpdateAsync(id, await ReadProfileAsync(file, cancellationToken).ConfigureAwait(false), cancellationToken)
                    .ConfigureAwait(false);
                return "Publisher updated. Microsoft authorization is unverified.";
            case ["publisher", "remove", string id]:
                await store.RemoveAsync(id, cancellationToken).ConfigureAwait(false);
                return "Publisher removed from local configuration.";
            default:
                throw new InputException("Invalid publisher command. Run 'AnalyticsET --help' for syntax.");
        }
    }

    private static async Task<string> ShowAsync(IPublisherStore store, string[] ids, bool all, CancellationToken cancellationToken)
    {
        PublisherConfiguration configuration = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PublisherProfile> selected = PublisherSelection.Resolve(configuration, ids, all);
        return JsonSerializer.Serialize(selected.Select(profile => new
        {
            profile,
            authorizationStatus = "unverified",
            contexts = AuthorizationContext.ForProfile(profile)
        }), ConfigurationJson.Options);
    }

    private static async Task<PublisherProfile> ReadProfileAsync(string file, CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
        {
            throw new InputException("Profile input file is missing. Use --file <profile.json>.");
        }

        return ConfigurationJson.Deserialize<PublisherProfile>(
            await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false));
    }
}
