using System.Text.RegularExpressions;

namespace InsTK.Core;

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
    ProgramAssignmentInfo? Program,
    AssignmentRubricInfo? Rubric);

internal sealed record TutorialAssignmentInfo(
    string? SeriesUrl,
    string? TargetUrl,
    TutorialSourceInfo? Source,
    string? Notes);

internal sealed record TutorialSourceInfo(
    string Type,
    string Location,
    string? Label);

internal sealed record ProgramAssignmentInfo(
    string CompetencyFolder,
    string? SpecPath,
    string? Notes);

internal sealed record AssignmentRubricInfo(
    string? GradingApproach,
    string? Summary,
    IReadOnlyList<RubricCriterionInfo>? Criteria);

internal sealed record RubricCriterionInfo(
    string Id,
    string Description,
    int Points,
    IReadOnlyList<string>? EvidenceHints,
    string? FrameworkNotes);

internal sealed record GradingWorkItem(
    int Index,
    string? Student,
    string? ActivityName,
    string ActivityType,
    string AssignmentKey,
    string SubmissionStatus,
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
    string SubmissionStatus,
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
    string SubmissionStatus,
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
    string? TutorialSourceType,
    string? TutorialSourceLocation,
    string? CompetencyFolderPath,
    string? SpecPath,
    AssignmentRubricInfo? Rubric,
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
    private static readonly Regex ProgramCodePrefixRegex = new(
        @"^\s*(?:P|E)\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
                || activityName.Contains("Competency", StringComparison.OrdinalIgnoreCase)
                || ProgramCodePrefixRegex.IsMatch(activityName));
}

internal static class SubmissionStatusClassifier
{
    private static readonly Regex ExtensionRequestRegex = new(
        @"\b(extension|extra\s+time|more\s+time|late\s+pass|need\s+more\s+time)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Classify(string? rawText, string? cloneUrl)
    {
        if (IsExtensionRequest(rawText) && string.IsNullOrWhiteSpace(cloneUrl))
        {
            return "extension-request";
        }

        return string.IsNullOrWhiteSpace(cloneUrl)
            ? "missing-repo-link"
            : "ready-to-grade";
    }

    public static string ClassifyCloneFailure(string submissionStatus, string? prepError)
    {
        if (!string.Equals(submissionStatus, "ready-to-grade", StringComparison.OrdinalIgnoreCase))
        {
            return submissionStatus;
        }

        if (string.IsNullOrWhiteSpace(prepError))
        {
            return submissionStatus;
        }

        return prepError.Contains("repository not found", StringComparison.OrdinalIgnoreCase)
            ? "bad-repo-link"
            : submissionStatus;
    }

    private static bool IsExtensionRequest(string? rawText)
        => !string.IsNullOrWhiteSpace(rawText) && ExtensionRequestRegex.IsMatch(rawText);
}
