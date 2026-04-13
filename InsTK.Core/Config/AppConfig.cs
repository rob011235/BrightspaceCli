using System.Text.Json;

namespace InsTK.Core;

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
        var currentDirectory = Directory.GetCurrentDirectory();
        var path = Path.Combine(currentDirectory, "instk.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(currentDirectory, "brightspacecli.json");
            if (!File.Exists(path))
            {
                return new AppConfig();
            }
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
