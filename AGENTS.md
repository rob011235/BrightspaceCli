# BrightspaceCli Workflow

Use this repository for Brightspace browser automation and scraping.

## Branch Strategy

Follow this branch workflow unless the user explicitly asks for something different.

1. Start all new work from `dev`.
2. Create a feature branch off `dev` for each task.
3. Make and verify changes on the feature branch.
4. Merge feature branches back into `dev` when complete.
5. Do not start the next feature branch until the previous completed feature branch has been merged into `dev`.
6. Do not merge into `qa` or `main` unless the user explicitly asks for that step.
7. Treat `qa` as pre-deployment validation.
8. Treat `main` as deployment-ready.

## Current Tool Direction

1. Prefer live browser automation with Playwright over saved HTML scraping when robustness matters.
2. Use manual Brightspace login in a real browser and persist session state locally.
3. Treat Quick Eval list pages as the source of student names, activity names, dates, and evaluation links.
4. Treat individual evaluation pages as the source of pasted repo URLs, branch hints, comments, and submission detail.
5. Keep JSON outputs structured so they can feed later batch grading and tutorial-follow workflows.
