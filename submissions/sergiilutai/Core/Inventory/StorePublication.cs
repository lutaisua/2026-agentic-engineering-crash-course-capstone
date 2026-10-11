using System.Text.Json;

namespace Core.AnalyticsET.Inventory;

internal static class StorePublication
{
    internal static (PublicationState State, string? Evidence) Classify(JsonElement resource, string sourceId, bool addon)
    {
        string field = addon ? "lastPublishedInAppProductSubmission" : "lastPublishedApplicationSubmission";
        string prefix = (addon ? "inappproducts/" : "applications/") + sourceId + "/submissions/";
        if (resource.TryGetProperty(field, out JsonElement published) && published.ValueKind == JsonValueKind.Object &&
            published.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String &&
            published.TryGetProperty("resourceLocation", out JsonElement location) && location.ValueKind == JsonValueKind.String)
        {
            string? submission = id.GetString();
            if (!string.IsNullOrWhiteSpace(submission) && submission.All(char.IsAsciiDigit) && location.GetString() == prefix + submission)
            {
                return (PublicationState.Published, field + ":" + submission);
            }
        }
        // The resource docs do not define absent/null fields as proof that no publication ever occurred.
        // Neither a pending submission nor a placeholder/real firstPublishedDate closes that gap.
        return (PublicationState.Unknown, null);
    }
}
