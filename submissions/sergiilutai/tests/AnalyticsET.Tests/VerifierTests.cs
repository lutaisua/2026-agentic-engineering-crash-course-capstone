using System.Diagnostics;
using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class VerifierTests
{
    [TestMethod]
    [DataRow("none", 0, 3)]
    [DataRow("build", 7, 1)]
    [DataRow("test", 7, 2)]
    [DataRow("format", 7, 3)]
    public async Task VerifierPropagatesChildFailureAndRunsAllSuccessfulChecks(string failingCommand, int childExitCode, int expectedCalls)
    {
        using TemporaryDirectory directory = new();
        string command = Path.Combine(directory.Path, "fake-dotnet.cmd");
        string log = Path.Combine(directory.Path, "commands.txt");
        await File.WriteAllTextAsync(command, $"@echo off\r\necho %*>>\"{log}\"\r\nif \"%1\" == \"{failingCommand}\" exit /b 7\r\nexit /b 0\r\n");
        ProcessStartInfo start = new("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directory.Path
        };
        foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(CliProcess.ProjectDirectory, "scripts", "verify.ps1"),
            "-DotnetPath", command })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            string stdout = await output;
            string stderr = await error;

            process.ExitCode.Should().Be(childExitCode, stderr);
            string[] calls = await File.ReadAllLinesAsync(log);
            calls.Should().HaveCount(expectedCalls);
            calls[0].Should().StartWith("build AnalyticsET.slnx");
            if (childExitCode == 0)
            {
                calls[1].Should().Contain("TestCategory!=Live");
                calls[2].Should().Contain("--verify-no-changes --no-restore");
                stdout.Should().Contain("live reports remain unverified");
            }
            else
            {
                stdout.Should().NotContain("verification passed");
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
