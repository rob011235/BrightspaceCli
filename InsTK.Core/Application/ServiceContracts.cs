namespace InsTK.Core;

public interface IInsTkApplication
{
    Task<int> ExecuteAsync(IInsTkCommand command, ICommandHost host);
}

internal interface IAppConfigProvider
{
    AppConfig Current { get; }
}

public interface IBrightspaceAutomationService
{
    Task<int> LoginAsync(LoginCommand command, ICommandHost host);
    Task<int> ScrapeQuickEvalAsync(ScrapeQuickEvalCommand command, ICommandHost host);
    Task<int> ScrapeSubmissionAsync(ScrapeSubmissionCommand command, ICommandHost host);
    Task<int> ScrapeSubmissionMapAsync(ScrapeSubmissionMapCommand command, ICommandHost host);
}

public interface IGradingWorklistBuilder
{
    Task<int> BuildAsync(BuildGradingWorklistCommand command, ICommandHost host);
}

public interface IRepoPreparationService
{
    Task<int> PrepareAsync(PrepareGradingReposCommand command, ICommandHost host);
}

public interface IGradingRunnerBuilder
{
    Task<int> BuildAsync(BuildGradingRunnerCommand command, ICommandHost host);
}
