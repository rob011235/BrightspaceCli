# BrightspaceCli

A small C# command-line tool for live Brightspace scraping with a persisted authenticated browser session.

## Purpose

This project is intended to reduce grading friction by automating the repetitive Brightspace steps:

- log into Brightspace once in a real browser
- save session state locally
- scrape Quick Eval list pages
- scrape individual submission pages for pasted GitHub links and comments
- export structured JSON that can be used for batch processing

## Commands

```powershell
dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- login --url "https://mycourses.cnm.edu/d2l/le/224618/quickeval/" --state ".brightspace/session.json"
dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- scrape-quickeval --url "https://mycourses.cnm.edu/d2l/le/224618/quickeval/" --state ".brightspace/session.json" --out "_grading/quickeval-live.json"
dotnet run --project C:\Users\Rob011235\source\repos\BrightspaceCli -- scrape-submission --url "https://mycourses.cnm.edu/d2l/le/activities/iterator/..." --state ".brightspace/session.json" --out "_grading/submission-live.json"
```

## Current Shape

- `login` opens a headed browser so you can authenticate manually and then save Playwright session state.
- `scrape-quickeval` extracts the currently visible Quick Eval rows.
- `scrape-submission` extracts visible links and detects GitHub repo hints from an individual evaluation page.

## Notes

- This project depends on `Microsoft.Playwright` and needs a normal `dotnet restore` on a machine with NuGet access.
- After restore, you will likely also need to install the Playwright browser dependencies.
- Quick Eval list pages do not usually contain the GitHub repo URL directly. The repo URL is more likely to appear on the individual submission page.

## Next Useful Additions

- paging through Quick Eval automatically
- opening each evaluation row and scraping detail pages in sequence
- merging list and detail data into one `submission-map.json`
- assignment-level batch export commands
