using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AnalyticsET.Tests;

internal sealed record DiagnosticField(string State, string? Value = null);
internal sealed record DiagnosticErrorRecord(string Path, DiagnosticField Code, DiagnosticField Target,
    DiagnosticField ProductType, string? TypeSource);

internal sealed record StoreDiagnosticErrorSummary(string Code, bool MentionsApplication, bool MentionsSubmission,
    bool MentionsPublication, bool MentionsParameter, bool MentionsTop, bool MentionsUnsupported, bool MentionsAppType)
{
    internal const int MaximumBytes = 16384;
    internal static readonly string[] Codes = ["NotAllowed", "BadRequest", "InvalidState", "InvalidIapProductType",
        "InvalidApplicationType", "InAppProductsNotSupported", "NotFound", "NotSupported", "Unsupported", "Unauthorized", "Forbidden"];
    internal static readonly string[] Targets = ["productType", "applicationId", "inAppProductId", "top", "skip"];
    internal static readonly string[] Types = ["Subscription", "Durable", "Consumable", "ManagedConsumable", "NotSet"];
    public IReadOnlyList<DiagnosticErrorRecord> Records { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
    [JsonIgnore] public IReadOnlyList<DiagnosticCorrelation> BodyCorrelations { get; init; } = [];
    private static readonly Regex TypeMessage = new(@"^API does not support in-app-product type:\s*(Subscription|Durable|Consumable|ManagedConsumable|NotSet)\.?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static async Task<StoreDiagnosticErrorSummary> ReadAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumBytes) { return Empty("error-body-too-large"); }
        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream buffer = new();
        byte[] block = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(block, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > MaximumBytes) { return Empty("error-body-too-large"); }
            buffer.Write(block, 0, count);
        }
        return Parse(buffer.ToArray());
    }

    internal static StoreDiagnosticErrorSummary Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaximumBytes) { return Empty("error-body-too-large"); }
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            List<(JsonElement Value, string Path)> nodes = [];
            HashSet<string> limitations = [];
            Collect(document.RootElement, "$", 0, nodes, limitations);
            List<DiagnosticErrorRecord> records = [];
            List<DiagnosticCorrelation> correlations = [];
            List<string> messages = [];
            foreach (var node in nodes.Where(node => node.Value.ValueKind == JsonValueKind.Object))
            {
                bool duplicate = node.Value.EnumerateObject().GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1);
                foreach (string name in new[] { "correlationId", "requestId" })
                {
                    JsonElement value = Property(node.Value, name);
                    if (value.ValueKind != JsonValueKind.Undefined)
                    {
                        correlations.Add(duplicate ? new("body:" + node.Path + "." + name, "ambiguous") :
                        DiagnosticCorrelationCapture.Value("body:" + node.Path + "." + name, value));
                    }
                }
                JsonElement code = Property(node.Value, "code"), target = Property(node.Value, "target"), type = Property(node.Value, "productType");
                JsonElement message = Property(node.Value, "message"), description = Property(node.Value, "description");
                if (code.ValueKind == JsonValueKind.Undefined && target.ValueKind == JsonValueKind.Undefined &&
                    type.ValueKind == JsonValueKind.Undefined && message.ValueKind == JsonValueKind.Undefined && description.ValueKind == JsonValueKind.Undefined)
                { continue; }
                if (records.Count == 8) { limitations.Add("error-record-limit"); continue; }
                if (duplicate)
                {
                    limitations.Add("duplicate-error-field");
                    records.Add(new(node.Path, new("ambiguous"), new("ambiguous"), new("ambiguous"), null));
                    continue;
                }
                DiagnosticField typeField = Allowed(type, Types);
                string? typeSource = typeField.State == "present" ? "productType-field" : null;
                List<string> patternedTypes = [];
                foreach (JsonElement text in new[] { message, description }.Where(value => value.ValueKind == JsonValueKind.String))
                {
                    string bodyText = text.GetString()!;
                    messages.Add(bodyText);
                    Match match = TypeMessage.Match(bodyText);
                    if (match.Success) { patternedTypes.Add(match.Groups[1].Value); }
                }
                if (typeField.Value is not null) { patternedTypes.Add(typeField.Value); }
                if (patternedTypes.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                { typeField = new("ambiguous"); typeSource = null; limitations.Add("conflicting-product-types"); }
                else if (typeField.State == "absent" && patternedTypes.Count > 0)
                { typeField = new("present", patternedTypes[0]); typeSource = "known-message-pattern"; }
                records.Add(new(node.Path, Allowed(code, Codes), Allowed(target, Targets), typeField, typeSource));
            }
            string[] tuples = records.Where(record => record.Code.Value is not null).Select(record =>
                record.Code.Value + ":" + record.Target.Value + ":" + record.ProductType.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (tuples.Length > 1) { limitations.Add("multiple-error-records"); }
            string safeCode = tuples.Length > 1 ? "ambiguous-service-error" :
                records.Select(record => record.Code.Value).FirstOrDefault(value => value is not null) ?? "unrecognized-service-code";
            string messagesInMemory = string.Join('\n', messages);
            return new(safeCode, Has(messagesInMemory, "application") || Has(messagesInMemory, "app "), Has(messagesInMemory, "submi"),
                Has(messagesInMemory, "publish"), Has(messagesInMemory, "parameter") || Has(messagesInMemory, "query"), Has(messagesInMemory, "top"),
                Has(messagesInMemory, "unsupported") || Has(messagesInMemory, "not supported") || Has(messagesInMemory, "does not support"),
                Has(messagesInMemory, "type") || Has(messagesInMemory, "win32") || Has(messagesInMemory, "desktop") || Has(messagesInMemory, "msi") || Has(messagesInMemory, "exe"))
            {
                Records = records,
                Limitations = limitations.Order(StringComparer.Ordinal).ToArray(),
                BodyCorrelations = DiagnosticCorrelationCapture.Limit(correlations)
            };
        }
        catch (JsonException) { return Empty("unrecognized-error-body"); }
    }

    internal static DiagnosticField Allowed(JsonElement value, IReadOnlyList<string> values) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => new("absent"),
        JsonValueKind.String when values.Contains(value.GetString()!, StringComparer.OrdinalIgnoreCase) => new("present", value.GetString()),
        JsonValueKind.String => new("unrecognized"),
        _ => new("malformed")
    };
    internal static JsonElement Property(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object ? node.EnumerateObject()
        .FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value : default;
    private static bool Has(string message, string word) => message.Contains(word, StringComparison.OrdinalIgnoreCase);
    private static StoreDiagnosticErrorSummary Empty(string code) => new(code, false, false, false, false, false, false, false) { Limitations = [code] };
    private static void Collect(JsonElement value, string path, int depth, List<(JsonElement, string)> nodes, HashSet<string> limitations)
    {
        if (depth > 8) { limitations.Add("error-depth-limit"); return; }
        if (nodes.Count == 128) { limitations.Add("error-node-limit"); return; }
        nodes.Add((value, path));
        if (value.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement child in value.EnumerateArray()) { Collect(child, path + "[" + index++ + "]", depth + 1, nodes, limitations); }
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (string name in new[] { "error", "innererror", "details", "data" })
            {
                JsonElement child = Property(value, name);
                if (child.ValueKind != JsonValueKind.Undefined) { Collect(child, path + "." + name, depth + 1, nodes, limitations); }
            }
        }
    }
}

[TestClass]
public sealed class StoreDiagnosticErrorSummaryTests
{
    [TestMethod]
    public async Task ErrorSummaryRetainsKnownCodeAndSignalsWithoutEchoingPrivatePayload()
    {
        const string privateValue = "FICTIONAL-PRIVATE-VALUE";
        using StringContent content = new(JsonSerializer.Serialize(new
        {
            error = new { code = "BadRequest", message = "Application submission query parameter top: " + privateValue },
            token = privateValue
        }));
        StoreDiagnosticErrorSummary summary = await StoreDiagnosticErrorSummary.ReadAsync(content, default);
        Assert.AreEqual("BadRequest", summary.Code);
        Assert.IsTrue(summary.MentionsApplication && summary.MentionsSubmission && summary.MentionsParameter && summary.MentionsTop);
        Assert.IsFalse(JsonSerializer.Serialize(summary).Contains(privateValue, StringComparison.Ordinal));
        using StringContent untrustedCode = new(JsonSerializer.Serialize(new { code = privateValue, message = privateValue }));
        Assert.AreEqual("unrecognized-service-code", (await StoreDiagnosticErrorSummary.ReadAsync(untrustedCode, default)).Code);
    }

    [TestMethod]
    public async Task OversizedAndNonJsonErrorBodiesAreNotLogged()
    {
        using StringContent large = new(new string('x', 16385));
        Assert.AreEqual("error-body-too-large", (await StoreDiagnosticErrorSummary.ReadAsync(large, default)).Code);
        using StringContent nonJson = new("FICTIONAL-PRIVATE-HTML");
        Assert.AreEqual("unrecognized-error-body", (await StoreDiagnosticErrorSummary.ReadAsync(nonJson, default)).Code);
    }

    [TestMethod]
    public async Task NestedStoreErrorsAndCaseVariantsRemainSanitized()
    {
        using StringContent content = new("""
            {"code":"unknown","innererror":{"code":"InvalidApplicationType","data":[
                {"Message":"This desktop application type is not supported: FICTIONAL-PRIVATE-APP"}]}}
            """);
        StoreDiagnosticErrorSummary summary = await StoreDiagnosticErrorSummary.ReadAsync(content, default);
        Assert.AreEqual("InvalidApplicationType", summary.Code);
        Assert.IsTrue(summary.MentionsUnsupported && summary.MentionsAppType && summary.MentionsApplication);
        Assert.IsFalse(JsonSerializer.Serialize(summary).Contains("FICTIONAL-PRIVATE-APP", StringComparison.Ordinal));
        using StringContent variant = new("""{"code":"invalidIapProductType"}""");
        Assert.AreEqual("invalidIapProductType", (await StoreDiagnosticErrorSummary.ReadAsync(variant, default)).Code);
    }
}
