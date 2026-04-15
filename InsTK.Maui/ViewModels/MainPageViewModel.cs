using System.Collections.ObjectModel;
using System.Windows.Input;
using InsTK.Core;

namespace InsTK.Maui;

public sealed class MainPageViewModel : ObservableObject
{
    private readonly WorkspaceService workspaceService;
    private readonly InsTkCommandRunner commandRunner;
    private readonly GradingRunService gradingRunService;
    private string workspaceRoot = string.Empty;
    private string configPath = "(not found)";
    private string browserChannel = "msedge";
    private string quickEvalUrl = string.Empty;
    private string statePath = ".brightspace/session.json";
    private string submissionMapPath = "_grading/submission-map.json";
    private string assignmentRegistryPath = string.Empty;
    private string gradingWorklistPath = "_grading/grading-worklist.json";
    private string gradingRepoRoot = "C:\\grading\\repos";
    private string repoQueuePath = "_grading/grading-repo-queue.json";
    private string courseRootPath = string.Empty;
    private string gradingRunRoot = "_grading/runs";
    private string gradingRunnerOutPath = "_grading/grading-runner.json";
    private string status = "Ready to grade.";
    private string logText = "InsTK MAUI host initialized.";
    private bool isBusy;
    private bool isAwaitingInput;
    private CourseOption? selectedCourse;
    private GradingRunQueueItem? selectedQueueItem;

    public MainPageViewModel(WorkspaceService workspaceService, InsTkCommandRunner commandRunner, GradingRunService gradingRunService)
    {
        this.workspaceService = workspaceService;
        this.commandRunner = commandRunner;
        this.gradingRunService = gradingRunService;
        commandRunner.InputPendingChanged += OnInputPendingChanged;

        AvailableCourses = [];
        GradingQueue = [];

        ReloadWorkspaceCommand = new AsyncCommand(ReloadWorkspaceAsync, () => !IsBusy);
        OpenBrightspaceLoginCommand = new AsyncCommand(RunLoginAsync, CanUseBrightspace);
        LoadSubmissionListCommand = new AsyncCommand(RunSubmissionMapAsync, CanUseBrightspace);
        PrepareGradingFilesCommand = new AsyncCommand(RunPrepareGradingFilesAsync, CanPrepareGradingFiles);
        RefreshGradingQueueCommand = new AsyncCommand(RefreshGradingQueueAsync, () => !IsBusy);
        OpenSelectedForGradingCommand = new Command(OpenSelectedForGrading, CanOpenSelectedQueueItem);
        OpenPromptCommand = new Command(OpenPrompt, CanOpenSelectedQueueItem);
        OpenRepoCommand = new Command(OpenRepo, CanOpenSelectedQueueItem);
        OpenBrightspaceCommand = new Command(OpenBrightspace, CanOpenSelectedQueueItem);
        OpenReportCommand = new Command(OpenReport, CanOpenSelectedQueueItem);
        ContinueCommand = new Command(ContinueExecution, () => IsAwaitingInput);
        ClearLogCommand = new Command(ClearLog);

        ApplySnapshot(workspaceService.LoadSnapshot());
    }

    public ObservableCollection<CourseOption> AvailableCourses { get; }
    public ObservableCollection<GradingRunQueueItem> GradingQueue { get; }

    public string WorkspaceRoot
    {
        get => workspaceRoot;
        private set => SetProperty(ref workspaceRoot, value);
    }

    public string ConfigPath
    {
        get => configPath;
        private set => SetProperty(ref configPath, value);
    }

    public CourseOption? SelectedCourse
    {
        get => selectedCourse;
        set
        {
            if (!SetProperty(ref selectedCourse, value))
            {
                return;
            }

            if (value is not null)
            {
                CourseRootPath = value.FullPath;
                Status = $"Selected course: {value.Name}";
            }

            RaiseCommandStates();
        }
    }

    public string SelectedCourseName
        => SelectedCourse?.Name ?? "No course selected";

    public GradingRunQueueItem? SelectedQueueItem
    {
        get => selectedQueueItem;
        set
        {
            if (SetProperty(ref selectedQueueItem, value))
            {
                OnPropertyChanged(nameof(SelectedQueueItemSummary));
                RaiseCommandStates();
            }
        }
    }

    public string SelectedQueueItemSummary
        => SelectedQueueItem is null
            ? "Select a prepared submission to start grading."
            : SelectedQueueItem.StatusSummary;

    public string BrowserChannel
    {
        get => browserChannel;
        set => SetProperty(ref browserChannel, value);
    }

    public string QuickEvalUrl
    {
        get => quickEvalUrl;
        set
        {
            if (SetProperty(ref quickEvalUrl, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string StatePath
    {
        get => statePath;
        set => SetProperty(ref statePath, value);
    }

    public string SubmissionMapPath
    {
        get => submissionMapPath;
        set => SetProperty(ref submissionMapPath, value);
    }

    public string AssignmentRegistryPath
    {
        get => assignmentRegistryPath;
        set
        {
            if (SetProperty(ref assignmentRegistryPath, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string GradingWorklistPath
    {
        get => gradingWorklistPath;
        set => SetProperty(ref gradingWorklistPath, value);
    }

    public string GradingRepoRoot
    {
        get => gradingRepoRoot;
        set => SetProperty(ref gradingRepoRoot, value);
    }

    public string RepoQueuePath
    {
        get => repoQueuePath;
        set => SetProperty(ref repoQueuePath, value);
    }

    public string CourseRootPath
    {
        get => courseRootPath;
        set
        {
            if (SetProperty(ref courseRootPath, value))
            {
                OnPropertyChanged(nameof(CourseSummary));
                RaiseCommandStates();
            }
        }
    }

    public string GradingRunRoot
    {
        get => gradingRunRoot;
        set => SetProperty(ref gradingRunRoot, value);
    }

    public string GradingRunnerOutPath
    {
        get => gradingRunnerOutPath;
        set => SetProperty(ref gradingRunnerOutPath, value);
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public string LogText
    {
        get => logText;
        private set => SetProperty(ref logText, value);
    }

    public string CourseSummary
        => string.IsNullOrWhiteSpace(CourseRootPath)
            ? "Pick the course materials folder that contains AGENTS.md and your lesson files."
            : $"Course materials: {CourseRootPath}";

    public string QuickEvalHelp
        => "Quick Eval is Brightspace's grading queue. Loading it pulls the current submissions and evaluation links for the selected course.";

    public string GradingPrepHelp
        => "Prepare grading files creates the internal grading packet: submission map, grading worklist, local repo queue, and final grading runner JSON.";

    public string GradingQueueHelp
        => GradingQueue.Count == 0
            ? "No prepared grading items loaded yet."
            : $"{GradingQueue.Count} prepared grading item(s) loaded.";

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                RaiseCommandStates();
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public bool IsAwaitingInput
    {
        get => isAwaitingInput;
        private set
        {
            if (SetProperty(ref isAwaitingInput, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public ICommand ReloadWorkspaceCommand { get; }
    public ICommand OpenBrightspaceLoginCommand { get; }
    public ICommand LoadSubmissionListCommand { get; }
    public ICommand PrepareGradingFilesCommand { get; }
    public ICommand RefreshGradingQueueCommand { get; }
    public ICommand OpenSelectedForGradingCommand { get; }
    public ICommand OpenPromptCommand { get; }
    public ICommand OpenRepoCommand { get; }
    public ICommand OpenBrightspaceCommand { get; }
    public ICommand OpenReportCommand { get; }
    public ICommand ContinueCommand { get; }
    public ICommand ClearLogCommand { get; }

    private Task ReloadWorkspaceAsync()
    {
        ApplySnapshot(workspaceService.LoadSnapshot());
        AppendLog("Reloaded workspace configuration.");
        return Task.CompletedTask;
    }

    private Task RunLoginAsync()
        => RunCommandAsync(
            "Brightspace login is ready.",
            new LoginCommand(
                Url: NullIfEmpty(QuickEvalUrl),
                StatePath: NullIfEmpty(StatePath),
                Channel: NullIfEmpty(BrowserChannel)));

    private Task RunSubmissionMapAsync()
        => RunCommandAsync(
            "Loaded the current Brightspace submission list.",
            new ScrapeSubmissionMapCommand(
                Url: NullIfEmpty(QuickEvalUrl),
                StatePath: NullIfEmpty(StatePath),
                OutPath: NullIfEmpty(SubmissionMapPath),
                ScrapeAllPages: true,
                Channel: NullIfEmpty(BrowserChannel)));

    private async Task RunPrepareGradingFilesAsync()
    {
        await RunCommandAsync(
            "Matched submissions to your assignment registry.",
            new BuildGradingWorklistCommand(
                SubmissionMapPath: NullIfEmpty(SubmissionMapPath),
                RegistryPath: NullIfEmpty(AssignmentRegistryPath),
                OutPath: NullIfEmpty(GradingWorklistPath)));

        if (!LastCommandSucceeded)
        {
            return;
        }

        await RunCommandAsync(
            "Prepared local repositories for grading.",
            new PrepareGradingReposCommand(
                WorklistPath: NullIfEmpty(GradingWorklistPath),
                RepoRoot: NullIfEmpty(GradingRepoRoot),
                OutPath: NullIfEmpty(RepoQueuePath)));

        if (!LastCommandSucceeded)
        {
            return;
        }

        await RunCommandAsync(
            "Grading packet is ready.",
            new BuildGradingRunnerCommand(
                RepoQueuePath: NullIfEmpty(RepoQueuePath),
                CourseRoot: NullIfEmpty(CourseRootPath),
                RunRoot: NullIfEmpty(GradingRunRoot),
                OutPath: NullIfEmpty(GradingRunnerOutPath)));

        if (LastCommandSucceeded)
        {
            await RefreshGradingQueueAsync();
        }
    }

    private bool LastCommandSucceeded { get; set; }

    private async Task RunCommandAsync(string successStatus, IInsTkCommand command)
    {
        IsBusy = true;
        IsAwaitingInput = false;
        LastCommandSucceeded = false;
        Status = "Working...";
        AppendLog(string.Empty);
        AppendLog($"> {GetHumanCommandName(command)}");

        try
        {
            var exitCode = await commandRunner.ExecuteAsync(command, HandleLog);
            LastCommandSucceeded = exitCode == 0;
            Status = exitCode == 0
                ? successStatus
                : $"Step failed with exit code {exitCode}.";
            AppendLog($"Exit code: {exitCode}");
        }
        catch (Exception ex)
        {
            Status = "Step failed.";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            IsAwaitingInput = false;
        }
    }

    private void ContinueExecution()
    {
        commandRunner.SubmitInput(string.Empty);
        AppendLog("Submitted confirmation from the app.");
    }

    private void ClearLog()
        => LogText = string.Empty;

    private Task RefreshGradingQueueAsync()
    {
        GradingQueue.Clear();
        foreach (var item in gradingRunService.LoadQueue(GradingRunnerOutPath))
        {
            GradingQueue.Add(item);
        }

        SelectedQueueItem = GradingQueue.FirstOrDefault(item => !item.HasError) ?? GradingQueue.FirstOrDefault();
        Status = GradingQueue.Count == 0
            ? "No prepared grading items found yet."
            : $"Loaded {GradingQueue.Count} prepared grading item(s).";
        OnPropertyChanged(nameof(GradingQueueHelp));
        return Task.CompletedTask;
    }

    private void OpenSelectedForGrading()
    {
        if (SelectedQueueItem is null)
        {
            return;
        }

        OpenPrompt();
        OpenReport();
        OpenRepo();

        if (!string.IsNullOrWhiteSpace(SelectedQueueItem.EvaluationUrl))
        {
            OpenBrightspace();
        }

        AppendLog($"Opened grading materials for {SelectedQueueItem.DisplayName}.");
    }

    private void OpenPrompt()
    {
        if (SelectedQueueItem is null)
        {
            return;
        }

        gradingRunService.OpenPath(SelectedQueueItem.PromptPath);
    }

    private void OpenRepo()
    {
        if (SelectedQueueItem is null)
        {
            return;
        }

        gradingRunService.OpenFolder(SelectedQueueItem.WorkingFolder);
    }

    private void OpenBrightspace()
    {
        if (SelectedQueueItem is null)
        {
            return;
        }

        gradingRunService.OpenUrl(SelectedQueueItem.EvaluationUrl);
    }

    private void OpenReport()
    {
        if (SelectedQueueItem is null)
        {
            return;
        }

        gradingRunService.OpenPath(SelectedQueueItem.ReportPath);
    }

    private void ApplySnapshot(WorkspaceSnapshot snapshot)
    {
        WorkspaceRoot = snapshot.WorkspaceRoot;
        ConfigPath = snapshot.ConfigPath ?? "(not found)";
        BrowserChannel = snapshot.Config.BrowserChannel ?? "msedge";
        QuickEvalUrl = snapshot.Config.QuickEvalUrl ?? string.Empty;
        StatePath = snapshot.Config.StatePath ?? ".brightspace/session.json";
        SubmissionMapPath = snapshot.Config.SubmissionMapOutPath ?? "_grading/submission-map.json";
        AssignmentRegistryPath = snapshot.Config.AssignmentRegistryPath ?? string.Empty;
        GradingWorklistPath = snapshot.Config.GradingWorklistOutPath ?? "_grading/grading-worklist.json";
        GradingRepoRoot = snapshot.Config.GradingRepoRoot ?? "C:\\grading\\repos";
        RepoQueuePath = snapshot.Config.GradingRepoQueueOutPath ?? "_grading/grading-repo-queue.json";
        CourseRootPath = snapshot.Config.CourseRootPath ?? string.Empty;
        GradingRunRoot = snapshot.Config.GradingRunRoot ?? "_grading/runs";
        GradingRunnerOutPath = snapshot.Config.GradingRunnerOutPath ?? "_grading/grading-runner.json";

        AvailableCourses.Clear();
        foreach (var course in snapshot.Courses)
        {
            AvailableCourses.Add(course);
        }

        SelectedCourse = AvailableCourses.FirstOrDefault(course =>
            string.Equals(course.FullPath, CourseRootPath, StringComparison.OrdinalIgnoreCase));

        if (SelectedCourse is null && !string.IsNullOrWhiteSpace(CourseRootPath))
        {
            SelectedCourse = new CourseOption(Path.GetFileName(CourseRootPath.TrimEnd(Path.DirectorySeparatorChar)), CourseRootPath);
            if (!AvailableCourses.Any(course => string.Equals(course.FullPath, SelectedCourse.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                AvailableCourses.Insert(0, SelectedCourse);
            }
        }

        Status = snapshot.ConfigPath is null
            ? "Workspace loaded. No instk.json found yet."
            : "Workspace loaded. Pick a course and start grading.";

        OnPropertyChanged(nameof(CourseSummary));
        OnPropertyChanged(nameof(SelectedCourseName));
        _ = RefreshGradingQueueAsync();
        RaiseCommandStates();
    }

    private void HandleLog(string line, bool isError)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        AppendLog(isError ? $"ERROR: {line}" : line);
    }

    private void AppendLog(string line)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LogText = string.IsNullOrEmpty(LogText)
                ? line
                : $"{LogText}{Environment.NewLine}{line}";
        });
    }

    private void OnInputPendingChanged(object? sender, bool isPending)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsAwaitingInput = isPending;
            if (isPending)
            {
                Status = "Waiting for you to finish the browser step.";
                AppendLog("Finish the sign-in step in the browser, then click Continue.");
            }
        });
    }

    private bool CanUseBrightspace()
        => !IsBusy && !string.IsNullOrWhiteSpace(QuickEvalUrl);

    private bool CanPrepareGradingFiles()
        => !IsBusy
            && !string.IsNullOrWhiteSpace(QuickEvalUrl)
            && !string.IsNullOrWhiteSpace(AssignmentRegistryPath)
            && !string.IsNullOrWhiteSpace(CourseRootPath);

    private void RaiseCommandStates()
    {
        if (ReloadWorkspaceCommand is AsyncCommand reload)
        {
            reload.RaiseCanExecuteChanged();
        }

        if (OpenBrightspaceLoginCommand is AsyncCommand login)
        {
            login.RaiseCanExecuteChanged();
        }

        if (LoadSubmissionListCommand is AsyncCommand loadSubmissionList)
        {
            loadSubmissionList.RaiseCanExecuteChanged();
        }

        if (PrepareGradingFilesCommand is AsyncCommand prepareGradingFiles)
        {
            prepareGradingFiles.RaiseCanExecuteChanged();
        }

        if (RefreshGradingQueueCommand is AsyncCommand refreshGradingQueue)
        {
            refreshGradingQueue.RaiseCanExecuteChanged();
        }

        if (OpenSelectedForGradingCommand is Command openSelected)
        {
            openSelected.ChangeCanExecute();
        }

        if (OpenPromptCommand is Command openPrompt)
        {
            openPrompt.ChangeCanExecute();
        }

        if (OpenRepoCommand is Command openRepo)
        {
            openRepo.ChangeCanExecute();
        }

        if (OpenBrightspaceCommand is Command openBrightspace)
        {
            openBrightspace.ChangeCanExecute();
        }

        if (OpenReportCommand is Command openReport)
        {
            openReport.ChangeCanExecute();
        }

        if (ContinueCommand is Command continueCommand)
        {
            continueCommand.ChangeCanExecute();
        }

        OnPropertyChanged(nameof(SelectedCourseName));
        OnPropertyChanged(nameof(SelectedQueueItemSummary));
        OnPropertyChanged(nameof(GradingQueueHelp));
    }

    private bool CanOpenSelectedQueueItem()
        => SelectedQueueItem is not null && !SelectedQueueItem.HasError;

    private static string GetHumanCommandName(IInsTkCommand command)
        => command switch
        {
            LoginCommand => "Open Brightspace login",
            ScrapeSubmissionMapCommand => "Load Brightspace submission list",
            BuildGradingWorklistCommand => "Match submissions to the assignment registry",
            PrepareGradingReposCommand => "Prepare local student repositories",
            BuildGradingRunnerCommand => "Create the final grading packet",
            _ => command.GetType().Name,
        };

    private static string? NullIfEmpty(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
