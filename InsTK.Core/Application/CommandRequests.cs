namespace InsTK.Core;

public interface IInsTkCommand;

public sealed record LoginCommand(
    string? Url = null,
    string? StatePath = null,
    string? Channel = null) : IInsTkCommand;

public sealed record ScrapeQuickEvalCommand(
    string? Url = null,
    string? StatePath = null,
    string? OutPath = null,
    bool ScrapeAllPages = true,
    string? Channel = null) : IInsTkCommand;

public sealed record ScrapeSubmissionCommand(
    string? Url = null,
    string? StatePath = null,
    string? OutPath = null,
    string? Channel = null) : IInsTkCommand;

public sealed record ScrapeSubmissionMapCommand(
    string? Url = null,
    string? StatePath = null,
    string? OutPath = null,
    int? Limit = null,
    bool ScrapeAllPages = true,
    string? Channel = null) : IInsTkCommand;

public sealed record BuildGradingWorklistCommand(
    string? SubmissionMapPath = null,
    string? RegistryPath = null,
    string? OutPath = null) : IInsTkCommand;

public sealed record PrepareGradingReposCommand(
    string? WorklistPath = null,
    string? RepoRoot = null,
    string? OutPath = null,
    int? Limit = null) : IInsTkCommand;

public sealed record BuildGradingRunnerCommand(
    string? RepoQueuePath = null,
    string? CourseRoot = null,
    string? RunRoot = null,
    string? OutPath = null,
    int? Limit = null) : IInsTkCommand;
