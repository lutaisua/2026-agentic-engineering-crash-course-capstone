namespace Console.AnalyticsET.Cli;

internal sealed record CommandLine(string ConfigurationPath, string[] Arguments)
{
    internal static CommandLine Parse(string[] arguments)
    {
        List<string> remaining = [];
        string? overridePath = null;
        for (int index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] != "--config")
            {
                remaining.Add(arguments[index]);
                continue;
            }

            if (overridePath is not null || ++index == arguments.Length ||
                string.IsNullOrWhiteSpace(arguments[index]) || arguments[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new InputException("Use --config <file> once with a configuration file path.");
            }

            overridePath = arguments[index];
        }

        string path = overridePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalyticsET", "publishers.json");
        try
        {
            return new CommandLine(Path.GetFullPath(path), [.. remaining]);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InputException("Configuration path is invalid. Use --config <file>.");
        }
    }
}
