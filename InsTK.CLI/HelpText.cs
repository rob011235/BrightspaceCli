internal static class HelpText
{
    public static string Build()
        =>
        """
        InsTK CLI

        Commands
          login
            Open a headed browser, let you log in manually, then save session state.
          scrape-quickeval
            Load a Quick Eval page with a saved session and export the visible submission rows.
          scrape-submission
            Load an individual submission/evaluation page and export comments, links, and GitHub hints.
          scrape-submission-map
            Load the Quick Eval page, visit each evaluation URL, and export merged row plus detail data.
          build-grading-worklist
            Join a submission map with an assignment registry and export a grading worklist.
          prepare-grading-repos
            Clone or update repos from a grading worklist and export a repo-ready grading queue.
          build-grading-runner
            Build a Codex-ready grading run queue with prompts, report paths, and tutorial/spec context.

        Examples
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- login
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- scrape-quickeval
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- scrape-quickeval --first-page-only
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- scrape-submission --url "https://mycourses.cnm.edu/d2l/le/activities/iterator/..."
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- scrape-submission-map --limit 5
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- build-grading-worklist --registry "C:\grading\assignment-registry.json"
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- prepare-grading-repos --limit 5
          dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli\InsTK.CLI -- build-grading-runner --limit 5

        Config
          Put shared defaults in instk.json. The core library also falls back to brightspacecli.json for compatibility.
        """;
}
