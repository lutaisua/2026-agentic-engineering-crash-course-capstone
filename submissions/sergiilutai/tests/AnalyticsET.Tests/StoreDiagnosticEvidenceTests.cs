using System.Net;
using System.Text.Json;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class StoreDiagnosticEvidenceTests
{
    [TestMethod]
    public async Task KnownTypeErrorPreservesAssociatedFieldsAndKeepsPrivateCorrelationsOutOfPublicProjection()
    {
        using StringContent content = new("""
            {"code":"NotAllowed","target":"productType","message":"API does not support in-app-product type: Subscription",
             "correlationId":"11111111-2222-3333-4444-555555555555","token":"PRIVATE-CANARY"}
            """);
        StoreDiagnosticErrorSummary summary = await StoreDiagnosticErrorSummary.ReadAsync(content, default);
        Assert.AreEqual("NotAllowed", summary.Code);
        Assert.AreEqual("productType", summary.Records.Single().Target.Value);
        Assert.AreEqual("Subscription", summary.Records.Single().ProductType.Value);
        Assert.AreEqual("known-message-pattern", summary.Records.Single().TypeSource);
        Assert.IsTrue(summary.MentionsUnsupported);
        Assert.AreEqual("present", summary.BodyCorrelations.Single().State);
        string publicJson = JsonSerializer.Serialize(summary);
        Assert.IsFalse(publicJson.Contains("PRIVATE-CANARY", StringComparison.Ordinal));
        Assert.IsFalse(publicJson.Contains("11111111-2222", StringComparison.Ordinal));
        Assert.IsFalse(publicJson.Contains("API does not", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ErrorRecordsDoNotMergeAcrossNodesAndRejectUnknownSuffixesAndDuplicateFields()
    {
        using StringContent content = new("""
            {"error":{"code":"notallowed","innererror":{"target":"productType","productType":"ManagedConsumable"}},
             "details":[{"code":"InvalidPrivateIdentifier","target":"PRIVATE-CANARY"}]}
            """);
        StoreDiagnosticErrorSummary summary = await StoreDiagnosticErrorSummary.ReadAsync(content, default);
        DiagnosticErrorRecord outer = summary.Records.Single(record => record.Code.Value == "notallowed");
        Assert.AreEqual("absent", outer.Target.State);
        Assert.AreEqual("absent", outer.ProductType.State);
        Assert.IsTrue(summary.Records.Any(record => record.ProductType.Value == "ManagedConsumable" && record.Code.State == "absent"));
        Assert.IsFalse(JsonSerializer.Serialize(summary).Contains("PRIVATE", StringComparison.Ordinal));
        using StringContent duplicate = new("""{"code":"NotAllowed","CODE":"BadRequest","target":"productType"}""");
        Assert.AreEqual("ambiguous", (await StoreDiagnosticErrorSummary.ReadAsync(duplicate, default)).Records.Single().Code.State);
    }

    [TestMethod]
    public async Task AmbiguousTypesRecordAndDepthLimitsRemainExplicit()
    {
        using StringContent conflict = new("""{"code":"NotAllowed","productType":"Durable","message":"API does not support in-app-product type: Subscription"}""");
        Assert.AreEqual("ambiguous", (await StoreDiagnosticErrorSummary.ReadAsync(conflict, default)).Records.Single().ProductType.State);
        using StringContent many = new(JsonSerializer.Serialize(new { details = Enumerable.Range(0, 10).Select(_ => new { code = "BadRequest" }) }));
        StoreDiagnosticErrorSummary bounded = await StoreDiagnosticErrorSummary.ReadAsync(many, default);
        Assert.AreEqual(8, bounded.Records.Count);
        CollectionAssert.Contains(bounded.Limitations.ToArray(), "error-record-limit");
        string nested = "{\"code\":\"NotAllowed\"}";
        for (int index = 0; index < 10; index++) { nested = "{\"innererror\":" + nested + "}"; }
        using StringContent deep = new(nested);
        CollectionAssert.Contains((await StoreDiagnosticErrorSummary.ReadAsync(deep, default)).Limitations.ToArray(), "error-depth-limit");
    }

    [TestMethod]
    public void CorrelationsAreValidatedAndRejectInjectionConflictsAndCounterOverflow()
    {
        using HttpResponseMessage first = new(HttpStatusCode.BadRequest);
        first.Headers.TryAddWithoutValidation("Request-ID", "11111111-2222-3333-4444-555555555555");
        first.Headers.TryAddWithoutValidation("MS-CV", "AAAAAAAAAAAAAAAAAAAAAA.0.1!");
        first.Headers.TryAddWithoutValidation("Authorization", "PRIVATE-CANARY");
        IReadOnlyList<DiagnosticCorrelation> captured = DiagnosticCorrelationCapture.Read(first);
        Assert.AreEqual(2, captured.Count(field => field.State == "present"));
        using HttpResponseMessage next = new(HttpStatusCode.BadRequest);
        next.Headers.TryAddWithoutValidation("Request-ID", new[] { "11111111-2222-3333-4444-555555555555", "22222222-2222-3333-4444-555555555555" });
        next.Headers.TryAddWithoutValidation("X-Correlation-ID", "PRIVATE-CANARY");
        next.Headers.TryAddWithoutValidation("MS-CV", "AAAAAAAAAAAAAAAAAAAAAA.4294967296");
        next.Headers.TryAddWithoutValidation("X-MS-Request-ID", new string('x', 129));
        IReadOnlyList<DiagnosticCorrelation> rejected = DiagnosticCorrelationCapture.Read(next);
        Assert.AreEqual("ambiguous", rejected.Single(field => field.Field == "Request-ID").State);
        Assert.AreEqual("unsupported-format", rejected.Single(field => field.Field == "MS-CV").State);
        Assert.AreEqual("oversized", rejected.Single(field => field.Field == "X-MS-Request-ID").State);
        Assert.IsFalse(JsonSerializer.Serialize(rejected).Contains("PRIVATE-CANARY", StringComparison.Ordinal));
        Assert.AreEqual("present", captured.Single(field => field.Field == "Request-ID").State);
    }
}
