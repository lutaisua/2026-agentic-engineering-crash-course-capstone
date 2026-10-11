using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnalyticsET.Tests;

internal sealed record DiagnosticCorrelation(string Field, string State, string? Value = null);

internal static class DiagnosticCorrelationCapture
{
    private static readonly string[] Headers = ["X-Correlation-ID", "X-MS-Correlation-Request-ID", "X-MS-Request-ID", "Request-ID", "MS-CV"];
    private static readonly Regex Vector = new(@"^[A-Za-z0-9+/]{21}[AQgw]\.(?:[0-9]{1,10}\.)*[0-9]{1,10}!?$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static IReadOnlyList<DiagnosticCorrelation> Read(HttpResponseMessage response, IReadOnlyList<DiagnosticCorrelation>? body = null)
    {
        List<DiagnosticCorrelation> found = [];
        foreach (string name in Headers)
        {
            if (!response.Headers.TryGetValues(name, out IEnumerable<string>? values)) { found.Add(new(name, "absent")); continue; }
            string[] candidates = values.Take(9).ToArray();
            if (candidates.Length > 8) { found.Add(new(name, "excessive")); }
            else if (candidates.Distinct(StringComparer.Ordinal).Count() != 1) { found.Add(new(name, "ambiguous")); }
            else { found.Add(Value(name, candidates[0], name == "MS-CV")); }
        }
        if (body is not null) { found.AddRange(body); }
        return Limit(found);
    }
    internal static DiagnosticCorrelation Value(string name, JsonElement value) => value.ValueKind == JsonValueKind.String ?
        Value(name, value.GetString()!) : new(name, value.ValueKind == JsonValueKind.Null ? "absent" : "malformed");
    private static DiagnosticCorrelation Value(string name, string value, bool vector = false)
    {
        if (value.Length > 128) { return new(name, "oversized"); }
        bool valid = vector ? Vector.IsMatch(value) && value[(value.IndexOf('.') + 1)..].TrimEnd('!').Split('.')
            .All(counter => uint.TryParse(counter, NumberStyles.None, CultureInfo.InvariantCulture, out _)) :
            Guid.TryParseExact(value, "D", out _) || Guid.TryParseExact(value, "N", out _);
        return valid ? new(name, "present", value) : new(name, "unsupported-format");
    }
    internal static IReadOnlyList<DiagnosticCorrelation> Limit(IEnumerable<DiagnosticCorrelation> values)
    {
        DiagnosticCorrelation[] bounded = values.Take(9).ToArray();
        return bounded.Length <= 8 ? bounded : bounded.Take(7).Append(new("remaining", "correlation-field-limit")).ToArray();
    }
}
