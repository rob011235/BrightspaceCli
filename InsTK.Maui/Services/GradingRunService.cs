using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace InsTK.Maui;

public sealed class GradingRunService
{
    private const string CodexCommandName = "codex.cmd";

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

    public async Task<int> RunAgentGradingAsync(GradingRunQueueItem item, Action<string, bool> onLog, CancellationToken cancellationToken = default)
    {
        var codexPath = ResolveCodexPath()
            ?? throw new InvalidOperationException("Codex CLI not found. Install it or add it to PATH.");

        var workingDirectory = ResolveWorkingDirectory(item);
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            throw new InvalidOperationException($"Working directory not found: {workingDirectory}");
        }

        if (string.IsNullOrWhiteSpace(item.PromptPath) || !File.Exists(item.PromptPath))
        {
            throw new InvalidOperationException($"Prompt file not found: {item.PromptPath}");
        }

        EnsureParentDirectory(item.ReportPath);
        var promptText = await File.ReadAllTextAsync(item.PromptPath, cancellationToken);
        var lastMessagePath = Path.Combine(
            Path.GetTempPath(),
            $"instk-codex-last-message-{item.Index}-{Guid.NewGuid():N}.txt");

        var startInfo = new ProcessStartInfo
        {
            FileName = codexPath,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("-");
        startInfo.ArgumentList.Add("--full-auto");
        startInfo.ArgumentList.Add("--skip-git-repo-check");
        startInfo.ArgumentList.Add("--output-last-message");
        startInfo.ArgumentList.Add(lastMessagePath);
        startInfo.ArgumentList.Add("--add-dir");
        startInfo.ArgumentList.Add(Path.GetDirectoryName(item.ReportPath)!);
        startInfo.ArgumentList.Add("--color");
        startInfo.ArgumentList.Add("never");

        var courseRoot = TryExtractCourseRoot(promptText);
        if (!string.IsNullOrWhiteSpace(courseRoot) && Directory.Exists(courseRoot))
        {
            startInfo.ArgumentList.Add("--add-dir");
            startInfo.ArgumentList.Add(courseRoot);
        }

        if (!string.IsNullOrWhiteSpace(item.WorkingFolder)
            && Directory.Exists(item.WorkingFolder)
            && !string.Equals(item.WorkingFolder, workingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("--add-dir");
            startInfo.ArgumentList.Add(item.WorkingFolder);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                onLog(args.Data, false);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                onLog(FormatCodexStderr(args.Data), IsActualErrorLine(args.Data));
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start Codex CLI process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.StandardInput.WriteAsync(promptText);
        process.StandardInput.Close();

        await process.WaitForExitAsync(cancellationToken);

        if ((!File.Exists(item.ReportPath) || new FileInfo(item.ReportPath).Length == 0)
            && File.Exists(lastMessagePath))
        {
            var lastMessage = await File.ReadAllTextAsync(lastMessagePath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(lastMessage))
            {
                await File.WriteAllTextAsync(item.ReportPath, lastMessage, cancellationToken);
                onLog($"Wrote fallback report from Codex final message to {item.ReportPath}", false);
            }
        }

        TryDelete(lastMessagePath);
        return process.ExitCode;
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

    private static string? ResolveCodexPath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(segment, CodexCommandName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var npmCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm",
            CodexCommandName);

        return File.Exists(npmCandidate) ? npmCandidate : null;
    }

    private static string ResolveWorkingDirectory(GradingRunQueueItem item)
        => !string.IsNullOrWhiteSpace(item.RepoPath) ? item.RepoPath : item.WorkingFolder;

    private static string? TryExtractCourseRoot(string promptText)
    {
        const string prefix = "Course root: ";
        using var reader = new StringReader(promptText);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return null;
    }

    private static void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string FormatCodexStderr(string line)
        => IsActualErrorLine(line) ? line : $"[codex] {line}";

    private static bool IsActualErrorLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        return trimmed.StartsWith("error:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("failed", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("fatal", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("exception", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class GradingRunQueueItem : ObservableObject
{
    private bool isSelected;
    private string runtimeStatus;

    public GradingRunQueueItem(
        int index,
        string student,
        string activityName,
        string submissionStatus,
        string gradingMode,
        string promptPath,
        string reportPath,
        string repoPath,
        string selectedFolderPath,
        string gradingTargetPath,
        string evaluationUrl,
        string error)
    {
        Index = index;
        Student = student;
        ActivityName = activityName;
        SubmissionStatus = submissionStatus;
        GradingMode = gradingMode;
        PromptPath = promptPath;
        ReportPath = reportPath;
        RepoPath = repoPath;
        SelectedFolderPath = selectedFolderPath;
        GradingTargetPath = gradingTargetPath;
        EvaluationUrl = evaluationUrl;
        Error = error;
        runtimeStatus = HasError ? $"Blocked: {Error}" : $"Ready: {SubmissionStatus}";
    }

    public int Index { get; }
    public string Student { get; }
    public string ActivityName { get; }
    public string SubmissionStatus { get; }
    public string GradingMode { get; }
    public string PromptPath { get; }
    public string ReportPath { get; }
    public string RepoPath { get; }
    public string SelectedFolderPath { get; }
    public string GradingTargetPath { get; }
    public string EvaluationUrl { get; }
    public string Error { get; }
    public string DisplayName => $"{Student} - {ActivityName}";
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool IsSelectable => !HasError;
    public string WorkingFolder => !string.IsNullOrWhiteSpace(SelectedFolderPath) ? SelectedFolderPath : GradingTargetPath;

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public string RuntimeStatus
    {
        get => runtimeStatus;
        set => SetProperty(ref runtimeStatus, value);
    }
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
