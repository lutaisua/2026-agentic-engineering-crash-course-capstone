using System.Net;
using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class StoreDiagnosticTransportTests
{
    internal static readonly Uri Endpoint = new("https://manage.devcenter.microsoft.com/v1.0/my/inappproducts");
    internal static readonly Uri TokenEndpoint = new("https://login.microsoftonline.com/11111111-2222-3333-4444-555555555555/oauth2/token");

    [TestMethod]
    public async Task EveryRetryRetainsItsOwnTimeAndPrivateCorrelationButPublicProjectionDoesNot()
    {
        int calls = 0;
        Clock clock = new();
        using StoreDiagnosticTransport transport = new(new InventoryHttpTests.Handler(_ =>
        {
            calls++;
            HttpResponseMessage response = new(calls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.BadRequest)
            { Content = new StringContent("{\"code\":\"NotAllowed\",\"target\":\"productType\"}") };
            response.Headers.TryAddWithoutValidation("Request-ID", calls == 1 ? "11111111-2222-3333-4444-555555555555" : "22222222-2222-3333-4444-555555555555");
            return Task.FromResult(response);
        }), TokenEndpoint, new Dictionary<string, DiagnosticRoute> { [Endpoint.AbsoluteUri] = new("account-collection", "none") }, clock);
        using HttpClient client = new(transport);
        BoundedJsonHttp http = new(client, clock, (_, _) => { clock.Now = clock.Now.AddSeconds(1); return Task.CompletedTask; });
        await Assert.ThrowsAsync<InventoryException>(async () => await http.SendAsync(() => new(HttpMethod.Get, Endpoint), StoreInventoryReader.BaseUri, false, default));
        Assert.AreEqual(2, calls);
        Assert.AreEqual(1, transport.Observations[0].Attempt);
        Assert.AreEqual(2, transport.Observations[1].Attempt);
        Assert.AreNotEqual(transport.Observations[0].StartedAt, transport.Observations[1].StartedAt);
        Assert.AreNotEqual(transport.Observations[0].Correlations.Single(item => item.Field == "Request-ID").Value,
            transport.Observations[1].Correlations.Single(item => item.Field == "Request-ID").Value);
        string publicJson = JsonSerializer.Serialize(transport.Observations.Select(item => item.PublicProjection()));
        Assert.IsFalse(publicJson.Contains("11111111", StringComparison.Ordinal));
        Assert.IsFalse(publicJson.Contains("22222222", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RejectsUnknownDestinationsAndAggregateBudgetBeforeTransmission()
    {
        InventoryHttpTests.Handler inner = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        { Content = new StringContent("{}") }));
        using StoreDiagnosticTransport transport = new(inner, TokenEndpoint,
            new Dictionary<string, DiagnosticRoute> { [Endpoint.AbsoluteUri] = new("account-collection", "none") }, TimeProvider.System, 1);
        using HttpClient client = new(transport);
        using HttpRequestMessage rejected = new(HttpMethod.Get, "https://evil.example/inappproducts");
        rejected.Headers.Authorization = new("Bearer", "PRIVATE-CANARY");
        await Assert.ThrowsAsync<InventoryException>(() => client.SendAsync(rejected));
        Assert.AreEqual(0, inner.Calls);
        using HttpResponseMessage observed = await client.GetAsync(Endpoint);
        await Assert.ThrowsAsync<InventoryException>(() => client.GetAsync(Endpoint));
        Assert.AreEqual(1, inner.Calls);
    }

    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
