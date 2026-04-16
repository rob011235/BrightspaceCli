# Assignment Authoring Redesign

## Purpose

This note proposes a post-semester redesign for `InsTK` that starts with assignment creation instead of starting with Brightspace scraping and grading prep. The goal is to make grading easier, more consistent, and less dependent on open-ended LLM interpretation by capturing stronger assignment artifacts up front.

The core idea is:

1. Help the instructor draft the assignment spec.
2. Help the instructor provide or generate a sample solution.
3. Help the instructor define a rubric in structured form.
4. Use those artifacts later to drive grading preparation, evidence gathering, and agent feedback.

This would shift the app from a grading-only utility into a full assignment lifecycle tool.

## Repository Strategy

The current preferred repository model is:

1. one GitHub organization shared by participating instructors
2. one repository per course
3. one folder per assignment inside the course repository
4. one separate repository per sample solution project
5. the app treats the `main` branch as the definitive published version for grading

This design intentionally relies on standard GitHub collaboration features instead of custom workflow logic inside the app.

## GitHub Collaboration Model

Instructor collaboration should follow normal GitHub practices:

- protect `main`
- protect `dev`
- make assignment changes on feature branches
- review changes through pull requests
- merge approved work through normal GitHub branch policies

The app should assume:

- `main` is the published source of truth
- `dev` is an integration branch, not a grading source
- feature branches are draft authoring branches

This gives the grading workflow a stable rule: published grading content always comes from `main`.

## Why This Direction Makes Sense

The current pipeline already assumes there is an external assignment registry with structured metadata, tutorial/spec references, and optional rubric criteria. That means the redesign does not need a brand-new grading model. It mainly needs a better upstream workflow for producing the inputs the grading model already wants.

Benefits:

- better consistency across assignments
- less manual registry editing
- clearer grading expectations before submissions arrive
- easier reuse across semesters
- lower LLM ambiguity during grading
- more defensible feedback because grading criteria were defined before evaluation

## Portability Goal

The target outcome is that another instructor can install the app, sign into GitHub, sign into Brightspace, and grade without depending on any private local curriculum folders from the original app author.

That means the redesign should replace local curriculum assumptions with:

- GitHub-hosted published course content
- GitHub-hosted referenced sample repositories
- Brightspace scraping for live instructor submission data
- app-managed local caches only as an implementation detail

This is the correct direction, but portability requires a few explicit design rules beyond simply storing files in GitHub.

## Product Direction

The app should become a course-workflow tool with two major modes:

1. Author Assignments
2. Grade Submissions

The current grading workflow would remain, but it would become the second half of the product rather than the entry point.

## GitHub Discovery Model

The app needs a deterministic way to discover which repositories are valid course repositories.

Recommended rules:

- the user signs into GitHub from the app
- the app lists organizations the user belongs to
- the user selects the active teaching organization
- the app lists repositories in that organization
- a repository only counts as a course repository if `course.json` exists on the `main` branch

This avoids relying on naming conventions alone and makes repository discovery portable across instructors.

## Recommended Top-Level Workflow

### Phase 1: Assignment Authoring

The instructor creates or revises an assignment package.

Inputs:

- course
- assignment title
- assignment type
- learning objectives
- required capabilities
- constraints
- starter files or starter repo
- preferred solution shape

Outputs:

- assignment spec
- student-facing instructions
- sample solution reference
- rubric
- assignment registry entry

### Phase 2: Assignment Publishing Support

The app helps prepare the assignment for delivery.

Outputs could include:

- Brightspace-ready assignment text
- linked files or paths
- instructor notes
- grading notes
- sample solution storage path
- rubric summary for LMS entry

### Phase 3: Submission Grading

The existing grading pipeline consumes the authored package instead of relying on ad hoc external materials.

Inputs:

- authored assignment package
- Brightspace submission map
- prepared repos

Outputs:

- grading prompts
- evidence summaries
- reports
- reusable grading history

## Recommended Data Model

Instead of treating the assignment registry as a hand-authored JSON sidecar, treat it as the canonical output of the authoring workflow.

Each assignment package should have these sections:

### 1. Assignment Metadata

- assignment key
- title
- course
- term or revision
- assignment type
- status

### 2. Student Spec

- summary
- requirements
- deliverables
- submission instructions
- constraints
- hints or allowed variation

### 3. Instructor Intent

- what the assignment is actually testing
- common failure modes
- what variation is acceptable
- what should not be penalized

### 4. Reference Solution

- sample solution repo
- default branch or ref
- optional pinned tag or commit
- optional walkthrough notes
- optional expected project/file layout

### 5. Rubric

- summary
- criteria list
- criterion evidence hints
- severity or weight
- examples of acceptable equivalence

### 6. Grading Strategy

- exact-match vs capability-first vs tutorial-equivalence
- preferred grading provider
- fallback grading provider
- required build or run checks
- required file presence checks
- optional test commands
- fallback rules when code does not build

## Opinion on Sample Solutions

Using a sample completed assignment is a good idea, but it should not become the only grading truth.

The best use of a sample solution is:

- as a reference implementation
- as a source of expected structure and behaviors
- as a way to reduce grading ambiguity
- as a way to generate deterministic comparison signals before the LLM is invoked

It should not be used as:

- a strict file-by-file grading template
- a requirement that students match one exact architecture
- the only accepted implementation shape

The correct design is:

- sample solution
- plus structured rubric
- plus instructor-authored notes about acceptable equivalence

That would lower LLM load while preserving instructional flexibility.

The current recommended rule is:

- store sample projects in their own repositories
- reference them from the assignment metadata
- default to the sample repo `main` branch unless the assignment explicitly pins a tag or commit

This keeps the course repository focused on assignment definition while allowing sample projects to remain full runnable repositories with their own history.

## How This Could Reduce LLM Processing Load

The current open-ended grading flow asks the agent to discover too much:

- what the assignment wants
- what counts as success
- how much variation is acceptable
- what evidence matters

An authored assignment package would let the system compute more facts before the LLM sees anything.

Recommended pre-LLM evidence pipeline:

1. Resolve the assignment package.
2. Load the sample solution metadata.
3. Run deterministic checks on the student repo.
4. Produce a compact evidence summary.
5. Ask the LLM only to evaluate the evidence and write feedback.

Deterministic checks could include:

- build success
- test success
- expected file or project presence
- expected route, page, class, or API surface presence
- config or package presence
- path similarity to reference solution
- extracted error messages
- selected code snippets for required capabilities

That would make grading faster, cheaper, and more stable.

## Grading Provider Model

The grading system should not assume that every instructor has the same tools installed locally.

The recommended design is a provider-based grading model:

- default to Ollama for grading because it is local and free to use
- detect which providers are installed on the instructor machine
- allow the instructor to choose a preferred provider per assignment
- allow an optional fallback provider per assignment

Initial providers to support:

- Ollama
- Codex
- Claude

This gives the app a strong open-source path while still supporting stronger tools for more demanding assignments.

## Provider Detection and Setup

The app should include a grading setup page that:

- detects locally available providers
- shows whether Ollama, Codex, or Claude are installed and ready
- gives setup instructions for missing providers
- allows the instructor to test each provider

This is better than assuming Codex is installed or hard-wiring the grading path to a single CLI.

## Recommended Provider Defaults

Recommended defaults:

- app-wide default grading provider -> Ollama
- assignment-level preferred provider -> instructor selectable
- assignment-level fallback provider -> optional

This keeps initial setup lightweight while still supporting stronger providers when the instructor wants them.

## Escalation Rules

Assignments that start on a lower-cost provider should be able to escalate to a stronger provider when needed.

Recommended escalation triggers:

- deterministic evidence is incomplete
- build and source signals disagree
- the first-pass grading output is low confidence or inconclusive
- the assignment is marked high-stakes
- the generated feedback fails validation checks

In those cases, the app should allow or automatically trigger escalation from a lower-cost provider such as Ollama to a stronger provider such as Codex or Claude.

## Grading Provider Abstraction

The app should treat grading as a provider-based system instead of a single hard-coded CLI dependency.

Initial providers could be:

- Codex CLI provider
- Ollama provider
- Claude provider

Future providers could include:

- OpenAI API provider
- other local or hosted grading backends

This keeps the app open-source friendly while avoiding permanent coupling to one engine.

## Recommended UX Shape

### Start Screen

Replace the current grading-first landing page with two large paths:

- Create or Revise Assignment
- Grade Existing Submissions

### Assignment Authoring Wizard

Suggested steps:

1. Choose course
2. Choose assignment type
3. Draft assignment summary and requirements
4. Attach or create reference materials
5. Attach or identify sample solution repository
6. Draft rubric
7. Review generated assignment package
8. Export registry entry and assignment assets

### Rubric Builder

This should be structured, not freeform only.

Each rubric criterion should support:

- short label
- description
- required evidence
- acceptable alternatives
- weight or importance
- feedback guidance

### Sample Solution Screen

The instructor should be able to:

- point to a GitHub repository
- set a default branch, tag, or pinned commit
- mark expected focal areas
- add notes about acceptable deviations

### Grading Mode Selector

Per assignment, the instructor should be able to declare:

- capability-first
- reference-assisted
- tutorial-equivalence
- strict structure required

Per assignment, the instructor should also be able to declare:

- preferred grading provider
- fallback grading provider
- whether fallback to a stronger provider is allowed
- whether the assignment is high-stakes

That removes guesswork during grading.

## Suggested Artifact Layout

One possible future file structure:

```text
course-root/
  course.json
  assignments/
    program-04-crud-contacts/
      assignment.json
      spec.md
      rubric.json
      instructor-notes.md
```

And the sample solution would live in a separate repository, referenced from `assignment.json`.

This would be easier to version, review, and update than one large external registry file.

The app could still export a combined registry JSON for the grading pipeline when needed.

## Required Published Content

To remove dependency on local curriculum folders, the app should treat these files as required published artifacts inside each course repository:

- `course.json`
- one `assignment.json` per assignment folder
- one `spec.md` per assignment
- one `rubric.json` per assignment

Optional files:

- `instructor-notes.md`
- supporting docs

The app should not depend on undocumented folder conventions or freeform file discovery during grading.

## Recommended Metadata Files

The next schema design should define these files:

### `course.json`

This should identify:

- course key
- course title
- organization name
- canonical repository
- default published branch
- optional instructor/team metadata

### `assignment.json`

This should identify:

- assignment key
- title
- assignment type
- status
- spec path
- rubric path
- instructor notes path
- grading strategy
- preferred grading provider
- fallback grading provider
- high-stakes flag
- sample solution repo reference
- sample solution default ref
- optional pinned sample commit or tag

### `rubric.json`

This should remain structured and machine-readable.

Recommended fields:

- rubric summary
- criteria
- evidence hints
- acceptable equivalence notes
- weights or severity
- suggested feedback guidance

Markdown can still be used for the assignment spec and instructor notes, but the rubric should stay structured so grading prep can consume it reliably.

## Brightspace Mapping Rules

The app still needs to map Brightspace submissions to published GitHub assignment packages.

Recommended mapping model:

- every assignment has a stable `assignmentKey`
- every assignment may include one or more Brightspace title aliases
- the scraper captures activity name and activity type from Brightspace
- the grading-prep stage resolves a Brightspace row to an assignment by alias or deterministic matching rules

This mapping should live in published assignment metadata rather than machine-specific local configuration.

Useful future fields in `assignment.json`:

- `assignmentKey`
- `brightspaceTitles`
- `activityType`
- `assignmentPathHint`
- `preferredGradingProvider`
- `fallbackGradingProvider`

## Published-Version Rules

The app should use these branch rules consistently:

- grading always reads course content from the course repo `main` branch
- grading should default to the sample repo `main` branch
- assignment metadata may optionally pin a tag or commit for the sample repo
- the app should not use `dev` or feature branches as authoritative grading inputs

This keeps grading reproducible and prevents accidental use of draft assignment content.

## GitHub Sync and Local Cache

Even if GitHub is the source of truth, the app will still need local cached copies for prompt building, file inspection, and grading.

Recommended model:

- GitHub is the source of truth
- the app syncs course repos and referenced sample repos into an internal local cache
- the cache path is app-managed and not part of an instructor-maintained curriculum directory
- the app refreshes published content on demand and before grading prep when needed

The cache should be treated as disposable and reproducible.

Recommended cache contents:

- resolved course repo checkout
- resolved sample repo checkout
- generated grading artifacts
- session metadata describing which commits were used

## Published Revision Pinning

The app should not keep reading a moving branch during the middle of a grading run. It should resolve the published state at the start of the run.

Recommended rules:

- resolve the course repo `main` branch to a commit hash at grading-run start
- resolve the sample repo reference to a branch, tag, or pinned commit
- if the sample repo reference is a branch, resolve that branch to a commit hash at grading-run start
- store those resolved commits in the grading packet

This creates a reproducible grading snapshot and prevents content drift during longer grading batches.

## Run Isolation and Retention

The app should not write all prompts and reports into one shared global folder across multiple grading sessions.

Each grading batch should create its own run folder.

Recommended layout:

```text
grading-root/
  runs/
    2026-04-15_213737_224618/
      run.json
      grading-runner.json
      prompts/
      reports/
      logs/
      evidence/
```

Recommended naming:

- timestamp
- course or Brightspace identifier

This solves several problems:

- old reports do not get confused with current reports
- progress checks become accurate
- runs become easier to audit
- runs become easier to archive or delete
- grading results become reproducible as a batch

The run manifest should capture:

- run creation time
- course
- source submission map
- resolved course repo commit
- resolved sample repo commit(s)
- selected provider(s)
- item count
- completion state

Recommended retention behavior:

- do not overwrite previous runs
- keep each run isolated by default
- show current run and previous runs separately in the UI
- add manual archive or delete actions later
- optionally add retention rules after the basic run-history model is stable

## Authentication Model

Portability requires two separate authenticated systems.

### GitHub

The app needs GitHub access to:

- list organizations
- list repositories
- read repository contents from `main`
- inspect refs and default branches
- clone or download published course and sample content

### Brightspace

The app needs Brightspace access to:

- sign in with the instructor's account
- persist local session state
- scrape Quick Eval and submission pages for that instructor

These should remain separate authentication flows in the app.

## Instructor Onboarding Flow

To make the app portable for another instructor, first-run onboarding should be explicit.

Recommended onboarding:

1. Sign into GitHub
2. Choose the teaching organization
3. Discover available course repositories from `main`
4. Choose the course repository
5. Sync published assignment content locally
6. Sign into Brightspace
7. Provide or confirm the Quick Eval URL
8. Start grading

After onboarding, the app should remember:

- selected organization
- selected course repo
- local cache paths
- Brightspace session state path

This replaces the current dependency on a local course-materials folder.

## What the App Should No Longer Assume

With this redesign, the app should stop assuming:

- there is a local Dropbox or curriculum root
- there is a local course materials folder prepared by the app author
- there is a local AGENTS.md outside the app-managed cache
- grading context comes from manually maintained local folders

Instead, the app should assume:

- published course content comes from GitHub
- live submission data comes from Brightspace
- grading artifacts are generated locally from those two sources
- the grading provider can vary by assignment and instructor setup
- each grading batch has its own isolated run folder

## Migration Strategy

This redesign does not need a big-bang rewrite.

Recommended sequence:

1. Keep the current grading pipeline.
2. Add assignment-package authoring as a new docs/data workflow.
3. Generate the existing assignment registry from authored packages.
4. Add GitHub-backed course repositories as the canonical assignment source.
5. Add GitHub discovery, sync, and local caching for published course content.
6. Update grading prep to prefer authored packages from the course repo `main` branch when available.
7. Add resolved commit pinning to the grading packet.
8. Add isolated per-run folders and run manifests.
9. Add reference-solution-assisted evidence generation later.

This keeps risk low and avoids breaking current semester operations.

## Design Principles

- author once, grade many times
- store instructor intent explicitly
- separate deterministic evidence gathering from subjective feedback writing
- prefer structured data over long prompt prose
- allow equivalent implementations unless the instructor explicitly forbids them
- make every grading decision trace back to authored criteria

## Risks

- instructors may not want a long setup workflow
- sample solutions can bias grading too narrowly
- over-structuring can make quick assignments slower to create
- rubric authoring can become burdensome if the UI is too detailed

These risks suggest two authoring modes:

- quick authoring for simple assignments
- guided authoring for important or reusable assignments

## Recommendation

This redesign is worth doing.

The strongest product direction is not "replace grading with sample comparison." The stronger direction is "move assignment intent capture to the front of the workflow, then let sample solutions and rubrics reduce grading ambiguity later."

In practical terms:

- yes, add assignment creation
- yes, support sample solutions
- yes, add structured rubric authoring
- no, do not rely on sample diff alone as the grading model

The grading system will perform better if it receives a clear authored assignment package instead of having to reconstruct one on the fly.

The recommended storage and collaboration model is now:

- GitHub organization for shared instructor access
- one course repo per course
- assignment folders inside the course repo
- separate sample repos referenced from assignment metadata
- protected `main` and `dev` branches with normal feature branch and PR workflow
- app reads `main` as the definitive published branch
- app resolves published GitHub content into a local pinned grading context per run
- app no longer depends on private local curriculum folders
- app can route grading between multiple detected providers based on assignment metadata
- app stores grading outputs in isolated run folders instead of shared global report buckets

## Immediate Follow-Up Ideas

After the semester, the next design steps could be:

1. Define `course.json`, `assignment.json`, and `rubric.json`.
2. Define the sample-solution reference object and ref-resolution rules.
3. Define GitHub discovery rules and the cache layout.
4. Define a grading-provider abstraction, provider detection rules, and setup guidance.
5. Prototype an assignment authoring wizard in the MAUI app.
6. Add export logic that generates the current assignment registry from authored packages.
7. Add commit-resolution metadata to the grading packet.
8. Define the per-run folder structure and `run.json` manifest.
9. Add a reference-solution evidence stage before agent grading.
