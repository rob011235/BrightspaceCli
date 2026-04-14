namespace InsTK.Core;

internal sealed class GradingWorklistBuilder : IGradingWorklistBuilder
{
    private readonly AppConfig config;

    public GradingWorklistBuilder(IAppConfigProvider configProvider)
    {
        config = configProvider.Current;
    }

    public async Task<int> BuildAsync(BuildGradingWorklistCommand command, ICommandHost host)
    {
        var submissionMapPath = CorePaths.ResolvePath(command.SubmissionMapPath ?? config.SubmissionMapOutPath ?? "_grading/submission-map.json");
        var registryPath = CorePaths.ResolvePath(command.RegistryPath ?? config.AssignmentRegistryPath ?? throw new InvalidOperationException("Missing assignment registry path."));
        var outPath = CorePaths.ResolvePath(command.OutPath ?? config.GradingWorklistOutPath ?? "_grading/grading-worklist.json");

        if (!File.Exists(submissionMapPath))
        {
            throw new InvalidOperationException($"Submission map not found: {submissionMapPath}");
        }

        if (!File.Exists(registryPath))
        {
            throw new InvalidOperationException($"Assignment registry not found: {registryPath}");
        }

        var submissionMap = await JsonFileStore.ReadAsync<SubmissionMapResult>(submissionMapPath);
        var registry = await JsonFileStore.ReadAsync<AssignmentRegistry>(registryPath);
        var assignmentIndex = registry.Assignments.ToDictionary(
            assignment => assignment.AssignmentKey,
            assignment => assignment,
            StringComparer.OrdinalIgnoreCase);

        var items = submissionMap.Submissions
            .Select(submission =>
            {
                assignmentIndex.TryGetValue(submission.AssignmentKey, out var assignment);
                var submissionStatus = SubmissionStatusClassifier.Classify(submission.RawText, submission.CloneUrl);
                var gradingMode = submissionStatus == "extension-request"
                    ? "extension-request"
                    : assignment is null
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
                    submissionStatus,
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
            InsTkDefaults.ArtifactSchemaVersion,
            DateTimeOffset.UtcNow,
            submissionMapPath,
            registryPath,
            items.Count,
            items.Count(static item => !item.RegistryMatched),
            items);

        await JsonFileStore.WriteAsync(outPath, result);
        host.Out.WriteLine($"Wrote {items.Count} work items to {outPath}");
        return 0;
    }
}
