using FluentAssertions;

namespace AnalyticsET.Tests;

[TestClass]
public sealed class CliTests
{
    [TestMethod]
    public async Task ConsoleApplicationStartsSuccessfully()
    {
        var result = await CliProcess.RunAsync();

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("AnalyticsET");
        result.Error.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("--help", "unverified")]
    [DataRow("--version", "0.1.0")]
    public async Task HelpAndVersionDoNotReadConfiguration(string command, string expected)
    {
        var result = await CliProcess.RunAsync("--config", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), command);

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain(expected);
        result.Error.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ConfigurationOverrideResolvesRelativePaths()
    {
        var result = await CliProcess.RunAsync("config", "path", "--config", "profiles.json");

        result.ExitCode.Should().Be(0);
        result.Output.Trim().Should().Be(Path.Combine(CliProcess.ProjectDirectory, "profiles.json"));
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("--password")]
    [DataRow("--config")]
    public async Task InvalidArgumentsReturnActionableInputError(string argument)
    {
        var result = await CliProcess.RunAsync(argument);

        result.ExitCode.Should().Be(3);
        result.Output.Should().BeEmpty();
        result.Error.Should().NotBeEmpty();
    }
}
