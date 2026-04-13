using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace InsTK.Core;

internal sealed class BrightspaceAutomationService : IBrightspaceAutomationService
{
    private readonly AppConfig config;

    public BrightspaceAutomationService(IAppConfigProvider configProvider)
    {
        config = configProvider.Current;
    }

    public async Task<int> LoginAsync(LoginCommand command, ICommandHost host)
    {
        var url = command.Url ?? config.QuickEvalUrl ?? throw new InvalidOperationException("Missing login URL.");
        var statePath = CorePaths.ResolvePath(command.StatePath ?? config.StatePath ?? throw new InvalidOperationException("Missing state path."));
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright, command.Channel, headless: false);

        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        host.Out.WriteLine($"Opening {url}");
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        host.Out.WriteLine("Complete the Brightspace login in the browser, then press Enter here to save the session.");
        host.In.ReadLine();

        await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = statePath });
        host.Out.WriteLine($"Saved session state to {statePath}");
        await browser.CloseAsync();
        return 0;
    }

    public async Task<int> ScrapeQuickEvalAsync(ScrapeQuickEvalCommand command, ICommandHost host)
    {
        var url = command.Url ?? config.QuickEvalUrl ?? throw new InvalidOperationException("Missing Quick Eval URL.");
        var statePath = CorePaths.ResolvePath(command.StatePath ?? config.StatePath ?? throw new InvalidOperationException("Missing state path."));
        var outPath = CorePaths.ResolvePath(command.OutPath ?? config.QuickEvalOutPath ?? "_grading/quickeval-live.json");
        var scrapeAllPages = command.ScrapeAllPages;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright, command.Channel, headless: true);

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = statePath,
        });

        var page = await context.NewPageAsync();
        var submissions = await ScrapeQuickEvalSubmissionsAsync(page, url, scrapeAllPages, host);
        var rowCount = submissions.Count;

        var result = new QuickEvalListResult(
            InsTkDefaults.ScraperName,
            DateTimeOffset.UtcNow,
            url,
            rowCount,
            submissions);

        await JsonFileStore.WriteAsync(outPath, result);
        host.Out.WriteLine($"Wrote {rowCount} submissions to {outPath}");
        return 0;
    }

    public async Task<int> ScrapeSubmissionAsync(ScrapeSubmissionCommand command, ICommandHost host)
    {
        var url = command.Url ?? config.SubmissionUrl ?? throw new InvalidOperationException("Missing submission URL.");
        var statePath = CorePaths.ResolvePath(command.StatePath ?? config.StatePath ?? throw new InvalidOperationException("Missing state path."));
        var outPath = CorePaths.ResolvePath(command.OutPath ?? config.SubmissionOutPath ?? "_grading/submission-live.json");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright, command.Channel, headless: true);

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = statePath,
        });

        var page = await context.NewPageAsync();
        var result = await ScrapeSubmissionDetailAsync(page, url);

        await JsonFileStore.WriteAsync(outPath, result);
        host.Out.WriteLine($"Wrote submission detail to {outPath}");
        return 0;
    }

    public async Task<int> ScrapeSubmissionMapAsync(ScrapeSubmissionMapCommand command, ICommandHost host)
    {
        var url = command.Url ?? config.QuickEvalUrl ?? throw new InvalidOperationException("Missing Quick Eval URL.");
        var statePath = CorePaths.ResolvePath(command.StatePath ?? config.StatePath ?? throw new InvalidOperationException("Missing state path."));
        var outPath = CorePaths.ResolvePath(command.OutPath ?? config.SubmissionMapOutPath ?? "_grading/submission-map.json");
        var limit = command.Limit;
        var scrapeAllPages = command.ScrapeAllPages;

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright, command.Channel, headless: true);

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = statePath,
        });

        var quickEvalPage = await context.NewPageAsync();
        var quickEvalSubmissions = await ScrapeQuickEvalSubmissionsAsync(quickEvalPage, url, scrapeAllPages, host);
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

            host.Out.WriteLine($"Scraping submission {i + 1} of {targetSubmissions.Count}: {submission.Student} - {submission.ActivityName}");

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
            InsTkDefaults.ArtifactSchemaVersion,
            InsTkDefaults.ScraperName,
            DateTimeOffset.UtcNow,
            url,
            quickEvalSubmissions.Count,
            entries.Count,
            entries);

        await JsonFileStore.WriteAsync(outPath, result);
        host.Out.WriteLine($"Wrote {entries.Count} merged submissions to {outPath}");
        return 0;
    }

    private static async Task<List<QuickEvalSubmission>> ScrapeQuickEvalSubmissionsAsync(IPage page, string url, bool scrapeAllPages, ICommandHost host)
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

            var moved = await TryAdvanceQuickEvalPageAsync(page, pageNumber, host);
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

    private static async Task<bool> TryAdvanceQuickEvalPageAsync(IPage page, int pageNumber, ICommandHost host)
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

            host.Out.WriteLine($"Advancing Quick Eval page {pageNumber + 1} using selector {selector}");
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
            InsTkDefaults.ScraperName,
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

    private async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright, string? channelOverride, bool headless)
    {
        var channel = channelOverride ?? config.BrowserChannel ?? GetDefaultBrowserChannel();

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
}
