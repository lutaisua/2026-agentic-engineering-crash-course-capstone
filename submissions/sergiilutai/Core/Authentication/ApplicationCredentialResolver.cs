using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;

namespace Core.AnalyticsET.Authentication;

internal sealed class ApplicationCredential(string secretValue, string generation)
{
    [JsonIgnore]
    public string SecretValue { get; } = secretValue;
    public string Generation { get; } = generation;
    public override string ToString() => "[protected application credential]";
}

internal sealed class VaultCredential(string secretValue, string version)
{
    [JsonIgnore]
    public string SecretValue { get; } = secretValue;
    public string Version { get; } = version;
    public override string ToString() => "[protected Windows credential]";
}

internal interface ICredentialVault
{
    VaultCredential Read(string target);
}

internal interface IApplicationCredentialResolver
{
    Task<ApplicationCredential> ResolveAsync(AuthorizationContext context, CancellationToken cancellationToken);
}

internal sealed class ApplicationCredentialResolver(string mappingPath, IPrivateStorageGuard privacy, ICredentialVault vault)
    : IApplicationCredentialResolver
{
    private readonly string mappingPath = Path.GetFullPath(mappingPath ?? throw new ArgumentNullException(nameof(mappingPath)));
    private readonly IPrivateStorageGuard privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
    private readonly ICredentialVault vault = vault ?? throw new ArgumentNullException(nameof(vault));

    internal static string DefaultMappingPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".analyticset", "credentials", "references.json");

    public async Task<ApplicationCredential> ResolveAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ConfigurationValidator.IsSafeId(context.CredentialReference) || !File.Exists(mappingPath))
        {
            throw new InputException("Credential reference mapping is missing or invalid. Configure the private references file.");
        }

        privacy.PrepareDirectory(Path.GetDirectoryName(mappingPath)!);
        privacy.VerifyFile(mappingPath);
        CredentialMappings mappings = ConfigurationJson.Deserialize<CredentialMappings>(
            await File.ReadAllTextAsync(mappingPath, cancellationToken).ConfigureAwait(false));
        if (mappings.SchemaVersion != 1 || mappings.References is null ||
            mappings.References.Keys.Any(key => !ConfigurationValidator.IsSafeId(key)))
        {
            throw new InputException("Credential mappings require schemaVersion 1 and safe reference IDs.");
        }

        if (!mappings.References.TryGetValue(context.CredentialReference, out CredentialTarget? reference) || reference is null ||
            string.IsNullOrWhiteSpace(reference.Target) || reference.Target != reference.Target.Trim() ||
            reference.Target.Length > 32767 || reference.Target.Any(char.IsControl) ||
            !ConfigurationValidator.IsSafeId(reference.Generation))
        {
            throw new InputException("Credential reference is not mapped to a valid Windows generic credential.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        VaultCredential credential = vault.Read(reference.Target);
        if (string.IsNullOrWhiteSpace(credential.SecretValue))
        {
            throw new InputException("Configured Windows credential has no application secret value.");
        }

        string generation = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new[] { reference.Target, reference.Generation, credential.Version })));
        return new(credential.SecretValue, generation);
    }

    private sealed record CredentialMappings
    {
        public int SchemaVersion { get; init; }
        public Dictionary<string, CredentialTarget>? References { get; init; }
    }

    private sealed record CredentialTarget
    {
        public string Target { get; init; } = "";
        public string Generation { get; init; } = "1";
    }
}
