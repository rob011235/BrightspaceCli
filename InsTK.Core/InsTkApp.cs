namespace InsTK.Core;

public interface ICommandHost
{
    TextWriter Out { get; }
    TextWriter Error { get; }
    TextReader In { get; }
}

public sealed class InsTkApplication : IInsTkApplication
{
    private readonly IBrightspaceAutomationService brightspaceAutomation;
    private readonly IGradingWorklistBuilder gradingWorklistBuilder;
    private readonly IRepoPreparationService repoPreparationService;
    private readonly IGradingRunnerBuilder gradingRunnerBuilder;

    internal InsTkApplication(
        IBrightspaceAutomationService brightspaceAutomation,
        IGradingWorklistBuilder gradingWorklistBuilder,
        IRepoPreparationService repoPreparationService,
        IGradingRunnerBuilder gradingRunnerBuilder)
    {
        this.brightspaceAutomation = brightspaceAutomation;
        this.gradingWorklistBuilder = gradingWorklistBuilder;
        this.repoPreparationService = repoPreparationService;
        this.gradingRunnerBuilder = gradingRunnerBuilder;
    }

    public static InsTkApplication CreateDefault()
    {
        IAppConfigProvider configProvider = new AppConfigProvider();
        return new InsTkApplication(
            new BrightspaceAutomationService(configProvider),
            new GradingWorklistBuilder(configProvider),
            new RepoPreparationService(configProvider),
            new GradingRunnerBuilder(configProvider));
    }

    public async Task<int> ExecuteAsync(IInsTkCommand command, ICommandHost host)
        => command switch
        {
            LoginCommand login => await LoginAsync(login, host),
            ScrapeQuickEvalCommand scrapeQuickEval => await ScrapeQuickEvalAsync(scrapeQuickEval, host),
            ScrapeSubmissionCommand scrapeSubmission => await ScrapeSubmissionAsync(scrapeSubmission, host),
            ScrapeSubmissionMapCommand scrapeSubmissionMap => await ScrapeSubmissionMapAsync(scrapeSubmissionMap, host),
            BuildGradingWorklistCommand buildGradingWorklist => await BuildGradingWorklistAsync(buildGradingWorklist, host),
            PrepareGradingReposCommand prepareGradingRepos => await PrepareGradingReposAsync(prepareGradingRepos, host),
            BuildGradingRunnerCommand buildGradingRunner => await BuildGradingRunnerAsync(buildGradingRunner, host),
            _ => Fail(host, $"Unknown command type: {command.GetType().Name}"),
        };

    private static int Fail(ICommandHost host, string message)
    {
        host.Error.WriteLine(message);
        return 1;
    }

    public async Task<int> LoginAsync(LoginCommand command, ICommandHost host)
        => await brightspaceAutomation.LoginAsync(command, host);

    public async Task<int> ScrapeQuickEvalAsync(ScrapeQuickEvalCommand command, ICommandHost host)
        => await brightspaceAutomation.ScrapeQuickEvalAsync(command, host);

    public async Task<int> ScrapeSubmissionAsync(ScrapeSubmissionCommand command, ICommandHost host)
        => await brightspaceAutomation.ScrapeSubmissionAsync(command, host);

    public async Task<int> ScrapeSubmissionMapAsync(ScrapeSubmissionMapCommand command, ICommandHost host)
        => await brightspaceAutomation.ScrapeSubmissionMapAsync(command, host);

    public async Task<int> BuildGradingWorklistAsync(BuildGradingWorklistCommand command, ICommandHost host)
        => await gradingWorklistBuilder.BuildAsync(command, host);

    public async Task<int> PrepareGradingReposAsync(PrepareGradingReposCommand command, ICommandHost host)
        => await repoPreparationService.PrepareAsync(command, host);

    public async Task<int> BuildGradingRunnerAsync(BuildGradingRunnerCommand command, ICommandHost host)
        => await gradingRunnerBuilder.BuildAsync(command, host);

}
