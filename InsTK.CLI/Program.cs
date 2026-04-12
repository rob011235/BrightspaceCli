using InsTK.Core;

var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();

if (string.IsNullOrWhiteSpace(command) || command is "help" or "--help" or "-h")
{
    PrintHelp();
    return 0;
}

var options = CommandLineOptions.Parse(args.Skip(1).ToArray());
var host = new ConsoleCommandHost();

try
{
    return await InsTkApplication.ExecuteAsync(command, options, host);
}
catch (Exception ex)
{
    host.Error.WriteLine(ex.Message);
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        InsTK CLI

        Commands
          login
            Open a headed browser, let you log in manually, then save session state.
          scrape-quickeval
            Load a Quick Eval page with a saved session and export the visible submission rows.
          scrape-submission
            Load an individual submission/evaluation page and export comments, links, and GitHub hints.
          scrape-submission-map
            Load the Quick Eval page, visit each evaluation URL, and export merged row plus detail data.
          build-grading-worklist
            Join a submission map with an assignment registry and export a grading worklist.
          prepare-grading-repos
            Clone or update repos from a grading worklist and export a repo-ready grading queue.
          build-grading-runner
            Build a Codex-ready grading run queue with prompts, report paths, and tutorial/spec context.

        Examples
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- login
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- scrape-quickeval
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- scrape-quickeval --first-page-only
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- scrape-submission --url "https://mycourses.cnm.edu/d2l/le/activities/iterator/..."
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- scrape-submission-map --limit 5
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- build-grading-worklist --registry "C:\grading\assignment-registry.json"
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- prepare-grading-repos --limit 5
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- build-grading-runner --limit 5

        Config
          Put shared defaults in instk.json. The core library also falls back to brightspacecli.json for compatibility.
        """);
}

internal sealed class CommandLineOptions : ICommandOptions
{
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options.values[key[2..]] = "true";
                continue;
            }

            options.values[key[2..]] = args[i + 1];
            i++;
        }

        return options;
    }

    public string? Get(string name)
        => values.TryGetValue(name, out var value) ? value : null;

    public bool HasFlag(string name)
        => string.Equals(Get(name), "true", StringComparison.OrdinalIgnoreCase);

    public string Require(string name)
        => Get(name) ?? throw new InvalidOperationException($"Missing required option --{name}");

    public string GetOrDefault(string name, string? fallback)
        => Get(name) ?? fallback ?? throw new InvalidOperationException($"Missing required option --{name}");
}

internal sealed class ConsoleCommandHost : ICommandHost
{
    public TextWriter Out => Console.Out;
    public TextWriter Error => Console.Error;
    public TextReader In => Console.In;
}
