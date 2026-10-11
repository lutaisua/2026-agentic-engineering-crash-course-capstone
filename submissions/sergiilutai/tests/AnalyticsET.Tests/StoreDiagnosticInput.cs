using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

internal sealed record StoreDiagnosticInput(int SchemaVersion, string PublisherId, string ParentApplicationStoreId,
    string SubscriptionStoreId, string ExpectedProductType, DateTimeOffset ConfirmedAt)
{
    public override string ToString() => "[protected diagnostic sample]";
    internal static StoreDiagnosticInput Parse(string json, TimeProvider clock)
    {
        try
        {
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 16384) { throw Invalid(); }
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string[] fields = ["schemaVersion", "publisherId", "parentApplicationStoreId", "subscriptionStoreId", "expectedProductType", "confirmedAt"];
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != fields.Length ||
                root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Length ||
                root.EnumerateObject().Any(property => !fields.Contains(property.Name, StringComparer.Ordinal)) ||
                !root.GetProperty("schemaVersion").TryGetInt32(out int version) || version != 1) { throw Invalid(); }
            string Text(string field) => root.GetProperty(field).ValueKind == JsonValueKind.String ? root.GetProperty(field).GetString()! : throw Invalid();
            string publisher = Text("publisherId"), parent = Text("parentApplicationStoreId"), child = Text("subscriptionStoreId"), type = Text("expectedProductType");
            if (!ConfigurationValidator.IsSafeId(publisher) || !IsStoreId(parent) || !IsStoreId(child) || parent == child || type != "Subscription" ||
                !Regex.IsMatch(Text("confirmedAt"), @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ||
                !DateTimeOffset.TryParse(Text("confirmedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset confirmed) ||
                confirmed.Offset != TimeSpan.Zero || confirmed <= DateTimeOffset.UnixEpoch || confirmed > clock.GetUtcNow().AddMinutes(5)) { throw Invalid(); }
            return new(version, publisher, parent, child, type, confirmed);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw Invalid(); }
    }
    internal static bool IsStoreId(string value) => value.Length is > 0 and <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    private static InputException Invalid() => new("Diagnostic sample input is invalid; inspect its private fields locally.");
}

internal sealed record DiagnosticPreflight(StoreDiagnosticInput Input, AuthorizationContext Context, string OutputRoot);

internal static class StoreDiagnosticPreflight
{
    internal static async Task<DiagnosticPreflight> LoadAsync(string configurationPath, string inputPath, string mappingPath,
        string outputRoot, IPrivateStorageGuard privacy, TimeProvider clock, CancellationToken cancellationToken)
    {
        foreach (string file in new[] { configurationPath, inputPath, mappingPath })
        {
            cancellationToken.ThrowIfCancellationRequested();
            privacy.PrepareDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            privacy.VerifyFile(file);
            if (!File.Exists(file) || new FileInfo(file).Length > 1024 * 1024) { throw new InputException("Diagnostic prerequisite file is missing or too large."); }
        }
        StoreDiagnosticInput sample = StoreDiagnosticInput.Parse(await File.ReadAllTextAsync(inputPath, cancellationToken).ConfigureAwait(false), clock);
        PublisherConfiguration configuration = await new PublisherStore(configurationPath, new AtomicFileWriter(new NewFileCommitter()))
            .LoadAsync(cancellationToken).ConfigureAwait(false);
        AuthorizationContext context = Select(configuration, sample);
        using JsonDocument mapping = JsonDocument.Parse(await File.ReadAllTextAsync(mappingPath, cancellationToken).ConfigureAwait(false));
        JsonElement root = mapping.RootElement;
        ConfigurationJson.VerifyProperties(root);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out JsonElement schema) ||
            !schema.TryGetInt32(out int version) || version != 1 || !root.TryGetProperty("references", out JsonElement references) ||
            references.ValueKind != JsonValueKind.Object || references.EnumerateObject().Any(property => !ConfigurationValidator.IsSafeId(property.Name)) ||
            !references.TryGetProperty(context.CredentialReference, out JsonElement reference) ||
            reference.ValueKind != JsonValueKind.Object || !reference.TryGetProperty("target", out JsonElement target) ||
            target.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(target.GetString()) ||
            target.GetString()!.Any(char.IsControl) || target.GetString()!.Length > 32767 || target.GetString() != target.GetString()!.Trim() ||
            (reference.TryGetProperty("generation", out JsonElement generation) &&
                (generation.ValueKind != JsonValueKind.String || !ConfigurationValidator.IsSafeId(generation.GetString()))))
        { throw new InputException("The selected diagnostic credential reference is not configured."); }
        return new(sample, context, privacy.PrepareDirectory(outputRoot));
    }

    internal static AuthorizationContext Select(PublisherConfiguration configuration, StoreDiagnosticInput sample)
    {
        ConfigurationValidator.Validate(configuration);
        PublisherProfile? profile = configuration.Publishers.SingleOrDefault(profile => profile.Id == sample.PublisherId);
        if (configuration.SchemaVersion != 2 || profile is null || !profile.Accounts.TryGetValue(ApiResources.StoreProgram, out ProgramScope? scope) ||
            scope.Discovery?.Enabled != true) { throw new InputException("The selected diagnostic publisher requires enabled schema 2 Store inventory."); }
        return AuthorizationContext.Create(profile, ApiResources.StoreProgram, ApiResources.StoreInventory);
    }
}
