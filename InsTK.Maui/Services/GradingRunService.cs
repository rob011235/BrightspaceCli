using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;

namespace InsTK.Maui;

public sealed class GradingRunService
{
    public IReadOnlyList<GradingRunQueueItem> LoadQueue(string gradingRunnerPath)
    {
        if (string.IsNullOrWhiteSpace(gradingRunnerPath) || !File.Exists(gradingRunnerPath))
        {
            return [];
        }

        var result = JsonSerializer.Deserialize<GradingRunnerFile>(
            File.ReadAllText(gradingRunnerPath),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

        return result?.Items?
            .Select(item => new GradingRunQueueItem(
                item.Index,
                item.Student ?? "(unknown student)",
                item.ActivityName ?? "(unknown activity)",
                item.SubmissionStatus ?? string.Empty,
                item.GradingMode ?? string.Empty,
                item.PromptPath ?? string.Empty,
                item.ReportPath ?? string.Empty,
                item.RepoPath ?? string.Empty,
                item.SelectedFolderPath ?? string.Empty,
                item.GradingTargetPath ?? string.Empty,
                item.EvaluationUrl ?? string.Empty,
                item.Error ?? string.Empty))
            .OrderBy(item => item.HasError)
            .ThenBy(item => item.Student, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    public void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        EnsureParentDirectory(path);

        if (!File.Exists(path) && Path.HasExtension(path))
        {
            File.WriteAllText(path, string.Empty);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    public void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var target = Directory.Exists(path)
            ? path
            : Path.GetDirectoryName(path);

        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        Directory.CreateDirectory(target);
        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true,
        });
    }

    private static void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }
}

public sealed record GradingRunQueueItem(
    int Index,
    string Student,
    string ActivityName,
    string SubmissionStatus,
    string GradingMode,
    string PromptPath,
    string ReportPath,
    string RepoPath,
    string SelectedFolderPath,
    string GradingTargetPath,
    string EvaluationUrl,
    string Error)
{
    public string DisplayName => $"{Student} - {ActivityName}";
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public string StatusSummary => HasError
        ? $"Blocked: {Error}"
        : $"Ready: {SubmissionStatus}";
    public string WorkingFolder => !string.IsNullOrWhiteSpace(SelectedFolderPath) ? SelectedFolderPath : GradingTargetPath;
}

public sealed record GradingRunnerFile(
    IReadOnlyList<GradingRunnerFileItem>? Items);

public sealed record GradingRunnerFileItem(
    int Index,
    string? Student,
    string? ActivityName,
    string? SubmissionStatus,
    string? GradingMode,
    string? PromptPath,
    string? ReportPath,
    string? RepoPath,
    string? SelectedFolderPath,
    string? GradingTargetPath,
    string? EvaluationUrl,
    string? Error);
