using System.Text.Json;
using Core.AnalyticsET.Inventory;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class StorePublicationTests
{
    [TestMethod]
    public void PublishedEvidenceSurvivesPendingDraftAndDoesNotDependOnFirstPublishedDate()
    {
        using JsonDocument app = JsonDocument.Parse("""
            {"firstPublishedDate":"1601-01-01T00:00:00Z",
             "lastPublishedApplicationSubmission":{"id":"123","resourceLocation":"applications/fictional-app/submissions/123"},
             "pendingApplicationSubmission":{"id":"456"}}
            """);
        StorePublication.Classify(app.RootElement, "fictional-app", false).State.Should().Be(PublicationState.Published);
    }

    [TestMethod]
    public void MissingNullMalformedAndWrongParentPublicationAreUnknownAndChildrenNeedOwnProof()
    {
        foreach (string json in new[] { "{}", "{\"lastPublishedApplicationSubmission\":null}",
            "{\"firstPublishedDate\":\"2026-01-01T00:00:00Z\",\"pendingApplicationSubmission\":{\"id\":\"123\"}}",
            "{\"lastPublishedApplicationSubmission\":{\"id\":\"123\",\"resourceLocation\":\"applications/wrong/submissions/123\"}}" })
        {
            using JsonDocument app = JsonDocument.Parse(json);
            StorePublication.Classify(app.RootElement, "fictional-app", false).Should().Be((PublicationState.Unknown, (string?)null));
        }
        using JsonDocument child = JsonDocument.Parse("""
            {"lastPublishedApplicationSubmission":{"id":"123","resourceLocation":"applications/fictional-app/submissions/123"},
             "pendingInAppProductSubmission":{"id":"456"}}
            """);
        StorePublication.Classify(child.RootElement, "fictional-child", true).State.Should().Be(PublicationState.Unknown);
        using JsonDocument publishedChild = JsonDocument.Parse("""
            {"lastPublishedInAppProductSubmission":{"id":"789","resourceLocation":"inappproducts/fictional-child/submissions/789"}}
            """);
        StorePublication.Classify(publishedChild.RootElement, "fictional-child", true).State.Should().Be(PublicationState.Published);
    }
}
