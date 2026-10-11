using System.Text.Json;
using System.Text.RegularExpressions;
using Console.AnalyticsET.Cli;

namespace Core.AnalyticsET.Publishers;

internal static partial class ConfigurationValidator
{
    internal static void Validate(PublisherConfiguration configuration)
    {
        if (configuration.SchemaVersion is not (1 or 2) || configuration.Publishers is null)
        {
            throw new InputException("Unsupported configuration. Expected schemaVersion 1 or 2 and a publishers array.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (PublisherProfile profile in configuration.Publishers)
        {
            Validate(profile, configuration.SchemaVersion);
            if (!ids.Add(profile.Id))
            {
                throw new InputException("Publisher IDs must be unique.");
            }
        }

        ConfigurationJson.VerifyProperties(JsonSerializer.SerializeToElement(configuration, ConfigurationJson.Options));
    }

    internal static void Validate(PublisherProfile profile, int schemaVersion = 1)
    {
        if (schemaVersion is not (1 or 2))
        {
            throw new InputException("Unsupported profile schema version.");
        }

        if (profile is null || !IsSafeId(profile.Id))
        {
            throw new InputException("Publisher ID must be 1-64 lowercase letters/digits/hyphens and cannot be a Windows device name.");
        }

        RequireText(profile.DisplayName, "displayName", 120);
        if (!Guid.TryParse(profile.TenantId, out Guid tenant) || tenant == Guid.Empty)
        {
            throw new InputException("tenantId must be a nonempty Entra GUID.");
        }

        if (profile.Programs is null || profile.Programs.Count == 0 ||
            profile.Programs.Distinct(StringComparer.Ordinal).Count() != profile.Programs.Count ||
            profile.Programs.Any(program => program is not (ApiResources.StoreProgram or ApiResources.MarketplaceProgram)))
        {
            throw new InputException("programs must contain distinct microsoft-store and/or azure-marketplace values.");
        }

        if (profile.Accounts is null || profile.Authorization is null ||
            profile.Accounts.Keys.Any(key => !profile.Programs.Contains(key, StringComparer.Ordinal)))
        {
            throw new InputException("accounts must match enabled programs and authorization must be an object.");
        }

        foreach (string program in profile.Programs)
        {
            if (!profile.Accounts.TryGetValue(program, out ProgramScope? scope) || scope is null)
            {
                throw new InputException("Each enabled program needs account settings.");
            }

            RequireText(scope.PublisherAccountId, "publisherAccountId", 200);
            if (scope.ProductIds is null || (schemaVersion == 1 && scope.ProductIds.Count == 0) ||
                scope.ProductIds.Distinct(StringComparer.Ordinal).Count() != scope.ProductIds.Count)
            {
                throw new InputException("Each enabled program needs distinct productIds (Store IDs or Marketplace offer IDs).");
            }

            foreach (string product in scope.ProductIds)
            {
                RequireText(product, "productIds", 200);
            }

            if (schemaVersion == 1 && (scope.Discovery is not null || scope.ExportSelection is not null))
            {
                throw new InputException("Discovery settings require explicit migration to schemaVersion 2.");
            }

            if (schemaVersion == 2)
            {
                ValidateDiscoveryScope(scope, program);
            }

            string resource = program == ApiResources.StoreProgram ? ApiResources.Store : ApiResources.Marketplace;
            if (!profile.Authorization.ContainsKey(resource))
            {
                throw new InputException("Each enabled program needs its analytics authorization settings.");
            }
        }

        foreach ((string resource, AuthorizationSettings settings) in profile.Authorization)
        {
            if (!ApiResources.Audiences.ContainsKey(resource) || settings is null ||
                (schemaVersion == 1 && ApiResources.IsInventory(resource)) ||
                (ApiResources.IsInventory(resource) &&
                    !profile.Programs.Any(program => ApiResources.MatchesProgram(resource, program))))
            {
                throw new InputException("Unknown API resource in authorization.");
            }

            if (!Guid.TryParse(settings.ClientId, out Guid client) || client == Guid.Empty)
            {
                throw new InputException("clientId must be a nonempty Entra application GUID.");
            }

            RequireText(settings.AuthorizedAccountId, "authorizedAccountId", 200);
            if (!IsSafeId(settings.CredentialReference))
            {
                throw new InputException("credentialReference must be a safe reference ID, never a credential value.");
            }
        }

        ConfigurationJson.VerifyProperties(JsonSerializer.SerializeToElement(profile, ConfigurationJson.Options));
    }

    internal static int ProfileSchema(PublisherProfile profile) => profile.Accounts is not null &&
        profile.Accounts.Values.Any(scope => scope is not null &&
            (scope.Discovery is not null || scope.ExportSelection is not null || scope.ProductIds is { Count: 0 })) ? 2 : 1;

    private static void ValidateDiscoveryScope(ProgramScope scope, string program)
    {
        if (scope.ProductIds.Count != 0)
        {
            throw new InputException("Schema 2 uses exportSelection instead of productIds. Migrate existing configuration explicitly.");
        }

        if (scope.Discovery is { } discovery)
        {
            CoverageSettings? coverage = discovery.Coverage;
            if (coverage is null || coverage.ExpectedOfferTypes is null ||
                coverage.ExpectedOfferTypes.Distinct(StringComparer.Ordinal).Count() != coverage.ExpectedOfferTypes.Count)
            {
                throw new InputException("Discovery coverage needs distinct expectedOfferTypes.");
            }

            foreach (string offerType in coverage.ExpectedOfferTypes)
            {
                RequireText(offerType, "expectedOfferTypes", 100);
            }

            if (program == ApiResources.StoreProgram && coverage.ExpectedOfferTypes.Count != 0)
            {
                throw new InputException("expectedOfferTypes applies only to Marketplace discovery.");
            }

            if ((coverage.VerifiedAt is null) != (coverage.EvidenceReference is null))
            {
                throw new InputException("Coverage verification needs both verifiedAt and evidenceReference.");
            }

            if (coverage.EvidenceReference is not null && !IsSafeId(coverage.EvidenceReference))
            {
                throw new InputException("evidenceReference must be a safe private evidence reference.");
            }
        }

        HashSet<(string Kind, string SourceId, string? ParentId)> identities = [];
        foreach (ProductSelection selection in scope.ExportSelection ?? [])
        {
            if (selection is null || !IsSelectionKind(selection.Kind, program))
            {
                throw new InputException("Export selection kind must match the configured program.");
            }

            RequireText(selection.SourceId, "sourceId", 400);
            bool child = selection.Kind is "store-addon" or "marketplace-plan";
            if (child)
            {
                RequireText(selection.ParentId, "parentId", 400);
            }
            else if (selection.ParentId is not null)
            {
                throw new InputException("Only child selections have parentId.");
            }

            if (!identities.Add((selection.Kind, selection.SourceId, selection.ParentId)))
            {
                throw new InputException("Export selections must have distinct typed identities.");
            }
        }
    }

    private static bool IsSelectionKind(string kind, string program) => kind == "legacy" ||
        (program == ApiResources.StoreProgram && kind is "store-app" or "store-addon") ||
        (program == ApiResources.MarketplaceProgram && kind is "marketplace-offer" or "marketplace-plan");

    internal static bool IsSafeId(string? id) => id is not null && SafeIdPattern().IsMatch(id) &&
        !DeviceNamePattern().IsMatch(id);

    private static void RequireText(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength ||
            value != value.Trim() || value.Any(char.IsControl))
        {
            throw new InputException($"{field} is required and must be trimmed text without control characters.");
        }
    }

    [GeneratedRegex(@"\A[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdPattern();

    [GeneratedRegex(@"\A(con|prn|aux|nul|com[1-9]|lpt[1-9])\z", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceNamePattern();
}
