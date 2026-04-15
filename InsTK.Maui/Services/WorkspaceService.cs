using System.Text.Json;

namespace InsTK.Maui;

public sealed class WorkspaceService
{
    public WorkspaceSnapshot LoadSnapshot()
    {
        var workspaceRoot = FindWorkspaceRoot();
        var configPath = ResolveConfigPath(workspaceRoot);

        WorkspaceConfigSnapshot? config = null;
        if (configPath is not null)
        {
            config = JsonSerializer.Deserialize<WorkspaceConfigSnapshot>(
                File.ReadAllText(configPath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
        }

        return new WorkspaceSnapshot(
            workspaceRoot,
            configPath,
            config ?? new WorkspaceConfigSnapshot(),
            DiscoverCourses(config));
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "InsTK.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string? ResolveConfigPath(string workspaceRoot)
    {
        var instkPath = Path.Combine(workspaceRoot, "instk.json");
        if (File.Exists(instkPath))
        {
            return instkPath;
        }

        var compatibilityPath = Path.Combine(workspaceRoot, "brightspacecli.json");
        return File.Exists(compatibilityPath) ? compatibilityPath : null;
    }

    private static IReadOnlyList<CourseOption> DiscoverCourses(WorkspaceConfigSnapshot? config)
    {
        var configuredRoot = config?.CourseRootPath;
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return [];
        }

        var normalizedRoot = Path.GetFullPath(configuredRoot);
        var current = new DirectoryInfo(normalizedRoot);
        var parent = current.Parent;
        if (parent is null || !parent.Exists)
        {
            return [new CourseOption(current.Name, normalizedRoot)];
        }

        return parent.EnumerateDirectories()
            .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
            .Select(directory => new CourseOption(directory.Name, directory.FullName))
            .ToArray();
    }
}

public sealed record WorkspaceSnapshot(
    string WorkspaceRoot,
    string? ConfigPath,
    WorkspaceConfigSnapshot Config,
    IReadOnlyList<CourseOption> Courses);

public sealed record CourseOption(
    string Name,
    string FullPath);

public sealed record WorkspaceConfigSnapshot
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
}
