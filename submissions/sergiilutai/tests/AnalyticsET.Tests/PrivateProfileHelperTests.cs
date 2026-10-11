using System.Diagnostics;
using System.Text.Json;
using Core.AnalyticsET.Publishers;
using Core.AnalyticsET.Storage;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PrivateProfileHelperTests
{
    [TestMethod]
    public async Task MaskedRecoveryCreatesRuntimeProfileAndPreservesMalformedRegistration()
    {
        using TemporaryDirectory directory = new();
        string registrationDirectory = new WindowsPrivateStorageGuard().PrepareDirectory(Path.Combine(directory.Path, "registration"));
        string registration = Path.Combine(registrationDirectory, "broken.json");
        await File.WriteAllTextAsync(registration, "{malformed-registration-DO-NOT-ECHO");
        byte[] before = await File.ReadAllBytesAsync(registration);
        string root = Path.Combine(directory.Path, "recovery");
        var result = await RunAsync(registration, root, maskedRecovery: true);
        result.ExitCode.Should().Be(0, result.Error);
        (result.Output + result.Error).Should().NotContain("DO-NOT-ECHO").And.NotContain("11111111").And.NotContain("22222222");
        (await File.ReadAllBytesAsync(registration)).Should().Equal(before);
        PublisherConfiguration configuration = ConfigurationJson.Deserialize<PublisherConfiguration>(
            await File.ReadAllTextAsync(Path.Combine(root, "runtime", "publishers.json")));
        ConfigurationValidator.Validate(configuration);
        configuration.Publishers.Single().TenantId.Should().Be("11111111-1111-1111-1111-111111111111");
    }

    [TestMethod]
    public async Task HelperBuildsValidPrivateProfileWithoutProductIdsOrVaultAccessAndPreservesExistingSetup()
    {
        using TemporaryDirectory directory = new();
        WindowsPrivateStorageGuard guard = new();
        string registrationDirectory = guard.PrepareDirectory(Path.Combine(directory.Path, "registration"));
        string registration = Path.Combine(registrationDirectory, "entra.json");
        await File.WriteAllTextAsync(registration, """
            {"clientId":"22222222-2222-2222-2222-222222222222","tenantId":"11111111-1111-1111-1111-111111111111"}
            """);
        File.SetAttributes(registration, File.GetAttributes(registration) | FileAttributes.Hidden);
        string root = Path.Combine(directory.Path, "operator-private");
        var created = await RunAsync(registration, root);
        created.ExitCode.Should().Be(0, created.Error);
        created.Output.Should().Contain("No credential was read").And.NotContain("22222222").And.NotContain("11111111");
        string path = Path.Combine(root, "runtime", "publishers.json");
        byte[] before = await File.ReadAllBytesAsync(path);
        PublisherConfiguration configuration = ConfigurationJson.Deserialize<PublisherConfiguration>(await File.ReadAllTextAsync(path));
        ConfigurationValidator.Validate(configuration);
        configuration.SchemaVersion.Should().Be(2);
        configuration.Publishers.Should().ContainSingle().Which.Accounts[ApiResources.StoreProgram].ProductIds.Should().BeEmpty();
        guard.VerifyFile(path);
        string mapping = Path.Combine(root, "credentials", "references.json");
        guard.VerifyFile(mapping);
        using JsonDocument references = JsonDocument.Parse(await File.ReadAllTextAsync(mapping));
        references.RootElement.GetProperty("references").GetProperty("fictional-store").GetProperty("target").GetString()
            .Should().Be("AnalyticsET/fictional-not-a-real-credential");
        (await RunAsync(registration, root)).ExitCode.Should().Be(1);
        (await File.ReadAllBytesAsync(path)).Should().Equal(before);
    }

    [TestMethod]
    [DataRow("FICTIONAL")]
    [DataRow("fictional_with_underscores")]
    public async Task HelperRejectsAliasesTheApplicationWouldRejectBeforeCreatingFiles(string alias)
    {
        using TemporaryDirectory directory = new();
        string root = Path.Combine(directory.Path, "rejected");
        var result = await RunAsync(Path.Combine(directory.Path, "unused.json"), root, maskedRecovery: true, publisherId: alias);
        result.ExitCode.Should().Be(1);
        result.Error.Should().Contain("profile-input").And.NotContain(alias);
        Directory.Exists(root).Should().BeFalse();
    }

    [TestMethod]
    public async Task ExplicitAliasRepairUpdatesBothFilesPreservesPrivateBackupsAndIsIdempotent()
    {
        using TemporaryDirectory directory = new();
        string root = Path.Combine(directory.Path, "repair");
        string registration = Path.Combine(directory.Path, "unused.json");
        (await RunAsync(registration, root, maskedRecovery: true)).ExitCode.Should().Be(0);
        string path = Path.Combine(root, "runtime", "publishers.json");
        string mapping = Path.Combine(root, "credentials", "references.json");
        await MakeUppercaseAsync(path, mapping);
        byte[] originalConfiguration = await File.ReadAllBytesAsync(path);
        byte[] originalMapping = await File.ReadAllBytesAsync(mapping);

        var repaired = await RunAsync(registration, root, repair: true);
        repaired.ExitCode.Should().Be(0, repaired.Error);
        repaired.Output.Should().Contain("No credential was read").And.NotContain("FICTIONAL").And.NotContain("11111111");
        PublisherConfiguration configuration = ConfigurationJson.Deserialize<PublisherConfiguration>(await File.ReadAllTextAsync(path));
        ConfigurationValidator.Validate(configuration);
        configuration.Publishers.Single().Id.Should().Be("fictional");
        configuration.Publishers.Single().Authorization.Values.Should().OnlyContain(auth => auth.CredentialReference == "fictional-store");
        using JsonDocument references = JsonDocument.Parse(await File.ReadAllTextAsync(mapping));
        references.RootElement.GetProperty("references").EnumerateObject().Should().ContainSingle()
            .Which.Name.Should().Be("fictional-store");
        WindowsPrivateStorageGuard guard = new();
        string configurationBackup = Directory.GetFiles(Path.GetDirectoryName(path)!, "*.bak").Single();
        string mappingBackup = Directory.GetFiles(Path.GetDirectoryName(mapping)!, "*.bak").Single();
        (await File.ReadAllBytesAsync(configurationBackup)).Should().Equal(originalConfiguration);
        (await File.ReadAllBytesAsync(mappingBackup)).Should().Equal(originalMapping);
        foreach (string file in new[] { path, mapping, configurationBackup, mappingBackup }) { guard.VerifyFile(file); }
        (await RunAsync(registration, root, repair: true)).ExitCode.Should().Be(0);
        Directory.GetFiles(Path.GetDirectoryName(path)!, "*.bak").Should().ContainSingle();
    }

    [TestMethod]
    public async Task AliasRepairRollsBackMappingWhenConfigurationCannotBeReplaced()
    {
        using TemporaryDirectory directory = new();
        string root = Path.Combine(directory.Path, "failed-repair");
        string registration = Path.Combine(directory.Path, "unused.json");
        (await RunAsync(registration, root, maskedRecovery: true)).ExitCode.Should().Be(0);
        string path = Path.Combine(root, "runtime", "publishers.json");
        string mapping = Path.Combine(root, "credentials", "references.json");
        await MakeUppercaseAsync(path, mapping);
        byte[] originalConfiguration = await File.ReadAllBytesAsync(path);
        byte[] originalMapping = await File.ReadAllBytesAsync(mapping);
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await RunAsync(registration, root, repair: true);
            result.ExitCode.Should().Be(1);
            result.Error.Should().Contain("alias-repair-rollback").And.NotContain("FICTIONAL");
        }
        (await File.ReadAllBytesAsync(path)).Should().Equal(originalConfiguration);
        (await File.ReadAllBytesAsync(mapping)).Should().Equal(originalMapping);
    }

    private static async Task MakeUppercaseAsync(string path, string mapping)
    {
        string configurationJson = (await File.ReadAllTextAsync(path)).Replace("\"fictional\"", "\"FICTIONAL\"", StringComparison.Ordinal)
            .Replace("fictional-store", "FICTIONAL-store", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, configurationJson);
        await File.WriteAllTextAsync(mapping, (await File.ReadAllTextAsync(mapping)).Replace("fictional-store", "FICTIONAL-store", StringComparison.Ordinal));
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string registration, string root,
        bool maskedRecovery = false, string publisherId = "fictional", bool repair = false)
    {
        ProcessStartInfo start = new("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        string scriptPath = Path.Combine(CliProcess.ProjectDirectory, "scripts", "New-PrivatePublisherProfile.ps1");
        string[] scriptArguments = repair ? [scriptPath, "-PrivateRoot", root, "-RepairPublisherAlias"] :
            [scriptPath, "-RegistrationPath", registration, "-PrivateRoot", root, "-PublisherId", publisherId, "-DisplayName", "Fictional publisher",
            "-PublisherAccountId", "fictional-account", "-CredentialTarget", "AnalyticsET/fictional-not-a-real-credential"];
        start.ArgumentList.Add("-NoProfile");
        if (maskedRecovery)
        {
            start.ArgumentList.Add("-Command");
            string invocation = "& " + string.Join(' ', scriptArguments.Select(argument => "'" + argument.Replace("'", "''", StringComparison.Ordinal) + "'")) +
                " -PromptForRegistrationIds";
            // Only fictional fixture GUIDs enter this isolated Read-Host substitute; production prompts are masked.
            string hostStub = """
                function global:Read-Host {
                    param([string]$Prompt, [switch]$AsSecureString)
                    if (-not $AsSecureString) { throw 'Registration prompts must be masked.' }
                    $fixture = if ($Prompt.StartsWith('Application')) { '22222222-2222-2222-2222-222222222222' } else { '11111111-1111-1111-1111-111111111111' }
                    $protected = [Security.SecureString]::new()
                    foreach ($character in $fixture.ToCharArray()) { $protected.AppendChar($character) }
                    $protected.MakeReadOnly()
                    return $protected
                }
                """;
            // PowerShell parameter names must be tokens, not quoted expressions after the command.
            invocation = invocation.Replace("'-RegistrationPath'", "-RegistrationPath", StringComparison.Ordinal)
                .Replace("'-PrivateRoot'", "-PrivateRoot", StringComparison.Ordinal).Replace("'-PublisherId'", "-PublisherId", StringComparison.Ordinal)
                .Replace("'-DisplayName'", "-DisplayName", StringComparison.Ordinal).Replace("'-PublisherAccountId'", "-PublisherAccountId", StringComparison.Ordinal)
                .Replace("'-CredentialTarget'", "-CredentialTarget", StringComparison.Ordinal);
            start.ArgumentList.Add(hostStub + Environment.NewLine + invocation);
        }
        else
        {
            start.ArgumentList.Add("-File");
            foreach (string argument in scriptArguments) { start.ArgumentList.Add(argument); }
        }
        using Process process = Process.Start(start)!;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(40));
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }
}
