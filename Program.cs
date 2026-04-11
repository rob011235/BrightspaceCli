using System.Text.Json;
using Microsoft.Playwright;

var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();

if (string.IsNullOrWhiteSpace(command) || command is "help" or "--help" or "-h")
{
    PrintHelp();
    return 0;
}

var options = CommandLineOptions.Parse(args.Skip(1).ToArray());

try
{
    return command switch
    {
        "login" => await BrightspaceCli.LoginAsync(options),
        "scrape-quickeval" => await BrightspaceCli.ScrapeQuickEvalAsync(options),
        "scrape-submission" => await BrightspaceCli.ScrapeSubmissionAsync(options),
        _ => Fail($"Unknown command: {command}"),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        Brightspace CLI

        Commands
          login
            Open a headed browser, let you log in manually, then save session state.
          scrape-quickeval
            Load a Quick Eval page with a saved session and export the visible submission rows.
          scrape-submission
            Load an individual submission/evaluation page and export comments, links, and GitHub hints.

        Examples
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- login --url "https://mycourses.cnm.edu/d2l/le/224618/quickeval/" --state ".brightspace/session.json"
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- scrape-quickeval --url "https://mycourses.cnm.edu/d2l/le/224618/quickeval/" --state ".brightspace/session.json" --out "_grading/quickeval-live.json"
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- scrape-submission --url "https://mycourses.cnm.edu/d2l/le/activities/iterator/..." --state ".brightspace/session.json" --out "_grading/submission-live.json"
        """);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

internal sealed class CommandLineOptions
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

    public string Require(string name)
        => Get(name) ?? throw new InvalidOperationException($"Missing required option --{name}");
}

internal static class BrightspaceCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<int> LoginAsync(CommandLineOptions options)
    {
        var url = options.Require("url");
        var statePath = ResolvePath(options.Require("state"));
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
        });

        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        Console.WriteLine($"Opening {url}");
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Console.WriteLine("Complete the Brightspace login in the browser, then press Enter here to save the session.");
        Console.ReadLine();

        await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = statePath });
        Console.WriteLine($"Saved session state to {statePath}");
        await browser.CloseAsync();
        return 0;
    }

    public static async Task<int> ScrapeQuickEvalAsync(CommandLineOptions options)
    {
        var url = options.Require("url");
        var statePath = ResolvePath(options.Require("state"));
        var outPath = ResolvePath(options.Get("out") ?? "_grading/quickeval-live.json");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = statePath,
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForSelectorAsync("d2l-quick-eval-submissions-table tbody > tr");

        var rows = page.Locator("d2l-quick-eval-submissions-table tbody > tr");
        var rowCount = await rows.CountAsync();
        var submissions = new List<QuickEvalSubmission>();

        for (var i = 0; i < rowCount; i++)
        {
            var row = rows.Nth(i);
            var nameNode = row.Locator("d2l-link.d2l-quick-eval-submissions-table-name-link");
            var activityNode = row.Locator("d2l-activity-name .d2l-activity-name-text");
            var dateNode = row.Locator("td.d2l-quick-eval-submissions-overflow-hidden span");
            var evaluationNode = row.Locator("d2l-link.d2l-quick-eval-submissions-table-name-link a");

            var student = await ReadNamedValueAsync(nameNode);
            var activityName = await ReadNamedValueAsync(activityNode);
            var submittedAt = await ReadNamedValueAsync(dateNode);
            var evaluationUrl = await ReadHrefAsync(evaluationNode);

            submissions.Add(new QuickEvalSubmission(
                i,
                student,
                activityName,
                submittedAt,
                evaluationUrl,
                [.. new[] { evaluationUrl }.Where(static value => !string.IsNullOrWhiteSpace(value))!]));
        }

        var result = new QuickEvalListResult(
            "BrightspaceCli",
            DateTimeOffset.UtcNow,
            url,
            rowCount,
            submissions);

        await WriteJsonAsync(outPath, result);
        Console.WriteLine($"Wrote {rowCount} submissions to {outPath}");
        return 0;
    }

    public static async Task<int> ScrapeSubmissionAsync(CommandLineOptions options)
    {
        var url = options.Require("url");
        var statePath = ResolvePath(options.Require("state"));
        var outPath = ResolvePath(options.Get("out") ?? "_grading/submission-live.json");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = statePath,
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var title = await page.TitleAsync();
        var bodyText = await page.Locator("body").InnerTextAsync();
        var links = await page.Locator("a[href]").EvaluateAllAsync<string[]>(
            "nodes => nodes.map(n => n.href).filter(Boolean)");

        var uniqueLinks = links
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var repoUrl = uniqueLinks.FirstOrDefault(static value =>
            value.Contains("github.com/", StringComparison.OrdinalIgnoreCase));

        var github = GitHubHintParser.Parse(repoUrl);
        var result = new SubmissionDetailResult(
            "BrightspaceCli",
            DateTimeOffset.UtcNow,
            title,
            url,
            repoUrl,
            github.Owner,
            github.Repo,
            github.CloneUrl,
            github.BranchHint,
            github.SubdirHint,
            uniqueLinks,
            bodyText);

        await WriteJsonAsync(outPath, result);
        Console.WriteLine($"Wrote submission detail to {outPath}");
        return 0;
    }

    private static async Task<string?> ReadNamedValueAsync(ILocator locator)
    {
        if (await locator.CountAsync() == 0)
        {
            return null;
        }

        var title = await locator.First.GetAttributeAsync("title");
        if (!string.IsNullOrWhiteSpace(title))
        {
            return StripEvaluatePrefix(title);
        }

        var aria = await locator.First.GetAttributeAsync("aria-label");
        if (!string.IsNullOrWhiteSpace(aria))
        {
            return StripEvaluatePrefix(aria);
        }

        var text = (await locator.First.InnerTextAsync()).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : StripEvaluatePrefix(text);
    }

    private static async Task<string?> ReadHrefAsync(ILocator locator)
    {
        if (await locator.CountAsync() == 0)
        {
            return null;
        }

        return await locator.First.GetAttributeAsync("href");
    }

    private static string StripEvaluatePrefix(string value)
        => value.StartsWith("Evaluate ", StringComparison.OrdinalIgnoreCase)
            ? value["Evaluate ".Length..].Trim()
            : value.Trim();

    private static string ResolvePath(string path)
        => Path.GetFullPath(path, Directory.GetCurrentDirectory());

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions));
    }
}

internal sealed record QuickEvalSubmission(
    int Index,
    string? Student,
    string? ActivityName,
    string? SubmittedAt,
    string? EvaluationUrl,
    IReadOnlyList<string> Urls);

internal sealed record QuickEvalListResult(
    string Scraper,
    DateTimeOffset ScrapedAt,
    string PageUrl,
    int SubmissionCount,
    IReadOnlyList<QuickEvalSubmission> Submissions);

internal sealed record SubmissionDetailResult(
    string Scraper,
    DateTimeOffset ScrapedAt,
    string PageTitle,
    string PageUrl,
    string? RepoUrl,
    string? Owner,
    string? Repo,
    string? CloneUrl,
    string? BranchHint,
    string? SubdirHint,
    IReadOnlyList<string> Urls,
    string RawText);

internal sealed record GitHubHints(
    string? Owner,
    string? Repo,
    string? CloneUrl,
    string? BranchHint,
    string? SubdirHint);

internal static class GitHubHintParser
{
    public static GitHubHints Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new GitHubHints(null, null, null, null, null);
        }

        if (!uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return new GitHubHints(null, null, null, null, null);
        }

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var owner = parts.ElementAtOrDefault(0);
        var repo = parts.ElementAtOrDefault(1)?.Replace(".git", string.Empty, StringComparison.OrdinalIgnoreCase);
        string? branchHint = null;
        string? subdirHint = null;

        if (parts.Length > 3 && string.Equals(parts[2], "tree", StringComparison.OrdinalIgnoreCase))
        {
            branchHint = Uri.UnescapeDataString(parts[3]);
            subdirHint = parts.Length > 4 ? string.Join('/', parts[4..].Select(Uri.UnescapeDataString)) : null;
        }
        else if (parts.Length > 4 && string.Equals(parts[2], "blob", StringComparison.OrdinalIgnoreCase))
        {
            branchHint = Uri.UnescapeDataString(parts[3]);
            subdirHint = parts.Length > 5 ? string.Join('/', parts[4..^1].Select(Uri.UnescapeDataString)) : null;
        }

        return new GitHubHints(
            owner,
            repo,
            owner is not null && repo is not null ? $"https://github.com/{owner}/{repo}.git" : null,
            branchHint,
            subdirHint);
    }
}
