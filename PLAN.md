 # InsTK Plan

## Purpose

Build a C# CLI that automates Brightspace collection work with a real authenticated browser session so grading prep does not depend on manual ZIP exports or saved HTML.

The tool should eventually support:

1. logging into Brightspace in a real browser
2. persisting session state locally
3. scraping Quick Eval list pages
4. scraping individual submission/evaluation pages
5. extracting student names, activity names, dates, comments, pasted URLs, and GitHub repo links
6. exporting structured JSON that can feed batch grading and tutorial-follow workflows

## Current State

The repository currently contains a first-pass Playwright-based CLI with these commands:

- `login`
- `scrape-quickeval`
- `scrape-submission`

Current files:

- `InsTK.sln`
- `InsTK.CLI/InsTK.CLI.csproj`
- `InsTK.Core/InsTK.Core.csproj`
- `Program.cs`
- `README.md`
- `AGENTS.md`

The current CLI shape is intentionally simple and lives in a single `Program.cs` file. It is ready for smoke testing, but it should not yet be treated as stable.

## What Has Already Been Learned

### Brightspace Page Reality

The saved Quick Eval list page does not usually contain the GitHub repo URL itself.

The Quick Eval list page does contain:

- student name
- activity name
- submission date
- evaluation link for each row

The individual evaluation/submission page is the more likely place to find:

- pasted GitHub repo URL
- branch or subfolder hints in comments
- submission comments
- other direct submission details

That means robust automation needs both page types.

### Why Saved HTML Was Not Enough

A Python saved-HTML parser was built in the course workspace as a fallback, but it exposed the main limitation:

- saved HTML can help with offline extraction
- it is not the right long-term architecture for repeatable automation
- the robust path is live browser automation with session persistence

### Branch Workflow Requirement

The user wants this repo to follow:

- `dev` as the integration branch
- feature branches created off `dev`
- merge feature branches back into `dev`
- `qa` for pre-deployment testing
- `main` for deployment-ready code

Do not merge into `qa` or `main` unless explicitly asked.

Current working branch at the time this plan was written:

- `feature/add-branch-workflow`

## Smoke-Test Status

The user already ran:

1. `dotnet restore .\InsTK.sln`
2. `dotnet build .\InsTK.sln`

The README was updated to include the next step for Playwright browser installation:

```powershell
cd C:\Users\Rob011235\source\repos\BrightspaceCli
pwsh .\bin\Debug\net10.0\playwright.ps1 install
```

Fallback if that script is not present:

```powershell
playwright install
```

## Recommended Next Steps

### Immediate

1. Run the Playwright browser install if it has not already been done.
2. Run the `login` command and verify that session state is saved successfully.
3. Run `scrape-quickeval` against a live Quick Eval page and inspect the JSON output.
4. Run `scrape-submission` against one live evaluation page and verify that pasted URLs and GitHub links are captured.

### Next Development Slice

After the first smoke test works, the next useful feature work is:

1. add automatic paging or load-more support for Quick Eval
2. add a workflow that visits each evaluation row automatically
3. merge list-page and detail-page data into one `submission-map.json`
4. support assignment-level batch export instead of one-off page scraping

### Likely Refactor

Once the first smoke test is validated, refactor the single-file CLI into a cleaner structure such as:

- `Commands/`
- `Models/`
- `Services/`
- `Infrastructure/`

Suggested split:

- `LoginCommand`
- `ScrapeQuickEvalCommand`
- `ScrapeSubmissionCommand`
- `PlaywrightSessionFactory`
- `GitHubHintParser`
- `JsonFileWriter`

## Relationship To Course Workspace

There is related fallback tooling in the course workspace at:

- `C:\Users\Rob011235\Dropbox\CNM\_Curriculum\CIST 2284 .NET II\tools\brightspace\quickeval_scraper.py`

That Python tool should be treated as a fallback or offline helper, not the primary long-term solution.

The course workspace `AGENTS.md` was also updated to prefer this standalone repo for Brightspace automation work.

## Instructions For Future Sessions

If a future Codex session is started in this repo, begin by:

1. reading `AGENTS.md`
2. reading `README.md`
3. reading this file
4. checking the current git branch and status
5. continuing work from `dev` via a feature branch unless explicitly directed otherwise

## Suggested First Task From Here

If starting fresh in this repo, the most useful immediate task is:

- run a live smoke test and fix any selectors, session persistence issues, or Playwright setup problems found during the first real run
