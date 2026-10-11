using Console.AnalyticsET.Cli;

namespace Core.AnalyticsET.Publishers;

internal static class PublisherSelection
{
    internal static IReadOnlyList<PublisherProfile> Resolve(PublisherConfiguration configuration, string[] ids, bool all)
    {
        ConfigurationValidator.Validate(configuration);
        if (all == (ids.Length > 0) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length ||
            ids.Any(id => !ConfigurationValidator.IsSafeId(id)))
        {
            throw new InputException("Select --publisher <id[,id]> or --all, with distinct configured IDs.");
        }

        if (all)
        {
            return configuration.Publishers.Count > 0 ? configuration.Publishers.ToArray() :
                throw new InputException("No publishers configured. Use publisher add first.");
        }

        List<PublisherProfile> selected = [];
        foreach (string id in ids)
        {
            selected.Add(configuration.Publishers.SingleOrDefault(profile => profile.Id == id) ??
                throw new InputException("Unknown publisher ID. Run publisher list to see configured IDs."));
        }

        return selected;
    }
}
