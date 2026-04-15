namespace InsTK.Core;

internal static class PlaywrightDriverLocator
{
    private const string DriverSearchPathVariable = "PLAYWRIGHT_DRIVER_SEARCH_PATH";
    private const string DriverScriptName = "playwright.ps1";

    public static string? EnsureConfigured()
    {
        var current = Environment.GetEnvironmentVariable(DriverSearchPathVariable);
        if (!string.IsNullOrWhiteSpace(current) && IsValidDriverSearchPath(current))
        {
            return current;
        }

        var candidate = GetAppLocalPayloadRoot()
            ?? GetNuGetPackagePayloadRoot();

        if (candidate is not null)
        {
            Environment.SetEnvironmentVariable(DriverSearchPathVariable, candidate);
        }

        return candidate;
    }

    public static string Describe(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return "PLAYWRIGHT_DRIVER_SEARCH_PATH is not set.";
        }

        return string.Join(
            Environment.NewLine,
            [
                $"PLAYWRIGHT_DRIVER_SEARCH_PATH = {root}",
                $"playwright.ps1: {File.Exists(Path.Combine(root, DriverScriptName))}",
                $"local .playwright/package: {Directory.Exists(Path.Combine(root, ".playwright", "package"))}",
                $"local .playwright/node: {Directory.Exists(Path.Combine(root, ".playwright", "node"))}",
                $"parent .playwright/package: {Directory.Exists(Path.GetFullPath(Path.Combine(root, "..", ".playwright", "package"), root))}",
                $"parent .playwright/node: {Directory.Exists(Path.GetFullPath(Path.Combine(root, "..", ".playwright", "node"), root))}",
            ]);
    }

    private static string? GetAppLocalPayloadRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        return IsValidDriverSearchPath(baseDirectory) ? baseDirectory : null;
    }

    private static string? GetNuGetPackagePayloadRoot()
    {
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            packageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        }

        var playwrightRoot = Path.Combine(packageRoot, "microsoft.playwright");
        if (!Directory.Exists(playwrightRoot))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(playwrightRoot)
            .OrderByDescending(Path.GetFileName)
            .Select(GetDriverRootFromPackageVersion)
            .FirstOrDefault(IsValidDriverSearchPath);
    }

    private static string GetDriverRootFromPackageVersion(string packageVersionRoot)
        => Path.Combine(packageVersionRoot, "buildTransitive");

    private static bool IsValidDriverSearchPath(string root)
        => File.Exists(Path.Combine(root, DriverScriptName))
            && HasPayloadUnder(root, root)
            || File.Exists(Path.Combine(root, DriverScriptName))
            && HasPayloadUnder(root, Path.Combine(root, ".."));

    private static bool HasPayloadUnder(string root, string payloadBase)
        => Directory.Exists(Path.GetFullPath(Path.Combine(payloadBase, ".playwright", "package"), root))
            && Directory.Exists(Path.GetFullPath(Path.Combine(payloadBase, ".playwright", "node"), root));
}
