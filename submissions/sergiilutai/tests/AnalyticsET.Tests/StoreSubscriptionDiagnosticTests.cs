using System.Net;
using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Inventory;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class StoreSubscriptionDiagnosticTests
{
    [TestMethod]
    public async Task Complete400MatrixWithReadableSubscriptionSeparatesServiceAndParserAndPreservesState()
    {
        using Fixture fixture = new();
        byte[] configuration = File.ReadAllBytes(fixture.Configuration), mapping = File.ReadAllBytes(fixture.Mapping);
        DiagnosticExecution result = await fixture.RunAsync();
        Assert.AreEqual(0, result.Packet.ExitCode);
        Assert.AreEqual(6, result.Packet.Probes.Count);
        Assert.AreEqual(7, fixture.Calls);
        Assert.AreEqual("addon-detail", result.Packet.Probes[1].Operation);
        Assert.AreEqual("Subscription", result.Packet.Probes[1].Metadata.WireProductType?.Value);
        Assert.AreEqual(false, result.Packet.Probes[1].Metadata.ParserCompatible);
        CollectionAssert.Contains(result.Packet.Interpretations.ToArray(), "collection-versus-detail-difference");
        CollectionAssert.Contains(result.Packet.Interpretations.ToArray(), "local-parser-gap");
        CollectionAssert.AreEqual(configuration, File.ReadAllBytes(fixture.Configuration));
        CollectionAssert.AreEqual(mapping, File.ReadAllBytes(fixture.Mapping));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "inventory")));
        Assert.IsTrue(fixture.Credentials.Contexts.All(context => context.ResourceKey == ApiResources.StoreInventory && context.PublisherId == "publisher-one"));
    }

    [TestMethod]
    public async Task MatchingTypeRejections404WrongParentAndDurableTerminologyHaveDifferentInterpretations()
    {
        using (Fixture fixture = new() { Detail = Rejected() })
        {
            DiagnosticExecution result = await fixture.RunAsync();
            CollectionAssert.Contains(result.Packet.Interpretations.ToArray(), "matching-product-type-rejection-hypothesis");
            Assert.AreEqual(0, result.Packet.ExitCode);
        }
        foreach (string kind in new[] { "missing", "wrong-parent", "wrong-id", "malformed", "durable" })
        {
            using Fixture fixture = new();
            fixture.Detail = kind switch
            {
                "missing" => new(HttpStatusCode.NotFound) { Content = new StringContent("{}") },
                "wrong-parent" => Detail("Subscription", "OTHER-FICTIONAL"),
                "wrong-id" => InventoryHttpTests.Json("{\"id\":\"OTHER-FICTIONAL\"}"),
                "malformed" => InventoryHttpTests.Json("{}"),
                _ => Detail("Durable", "APP-FICTIONAL")
            };
            DiagnosticExecution result = await fixture.RunAsync();
            Assert.AreEqual(0, result.Packet.ExitCode);
            if (kind is "wrong-parent" or "wrong-id") { CollectionAssert.Contains(result.Packet.Interpretations.ToArray(), "sample-identity-or-parent-mismatch"); }
            if (kind == "missing") { Assert.AreEqual("resource-not-found", result.Packet.Probes[1].Outcome); }
            if (kind == "durable") { Assert.AreEqual(true, result.Packet.Probes[1].Metadata.ParserCompatible); }
            Assert.IsFalse(result.Packet.Interpretations.Contains("matching-product-type-rejection-hypothesis"));
        }
    }

    [TestMethod]
    public async Task AuthorizationRedirectBudgetAndGenerationChangesStopLaterProbes()
    {
        foreach (string stop in new[] { "forbidden", "unauthorized", "redirect", "budget", "generation" })
        {
            using Fixture fixture = new();
            if (stop == "generation") { fixture.Credentials.Rotate = true; }
            fixture.ParentStatus = stop switch
            {
                "forbidden" => HttpStatusCode.Forbidden,
                "unauthorized" => HttpStatusCode.Unauthorized,
                "redirect" => HttpStatusCode.Redirect,
                _ => HttpStatusCode.OK
            };
            DiagnosticExecution result = await fixture.RunAsync(maximumRequests: stop == "budget" ? 1 : 24);
            Assert.AreEqual(2, result.Packet.ExitCode);
            Assert.IsTrue(result.Packet.Probes.Skip(1).All(probe => probe.Status is null));
            Assert.IsTrue(fixture.Calls <= 4);
        }
    }

    [TestMethod]
    public async Task InvalidInputNeverReadsCredentialOrSendsHttpAndCancellationPreservesOnlyPartialEvidence()
    {
        using (Fixture invalid = new())
        {
            await File.WriteAllTextAsync(invalid.Input, "PRIVATE-CANARY");
            await Assert.ThrowsAsync<InputException>(() => invalid.RunAsync());
            Assert.AreEqual(0, invalid.Credentials.Contexts.Count);
            Assert.AreEqual(0, invalid.Calls);
        }
        using Fixture cancelled = new();
        using CancellationTokenSource token = new();
        cancelled.Cancel = token;
        DiagnosticExecution result = await cancelled.RunAsync(token.Token);
        Assert.AreEqual(130, result.Packet.ExitCode);
        Assert.IsTrue(result.Packet.Probes.Any(probe => probe.Outcome == "not-attempted"));
        Assert.IsTrue(File.Exists(Path.Combine(result.Directory, "receipt.json")));
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.Directory, "receipt.json")));
        Assert.AreEqual("partial", receipt.RootElement.GetProperty("completion").GetString());
    }

    [TestMethod]
    public async Task TransientAttemptsAndMissingCorrelationsDoNotInventEvidence()
    {
        using Fixture fixture = new() { RetryParent = true };
        DiagnosticExecution result = await fixture.RunAsync();
        Assert.AreEqual(0, result.Packet.ExitCode);
        Assert.AreEqual(2, result.Packet.Attempts.Count(attempt => attempt.Operation == "parent-control"));
        Assert.IsTrue(result.Packet.Attempts.All(attempt => attempt.Correlations.All(field => field.State == "absent")));
    }

    [TestMethod]
    public async Task RetryExhaustionTotalDeadlineAndGlobal401RecoveryStopRequests()
    {
        foreach (HttpStatusCode status in new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
        {
            using Fixture retry = new() { ParentStatus = status };
            DiagnosticExecution exhausted = await retry.RunAsync();
            Assert.AreEqual(2, exhausted.Packet.ExitCode);
            Assert.AreEqual(3, exhausted.Packet.Attempts.Count(attempt => attempt.Operation == "parent-control"));
            Assert.AreEqual(4, retry.Calls);
            Assert.IsTrue(exhausted.Packet.Probes.Skip(1).All(probe => probe.Status is null));
        }
        using (Fixture deadline = new()
        {
            ParentStatus = HttpStatusCode.ServiceUnavailable,
            Delay = (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token)
        })
        {
            DiagnosticExecution exhausted = await deadline.RunAsync(deadline: TimeSpan.FromMilliseconds(200));
            Assert.AreEqual(2, exhausted.Packet.ExitCode);
            CollectionAssert.Contains(exhausted.Packet.Probes.Select(probe => probe.Outcome).ToArray(), "diagnostic-deadline-exhausted");
            Assert.AreEqual(2, deadline.Calls);
        }
        using Fixture recovery = new()
        {
            RecoverParentOnce = true,
            Detail = new(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }
        };
        DiagnosticExecution stopped = await recovery.RunAsync();
        Assert.AreEqual(2, stopped.Packet.ExitCode);
        Assert.AreEqual(2, stopped.Packet.Attempts.Count(attempt => attempt.Operation == "token"));
        Assert.AreEqual("diagnostic-auth-recovery-exhausted", stopped.Packet.Probes[1].Outcome);
        Assert.IsTrue(stopped.Packet.Probes.Skip(2).All(probe => probe.Status is null));
    }

    [TestMethod]
    public async Task ReadableMatrixHasExactlyApprovedRoutesAndCannotAcceptInventory()
    {
        using Fixture fixture = new() { ReadableCollections = true };
        DiagnosticExecution result = await fixture.RunAsync();
        Assert.AreEqual(0, result.Packet.ExitCode);
        CollectionAssert.Contains(result.Packet.Interpretations.ToArray(), "failure-not-reproduced");
        CollectionAssert.AreEqual(StoreSubscriptionDiagnostic.Plan(StoreDiagnosticInputTests.Sample).Select(item => item.Uri.AbsoluteUri).ToArray(),
            fixture.Destinations.Where(uri => uri.Host == "manage.devcenter.microsoft.com").Select(uri => uri.AbsoluteUri).ToArray());
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "inventory")));
        Assert.IsTrue(JsonSerializer.Serialize(result.Packet.PublicProjection()).Contains("unverified", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task UnknownPublisherAndMissingInventoryBindingAreRejectedBeforeCredentialAccess()
    {
        foreach (bool missing in new[] { false, true })
        {
            using Fixture fixture = new();
            PublisherProfile profile = InventoryModelTests.Profile(missing ? "publisher-one" : "publisher-two");
            if (missing) { profile.Authorization.Remove(ApiResources.StoreInventory); }
            await File.WriteAllTextAsync(fixture.Configuration, JsonSerializer.Serialize(new PublisherConfiguration
            { SchemaVersion = 2, Publishers = [profile] }, StoreDiagnosticInputTests.JsonOptions));
            await Assert.ThrowsAsync<InputException>(() => fixture.RunAsync());
            Assert.AreEqual(0, fixture.Calls);
            Assert.AreEqual(0, fixture.Credentials.Contexts.Count);
        }
    }

    internal static HttpResponseMessage Rejected() => new(HttpStatusCode.BadRequest)
    { Content = new StringContent("{\"code\":\"NotAllowed\",\"target\":\"productType\",\"message\":\"API does not support in-app-product type: Subscription\"}") };
    internal static HttpResponseMessage Detail(string type = "Subscription", string parent = "APP-FICTIONAL") => InventoryHttpTests.Json(JsonSerializer.Serialize(new
    {
        id = "SUB-FICTIONAL",
        productType = type,
        applications = new { value = new[] { new { id = parent, resourceLocation = "applications/" + parent } }, totalCount = 1 },
        lastPublishedInAppProductSubmission = new { id = "123", resourceLocation = "inappproducts/SUB-FICTIONAL/submissions/123" }
    }));

    internal sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory temporary = new();
        internal string Root { get; }
        internal string Configuration => Path.Combine(Root, "publishers.json");
        internal string Input => Path.Combine(Root, "sample.json");
        internal string Mapping => Path.Combine(Root, "references.json");
        internal CountingCredentials Credentials { get; } = new();
        internal int Calls { get; private set; }
        internal HttpResponseMessage? Detail { get; set; }
        internal HttpStatusCode ParentStatus { get; set; } = HttpStatusCode.OK;
        internal CancellationTokenSource? Cancel { get; set; }
        internal bool RetryParent { get; set; }
        internal bool RecoverParentOnce { get; set; }
        internal bool ReadableCollections { get; set; }
        internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (_, _) => Task.CompletedTask;
        internal List<Uri> Destinations { get; } = [];
        private int parentCalls;
        internal Fixture()
        {
            Root = new WindowsPrivateStorageGuard().PrepareDirectory(Path.Combine(temporary.Path, "private"));
            PublisherProfile profile = InventoryModelTests.Profile();
            File.WriteAllText(Configuration, JsonSerializer.Serialize(new PublisherConfiguration { SchemaVersion = 2, Publishers = [profile] }, StoreDiagnosticInputTests.JsonOptions));
            File.WriteAllText(Input, JsonSerializer.Serialize(StoreDiagnosticInputTests.Sample, StoreDiagnosticInputTests.JsonOptions));
            File.WriteAllText(Mapping, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                references = new Dictionary<string, object> { [profile.Authorization[ApiResources.StoreInventory].CredentialReference] = new { target = "FICTIONAL-CREDENTIAL", generation = "1" } }
            }));
        }
        internal Task<DiagnosticExecution> RunAsync(CancellationToken cancellation = default, int maximumRequests = 24, TimeSpan? deadline = null) =>
            StoreSubscriptionDiagnostic.RunAsync(Configuration, Input, Mapping, Path.Combine(Root, "runs"), new WindowsPrivateStorageGuard(), Credentials,
                new InventoryHttpTests.Handler(Respond), new StoreDiagnosticTransportTests.Clock(), new AtomicFileWriter(new NewFileCommitter()),
                cancellation, Delay, deadline: deadline, maximumRequests: maximumRequests);
        private Task<HttpResponseMessage> Respond(HttpRequestMessage request)
        {
            Calls++;
            Destinations.Add(request.RequestUri!);
            if (request.Method == HttpMethod.Post)
            {
                Assert.AreEqual("login.microsoftonline.com", request.RequestUri!.Host);
                return Task.FromResult(InventoryHttpTests.Json("{\"access_token\":\"FICTIONAL-TOKEN\",\"token_type\":\"Bearer\",\"expires_in\":3600}"));
            }
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.IsNull(request.Content);
            Assert.AreEqual("manage.devcenter.microsoft.com", request.RequestUri!.Host);
            string path = request.RequestUri.AbsolutePath;
            if (path == "/v1.0/my/applications/APP-FICTIONAL")
            {
                parentCalls++;
                if (RecoverParentOnce && parentCalls == 1) { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }); }
                if (RetryParent && parentCalls == 1) { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") }); }
                Cancel?.Cancel();
                return Task.FromResult(ParentStatus == HttpStatusCode.OK ? InventoryHttpTests.Json("{\"id\":\"APP-FICTIONAL\"}") :
                    new HttpResponseMessage(ParentStatus) { Content = new StringContent("{}") });
            }
            if (path == "/v1.0/my/inappproducts/SUB-FICTIONAL") { return Task.FromResult(Detail ?? StoreSubscriptionDiagnosticTests.Detail()); }
            return Task.FromResult(ReadableCollections ? InventoryHttpTests.Json("{\"value\":[],\"totalCount\":0}") : Rejected());
        }
        public void Dispose() { Detail?.Dispose(); temporary.Dispose(); }
    }
    internal sealed class CountingCredentials : IApplicationCredentialResolver
    {
        internal List<AuthorizationContext> Contexts { get; } = [];
        internal bool Rotate { get; set; }
        public Task<ApplicationCredential> ResolveAsync(AuthorizationContext context, CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return Task.FromResult(new ApplicationCredential("FICTIONAL-SECRET", Rotate && Contexts.Count > 1 ? "fixture-v2" : "fixture-v1"));
        }
    }
}
