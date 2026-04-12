# BrightspaceCli

A small C# command-line tool for live Brightspace scraping with a persisted authenticated browser session.

## Purpose

This project is intended to reduce grading friction by automating the repetitive Brightspace steps:

- log into Brightspace once in a real browser
- save session state locally
- scrape Quick Eval list pages
- scrape individual submission pages for pasted GitHub links and comments
- export structured JSON that can be used for batch processing

## First-Time Setup

Run these steps before the first live test.

```powershell
cd C:\Users\Rob011235\source\repos\BrightspaceCli
dotnet restore .\BrightspaceCli.sln
dotnet build .\BrightspaceCli.sln
pwsh .\bin\Debug\net10.0\playwright.ps1 install
```

If the Playwright script is not present, try:

```powershell
cd C:\Users\Rob011235\source\repos\BrightspaceCli
playwright install
```

## Config

Browser choice now comes from `brightspacecli.json` in the repo root.

Example:

```json
{
  "browserChannel": "msedge",
  "quickEvalUrl": "https://mycourses.cnm.edu/d2l/le/224618/quickeval/",
  "submissionUrl": "https://mycourses.cnm.edu/d2l/le/activities/iterator/...",
  "statePath": ".brightspace/session.json",
  "quickEvalOutPath": "_grading/quickeval-live.json",
  "submissionOutPath": "_grading/submission-live.json",
  "submissionMapOutPath": "_grading/submission-map.json",
  "assignmentRegistryPath": "C:\\grading\\assignment-registry.json",
  "gradingWorklistOutPath": "C:\\grading\\grading-worklist.json",
  "gradingRepoRoot": "C:\\grading\\repos",
  "gradingRepoQueueOutPath": "C:\\grading\\grading-repo-queue.json",
  "courseRootPath": "C:\\Users\\Rob011235\\Dropbox\\CNM\\_Curriculum\\CIST 2284 .NET II",
  "gradingRunRoot": "C:\\grading\\runs",
  "gradingRunnerOutPath": "C:\\grading\\grading-runner.json"
}
```

You can change `browserChannel` to `chrome` if needed. Command-line values still override config values for a single run.

## First Test

After setup, the first useful smoke test is:

```powershell
cd C:\Users\Rob011235\source\repos\BrightspaceCli
dotnet run --project . -- login
```

If you want to override the configured browser once without editing the config file:

```powershell
dotnet run --project . -- login --channel chrome
```

Then save a live Quick Eval scrape with:

```powershell
cd C:\Users\Rob011235\source\repos\BrightspaceCli
dotnet run --project . -- scrape-quickeval
```

## Commands

```powershell
dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- login
dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- scrape-quickeval
dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- scrape-quickeval --first-page-only
dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- scrape-submission --url "https://mycourses.cnm.edu/d2l/le/activities/iterator/..."
dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- scrape-submission-map --limit 5
dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- build-grading-worklist --registry "C:\grading\assignment-registry.json"
dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- prepare-grading-repos --limit 5
dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- build-grading-runner --limit 5
```

## Current Shape

- `login` opens a headed browser so you can authenticate manually and then save Playwright session state.
- `scrape-quickeval` now attempts to page through all available Quick Eval rows by default.
- `scrape-submission` extracts visible links and detects GitHub repo hints from an individual evaluation page.
- `scrape-submission-map` starts from Quick Eval rows, visits each evaluation URL, and writes a merged batch export including preview URLs, repo hints, and assignment path hints when present.
- Quick Eval rows and submission-map entries now include `activityType` and `assignmentKey` so downstream grading tools can join submissions to a course assignment registry.
- `build-grading-worklist` joins `submission-map.json` with an external assignment registry and writes a grading-ready worklist.
- `prepare-grading-repos` clones or updates repos from the grading worklist and writes a repo-ready grading queue with resolved branch and folder hints.
- `build-grading-runner` turns the prepared repo queue into a Codex-ready grading run queue with prompt files, report paths, and resolved tutorial or competency context.

Use `--first-page-only` with `scrape-quickeval` or `scrape-submission-map` if you want to disable paging and only use the currently visible rows.

For grading runtime artifacts, prefer `C:\grading` over the course Dropbox workspace to avoid long path failures when cloning student repositories.

The grading runner writes:

- `C:\grading\grading-runner.json`
- `C:\grading\runs\prompts\...`
- `C:\grading\runs\reports\...`

Each prompt file contains the student, assignment, selected repo path, branch, selected folder, tutorial or spec context, and the required grading report format.

## Notes

- This project depends on `Microsoft.Playwright` and needs a normal `dotnet restore` on a machine with NuGet access.
- Quick Eval list pages do not usually contain the GitHub repo URL directly. The repo URL is more likely to appear on the individual submission page.
- The current code is ready for smoke testing, but it still needs live validation against your Brightspace workflow before it should be treated as stable.

## Next Useful Additions

- paging through Quick Eval automatically
- opening each evaluation row and scraping detail pages in sequence
- merging list and detail data into one `submission-map.json`
- assignment-level batch export commands
