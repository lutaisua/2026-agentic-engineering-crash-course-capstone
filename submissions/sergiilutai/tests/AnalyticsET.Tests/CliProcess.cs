using System.Diagnostics;

namespace AnalyticsET.Tests;

internal static class CliProcess
{
    internal static string ProjectDirectory
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AnalyticsET.csproj")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Cannot locate the submission.");
        }
    }

    internal static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] arguments)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        ProcessStartInfo start = new("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ProjectDirectory
        };
        start.ArgumentList.Add(Path.Combine(ProjectDirectory, "bin", configuration, "net10.0", "AnalyticsET.dll"));
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start CLI.");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await error);
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
