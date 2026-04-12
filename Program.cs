using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
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
        "scrape-submission-map" => await BrightspaceCli.ScrapeSubmissionMapAsync(options),
        "build-grading-worklist" => await BrightspaceCli.BuildGradingWorklistAsync(options),
        "prepare-grading-repos" => await BrightspaceCli.PrepareGradingReposAsync(options),
        "build-grading-runner" => await BrightspaceCli.BuildGradingRunnerAsync(options),
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
          scrape-submission-map
            Load the Quick Eval page, visit each evaluation URL, and export merged row plus detail data.
          build-grading-worklist
            Join a submission map with an assignment registry and export a grading worklist.
          prepare-grading-repos
            Clone or update repos from a grading worklist and export a repo-ready grading queue.
          build-grading-runner
            Build a Codex-ready grading run queue with prompts, report paths, and tutorial/spec context.

        Examples
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- login
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- scrape-quickeval
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- scrape-quickeval --first-page-only
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- scrape-submission --url "https://mycourses.cnm.edu/d2l/le/activities/iterator/..."
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- scrape-submission-map --limit 5
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- build-grading-worklist --registry "C:\Users\Rob011235\Dropbox\CNM\_Curriculum\CIST 2284 .NET II\_grading\assignment-registry.json"
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- prepare-grading-repos --limit 5
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- build-grading-runner --limit 5

        Config
          Put shared defaults in brightspacecli.json, for example:
            {
              "browserChannel": "msedge",
              "quickEvalUrl": "https://mycourses.cnm.edu/d2l/le/224618/quickeval/",
              "statePath": ".brightspace/session.json",
              "quickEvalOutPath": "_grading/quickeval-live.json",
              "submissionOutPath": "_grading/submission-live.json",
              "submissionMapOutPath": "_grading/submission-map.json",
              "assignmentRegistryPath": "C:\\grading\\assignment-registry.json",
              "gradingWorklistOutPath": "C:\\grading\\grading-worklist.json",
              "gradingRepoRoot": "C:\\grading\\repos",
              "gradingRepoQueueOutPath": "C:\\grading\\grading-repo-queue.json",
              "courseRootPath": "C:\\Users\\Rob011235\\Dropbox\\CNM\\_Curriculum\\CIST 2284 .NET II",
              "gradingRunRoot": "C:\\grading\\runs",
              "gradingRunnerOutPath": "C:\\grading\\grading-runner.json"
            }
          Command-line values still override config values for a single run.
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

    public bool HasFlag(string name)
        => string.Equals(Get(name), "true", StringComparison.OrdinalIgnoreCase);

    public string Require(string name)
        => Get(name) ?? throw new InvalidOperationException($"Missing required option --{name}");

    public string GetOrDefault(string name, string? fallback)
        => Get(name) ?? fallback ?? throw new InvalidOperationException($"Missing required option --{name}");
}

internal static class BrightspaceCli
{
    private const string ArtifactSchemaVersion = "1.0";
    private static readonly AppConfig Config = AppConfig.Load();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<int> LoginAsync(CommandLineOptions options)
    {
        var url = options.GetOrDefault("url", Config.QuickEvalUrl);
        var statePath = ResolvePath(options.GetOrDefault("state", Config.StatePath));
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright, options, headless: false);

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
        var url = options.GetOrDefault("url", Config.QuickEvalUrl);
        var statePath = ResolvePath(options.GetOrDefault("state", Config.StatePath));
        var outPath = ResolvePath(options.Get("out") ?? Config.QuickEvalOutPath ?? "_grading/quickeval-live.json");
        var scrapeAllPages = !options.HasFlag("first-page-only");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright, options, headless: true);

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = statePath,
        });

        var page = await context.NewPageAsync();
        var submissions = await ScrapeQuickEvalSubmissionsAsync(page, url, scrapeAllPages);
        var rowCount = submissions.Count;

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
        var url = options.GetOrDefault("url", Config.SubmissionUrl);
        var statePath = ResolvePath(options.GetOrDefault("state", Config.StatePath));
        var outPath = ResolvePath(options.Get("out") ?? Config.SubmissionOutPath ?? "_grading/submission-live.json");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright, options, headless: true);

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = statePath,
        });

        var page = await context.NewPageAsync();
        var result = await ScrapeSubmissionDetailAsync(page, url);

        await WriteJsonAsync(outPath, result);
        Console.WriteLine($"Wrote submission detail to {outPath}");
        return 0;
    }

    public static async Task<int> ScrapeSubmissionMapAsync(CommandLineOptions options)
    {
        var url = options.GetOrDefault("url", Config.QuickEvalUrl);
        var statePath = ResolvePath(options.GetOrDefault("state", Config.StatePath));
        var outPath = ResolvePath(options.Get("out") ?? Config.SubmissionMapOutPath ?? "_grading/submission-map.json");
        var limit = ParseOptionalInt(options.Get("limit"), "limit");
        var scrapeAllPages = !options.HasFlag("first-page-only");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright, options, headless: true);

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = statePath,
        });

        var quickEvalPage = await context.NewPageAsync();
        var quickEvalSubmissions = await ScrapeQuickEvalSubmissionsAsync(quickEvalPage, url, scrapeAllPages);
        var targetSubmissions = limit.HasValue
            ? quickEvalSubmissions.Take(limit.Value).ToList()
            : quickEvalSubmissions;

        var entries = new List<SubmissionMapEntry>();

        for (var i = 0; i < targetSubmissions.Count; i++)
        {
            var submission = targetSubmissions[i];
            if (string.IsNullOrWhiteSpace(submission.EvaluationUrl))
            {
                entries.Add(new SubmissionMapEntry(
                    submission.Index,
                    submission.Student,
                    submission.ActivityName,
                    submission.ActivityType,
                    submission.AssignmentKey,
                    submission.SubmittedAt,
                    submission.EvaluationUrl,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    Array.Empty<string>(),
                    string.Empty,
                    "Missing evaluation URL."));
                continue;
            }

            Console.WriteLine($"Scraping submission {i + 1} of {targetSubmissions.Count}: {submission.Student} - {submission.ActivityName}");

            var detailPage = await context.NewPageAsync();
            SubmissionDetailResult? detail = null;
            string? error = null;

            try
            {
                detail = await ScrapeSubmissionDetailAsync(detailPage, submission.EvaluationUrl);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                await detailPage.CloseAsync();
            }

            entries.Add(new SubmissionMapEntry(
                submission.Index,
                submission.Student,
                submission.ActivityName,
                submission.ActivityType,
                submission.AssignmentKey,
                submission.SubmittedAt,
                submission.EvaluationUrl,
                detail?.PageTitle,
                detail?.PreviewUrl,
                detail?.RepoUrl,
                detail?.Owner,
                detail?.Repo,
                detail?.CloneUrl,
                detail?.BranchHint,
                detail?.SubdirHint,
                detail?.AssignmentPathHint,
                detail?.Urls ?? Array.Empty<string>(),
                detail?.RawText ?? string.Empty,
                error));
        }

        var result = new SubmissionMapResult(
            ArtifactSchemaVersion,
            "BrightspaceCli",
            DateTimeOffset.UtcNow,
            url,
            quickEvalSubmissions.Count,
            entries.Count,
            entries);

        await WriteJsonAsync(outPath, result);
        Console.WriteLine($"Wrote {entries.Count} merged submissions to {outPath}");
        return 0;
    }

    public static async Task<int> BuildGradingWorklistAsync(CommandLineOptions options)
    {
        var submissionMapPath = ResolvePath(options.Get("submission-map") ?? Config.SubmissionMapOutPath ?? "_grading/submission-map.json");
        var registryPath = ResolvePath(options.GetOrDefault("registry", Config.AssignmentRegistryPath));
        var outPath = ResolvePath(options.Get("out") ?? Config.GradingWorklistOutPath ?? "_grading/grading-worklist.json");

        if (!File.Exists(submissionMapPath))
        {
            throw new InvalidOperationException($"Submission map not found: {submissionMapPath}");
        }

        if (!File.Exists(registryPath))
        {
            throw new InvalidOperationException($"Assignment registry not found: {registryPath}");
        }

        var submissionMap = await ReadJsonAsync<SubmissionMapResult>(submissionMapPath);
        var registry = await ReadJsonAsync<AssignmentRegistry>(registryPath);
        var assignmentIndex = registry.Assignments.ToDictionary(
            assignment => assignment.AssignmentKey,
            assignment => assignment,
            StringComparer.OrdinalIgnoreCase);

        var items = submissionMap.Submissions
            .Select(submission =>
            {
                assignmentIndex.TryGetValue(submission.AssignmentKey, out var assignment);
                var gradingMode = assignment is null
                    ? "unmapped"
                    : submission.ActivityType == "program"
                        ? "program-spec"
                        : "tutorial-follow";

                var selectedFolderHint = submission.AssignmentPathHint ?? submission.SubdirHint;
                return new GradingWorkItem(
                    submission.Index,
                    submission.Student,
                    submission.ActivityName,
                    submission.ActivityType,
                    submission.AssignmentKey,
                    gradingMode,
                    submission.RepoUrl,
                    submission.CloneUrl,
                    submission.EvaluationUrl,
                    submission.PreviewUrl,
                    submission.BranchHint,
                    submission.SubdirHint,
                    submission.AssignmentPathHint,
                    selectedFolderHint,
                    submission.RawText,
                    assignment is not null,
                    assignment);
            })
            .ToList();

        var result = new GradingWorklistResult(
            ArtifactSchemaVersion,
            DateTimeOffset.UtcNow,
            submissionMapPath,
            registryPath,
            items.Count,
            items.Count(static item => !item.RegistryMatched),
            items);

        await WriteJsonAsync(outPath, result);
        Console.WriteLine($"Wrote {items.Count} work items to {outPath}");
        return 0;
    }

    public static async Task<int> PrepareGradingReposAsync(CommandLineOptions options)
    {
        var worklistPath = ResolvePath(options.Get("worklist") ?? Config.GradingWorklistOutPath ?? "_grading/grading-worklist.json");
        var repoRoot = ResolvePath(options.GetOrDefault("repo-root", Config.GradingRepoRoot));
        var outPath = ResolvePath(options.Get("out") ?? Config.GradingRepoQueueOutPath ?? "_grading/grading-repo-queue.json");
        var limit = ParseOptionalInt(options.Get("limit"), "limit");

        if (!File.Exists(worklistPath))
        {
            throw new InvalidOperationException($"Grading worklist not found: {worklistPath}");
        }

        Directory.CreateDirectory(repoRoot);

        var worklist = await ReadJsonAsync<GradingWorklistResult>(worklistPath);
        var items = limit.HasValue
            ? worklist.Items.Take(limit.Value).ToList()
            : worklist.Items.ToList();

        var preparedItems = new List<PreparedRepoWorkItem>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            Console.WriteLine($"Preparing repo {i + 1} of {items.Count}: {item.Student} - {item.ActivityName}");

            if (string.IsNullOrWhiteSpace(item.CloneUrl))
            {
                preparedItems.Add(new PreparedRepoWorkItem(
                    item.Index,
                    item.Student,
                    item.ActivityName,
                    item.ActivityType,
                    item.AssignmentKey,
                    item.GradingMode,
                    item.RepoUrl,
                    item.CloneUrl,
                    null,
                    null,
                    item.BranchHint,
                    item.SubdirHint,
                    item.AssignmentPathHint,
                    item.SelectedFolderHint,
                    null,
                    "Missing clone URL."));
                continue;
            }

            var repoPath = Path.Combine(repoRoot, GetRepoFolderName(item));
            string? selectedBranch = null;
            string? selectedFolderPath = null;
            string? prepError = null;

            try
            {
                await EnsureRepoAsync(item.CloneUrl, repoPath);
                var branches = await GetRepoBranchesAsync(repoPath);
                selectedBranch = ResolvePreferredBranch(item, branches);
                if (!string.IsNullOrWhiteSpace(selectedBranch))
                {
                    await CheckoutBranchAsync(repoPath, selectedBranch);
                    selectedBranch = await GetCurrentBranchAsync(repoPath);
                }
                else
                {
                    selectedBranch = await GetCurrentBranchAsync(repoPath);
                }

                selectedFolderPath = ResolveSelectedFolderPath(repoPath, item.SelectedFolderHint);
            }
            catch (Exception ex)
            {
                prepError = ex.Message;
            }

            preparedItems.Add(new PreparedRepoWorkItem(
                item.Index,
                item.Student,
                item.ActivityName,
                item.ActivityType,
                item.AssignmentKey,
                item.GradingMode,
                item.RepoUrl,
                item.CloneUrl,
                repoPath,
                selectedBranch,
                item.BranchHint,
                item.SubdirHint,
                item.AssignmentPathHint,
                item.SelectedFolderHint,
                selectedFolderPath,
                prepError));
        }

        var result = new PreparedRepoQueueResult(
            ArtifactSchemaVersion,
            DateTimeOffset.UtcNow,
            worklistPath,
            repoRoot,
            preparedItems.Count,
            preparedItems.Count(static item => !string.IsNullOrWhiteSpace(item.Error)),
            preparedItems);

        await WriteJsonAsync(outPath, result);
        Console.WriteLine($"Wrote {preparedItems.Count} prepared repo items to {outPath}");
        return 0;
    }

    public static async Task<int> BuildGradingRunnerAsync(CommandLineOptions options)
    {
        var repoQueuePath = ResolvePath(options.Get("repo-queue") ?? Config.GradingRepoQueueOutPath ?? "_grading/grading-repo-queue.json");
        var courseRoot = ResolvePath(options.GetOrDefault("course-root", Config.CourseRootPath));
        var runRoot = ResolvePath(options.GetOrDefault("run-root", Config.GradingRunRoot));
        var outPath = ResolvePath(options.Get("out") ?? Config.GradingRunnerOutPath ?? Path.Combine(runRoot, "grading-runner.json"));
        var limit = ParseOptionalInt(options.Get("limit"), "limit");

        if (!File.Exists(repoQueuePath))
        {
            throw new InvalidOperationException($"Prepared repo queue not found: {repoQueuePath}");
        }

        if (!Directory.Exists(courseRoot))
        {
            throw new InvalidOperationException($"Course root not found: {courseRoot}");
        }

        var repoQueue = await ReadJsonAsync<PreparedRepoQueueResult>(repoQueuePath);
        if (!File.Exists(repoQueue.WorklistPath))
        {
            throw new InvalidOperationException($"Referenced grading worklist not found: {repoQueue.WorklistPath}");
        }

        var worklist = await ReadJsonAsync<GradingWorklistResult>(repoQueue.WorklistPath);
        if (!File.Exists(worklist.RegistryPath))
        {
            throw new InvalidOperationException($"Referenced assignment registry not found: {worklist.RegistryPath}");
        }

        var registry = await ReadJsonAsync<AssignmentRegistry>(worklist.RegistryPath);
        var worklistIndex = worklist.Items.ToDictionary(GetWorkItemKey, StringComparer.OrdinalIgnoreCase);
        var registryIndex = registry.Assignments.ToDictionary(
            assignment => assignment.AssignmentKey,
            assignment => assignment,
            StringComparer.OrdinalIgnoreCase);

        var promptsRoot = Path.Combine(runRoot, "prompts");
        var reportsRoot = Path.Combine(runRoot, "reports");
        Directory.CreateDirectory(promptsRoot);
        Directory.CreateDirectory(reportsRoot);

        var items = limit.HasValue
            ? repoQueue.Items.Take(limit.Value).ToList()
            : repoQueue.Items.ToList();

        var runItems = new List<GradingRunItem>();

        for (var i = 0; i < items.Count; i++)
        {
            var queueItem = items[i];
            Console.WriteLine($"Building grading run {i + 1} of {items.Count}: {queueItem.Student} - {queueItem.ActivityName}");

            worklistIndex.TryGetValue(GetWorkItemKey(queueItem), out var workItem);
            AssignmentRegistryEntry? assignment = workItem?.Registry;
            if (assignment is null && !string.IsNullOrWhiteSpace(queueItem.AssignmentKey))
            {
                registryIndex.TryGetValue(queueItem.AssignmentKey, out assignment);
            }

            var assignmentKeySegment = SanitizePathSegment(queueItem.AssignmentKey) ?? "unknown-assignment";
            var studentSegment = SanitizePathSegment(queueItem.Student) ?? $"item-{queueItem.Index}";
            var promptPath = Path.Combine(promptsRoot, assignmentKeySegment, $"{studentSegment}.md");
            var reportPath = Path.Combine(reportsRoot, assignmentKeySegment, $"{studentSegment}.md");
            Directory.CreateDirectory(Path.GetDirectoryName(promptPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

            var gradingTargetPath = queueItem.SelectedFolderPath ?? queueItem.RepoPath;
            var tutorialUrl = assignment?.Tutorial is null
                ? null
                : !string.IsNullOrWhiteSpace(assignment.Tutorial.TargetUrl)
                    ? assignment.Tutorial.TargetUrl
                    : assignment.Tutorial.SeriesUrl;
            var competencyFolderPath = ResolveCompetencyFolderPath(courseRoot, assignment?.Program);
            var specPath = ResolveSpecPath(courseRoot, assignment?.Program);
            var courseAgentsPath = ResolveOptionalFilePath(Path.Combine(courseRoot, "AGENTS.md"));
            var assignmentAgentsPath = ResolveOptionalFilePath(
                competencyFolderPath is null ? null : Path.Combine(competencyFolderPath, "AGENTS.md"));
            var prompt = BuildGradingPrompt(
                queueItem,
                workItem,
                assignment,
                courseRoot,
                courseAgentsPath,
                assignmentAgentsPath,
                tutorialUrl,
                competencyFolderPath,
                specPath,
                gradingTargetPath,
                reportPath);

            await File.WriteAllTextAsync(promptPath, prompt);

            string? error = queueItem.Error;
            if (string.IsNullOrWhiteSpace(error))
            {
                if (workItem is null)
                {
                    error = "Prepared repo item could not be matched back to the grading worklist.";
                }
                else if (assignment is null)
                {
                    error = "Assignment registry entry not found for this work item.";
                }
                else if (string.IsNullOrWhiteSpace(gradingTargetPath))
                {
                    error = "Missing repo path and selected folder path.";
                }
            }

            runItems.Add(new GradingRunItem(
                queueItem.Index,
                queueItem.Student,
                queueItem.ActivityName,
                queueItem.ActivityType,
                queueItem.AssignmentKey,
                queueItem.GradingMode,
                queueItem.RepoUrl,
                queueItem.CloneUrl,
                queueItem.RepoPath,
                queueItem.SelectedBranch,
                queueItem.SelectedFolderPath,
                gradingTargetPath,
                workItem?.EvaluationUrl,
                workItem?.PreviewUrl,
                tutorialUrl,
                competencyFolderPath,
                specPath,
                courseAgentsPath,
                assignmentAgentsPath,
                promptPath,
                reportPath,
                error));
        }

        var result = new GradingRunnerResult(
            ArtifactSchemaVersion,
            DateTimeOffset.UtcNow,
            repoQueuePath,
            repoQueue.WorklistPath,
            worklist.RegistryPath,
            courseRoot,
            runRoot,
            runItems.Count,
            runItems.Count(static item => !string.IsNullOrWhiteSpace(item.Error)),
            runItems);

        await WriteJsonAsync(outPath, result);
        Console.WriteLine($"Wrote {runItems.Count} grading run items to {outPath}");
        return 0;
    }

    private static async Task<List<QuickEvalSubmission>> ScrapeQuickEvalSubmissionsAsync(IPage page, string url, bool scrapeAllPages)
    {
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        var submissions = new List<QuickEvalSubmission>();
        var seenEvaluationUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pageNumber = 1;

        while (true)
        {
            var currentPageRows = await ReadQuickEvalRowsAsync(page, url);

            foreach (var row in currentPageRows)
            {
                if (string.IsNullOrWhiteSpace(row.EvaluationUrl) || seenEvaluationUrls.Add(row.EvaluationUrl))
                {
                    submissions.Add(row with { Index = submissions.Count });
                }
            }

            if (!scrapeAllPages)
            {
                break;
            }

            var moved = await TryAdvanceQuickEvalPageAsync(page, pageNumber);
            if (!moved)
            {
                break;
            }

            pageNumber++;
        }

        return submissions;
    }

    private static async Task<List<QuickEvalSubmission>> ReadQuickEvalRowsAsync(IPage page, string url)
    {
        var pageUri = new Uri(url, UriKind.Absolute);
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
            var evaluationUrl = ToAbsoluteUrl(pageUri, await ReadHrefAsync(evaluationNode));
            var activityType = AssignmentClassifier.GetActivityType(activityName);
            var assignmentKey = AssignmentClassifier.GetAssignmentKey(activityName, activityType);

            submissions.Add(new QuickEvalSubmission(
                i,
                student,
                activityName,
                activityType,
                assignmentKey,
                submittedAt,
                evaluationUrl,
                [.. new[] { evaluationUrl }.Where(static value => !string.IsNullOrWhiteSpace(value))!]));
        }

        return submissions;
    }

    private static async Task<bool> TryAdvanceQuickEvalPageAsync(IPage page, int pageNumber)
    {
        var beforeSignature = await GetQuickEvalPageSignatureAsync(page);
        var beforeRowCount = await GetQuickEvalRowCountAsync(page);
        var candidates = new[]
        {
            "d2l-button.d2l-quick-eval-submissions-table-load-more",
            "button:has-text('Load More')",
            "a:has-text('Load More')",
            "button:has-text('Load more')",
            "a:has-text('Load more')",
            "button:has-text('Next')",
            "a:has-text('Next')",
            "[aria-label='Next']",
            "[title='Next']",
        };

        foreach (var selector in candidates)
        {
            var candidate = page.Locator(selector).First;
            if (!await IsActionableAsync(candidate))
            {
                continue;
            }

            Console.WriteLine($"Advancing Quick Eval page {pageNumber + 1} using selector {selector}");
            await candidate.ClickAsync();

            try
            {
                await page.WaitForFunctionAsync(
                    """
                    previousRowCount => {
                      const rows = document.querySelectorAll('d2l-quick-eval-submissions-table tbody > tr');
                      return rows.length > previousRowCount;
                    }
                    """,
                    beforeRowCount,
                    new PageWaitForFunctionOptions { Timeout = 5000 });
            }
            catch (TimeoutException)
            {
                var afterRowCount = await GetQuickEvalRowCountAsync(page);
                var afterSignature = await GetQuickEvalPageSignatureAsync(page);
                if (afterRowCount <= beforeRowCount && afterSignature == beforeSignature)
                {
                    continue;
                }
            }

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            return true;
        }

        return false;
    }

    private static async Task<int> GetQuickEvalRowCountAsync(IPage page)
        => await page.Locator("d2l-quick-eval-submissions-table tbody > tr").CountAsync();

    private static async Task<string> GetQuickEvalPageSignatureAsync(IPage page)
        => await page.EvaluateAsync<string>(
            """
            () => {
              const rows = [...document.querySelectorAll('d2l-quick-eval-submissions-table tbody > tr')];
              const links = rows.map(row => {
                const anchor = row.querySelector('d2l-link.d2l-quick-eval-submissions-table-name-link a');
                return anchor?.getAttribute('href') || '';
              });

              return links.join('|');
            }
            """);

    private static async Task<bool> IsActionableAsync(ILocator locator)
    {
        if (await locator.CountAsync() == 0 || !await locator.IsVisibleAsync())
        {
            return false;
        }

        if (await locator.IsDisabledAsync())
        {
            return false;
        }

        var ariaDisabled = await locator.GetAttributeAsync("aria-disabled");
        return !string.Equals(ariaDisabled, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<SubmissionDetailResult> ScrapeSubmissionDetailAsync(IPage page, string url)
    {
        var pageUri = new Uri(url, UriKind.Absolute);
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var title = await page.TitleAsync();
        var bodyText = await ReadSubmissionTextAsync(page);
        var previewUrl = await ReadPreviewUrlAsync(page, pageUri);
        var links = await page.Locator("a[href]").EvaluateAllAsync<string[]>(
            "nodes => nodes.map(n => n.href).filter(Boolean)");

        var uniqueLinks = links
            .Where(static value => IsUsefulUrl(value))
            .Where(static value => !IsQuickEvalReturnUrl(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var repoUrl = uniqueLinks.FirstOrDefault(static value =>
            value.Contains("github.com/", StringComparison.OrdinalIgnoreCase));

        var github = GitHubHintParser.Parse(repoUrl);
        return new SubmissionDetailResult(
            "BrightspaceCli",
            DateTimeOffset.UtcNow,
            title,
            url,
            previewUrl,
            repoUrl,
            github.Owner,
            github.Repo,
            github.CloneUrl,
            github.BranchHint,
            github.SubdirHint,
            AssignmentPathHintParser.Parse(bodyText),
            uniqueLinks,
            bodyText);
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

    private static string? ToAbsoluteUrl(Uri pageUri, string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        return Uri.TryCreate(pageUri, href, out var absoluteUri)
            ? absoluteUri.ToString()
            : href;
    }

    private static bool IsUsefulUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsQuickEvalReturnUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.AbsolutePath.Contains("/quickeval/", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadSubmissionTextAsync(IPage page)
    {
        var submissionItem = page.Locator("d2l-consistent-evaluation-assignments-submission-item");
        if (await submissionItem.CountAsync() > 0)
        {
            var commentHtml = await submissionItem.First.GetAttributeAsync("comment");
            var commentText = ExtractTextFromHtml(commentHtml);
            if (!string.IsNullOrWhiteSpace(commentText))
            {
                return commentText;
            }
        }

        var submissionBlock = page.Locator("d2l-html-block.d2l-submission-item-text");
        if (await submissionBlock.CountAsync() > 0)
        {
            var html = await submissionBlock.First.GetAttributeAsync("html");
            var text = ExtractTextFromHtml(html);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        var selectors = new[]
        {
            "d2l-html-block",
            "d2l-assignment-submission-view",
            "d2l-assignment-evaluation",
            "main",
            "body",
        };

        foreach (var selector in selectors)
        {
            var locator = page.Locator(selector);
            if (await locator.CountAsync() == 0)
            {
                continue;
            }

            var text = NormalizeWhitespace(await locator.First.InnerTextAsync());
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        var fullText = await page.EvaluateAsync<string>(
            """
            () => {
              const seen = new Set();
              const parts = [];
              const nodes = document.querySelectorAll('textarea, input[type="text"], [data-automation], [aria-label], [title]');

              for (const node of nodes) {
                const candidates = [];

                if (node instanceof HTMLTextAreaElement || node instanceof HTMLInputElement) {
                  candidates.push(node.value);
                }

                candidates.push(node.getAttribute('data-automation'));
                candidates.push(node.getAttribute('aria-label'));
                candidates.push(node.getAttribute('title'));
                candidates.push(node.textContent);

                for (const candidate of candidates) {
                  const value = candidate?.trim();
                  if (!value || seen.has(value)) {
                    continue;
                  }

                  seen.add(value);
                  parts.push(value);
                }
              }

              return parts.join('\n');
            }
            """);

        return NormalizeWhitespace(fullText);
    }

    private static async Task<string?> ReadPreviewUrlAsync(IPage page, Uri pageUri)
    {
        var selectors = new[]
        {
            "d2l-consistent-evaluation-page",
            "consistent-evaluation-right-panel",
        };

        foreach (var selector in selectors)
        {
            var locator = page.Locator(selector);
            if (await locator.CountAsync() == 0)
            {
                continue;
            }

            var previewPath = await locator.First.GetAttributeAsync("preview-activity-path");
            var previewUrl = ToAbsoluteUrl(pageUri, previewPath);
            if (!string.IsNullOrWhiteSpace(previewUrl))
            {
                return previewUrl;
            }
        }

        return null;
    }

    private static string ExtractTextFromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var decoded = System.Net.WebUtility.HtmlDecode(html);
        if (string.IsNullOrWhiteSpace(decoded))
        {
            return string.Empty;
        }

        var withLineBreaks = Regex.Replace(decoded, @"<(br|/p|/div|/li)\b[^>]*>", Environment.NewLine, RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withLineBreaks, "<[^>]+>", " ");
        return NormalizeWhitespace(withoutTags);
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static line => !string.IsNullOrWhiteSpace(line)));
    }

    private static int? ParseOptionalInt(string? value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"Option --{optionName} must be a positive integer.");
        }

        return parsed;
    }

    private static string GetRepoFolderName(GradingWorkItem item)
    {
        var studentPart = SanitizePathSegment(item.Student) ?? "unknown-student";
        var assignmentPart = SanitizePathSegment(item.AssignmentKey) ?? "unknown-assignment";
        return $"{studentPart}__{assignmentPart}";
    }

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = Regex.Replace(value, @"[^A-Za-z0-9._-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static async Task EnsureRepoAsync(string cloneUrl, string repoPath)
    {
        if (!Directory.Exists(repoPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(repoPath)!);
            await RunGitAsync($"clone \"{cloneUrl}\" \"{repoPath}\"", Directory.GetCurrentDirectory());
            return;
        }

        await RunGitAsync("fetch --all --prune", repoPath);
    }

    private static async Task<List<string>> GetRepoBranchesAsync(string repoPath)
    {
        var output = await RunGitAsync("branch --all --format=\"%(refname:short)\"", repoPath);
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ResolvePreferredBranch(GradingWorkItem item, IReadOnlyList<string> branches)
    {
        var normalizedIndex = branches
            .Select(branch => new { Branch = branch, Key = NormalizeBranchKey(branch) })
            .ToList();

        if (!string.IsNullOrWhiteSpace(item.BranchHint))
        {
            var hinted = normalizedIndex.FirstOrDefault(branch => branch.Key == NormalizeBranchKey(item.BranchHint));
            if (hinted is not null)
            {
                return hinted.Branch;
            }
        }

        foreach (var candidate in GetDerivedBranchCandidates(item))
        {
            var match = normalizedIndex.FirstOrDefault(branch => branch.Key == NormalizeBranchKey(candidate));
            if (match is not null)
            {
                return match.Branch;
            }
        }

        var head = normalizedIndex.FirstOrDefault(static branch => branch.Key == "head");
        if (head is not null)
        {
            return null;
        }

        return null;
    }

    private static IEnumerable<string> GetDerivedBranchCandidates(GradingWorkItem item)
    {
        if (item.ActivityType == "program")
        {
            var numberMatch = Regex.Match(item.ActivityName ?? string.Empty, @"(program|competency)\s*(\d+)", RegexOptions.IgnoreCase);
            if (numberMatch.Success)
            {
                var number = numberMatch.Groups[2].Value;
                yield return $"program{number}";
                yield return $"program-{number}";
                yield return $"assignment-{number}";
                yield return $"competency-{number}";
            }
        }

        var assignmentKeyTail = item.AssignmentKey[(item.AssignmentKey.IndexOf('-') + 1)..];
        if (!string.IsNullOrWhiteSpace(assignmentKeyTail))
        {
            yield return assignmentKeyTail;
        }
    }

    private static string NormalizeBranchKey(string value)
    {
        var branch = value.Trim();
        branch = branch.StartsWith("remotes/origin/", StringComparison.OrdinalIgnoreCase)
            ? branch["remotes/origin/".Length..]
            : branch;
        return Regex.Replace(branch.ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
    }

    private static async Task CheckoutBranchAsync(string repoPath, string branch)
    {
        if (branch.StartsWith("remotes/origin/", StringComparison.OrdinalIgnoreCase))
        {
            var localBranch = branch["remotes/origin/".Length..];
            await RunGitAsync($"switch --track -C \"{localBranch}\" \"{branch}\"", repoPath);
            return;
        }

        await RunGitAsync($"switch \"{branch}\"", repoPath);
    }

    private static async Task<string?> GetCurrentBranchAsync(string repoPath)
    {
        var branch = await RunGitAsync("branch --show-current", repoPath);
        return string.IsNullOrWhiteSpace(branch) ? null : branch;
    }

    private static string? ResolveSelectedFolderPath(string repoPath, string? selectedFolderHint)
    {
        if (string.IsNullOrWhiteSpace(selectedFolderHint))
        {
            return null;
        }

        var normalizedHint = selectedFolderHint
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var normalizedRepoPath = Path.GetFullPath(repoPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var repoPathPrefix = normalizedRepoPath + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(normalizedHint, repoPath);
        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        return string.Equals(fullPath, normalizedRepoPath, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(repoPathPrefix, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    private static string GetWorkItemKey(GradingWorkItem item)
        => $"{item.Index}|{item.AssignmentKey}|{item.Student}";

    private static string GetWorkItemKey(PreparedRepoWorkItem item)
        => $"{item.Index}|{item.AssignmentKey}|{item.Student}";

    private static string? ResolveCompetencyFolderPath(string courseRoot, ProgramAssignmentInfo? program)
    {
        if (program is null || string.IsNullOrWhiteSpace(program.CompetencyFolder))
        {
            return null;
        }

        return Path.GetFullPath(program.CompetencyFolder, courseRoot);
    }

    private static string? ResolveSpecPath(string courseRoot, ProgramAssignmentInfo? program)
    {
        if (program is null || string.IsNullOrWhiteSpace(program.SpecPath))
        {
            return null;
        }

        return Path.IsPathRooted(program.SpecPath)
            ? program.SpecPath
            : Path.GetFullPath(program.SpecPath, courseRoot);
    }

    private static string? ResolveOptionalFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return File.Exists(path) ? path : null;
    }

    private static string BuildGradingPrompt(
        PreparedRepoWorkItem queueItem,
        GradingWorkItem? workItem,
        AssignmentRegistryEntry? assignment,
        string courseRoot,
        string? courseAgentsPath,
        string? assignmentAgentsPath,
        string? tutorialUrl,
        string? competencyFolderPath,
        string? specPath,
        string? gradingTargetPath,
        string reportPath)
    {
        var lines = new List<string>
        {
            "# Grading Task",
            string.Empty,
            $"Student: {queueItem.Student ?? "(unknown)"}",
            $"Assignment: {queueItem.ActivityName ?? "(unknown)"}",
            $"Activity type: {queueItem.ActivityType}",
            $"Assignment key: {queueItem.AssignmentKey}",
            $"Grading mode: {queueItem.GradingMode}",
            $"Repo URL: {queueItem.RepoUrl ?? "(missing)"}",
            $"Clone URL: {queueItem.CloneUrl ?? "(missing)"}",
            $"Repo path: {queueItem.RepoPath ?? "(missing)"}",
            $"Selected branch: {queueItem.SelectedBranch ?? "(not resolved)"}",
            $"Selected folder path: {queueItem.SelectedFolderPath ?? "(none)"}",
            $"Grading target path: {gradingTargetPath ?? "(missing)"}",
            $"Evaluation URL: {workItem?.EvaluationUrl ?? "(missing)"}",
            $"Preview URL: {workItem?.PreviewUrl ?? "(missing)"}",
            $"Course root: {courseRoot}",
            $"Course AGENTS.md: {courseAgentsPath ?? "(missing)"}",
            $"Assignment AGENTS.md: {assignmentAgentsPath ?? "(none)"}",
            $"Report path: {reportPath}",
            string.Empty,
            "## Source Context",
            string.Empty,
        };

        if (queueItem.GradingMode == "tutorial-follow")
        {
            lines.Add($"Tutorial URL: {tutorialUrl ?? "(missing)"}");
            lines.Add($"Tutorial notes: {assignment?.Tutorial?.Notes ?? "(none)"}");
            lines.Add(string.Empty);
            lines.Add("## Instructions");
            lines.Add(string.Empty);
            lines.Add("1. Open the course grading guide in the course AGENTS.md path above before inspecting the repo.");
            lines.Add("2. If an assignment-specific AGENTS.md exists, read it as well.");
            lines.Add("3. Inspect the grading target path first. Use the full repo when the selected folder path is null.");
            lines.Add("4. Compare the student implementation against the tutorial URL above. Focus on whether the same major steps, files, routes, services, and architectural moves are present.");
            lines.Add("5. Treat renamed symbols, extra features, and light restructuring as acceptable if the implementation still tracks the tutorial closely.");
            lines.Add("6. Assign a grade out of 100. Give 100 when the repository substantially implements the tutorial. Otherwise subtract only for meaningful missing or incomplete work.");
            lines.Add("7. Cite direct evidence with file references relative to the solution root when practical.");
            lines.Add("8. If you cannot verify runtime behavior, say so briefly and grade from source inspection.");
            lines.Add("9. Write the final grading report to the report path above using these sections exactly: Areas for Improvement, What You Did Well, Summary, Suggested Review.");
        }
        else
        {
            lines.Add($"Competency folder: {competencyFolderPath ?? "(missing)"}");
            lines.Add($"Spec path: {specPath ?? "(missing)"}");
            lines.Add($"Program notes: {assignment?.Program?.Notes ?? "(none)"}");
            lines.Add(string.Empty);
            lines.Add("## Instructions");
            lines.Add(string.Empty);
            lines.Add("1. Open the course grading guide in the course AGENTS.md path above before inspecting the repo.");
            lines.Add("2. If an assignment-specific AGENTS.md exists, read it as well.");
            lines.Add("3. Open the assignment spec before grading. Use the spec path above when present, otherwise inspect the competency folder.");
            lines.Add("4. Inspect the grading target path first. Use the full repo when the selected folder path is null.");
            lines.Add("5. Grade against the intent of the assignment spec, not exact implementation shape, unless the spec or AGENTS file requires strict conformance.");
            lines.Add("6. Start from 100 and subtract only the listed deductions.");
            lines.Add("7. Cite direct evidence with file references relative to the solution root when practical.");
            lines.Add("8. If you cannot verify runtime behavior, say so briefly and grade from source inspection.");
            lines.Add("9. Write the final grading report to the report path above using these sections exactly: Areas for Improvement, What You Did Well, Summary, Suggested Review.");
        }

        if (!string.IsNullOrWhiteSpace(workItem?.RawText))
        {
            lines.Add(string.Empty);
            lines.Add("## LMS Submission Text");
            lines.Add(string.Empty);
            lines.Add("```text");
            lines.Add(workItem.RawText);
            lines.Add("```");
        }

        if (!string.IsNullOrWhiteSpace(queueItem.Error))
        {
            lines.Add(string.Empty);
            lines.Add("## Prep Warning");
            lines.Add(string.Empty);
            lines.Add(queueItem.Error);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<string> RunGitAsync(string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {arguments} failed in {workingDirectory}: {stderr.Trim()}".Trim());
        }

        return stdout.Trim();
    }

    private static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright, CommandLineOptions options, bool headless)
    {
        var channel = options.Get("channel") ?? Config.BrowserChannel ?? GetDefaultBrowserChannel();

        try
        {
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = headless,
                Channel = channel,
            });
        }
        catch (PlaywrightException ex) when (!string.IsNullOrWhiteSpace(channel))
        {
            throw new InvalidOperationException(
                $"Failed to launch browser channel '{channel}'. Pass --channel chrome or --channel msedge, or install that browser.",
                ex);
        }
    }

    private static string GetDefaultBrowserChannel()
        => OperatingSystem.IsWindows() ? "msedge" : "chrome";

    private static string ResolvePath(string path)
        => Path.GetFullPath(path, Directory.GetCurrentDirectory());

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static async Task<T> ReadJsonAsync<T>(string path)
        => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize JSON from {path}");
}

internal sealed class AppConfig
{
    public string? BrowserChannel { get; init; }
    public string? QuickEvalUrl { get; init; }
    public string? SubmissionUrl { get; init; }
    public string? StatePath { get; init; }
    public string? QuickEvalOutPath { get; init; }
    public string? SubmissionOutPath { get; init; }
    public string? SubmissionMapOutPath { get; init; }
    public string? AssignmentRegistryPath { get; init; }
    public string? GradingWorklistOutPath { get; init; }
    public string? GradingRepoRoot { get; init; }
    public string? GradingRepoQueueOutPath { get; init; }
    public string? CourseRootPath { get; init; }
    public string? GradingRunRoot { get; init; }
    public string? GradingRunnerOutPath { get; init; }

    public static AppConfig Load()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "brightspacecli.json");
        if (!File.Exists(path))
        {
            return new AppConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid config file: {path}", ex);
        }
    }
}

internal sealed record QuickEvalSubmission(
    int Index,
    string? Student,
    string? ActivityName,
    string ActivityType,
    string AssignmentKey,
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
    string? PreviewUrl,
    string? RepoUrl,
    string? Owner,
    string? Repo,
    string? CloneUrl,
    string? BranchHint,
    string? SubdirHint,
    string? AssignmentPathHint,
    IReadOnlyList<string> Urls,
    string RawText);

internal sealed record SubmissionMapEntry(
    int Index,
    string? Student,
    string? ActivityName,
    string ActivityType,
    string AssignmentKey,
    string? SubmittedAt,
    string? EvaluationUrl,
    string? PageTitle,
    string? PreviewUrl,
    string? RepoUrl,
    string? Owner,
    string? Repo,
    string? CloneUrl,
    string? BranchHint,
    string? SubdirHint,
    string? AssignmentPathHint,
    IReadOnlyList<string> Urls,
    string RawText,
    string? Error);

internal sealed record SubmissionMapResult(
    string SchemaVersion,
    string Scraper,
    DateTimeOffset ScrapedAt,
    string PageUrl,
    int QuickEvalSubmissionCount,
    int ProcessedSubmissionCount,
    IReadOnlyList<SubmissionMapEntry> Submissions);

internal sealed record AssignmentRegistry(
    string Course,
    string? GeneratedFrom,
    DateTimeOffset? GeneratedAt,
    IReadOnlyList<AssignmentRegistryEntry> Assignments);

internal sealed record AssignmentRegistryEntry(
    string AssignmentKey,
    string ActivityType,
    string ActivityName,
    TutorialAssignmentInfo? Tutorial,
    ProgramAssignmentInfo? Program);

internal sealed record TutorialAssignmentInfo(
    string SeriesUrl,
    string? TargetUrl,
    string? Notes);

internal sealed record ProgramAssignmentInfo(
    string CompetencyFolder,
    string? SpecPath,
    string? Notes);

internal sealed record GradingWorkItem(
    int Index,
    string? Student,
    string? ActivityName,
    string ActivityType,
    string AssignmentKey,
    string GradingMode,
    string? RepoUrl,
    string? CloneUrl,
    string? EvaluationUrl,
    string? PreviewUrl,
    string? BranchHint,
    string? SubdirHint,
    string? AssignmentPathHint,
    string? SelectedFolderHint,
    string RawText,
    bool RegistryMatched,
    AssignmentRegistryEntry? Registry);

internal sealed record GradingWorklistResult(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string SubmissionMapPath,
    string RegistryPath,
    int ItemCount,
    int UnmappedCount,
    IReadOnlyList<GradingWorkItem> Items);

internal sealed record PreparedRepoWorkItem(
    int Index,
    string? Student,
    string? ActivityName,
    string ActivityType,
    string AssignmentKey,
    string GradingMode,
    string? RepoUrl,
    string? CloneUrl,
    string? RepoPath,
    string? SelectedBranch,
    string? BranchHint,
    string? SubdirHint,
    string? AssignmentPathHint,
    string? SelectedFolderHint,
    string? SelectedFolderPath,
    string? Error);

internal sealed record PreparedRepoQueueResult(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string WorklistPath,
    string RepoRoot,
    int ItemCount,
    int ErrorCount,
    IReadOnlyList<PreparedRepoWorkItem> Items);

internal sealed record GradingRunItem(
    int Index,
    string? Student,
    string? ActivityName,
    string ActivityType,
    string AssignmentKey,
    string GradingMode,
    string? RepoUrl,
    string? CloneUrl,
    string? RepoPath,
    string? SelectedBranch,
    string? SelectedFolderPath,
    string? GradingTargetPath,
    string? EvaluationUrl,
    string? PreviewUrl,
    string? TutorialUrl,
    string? CompetencyFolderPath,
    string? SpecPath,
    string? CourseAgentsPath,
    string? AssignmentAgentsPath,
    string PromptPath,
    string ReportPath,
    string? Error);

internal sealed record GradingRunnerResult(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string RepoQueuePath,
    string WorklistPath,
    string RegistryPath,
    string CourseRoot,
    string RunRoot,
    int ItemCount,
    int ErrorCount,
    IReadOnlyList<GradingRunItem> Items);

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

internal static class AssignmentPathHintParser
{
    private static readonly Regex CheckPathRegex = new(
        @"(?:check|use|see|look\s+at)\s+([A-Za-z0-9._\-/]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? Parse(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        var match = CheckPathRegex.Match(rawText);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value.Trim().TrimEnd('.', ',', ';', ':');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

internal static class AssignmentClassifier
{
    public static string GetActivityType(string? activityName)
        => ContainsProgramSignal(activityName) ? "program" : "tutorial";

    public static string GetAssignmentKey(string? activityName, string activityType)
    {
        if (string.IsNullOrWhiteSpace(activityName))
        {
            return $"{activityType}-unknown";
        }

        var normalized = activityName.ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"[^a-z0-9]+", "-");
        normalized = normalized.Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? $"{activityType}-unknown" : $"{activityType}-{normalized}";
    }

    private static bool ContainsProgramSignal(string? activityName)
        => !string.IsNullOrWhiteSpace(activityName)
            && (activityName.Contains("Program", StringComparison.OrdinalIgnoreCase)
                || activityName.Contains("Competency", StringComparison.OrdinalIgnoreCase));
}
