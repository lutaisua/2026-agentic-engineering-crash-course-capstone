using System.Net;
using System.Text.Json;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Networking;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

[TestClass]
[DoNotParallelize]
public sealed class StoreInventoryLiveDiagnostics
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("Live")]
    public async Task AssociatedPublisherWithKnownPublishedAppsHasNonemptyInventory()
    {
        string? path = Environment.GetEnvironmentVariable("ANALYTICSET_LIVE_CONFIG");
        if (string.IsNullOrWhiteSpace(path))
        {
            Assert.Inconclusive("Set ANALYTICSET_LIVE_CONFIG to the protected runtime configuration to authorize this read-only diagnostic.");
        }
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));
        WindowsPrivateStorageGuard privacy = new();
        using StatusProbeHandler probe = new();
        using HttpClient client = new(probe) { Timeout = TimeSpan.FromSeconds(60) };
        try
        {
            privacy.PrepareDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            privacy.VerifyFile(path);
            PublisherConfiguration configuration = await new PublisherStore(path, new AtomicFileWriter(new FileCommitter()))
                .LoadAsync(cancellation.Token);
            Assert.AreEqual(2, configuration.SchemaVersion, "The diagnostic requires an explicit schema 2 configuration.");
            Assert.AreEqual(1, configuration.Publishers.Count, "The diagnostic requires a single, deliberately selected publisher.");
            PublisherProfile profile = configuration.Publishers.Single();
            AuthorizationContext context = AuthorizationContext.Create(profile, ApiResources.StoreProgram, ApiResources.StoreInventory);
            ApplicationCredentialResolver credentials = new(ApplicationCredentialResolver.DefaultMappingPath, privacy, new WindowsGenericCredentialVault());
            ApplicationCredential credential = await credentials.ResolveAsync(context, cancellation.Token);
            context = context with { CredentialGeneration = credential.Generation };
            BoundedJsonHttp http = new(client, TimeProvider.System);
            ClientCredentialTokens tokens = new(http, credentials, TimeProvider.System);
            AccessToken token = await tokens.GetAsync(context, false, cancellation.Token);
            var matches = DecodedClaimMatches(token, context);
            TestContext.WriteLine(JsonSerializer.Serialize(new
            {
                tokenClaims = new { matches.Audience, matches.Tenant, matches.Application },
                claimCheck = "decoded-only-account-association-not-proved"
            }));
            if (matches.Audience == false || matches.Tenant == false || matches.Application == false)
            {
                Assert.Fail("Decoded token identity does not match the configured application/tenant/resource.");
            }

            StoreInventoryReader reader = new(new AuthorizedInventoryHttp(http, tokens));
            InventoryRequestBudget budget = new();
            DateTimeOffset observedAt = TimeProvider.System.GetUtcNow();
            IReadOnlyList<ProductRecord> apps = await reader.AppsAsync(context, observedAt, cancellation.Token, budget);
            List<ProductRecord> addons = [];
            foreach (ProductRecord app in apps)
            {
                addons.AddRange(await reader.ChildrenAsync(context, app, observedAt, cancellation.Token, budget));
            }
            TestContext.WriteLine(JsonSerializer.Serialize(new
            {
                appCount = apps.Count,
                publishedAppCount = apps.Count(app => app.Publication == PublicationState.Published),
                addonCount = addons.Count,
                publishedAddonCount = addons.Count(addon => addon.Publication == PublicationState.Published),
                unknownPublicationCount = apps.Concat(addons).Count(product => product.Publication == PublicationState.Unknown)
            }));
            Assert.IsTrue(apps.Count > 0, "The API returned an empty inventory despite the operator confirming published apps in the associated publisher account.");
        }
        catch (InventoryException error)
        {
            Assert.Fail("Read-only Store diagnostic failed: " + error.Category);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException or
            OperationCanceledException or Console.AnalyticsET.Cli.InputException)
        {
            // Do not let exception details print private configuration, request bodies, or credentials.
            Assert.Fail("Read-only Store diagnostic stopped: " + error.GetType().Name);
        }
        finally
        {
            TestContext.WriteLine(JsonSerializer.Serialize(new { httpStatuses = probe.Statuses }));
        }
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task CompareAddonCollectionsForPublishedAndUnknownApps()
    {
        string? path = Environment.GetEnvironmentVariable("ANALYTICSET_LIVE_CONFIG");
        if (string.IsNullOrWhiteSpace(path))
        {
            Assert.Inconclusive("Set ANALYTICSET_LIVE_CONFIG to authorize this read-only diagnostic.");
        }
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));
        WindowsPrivateStorageGuard privacy = new();
        using StatusProbeHandler probe = new();
        using HttpClient client = new(probe) { Timeout = TimeSpan.FromSeconds(60) };
        try
        {
            privacy.PrepareDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            privacy.VerifyFile(path);
            PublisherConfiguration configuration = await new PublisherStore(path, new AtomicFileWriter(new FileCommitter()))
                .LoadAsync(cancellation.Token);
            Assert.AreEqual(2, configuration.SchemaVersion);
            Assert.AreEqual(1, configuration.Publishers.Count, "Use a single deliberately selected publisher.");
            PublisherProfile profile = configuration.Publishers.Single();
            AuthorizationContext context = AuthorizationContext.Create(profile, ApiResources.StoreProgram, ApiResources.StoreInventory);
            ApplicationCredentialResolver credentials = new(ApplicationCredentialResolver.DefaultMappingPath, privacy, new WindowsGenericCredentialVault());
            ApplicationCredential credential = await credentials.ResolveAsync(context, cancellation.Token);
            context = context with { CredentialGeneration = credential.Generation };
            BoundedJsonHttp http = new(client, TimeProvider.System);
            ClientCredentialTokens tokens = new(http, credentials, TimeProvider.System);
            AuthorizedInventoryHttp authorized = new(http, tokens);
            IReadOnlyList<ProductRecord> apps = await new StoreInventoryReader(authorized)
                .AppsAsync(context, TimeProvider.System.GetUtcNow(), cancellation.Token);
            TestContext.WriteLine(JsonSerializer.Serialize(new
            {
                appInventory = new
                {
                    count = apps.Count,
                    published = apps.Count(app => app.Publication == PublicationState.Published),
                    unknown = apps.Count(app => app.Publication == PublicationState.Unknown),
                    firstParentPublication = apps.FirstOrDefault()?.Publication.ToString()
                }
            }));
            List<object> probes = [];
            foreach (PublicationState publication in new[] { PublicationState.Published, PublicationState.Unknown })
            {
                ProductRecord? parent = apps.FirstOrDefault(app => app.Publication == publication);
                if (parent is null) { continue; }
                foreach (bool withTop in new[] { true, false })
                {
                    string category = "collection-readable";
                    int? total = null;
                    bool? hasNext = null;
                    Uri destination = new(StoreInventoryReader.BaseUri, "applications/" + Uri.EscapeDataString(parent.Identity.SourceId) +
                        "/listinappproducts" + (withTop ? "?top=10" : ""));
                    try
                    {
                        using JsonDocument? document = await authorized.GetAsync(context, destination, StoreInventoryReader.BaseUri,
                            true, cancellation.Token);
                        if (document is null) { category = "documented-empty-collection"; total = 0; hasNext = false; }
                        else if (document.RootElement.TryGetProperty("totalCount", out JsonElement count) && count.TryGetInt32(out int number))
                        {
                            total = number;
                            hasNext = document.RootElement.TryGetProperty("@nextLink", out JsonElement next) && next.ValueKind == JsonValueKind.String;
                        }
                        else { category = "unrecognized-collection-shape"; }
                    }
                    catch (InventoryException error) { category = error.Category; }
                    probes.Add(new { parentPublication = publication.ToString(), query = withTop ? "top=10" : "default", category, total, hasNext });
                }
            }
            TestContext.WriteLine(JsonSerializer.Serialize(new { childCollectionProbes = probes }));
            List<object> accountProbes = [];
            foreach (bool withTop in new[] { true, false })
            {
                string category = "collection-readable";
                int? total = null;
                bool? hasNext = null;
                try
                {
                    using JsonDocument? document = await authorized.GetAsync(context,
                        new(StoreInventoryReader.BaseUri, "inappproducts" + (withTop ? "?top=10" : "")),
                        StoreInventoryReader.BaseUri, true, cancellation.Token);
                    if (document is null) { category = "documented-empty-collection"; total = 0; hasNext = false; }
                    else if (document.RootElement.TryGetProperty("totalCount", out JsonElement count) && count.TryGetInt32(out int number))
                    {
                        total = number;
                        hasNext = document.RootElement.TryGetProperty("@nextLink", out JsonElement next) && next.ValueKind == JsonValueKind.String;
                    }
                    else { category = "unrecognized-collection-shape"; }
                }
                catch (InventoryException error) { category = error.Category; }
                accountProbes.Add(new { query = withTop ? "top=10" : "default", category, total, hasNext });
            }
            TestContext.WriteLine(JsonSerializer.Serialize(new { accountAddonProbes = accountProbes }));
        }
        catch (Exception error) when (error is InventoryException or IOException or UnauthorizedAccessException or HttpRequestException or
            OperationCanceledException or Console.AnalyticsET.Cli.InputException)
        {
            Assert.Fail("Read-only parent probe stopped: " + error.GetType().Name);
        }
        finally { TestContext.WriteLine(JsonSerializer.Serialize(new { httpStatuses = probe.Statuses })); }
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task CompareKnownSubscriptionCollectionsAndDetail()
    {
        string? configuration = Environment.GetEnvironmentVariable("ANALYTICSET_LIVE_CONFIG");
        string? input = Environment.GetEnvironmentVariable("ANALYTICSET_DIAGNOSTIC_INPUT");
        string? output = Environment.GetEnvironmentVariable("ANALYTICSET_DIAGNOSTIC_OUTPUT");
        if (string.IsNullOrWhiteSpace(configuration) || string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
        { Assert.Inconclusive("Explicit protected configuration/input/output paths are required for the Subscription diagnostic."); }
        try
        {
            WindowsPrivateStorageGuard guard = new();
            using HttpClientHandler inner = new() { AllowAutoRedirect = false };
            ApplicationCredentialResolver credentials = new(ApplicationCredentialResolver.DefaultMappingPath, guard, new WindowsGenericCredentialVault());
            DiagnosticExecution result = await StoreSubscriptionDiagnostic.RunAsync(configuration, input, ApplicationCredentialResolver.DefaultMappingPath,
                output, guard, credentials, inner, TimeProvider.System, new AtomicFileWriter(new NewFileCommitter()), CancellationToken.None);
            TestContext.WriteLine("ANALYTICSET_DIAGNOSTIC_RESULT " + JsonSerializer.Serialize(new
            { exitCode = result.Packet.ExitCode, packetDirectory = result.Directory, summary = result.Packet.PublicProjection() }));
        }
        catch (Exception error)
        {
            // Never let framework failure messages include request URLs, JSON, IDs or vault details.
            TestContext.WriteLine("ANALYTICSET_DIAGNOSTIC_RESULT " + JsonSerializer.Serialize(new
            { exitCode = error is OperationCanceledException ? 130 : 1, category = "diagnostic-prerequisite-or-recording-failed" }));
            Assert.Fail("Subscription diagnostic did not record a packet. Inspect private prerequisites locally.");
        }
    }

    private static (bool? Audience, bool? Tenant, bool? Application) DecodedClaimMatches(AccessToken token, AuthorizationContext context)
    {
        string[] segments = token.Value.Split('.');
        if (segments.Length != 3 || segments[1].Length > 65536) { return (null, null, null); }
        try
        {
            string encoded = segments[1].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            using JsonDocument payload = JsonDocument.Parse(Convert.FromBase64String(encoded));
            JsonElement root = payload.RootElement;
            bool? audience = root.TryGetProperty("aud", out JsonElement aud) && aud.ValueKind == JsonValueKind.String ?
                aud.GetString() == context.Resource : null;
            return (audience, GuidMatch(root, "tid", context.TenantId), GuidMatch(root, "appid", context.ClientId));
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException)
        {
            return (null, null, null);
        }
    }

    private static bool? GuidMatch(JsonElement root, string name, string expected) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out Guid actual) ?
            actual == Guid.Parse(expected) : null;

    private sealed class StatusProbeHandler : DelegatingHandler
    {
        internal List<object> Statuses { get; } = [];
        internal StatusProbeHandler() : base(new HttpClientHandler { AllowAutoRedirect = false }) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            Uri destination = request.RequestUri!;
            string operation = destination.Host == "login.microsoftonline.com" ? "token" :
                destination.AbsolutePath.TrimEnd('/') == "/v1.0/my/applications" ? "app-collection" :
                destination.AbsolutePath.TrimEnd('/') == "/v1.0/my/inappproducts" ? "addon-global-collection" :
                destination.AbsolutePath.EndsWith("/listinappproducts", StringComparison.Ordinal) ? "addon-collection" : "addon-detail";
            StoreDiagnosticErrorSummary? error = response.IsSuccessStatusCode ? null :
                await StoreDiagnosticErrorSummary.ReadAsync(response.Content, cancellationToken);
            Statuses.Add(new { operation, status = (int)response.StatusCode, error });
            return response;
        }
    }
}
