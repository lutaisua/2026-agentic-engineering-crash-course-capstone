namespace Console.AnalyticsET.Cli;

internal sealed class CliApplication(TextWriter output, TextWriter error, PublisherCommands publishers, ConfigurationCommands configuration,
    InventoryCommands? inventory = null)
{
    private readonly TextWriter output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly TextWriter error = error ?? throw new ArgumentNullException(nameof(error));
    private readonly PublisherCommands publishers = publishers ?? throw new ArgumentNullException(nameof(publishers));
    private readonly ConfigurationCommands configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    internal async Task<int> RunAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            CommandLine command = CommandLine.Parse(arguments);
            string[] args = command.Arguments;
            if (args.Length == 0 || args is ["help"] or ["--help"] or ["-h"])
            {
                await output.WriteLineAsync(Help.AsMemory(), cancellationToken).ConfigureAwait(false);
                return 0;
            }

            if (args is ["version"] or ["--version"])
            {
                await output.WriteLineAsync("AnalyticsET 0.1.0".AsMemory(), cancellationToken).ConfigureAwait(false);
                return 0;
            }

            if (args is ["config", "path"])
            {
                await output.WriteLineAsync(command.ConfigurationPath.AsMemory(), cancellationToken).ConfigureAwait(false);
                return 0;
            }

            if (args.Length > 0 && (args[0] == "products" || args is ["publisher", "onboard", ..]))
            {
                if (inventory is null) { throw new InputException("Inventory services are unavailable."); }
                var result = await inventory.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
                await output.WriteLineAsync(result.Output.AsMemory(), cancellationToken).ConfigureAwait(false);
                return result.ExitCode;
            }

            if (args.Length > 0 && args[0] == "publisher")
            {
                string result = await publishers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
                await output.WriteLineAsync(result.AsMemory(), cancellationToken).ConfigureAwait(false);
                return 0;
            }

            if (args.Length > 0 && args[0] == "config")
            {
                string result = await configuration.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
                await output.WriteLineAsync(result.AsMemory(), cancellationToken).ConfigureAwait(false);
                return 0;
            }

            throw new InputException("Unknown command or option. Run 'AnalyticsET --help' for available commands.");
        }
        catch (InputException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 3;
        }
        catch (Core.AnalyticsET.Inventory.InventoryException exception)
        {
            await error.WriteLineAsync("Private inventory state could not be used: " + exception.Category).ConfigureAwait(false);
            return 2;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Cancelled.").ConfigureAwait(false);
            return 130;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync("Local file operation failed. Check file permissions, disk space, and concurrent writers.")
                .ConfigureAwait(false);
            return 1;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            await error.WriteLineAsync("Invalid file path or command input. Run 'AnalyticsET --help'.").ConfigureAwait(false);
            return 3;
        }
    }

    private const string Help = """
        AnalyticsET 0.1.0 - local Partner Center publisher setup

        Usage: AnalyticsET [--config <file>] <command>
          --help | help        Show this help (offline).
          --version | version  Show application version (offline).
          config path          Show the selected configuration file path.
          config validate [--publisher <id[,id]> | --all]
          config migrate --to-version 2 --output <new-private-file>
          publisher add --file <profile.json>
          publisher list
          publisher show --publisher <id[,id]> | --all
          publisher update <id> --file <profile.json>
          publisher remove <id>
          publisher onboard --publisher <id[,id]> | --all [--program <program>]
          products list --publisher <id[,id]> | --all [--program <program>] [--offline]
          products refresh --publisher <id[,id]> | --all [--program <program>]
          Inventory commands also accept --state <private-directory>.

        Default configuration: %LOCALAPPDATA%/AnalyticsET/publishers.json
        Microsoft authorization and report access are unverified.
        Exit codes: 0 success; 1 local operation failed; 2 incomplete discovery/cache; 3 invalid input/configuration; 130 cancelled.
        """;
}
