using System.Diagnostics;
using System.Text.RegularExpressions;

namespace InsTK.Core;

internal sealed class RepoPreparationService : IRepoPreparationService
{
    private readonly AppConfig config;

    public RepoPreparationService(IAppConfigProvider configProvider)
    {
        config = configProvider.Current;
    }

    public async Task<int> PrepareAsync(PrepareGradingReposCommand command, ICommandHost host)
    {
        var worklistPath = CorePaths.ResolvePath(command.WorklistPath ?? config.GradingWorklistOutPath ?? "_grading/grading-worklist.json");
        var repoRoot = CorePaths.ResolvePath(command.RepoRoot ?? config.GradingRepoRoot ?? throw new InvalidOperationException("Missing grading repo root."));
        var outPath = CorePaths.ResolvePath(command.OutPath ?? config.GradingRepoQueueOutPath ?? "_grading/grading-repo-queue.json");
        var limit = command.Limit;

        if (!File.Exists(worklistPath))
        {
            throw new InvalidOperationException($"Grading worklist not found: {worklistPath}");
        }

        Directory.CreateDirectory(repoRoot);

        var worklist = await JsonFileStore.ReadAsync<GradingWorklistResult>(worklistPath);
        var items = limit.HasValue
            ? worklist.Items.Take(limit.Value).ToList()
            : worklist.Items.ToList();

        var preparedItems = new List<PreparedRepoWorkItem>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            host.Out.WriteLine($"Preparing repo {i + 1} of {items.Count}: {item.Student} - {item.ActivityName}");

            if (!string.Equals(item.SubmissionStatus, "ready-to-grade", StringComparison.OrdinalIgnoreCase))
            {
                preparedItems.Add(new PreparedRepoWorkItem(
                    item.Index,
                    item.Student,
                    item.ActivityName,
                    item.ActivityType,
                    item.AssignmentKey,
                    item.SubmissionStatus,
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
                    null));
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.CloneUrl))
            {
                preparedItems.Add(new PreparedRepoWorkItem(
                    item.Index,
                    item.Student,
                    item.ActivityName,
                    item.ActivityType,
                    item.AssignmentKey,
                    item.SubmissionStatus,
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
            var submissionStatus = item.SubmissionStatus;

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
                submissionStatus = SubmissionStatusClassifier.ClassifyCloneFailure(item.SubmissionStatus, prepError);
            }

            preparedItems.Add(new PreparedRepoWorkItem(
                item.Index,
                item.Student,
                item.ActivityName,
                item.ActivityType,
                item.AssignmentKey,
                submissionStatus,
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
            InsTkDefaults.ArtifactSchemaVersion,
            DateTimeOffset.UtcNow,
            worklistPath,
            repoRoot,
            preparedItems.Count,
            preparedItems.Count(static item => !string.IsNullOrWhiteSpace(item.Error)),
            preparedItems);

        await JsonFileStore.WriteAsync(outPath, result);
        host.Out.WriteLine($"Wrote {preparedItems.Count} prepared repo items to {outPath}");
        return 0;
    }

    private static string GetRepoFolderName(GradingWorkItem item)
    {
        var studentPart = CorePaths.SanitizePathSegment(item.Student) ?? "unknown-student";
        var assignmentPart = CorePaths.SanitizePathSegment(item.AssignmentKey) ?? "unknown-assignment";
        return $"{studentPart}__{assignmentPart}";
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
}
