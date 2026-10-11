using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Publishers;

namespace Core.AnalyticsET.Authentication;

// Describes a reference without resolving or exposing a credential value.
internal sealed record CredentialReference(string Id, string ContextKey);

internal interface ICredentialReferenceInspector
{
    CredentialReference Describe(AuthorizationContext context);
}

internal sealed class OfflineCredentialReferenceInspector : ICredentialReferenceInspector
{
    public CredentialReference Describe(AuthorizationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!ConfigurationValidator.IsSafeId(context.CredentialReference))
        {
            throw new InputException("Invalid credential reference. Use a reference ID, never a credential value.");
        }

        return new(context.CredentialReference, context.StorageKey);
    }
}
