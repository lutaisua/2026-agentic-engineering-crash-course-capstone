using System.Net;
using System.Text.Json;
using Core.AnalyticsET.Inventory;

namespace Core.AnalyticsET.Networking;

internal sealed class BoundedJsonHttp(HttpClient client, TimeProvider clock,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ??
        ((duration, cancellationToken) => Task.Delay(duration, clock, cancellationToken));

    internal static void ValidateDestination(Uri destination, Uri approvedBase)
    {
        if (!destination.IsAbsoluteUri || destination.Scheme != "https" || destination.Authority != approvedBase.Authority ||
            !destination.AbsolutePath.StartsWith(approvedBase.AbsolutePath, StringComparison.Ordinal) ||
            destination.UserInfo.Length != 0 || destination.Fragment.Length != 0)
        { throw new InventoryException("rejected-request-destination"); }
    }

    internal async Task<JsonDocument?> SendAsync(Func<HttpRequestMessage> requestFactory, Uri approvedBase,
        bool emptyOnNotFound, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { return await SendCoreAsync(requestFactory, approvedBase, emptyOnNotFound, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new InventoryException("http-timeout"); }
    }

    private async Task<JsonDocument?> SendCoreAsync(Func<HttpRequestMessage> requestFactory, Uri approvedBase,
        bool emptyOnNotFound, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using HttpRequestMessage request = requestFactory();
            ValidateDestination(request.RequestUri!, approvedBase);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400) { throw new InventoryException("redirect-rejected"); }
            if (response.RequestMessage?.RequestUri is Uri returned && returned != request.RequestUri)
            { throw new InventoryException("redirect-rejected"); }
            if (response.StatusCode == HttpStatusCode.NotFound && emptyOnNotFound) { return null; }
            if (response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode is HttpStatusCode.InternalServerError or
                HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            {
                if (attempt == 2) { throw new InventoryException("retry-budget-exhausted"); }
                TimeSpan duration = response.Headers.RetryAfter?.Delta ??
                    (response.Headers.RetryAfter?.Date is DateTimeOffset retryDate ? retryDate - clock.GetUtcNow() : TimeSpan.FromSeconds(attempt + 1));
                if (duration > TimeSpan.FromSeconds(30)) { throw new InventoryException("retry-after-exceeds-budget"); }
                await delay(duration < TimeSpan.Zero ? TimeSpan.Zero : duration, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new InventoryException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "unauthorized",
                    HttpStatusCode.Forbidden => "forbidden-or-association-missing",
                    HttpStatusCode.NotFound => "resource-not-found",
                    HttpStatusCode.Conflict => "unsupported-store-feature",
                    _ => "http-request-failed"
                });
            }
            if (response.Content.Headers.ContentLength > MaximumBytes) { throw new InventoryException("response-too-large"); }
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using MemoryStream buffer = new();
            byte[] block = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(block, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > MaximumBytes) { throw new InventoryException("response-too-large"); }
                buffer.Write(block, 0, count);
            }
            JsonDocument document;
            try { document = JsonDocument.Parse(buffer.ToArray()); }
            catch (JsonException) { throw new InventoryException("invalid-response-json"); }
            try { ValidateJson(document.RootElement); return document; }
            catch { document.Dispose(); throw; }
        }
        throw new InventoryException("retry-budget-exhausted");
    }

    private static void ValidateJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) { throw new InventoryException("duplicate-response-property"); }
                ValidateJson(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray()) { ValidateJson(child); }
        }
    }
}
