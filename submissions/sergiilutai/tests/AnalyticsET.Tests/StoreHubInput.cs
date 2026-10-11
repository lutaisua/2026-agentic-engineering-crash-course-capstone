using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

internal sealed record StoreHubWindow(string StartUtc, string EndUtc)
{
    internal void Validate(TimeProvider clock)
    {
        DateTimeOffset Parse(string text) => DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset value) ?
            value : throw StoreHubInput.Invalid();
        DateTimeOffset start = Parse(StartUtc), end = Parse(EndUtc);
        if (start <= DateTimeOffset.UnixEpoch || start.TimeOfDay != TimeSpan.Zero || end.TimeOfDay != TimeSpan.Zero ||
            end > clock.GetUtcNow() || end - start < TimeSpan.FromDays(1) || end - start > TimeSpan.FromDays(7))
        { throw StoreHubInput.Invalid(); }
    }
}

internal sealed record StoreHubInput(int SchemaVersion, string ExperimentId, string PublisherId,
    string ParentApplicationStoreId, string SubscriptionStoreId, string ExpectedProductType,
    bool AppPublishedConfirmed, bool SubscriptionParentConfirmed, string ConfirmedAt,
    StoreHubWindow InstallsWindow, StoreHubWindow AddOnAcquisitionsWindow)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public override string ToString() => "[protected Store reporting experiment]";

    internal static StoreHubInput Parse(string json, TimeProvider clock)
    {
        try
        {
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 16384) { throw Invalid(); }
            using JsonDocument document = JsonDocument.Parse(json);
            ExactFields(document.RootElement, ["schemaVersion", "experimentId", "publisherId", "parentApplicationStoreId",
                "subscriptionStoreId", "expectedProductType", "appPublishedConfirmed", "subscriptionParentConfirmed", "confirmedAt",
                "installsWindow", "addOnAcquisitionsWindow"]);
            foreach (string field in new[] { "installsWindow", "addOnAcquisitionsWindow" })
            { ExactFields(document.RootElement.GetProperty(field), ["startUtc", "endUtc"]); }
            StoreHubInput input = JsonSerializer.Deserialize<StoreHubInput>(json, JsonOptions) ?? throw Invalid();
            if (input.SchemaVersion != 1 || !Guid.TryParseExact(input.ExperimentId, "D", out Guid id) || id == Guid.Empty ||
                input.ExperimentId != id.ToString("D") || !ConfigurationValidator.IsSafeId(input.PublisherId) ||
                string.IsNullOrEmpty(input.ParentApplicationStoreId) || string.IsNullOrEmpty(input.SubscriptionStoreId) ||
                !StoreDiagnosticInput.IsStoreId(input.ParentApplicationStoreId) || !StoreDiagnosticInput.IsStoreId(input.SubscriptionStoreId) ||
                string.Equals(input.ParentApplicationStoreId, "all", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input.SubscriptionStoreId, "all", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input.ParentApplicationStoreId, input.SubscriptionStoreId, StringComparison.OrdinalIgnoreCase) ||
                input.ExpectedProductType != "Subscription" || !input.AppPublishedConfirmed || !input.SubscriptionParentConfirmed ||
                !DateTimeOffset.TryParseExact(input.ConfirmedAt, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset confirmed) ||
                confirmed <= DateTimeOffset.UnixEpoch || confirmed > clock.GetUtcNow().AddMinutes(5) ||
                input.InstallsWindow is null || input.AddOnAcquisitionsWindow is null) { throw Invalid(); }
            input.InstallsWindow.Validate(clock);
            input.AddOnAcquisitionsWindow.Validate(clock);
            return input;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { throw Invalid(); }
    }

    internal static void ExactFields(JsonElement value, string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != names.Length ||
            value.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length ||
            value.EnumerateObject().Any(property => !names.Contains(property.Name, StringComparer.Ordinal))) { throw Invalid(); }
    }
    internal static InputException Invalid() => new("Invalid private Store reporting prerequisites. Check fields locally.");
}

internal sealed record StoreHubBinding(int SchemaVersion, string InputSha256, string ConfigurationKey, string CredentialGeneration,
    string ContractSha256);
internal sealed record StoreHubPreflight(StoreHubInput Input, AuthorizationContext Context, string ExperimentDirectory, string InputSha256,
    StoreHubBinding? PreviousBinding)
{
    internal const string ContractSha256 = "da38b521fb5b2d5bfda47e11adcb06214801126ecc446bc2ab91da25ae1b3742";

    internal static AuthorizationContext Select(PublisherConfiguration configuration, StoreHubInput input)
    {
        ConfigurationValidator.Validate(configuration);
        PublisherProfile? selected = configuration.Publishers.SingleOrDefault(profile => profile.Id == input.PublisherId);
        if (configuration.SchemaVersion != 2 || selected is null) { throw StoreHubInput.Invalid(); }
        // Analytics and submission share an audience, but their explicit bindings are distinct.
        return AuthorizationContext.Create(selected, ApiResources.StoreProgram, ApiResources.Store);
    }

    internal static async Task<StoreHubPreflight> LoadAsync(string configurationPath, string inputPath, string mappingPath,
        string experimentDirectory, IPrivateStorageGuard privacy, TimeProvider clock, CancellationToken cancellationToken)
    {
        string directory = privacy.PrepareDirectory(experimentDirectory);
        foreach (string file in new[] { configurationPath, inputPath, mappingPath })
        {
            cancellationToken.ThrowIfCancellationRequested();
            privacy.PrepareDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            privacy.VerifyFile(file);
            if (!File.Exists(file) || new FileInfo(file).Length > 1024 * 1024) { throw StoreHubInput.Invalid(); }
        }
        byte[] inputBytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
        StoreHubInput input = StoreHubInput.Parse(new System.Text.UTF8Encoding(false, true).GetString(inputBytes), clock);
        if (Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)) != input.ExperimentId ||
            Path.GetFullPath(inputPath) != Path.Combine(directory, "input.json")) { throw StoreHubInput.Invalid(); }
        PublisherConfiguration configuration = await new PublisherStore(configurationPath, new AtomicFileWriter(new NewFileCommitter()))
            .LoadAsync(cancellationToken).ConfigureAwait(false);
        AuthorizationContext context = Select(configuration, input);
        using JsonDocument mapping = JsonDocument.Parse(await File.ReadAllTextAsync(mappingPath, cancellationToken).ConfigureAwait(false));
        ConfigurationJson.VerifyProperties(mapping.RootElement);
        StoreHubInput.ExactFields(mapping.RootElement, ["schemaVersion", "references"]);
        if (!mapping.RootElement.GetProperty("schemaVersion").TryGetInt32(out int version) || version != 1) { throw StoreHubInput.Invalid(); }
        JsonElement references = mapping.RootElement.GetProperty("references");
        if (references.ValueKind != JsonValueKind.Object || !references.TryGetProperty(context.CredentialReference, out JsonElement reference))
        { throw StoreHubInput.Invalid(); }
        if (reference.ValueKind != JsonValueKind.Object || reference.EnumerateObject().Any(field => field.Name is not ("target" or "generation")) ||
            !reference.TryGetProperty("target", out JsonElement target) || target.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(target.GetString()) || target.GetString()!.Length > 32767 ||
            target.GetString() != target.GetString()!.Trim() || target.GetString()!.Any(char.IsControl) ||
            (reference.TryGetProperty("generation", out JsonElement generation) && (generation.ValueKind != JsonValueKind.String ||
                !ConfigurationValidator.IsSafeId(generation.GetString())))) { throw StoreHubInput.Invalid(); }
        string hash = StoreDiagnosticPacketStore.Hash(inputBytes);
        StoreHubBinding? previous = null;
        string bindingPath = Path.Combine(directory, "binding.json");
        if (File.Exists(bindingPath))
        {
            privacy.VerifyFile(bindingPath);
            if (new FileInfo(bindingPath).Length > 8192) { throw StoreHubInput.Invalid(); }
            string json = await File.ReadAllTextAsync(bindingPath, cancellationToken).ConfigureAwait(false);
            using JsonDocument binding = JsonDocument.Parse(json);
            StoreHubInput.ExactFields(binding.RootElement, ["schemaVersion", "inputSha256", "configurationKey", "credentialGeneration", "contractSha256"]);
            previous = JsonSerializer.Deserialize<StoreHubBinding>(json, StoreHubInput.JsonOptions) ?? throw StoreHubInput.Invalid();
            if (previous.SchemaVersion != 1 || previous.InputSha256 != hash || previous.ConfigurationKey != context.ConfigurationKey ||
                previous.ContractSha256 != ContractSha256 || string.IsNullOrWhiteSpace(previous.CredentialGeneration)) { throw StoreHubInput.Invalid(); }
        }
        return new(input, context, directory, hash, previous);
    }
}
