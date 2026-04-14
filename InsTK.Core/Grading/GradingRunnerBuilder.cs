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
            AssignmentRegistryEntry? assignment = null;
            if (!string.IsNullOrWhiteSpace(queueItem.AssignmentKey))
            {
                registryIndex.TryGetValue(queueItem.AssignmentKey, out assignment);
            }

            assignment ??= workItem?.Registry;

            var assignmentKeySegment = CorePaths.SanitizePathSegment(queueItem.AssignmentKey) ?? "unknown-assignment";
            var studentSegment = CorePaths.SanitizePathSegment(queueItem.Student) ?? $"item-{queueItem.Index}";
            var promptPath = Path.Combine(promptsRoot, assignmentKeySegment, $"{studentSegment}.md");
            var reportPath = Path.Combine(reportsRoot, assignmentKeySegment, $"{studentSegment}.md");
            Directory.CreateDirectory(Path.GetDirectoryName(promptPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

            var gradingTargetPath = queueItem.SelectedFolderPath ?? queueItem.RepoPath;
            var tutorialSource = ResolveTutorialSource(courseRoot, assignment?.Tutorial);
            var tutorialUrl = tutorialSource is not null && TutorialSourceUsesUrl(tutorialSource.Type)
                ? tutorialSource.Location
                : null;
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
                tutorialSource,
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
                else if (string.Equals(queueItem.SubmissionStatus, "extension-request", StringComparison.OrdinalIgnoreCase))
                {
                    error = null;
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
                queueItem.SubmissionStatus,
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
                tutorialSource?.Type,
                tutorialSource?.Location,
                competencyFolderPath,
                specPath,
                assignment?.Rubric,
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

    private static ResolvedTutorialSource? ResolveTutorialSource(string courseRoot, TutorialAssignmentInfo? tutorial)
    {
        if (tutorial?.Source is not null
            && !string.IsNullOrWhiteSpace(tutorial.Source.Type)
            && !string.IsNullOrWhiteSpace(tutorial.Source.Location))
        {
            var type = tutorial.Source.Type.Trim();
            var location = ResolveTutorialLocation(courseRoot, type, tutorial.Source.Location);
            return new ResolvedTutorialSource(type, location, tutorial.Source.Label);
        }

        var legacyUrl = !string.IsNullOrWhiteSpace(tutorial?.TargetUrl)
            ? tutorial.TargetUrl
            : tutorial?.SeriesUrl;
        return string.IsNullOrWhiteSpace(legacyUrl)
            ? null
            : new ResolvedTutorialSource("blog-url", legacyUrl, null);
    }

    private static void AppendRubric(List<string> lines, AssignmentRubricInfo? rubric)
    {
        lines.Add(string.Empty);
        lines.Add("## Rubric");
        lines.Add(string.Empty);

        if (rubric is null)
        {
            lines.Add("Rubric: (not provided)");
            return;
        }

        lines.Add($"Grading approach: {rubric.GradingApproach ?? "(not specified)"}");
        lines.Add($"Rubric summary: {rubric.Summary ?? "(none)"}");

        if (rubric.Criteria is null || rubric.Criteria.Count == 0)
        {
            lines.Add("Criteria: (not provided)");
            return;
        }

        lines.Add(string.Empty);
        lines.Add("Criteria:");

        foreach (var criterion in rubric.Criteria)
        {
            lines.Add($"- [{criterion.Id}] {criterion.Description} ({criterion.Points} points)");

            if (criterion.EvidenceHints is not null && criterion.EvidenceHints.Count > 0)
            {
                lines.Add($"  Evidence hints: {string.Join("; ", criterion.EvidenceHints)}");
            }

            if (!string.IsNullOrWhiteSpace(criterion.FrameworkNotes))
            {
                lines.Add($"  Framework notes: {criterion.FrameworkNotes}");
            }
        }
    }

    private static string ResolveTutorialLocation(string courseRoot, string type, string location)
        => string.Equals(type, "local-file", StringComparison.OrdinalIgnoreCase)
            ? (Path.IsPathRooted(location) ? location : Path.GetFullPath(location, courseRoot))
            : location;

    private static bool TutorialSourceUsesUrl(string type)
        => string.Equals(type, "blog-url", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "brightspace-doc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "url", StringComparison.OrdinalIgnoreCase);

    private static string BuildGradingPrompt(
        PreparedRepoWorkItem queueItem,
        GradingWorkItem? workItem,
        AssignmentRegistryEntry? assignment,
        string courseRoot,
        string? courseAgentsPath,
        string? assignmentAgentsPath,
        ResolvedTutorialSource? tutorialSource,
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
            $"Submission status: {queueItem.SubmissionStatus}",
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

        if (string.Equals(queueItem.SubmissionStatus, "extension-request", StringComparison.OrdinalIgnoreCase))
        {
            AppendRubric(lines, assignment?.Rubric);
            lines.Add(string.Empty);
            lines.Add("## Instructions");
            lines.Add(string.Empty);
            lines.Add("1. This LMS submission is an extension request, not a grading-ready code submission.");
            lines.Add("2. Do not grade the assignment and do not inspect the repo for completeness.");
            lines.Add("3. Write a brief acknowledgement to the report path above stating that the student requested an extension and no grade was issued from this submission.");
            lines.Add("4. Mention any directly relevant wording from the LMS submission text, but do not infer additional facts.");
        }
        else if (queueItem.GradingMode == "tutorial-follow")
        {
            lines.Add($"Tutorial source type: {tutorialSource?.Type ?? "(missing)"}");
            lines.Add($"Tutorial source location: {tutorialSource?.Location ?? "(missing)"}");
            lines.Add($"Tutorial source label: {tutorialSource?.Label ?? "(none)"}");
            lines.Add($"Tutorial notes: {assignment?.Tutorial?.Notes ?? "(none)"}");
            AppendRubric(lines, assignment?.Rubric);
            lines.Add(string.Empty);
            lines.Add("## Instructions");
            lines.Add(string.Empty);
            lines.Add("1. Open the course grading guide in the course AGENTS.md path above before inspecting the repo.");
            lines.Add("2. If an assignment-specific AGENTS.md exists, read it as well.");
            lines.Add("3. Open the tutorial source above before grading. It may be a blog URL, a Brightspace document URL, or a local file path.");
            lines.Add("4. Inspect the grading target path first. Use the full repo when the selected folder path is null.");
            lines.Add("5. Grade primarily against the rubric criteria when present. If no rubric is present, compare the student implementation against the tutorial source above.");
            lines.Add("6. Accept equivalent implementations across Windows Forms, MVC, MAUI, web, or console projects when the required capabilities are present.");
            lines.Add("7. Treat renamed symbols, extra features, and light restructuring as acceptable if the required capabilities are still implemented.");
            lines.Add("8. Use the rubric point totals when present. Otherwise assign a grade out of 100 and subtract only for meaningful missing or incomplete work.");
            lines.Add("9. Cite direct evidence with file references relative to the solution root when practical.");
            lines.Add("10. If you cannot verify runtime behavior, say so briefly and grade from source inspection.");
            lines.Add("11. Write the final grading report to the report path above using these sections exactly: Areas for Improvement, What You Did Well, Summary, Suggested Review.");
        }
        else
        {
            lines.Add($"Competency folder: {competencyFolderPath ?? "(missing)"}");
            lines.Add($"Spec path: {specPath ?? "(missing)"}");
            lines.Add($"Program notes: {assignment?.Program?.Notes ?? "(none)"}");
            AppendRubric(lines, assignment?.Rubric);
            lines.Add(string.Empty);
            lines.Add("## Instructions");
            lines.Add(string.Empty);
            lines.Add("1. Open the course grading guide in the course AGENTS.md path above before inspecting the repo.");
            lines.Add("2. If an assignment-specific AGENTS.md exists, read it as well.");
            lines.Add("3. Open the assignment spec before grading. Use the spec path above when present, otherwise inspect the competency folder.");
            lines.Add("4. Inspect the grading target path first. Use the full repo when the selected folder path is null.");
            lines.Add("5. Grade primarily against the rubric criteria when present. If no rubric is present, grade against the intent of the assignment spec.");
            lines.Add("6. Do not require a specific GUI framework unless the rubric, spec, or AGENTS file explicitly requires one.");
            lines.Add("7. Accept equivalent implementations across Windows Forms, MVC, MAUI, web, or console projects when the required capabilities are present.");
            lines.Add("8. Use the rubric point totals when present. Otherwise start from 100 and subtract only for meaningful missing or incorrect work.");
            lines.Add("9. Cite direct evidence with file references relative to the solution root when practical.");
            lines.Add("10. If you cannot verify runtime behavior, say so briefly and grade from source inspection.");
            lines.Add("11. Write the final grading report to the report path above using these sections exactly: Areas for Improvement, What You Did Well, Summary, Suggested Review.");
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

    private sealed record ResolvedTutorialSource(
        string Type,
        string Location,
        string? Label);
}
