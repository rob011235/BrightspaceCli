using InsTK.Core;

internal static class CommandRequestParser
{
    public static IInsTkCommand Parse(string command, string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key[2..]] = "true";
                continue;
            }

            values[key[2..]] = args[i + 1];
            i++;
        }

        return command switch
        {
            "login" => new LoginCommand(
                values.GetValueOrDefault("url"),
                values.GetValueOrDefault("state"),
                values.GetValueOrDefault("channel")),
            "scrape-quickeval" => new ScrapeQuickEvalCommand(
                values.GetValueOrDefault("url"),
                values.GetValueOrDefault("state"),
                values.GetValueOrDefault("out"),
                !IsTrue(values, "first-page-only"),
                values.GetValueOrDefault("channel")),
            "scrape-submission" => new ScrapeSubmissionCommand(
                values.GetValueOrDefault("url"),
                values.GetValueOrDefault("state"),
                values.GetValueOrDefault("out"),
                values.GetValueOrDefault("channel")),
            "scrape-submission-map" => new ScrapeSubmissionMapCommand(
                values.GetValueOrDefault("url"),
                values.GetValueOrDefault("state"),
                values.GetValueOrDefault("out"),
                ParseOptionalInt(values.GetValueOrDefault("limit"), "limit"),
                !IsTrue(values, "first-page-only"),
                values.GetValueOrDefault("channel")),
            "build-grading-worklist" => new BuildGradingWorklistCommand(
                values.GetValueOrDefault("submission-map"),
                values.GetValueOrDefault("registry"),
                values.GetValueOrDefault("out")),
            "prepare-grading-repos" => new PrepareGradingReposCommand(
                values.GetValueOrDefault("worklist"),
                values.GetValueOrDefault("repo-root"),
                values.GetValueOrDefault("out"),
                ParseOptionalInt(values.GetValueOrDefault("limit"), "limit")),
            "build-grading-runner" => new BuildGradingRunnerCommand(
                values.GetValueOrDefault("repo-queue"),
                values.GetValueOrDefault("course-root"),
                values.GetValueOrDefault("run-root"),
                values.GetValueOrDefault("out"),
                ParseOptionalInt(values.GetValueOrDefault("limit"), "limit")),
            _ => throw new InvalidOperationException($"Unknown command: {command}"),
        };
    }

    private static bool IsTrue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static int? ParseOptionalInt(string? value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"Option --{optionName} must be a positive integer.");
        }

        return parsed;
    }
}
