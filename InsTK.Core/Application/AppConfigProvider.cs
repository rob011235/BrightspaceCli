namespace InsTK.Core;

internal sealed class AppConfigProvider : IAppConfigProvider
{
    public AppConfig Current { get; } = AppConfig.Load();
}
