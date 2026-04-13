namespace InsTK.Core;

internal sealed class GradingRunnerBuilder : IGradingRunnerBuilder
{
    private readonly AppConfig config;

    public GradingRunnerBuilder(IAppConfigProvider configProvider)
    {
        config = configProvider.Current;
    }

    public async Task<int> BuildAsync(BuildGradingRunnerCommand command, ICommandHost host)
    {
        var repoQueuePath = CorePaths.ResolvePath(command.RepoQueuePath ?? config.GradingRepoQueueOutPath ?? "_grading/grading-repo-queue.json");
        var courseRoot = CorePaths.ResolvePath(command.CourseRoot ?? config.CourseRootPath ?? throw new InvalidOperationException("Missing course root path."));
        var runRoot = CorePaths.ResolvePath(command.RunRoot ?? config.GradingRunRoot ?? throw new InvalidOperationException("Missing grading run root."));
        var outPath = CorePaths.ResolvePath(command.OutPath ?? config.GradingRunnerOutPath ?? Path.Combine(runRoot, "grading-runner.json"));
        var limit = command.Limit;

        if (!File.Exists(repoQueuePath))
        {
            throw new InvalidOperationException($"Prepared repo queue not found: {repoQueuePath}");
        }

        if (!Directory.Exists(courseRoot))
        {
            throw new InvalidOperationException($"Course root not found: {courseRoot}");
        }

        var repoQueue = await JsonFileStore.ReadAsync<PreparedRepoQueueResult>(repoQueuePath);
        if (!File.Exists(repoQueue.WorklistPath))
        {
            throw new InvalidOperationException($"Referenced grading worklist not found: {repoQueue.WorklistPath}");
        }

        var worklist = await JsonFileStore.ReadAsync<GradingWorklistResult>(repoQueue.WorklistPath);
        if (!File.Exists(worklist.RegistryPath))
        {
            throw new InvalidOperationException($"Referenced assignment registry not found: {worklist.RegistryPath}");
        }

        var registry = await JsonFileStore.ReadAsync<AssignmentRegistry>(worklist.RegistryPath);
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
            host.Out.WriteLine($"Building grading run {i + 1} of {items.Count}: {queueItem.Student} - {queueItem.ActivityName}");

            worklistIndex.TryGetValue(GetWorkItemKey(queueItem), out var workItem);
            AssignmentRegistryEntry? assignment = workItem?.Registry;
            if (assignment is null && !string.IsNullOrWhiteSpace(queueItem.AssignmentKey))
            {
                registryIndex.TryGetValue(queueItem.AssignmentKey, out assignment);
            }

            var assignmentKeySegment = CorePaths.SanitizePathSegment(queueItem.AssignmentKey) ?? "unknown-assignment";
            var studentSegment = CorePaths.SanitizePathSegment(queueItem.Student) ?? $"item-{queueItem.Index}";
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
            var courseAgentsPath = CorePaths.ResolveOptionalFilePath(Path.Combine(courseRoot, "AGENTS.md"));
            var assignmentAgentsPath = CorePaths.ResolveOptionalFilePath(
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
            InsTkDefaults.ArtifactSchemaVersion,
            DateTimeOffset.UtcNow,
            repoQueuePath,
            repoQueue.WorklistPath,
            worklist.RegistryPath,
            courseRoot,
            runRoot,
            runItems.Count,
            runItems.Count(static item => !string.IsNullOrWhiteSpace(item.Error)),
            runItems);

        await JsonFileStore.WriteAsync(outPath, result);
        host.Out.WriteLine($"Wrote {runItems.Count} grading run items to {outPath}");
        return 0;
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
}
