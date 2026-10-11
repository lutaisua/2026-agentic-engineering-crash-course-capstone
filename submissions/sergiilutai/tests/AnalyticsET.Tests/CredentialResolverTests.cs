using System.Text.Json;
using Console.AnalyticsET.Cli;
using Core.AnalyticsET.Authentication;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class CredentialResolverTests
{
    [TestMethod]
    public async Task ProtectedAliasResolvesOnlyTheSecretValueAndRedactsSerialization()
    {
        using TemporaryDirectory directory = new();
        string privateDirectory = new WindowsPrivateStorageGuard().PrepareDirectory(Path.Combine(directory.Path, "private"));
        string mapping = Path.Combine(privateDirectory, "references.json");
        await File.WriteAllTextAsync(mapping,
            """{"schemaVersion":1,"references":{"store-reference":{"target":"AnalyticsET/example-store","generation":"1"}}} """);
        FakeVault vault = new();
        ApplicationCredentialResolver resolver = new(mapping, new WindowsPrivateStorageGuard(), vault);
        AuthorizationContext context = AuthorizationContext.Create(ProfileFixtures.Create(), ApiResources.StoreProgram, ApiResources.Store);

        ApplicationCredential credential = await resolver.ResolveAsync(context, default);

        credential.SecretValue.Should().Be("FAKE-SECRET-VALUE");
        credential.Generation.Should().HaveLength(64);
        credential.ToString().Should().NotContain("FAKE-SECRET-VALUE");
        JsonSerializer.Serialize(credential).Should().NotContain("FAKE-SECRET-VALUE").And.NotContain("SecretValue");
        vault.Target.Should().Be("AnalyticsET/example-store");
        vault.Version = "updated";
        (await resolver.ResolveAsync(context, default)).Generation.Should().NotBe(credential.Generation);
    }

    [TestMethod]
    public async Task MissingPrivateReferenceFailsBeforeReadingTheVault()
    {
        using TemporaryDirectory directory = new();
        FakeVault vault = new();
        ApplicationCredentialResolver resolver = new(Path.Combine(directory.Path, "missing.json"), new WindowsPrivateStorageGuard(), vault);
        AuthorizationContext context = AuthorizationContext.Create(ProfileFixtures.Create(), ApiResources.StoreProgram, ApiResources.Store);

        Func<Task> resolve = () => resolver.ResolveAsync(context, default);

        await resolve.Should().ThrowAsync<InputException>().WithMessage("*mapping is missing*");
        vault.Target.Should().BeNull();
    }

    [TestMethod]
    public async Task SecretIdAndValueCannotBeStoredInTheReferenceFile()
    {
        using TemporaryDirectory directory = new();
        string privateDirectory = new WindowsPrivateStorageGuard().PrepareDirectory(Path.Combine(directory.Path, "private"));
        string mapping = Path.Combine(privateDirectory, "references.json");
        await File.WriteAllTextAsync(mapping,
            """{"schemaVersion":1,"references":{"store-reference":{"target":"AnalyticsET/example-store","secretId":"DO-NOT-ECHO","secretValue":"DO-NOT-ECHO"}}} """);
        FakeVault vault = new();
        ApplicationCredentialResolver resolver = new(mapping, new WindowsPrivateStorageGuard(), vault);
        AuthorizationContext context = AuthorizationContext.Create(ProfileFixtures.Create(), ApiResources.StoreProgram, ApiResources.Store);

        Func<Task> resolve = () => resolver.ResolveAsync(context, default);

        (await resolve.Should().ThrowAsync<InputException>()).Which.Message.Should().NotContain("DO-NOT-ECHO");
        vault.Target.Should().BeNull();
    }

    [TestMethod]
    public async Task CancellationDoesNotReadPrivateFilesOrTheVault()
    {
        using TemporaryDirectory directory = new();
        FakeVault vault = new();
        ApplicationCredentialResolver resolver = new(Path.Combine(directory.Path, "missing.json"), new WindowsPrivateStorageGuard(), vault);
        AuthorizationContext context = AuthorizationContext.Create(ProfileFixtures.Create(), ApiResources.StoreProgram, ApiResources.Store);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> resolve = () => resolver.ResolveAsync(context, cancellation.Token);

        await resolve.Should().ThrowAsync<OperationCanceledException>();
        vault.Target.Should().BeNull();
    }

    private sealed class FakeVault : ICredentialVault
    {
        internal string? Target { get; private set; }
        internal string Version { get; set; } = "initial";
        public VaultCredential Read(string target)
        {
            Target = target;
            return new("FAKE-SECRET-VALUE", Version);
        }
    }
}
