using System.Text.Json;
using Console.AnalyticsET.Cli;

namespace Core.AnalyticsET.Publishers;

internal static class ConfigurationJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false
    };

    internal static T Deserialize<T>(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InputException("Configuration must contain a JSON object.");
            }

            VerifyProperties(document.RootElement);
            // The document version must be explicit, even though new configurations default to version 1.
            if (typeof(T) == typeof(PublisherConfiguration) &&
                (!document.RootElement.TryGetProperty("schemaVersion", out _) ||
                 !document.RootElement.TryGetProperty("publishers", out _)))
            {
                throw new InputException("Configuration must declare schemaVersion and a publishers array.");
            }

            return JsonSerializer.Deserialize<T>(json, Options) ??
                throw new InputException("Configuration must contain a JSON object.");
        }
        catch (JsonException)
        {
            // Parser details can include user input; never echo them.
            throw new InputException("Invalid configuration JSON. Check the documented schema; do not include credentials.");
        }
    }

    internal static void VerifyProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                string normalized = property.Name.Replace("_", "", StringComparison.Ordinal)
                    .Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
                if (normalized is "secret" or "secretid" or "secretvalue" or "clientsecret" or "password" or "accesstoken" or
                    "refreshtoken" or "privatekey" or "token" or "credentialvalue")
                {
                    throw new InputException("Credential values are prohibited in profile JSON. Use credentialReference.");
                }

                if (!names.Add(property.Name))
                {
                    throw new InputException("Duplicate JSON properties are not allowed.");
                }

                VerifyProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in value.EnumerateArray())
            {
                VerifyProperties(element);
            }
        }
    }
}
