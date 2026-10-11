using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;

namespace AnalyticsET.Tests;

internal sealed record DiagnosticRoute(string Operation, string Query);
internal sealed record DiagnosticAttempt(string Operation, string Query, int Attempt, DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt, int? Status, string? Failure, StoreDiagnosticErrorSummary? Error,
    IReadOnlyList<DiagnosticCorrelation> Correlations)
{
    internal object PublicProjection() => new { Operation, Query, Attempt, Status, Failure, serviceCode = Error?.Code };
}

internal sealed class StoreDiagnosticTransport : DelegatingHandler
{
    private readonly Uri tokenEndpoint;
    private readonly IReadOnlyDictionary<string, DiagnosticRoute> routes;
    private readonly TimeProvider clock;
    private readonly int maximumRequests;
    private readonly Dictionary<string, int> attempts = [];
    private int requestCount;
    internal List<DiagnosticAttempt> Observations { get; } = [];

    internal StoreDiagnosticTransport(HttpMessageHandler inner, Uri tokenEndpoint,
        IReadOnlyDictionary<string, DiagnosticRoute> routes, TimeProvider clock, int maximumRequests = 24) : base(inner)
    {
        if (tokenEndpoint.Scheme != "https" || tokenEndpoint.Host != "login.microsoftonline.com" ||
            tokenEndpoint.Port != 443 || tokenEndpoint.UserInfo.Length != 0 || tokenEndpoint.Query.Length != 0 || tokenEndpoint.Fragment.Length != 0 ||
            !Guid.TryParse(tokenEndpoint.AbsolutePath.Split('/').ElementAtOrDefault(1), out _) ||
            !tokenEndpoint.AbsolutePath.EndsWith("/oauth2/token", StringComparison.Ordinal) || maximumRequests < 1)
        { throw new InventoryException("invalid-diagnostic-token-destination"); }
        foreach (var route in routes)
        {
            BoundedJsonHttp.ValidateDestination(new(route.Key), StoreInventoryReader.BaseUri);
            if (route.Value.Operation is not ("parent-control" or "addon-detail" or "parent-collection" or "account-collection") ||
                route.Value.Query is not ("none" or "top=10")) { throw new InventoryException("invalid-diagnostic-operation"); }
        }
        this.tokenEndpoint = tokenEndpoint;
        this.routes = routes;
        this.clock = clock;
        this.maximumRequests = maximumRequests;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri destination = request.RequestUri ?? throw new InventoryException("diagnostic-destination-rejected");
        bool token = destination == tokenEndpoint && request.Method == HttpMethod.Post && request.Headers.Authorization is null &&
            request.Content?.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded";
        DiagnosticRoute? route = token ? new("token", "none") :
            request.Method == HttpMethod.Get && request.Content is null && routes.TryGetValue(destination.AbsoluteUri, out DiagnosticRoute? allowed) ? allowed : null;
        if (route is null) { throw new InventoryException("diagnostic-destination-rejected"); }
        if (Interlocked.Increment(ref requestCount) > maximumRequests) { throw new InventoryException("diagnostic-request-budget-exhausted"); }
        string key = route.Operation + ":" + route.Query;
        int attempt = attempts.GetValueOrDefault(key) + 1;
        attempts[key] = attempt;
        DateTimeOffset started = clock.GetUtcNow();
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            StoreDiagnosticErrorSummary? error = response.IsSuccessStatusCode ? null :
                await StoreDiagnosticErrorSummary.ReadAsync(response.Content, cancellationToken).ConfigureAwait(false);
            Observations.Add(new(route.Operation, route.Query, attempt, started, clock.GetUtcNow(), (int)response.StatusCode,
                null, error, DiagnosticCorrelationCapture.Read(response, error?.BodyCorrelations)));
            return response;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
        {
            Observations.Add(new(route.Operation, route.Query, attempt, started, clock.GetUtcNow(), response is null ? null : (int)response.StatusCode,
                error is OperationCanceledException ? "transport-cancelled" : "transport-failed", null,
                response is null ? [] : DiagnosticCorrelationCapture.Read(response)));
            response?.Dispose();
            throw;
        }
    }
}

internal sealed class SingleDiagnosticRecoveryTokens(IInventoryTokens inner) : IInventoryTokens
{
    private int renewals;
    public Task<AccessToken> GetAsync(AuthorizationContext context, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (forceRefresh && Interlocked.Increment(ref renewals) > 1) { throw new InventoryException("diagnostic-auth-recovery-exhausted"); }
        return inner.GetAsync(context, forceRefresh, cancellationToken);
    }
}
