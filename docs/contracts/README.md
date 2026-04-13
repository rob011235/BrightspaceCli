# InsTK Contracts

This folder contains the canonical example payloads for the `InsTK` grading pipeline.

These files are design-time contract references for the JSON artifacts passed between pipeline stages. They are not runtime outputs and they should not be edited casually. If an artifact shape changes intentionally, update the matching example files, the documented rules in this README, and any code that reads or writes that artifact.

## Purpose

`InsTK` is built as a staged pipeline. Each stage writes a JSON artifact that is consumed by the next stage.

The goals of these contract files are:

- make artifact shapes explicit
- reduce accidental schema drift
- give humans a stable reference when reading or changing the pipeline
- give future validation code and tests a known target
- make Codex or other agents less likely to invent new shapes

## Pipeline Artifacts

The current pipeline produces four core artifact types:

1. `submission-map`
2. `grading-worklist`
3. `grading-repo-queue`
4. `grading-runner`

These correspond to the main pipeline stages:

- Brightspace scraping
- grading enrichment
- repo preparation
- grading run generation

## Files in This Folder

Naming convention:

- `*.example.json` = happy-path example
- `*.failure.example.json` = failure-path example

Recommended files:

- `submission-map.example.json`
- `submission-map.failure.example.json`
- `grading-worklist.example.json`
- `grading-worklist.failure.example.json`
- `grading-repo-queue.example.json`
- `grading-repo-queue.failure.example.json`
- `grading-runner.example.json`
- `grading-runner.failure.example.json`

## General Rules

These rules apply to all artifact files unless explicitly documented otherwise.

### 1. JSON uses camelCase
Property names should use camelCase consistently.

### 2. Every artifact includes `schemaVersion`
Each top-level artifact must include a `schemaVersion` field.

Example:

```json
{
  "schemaVersion": "1.0"
}
```

### 3. Example files must match the current code
These examples are snapshots of the serialized runtime shape. If code changes a property name, adds or removes fields, or changes path sanitization behavior, update the matching example file in the same change.

### 4. Top-level artifact fields

`submission-map.example.json`

- `schemaVersion`
- `scraper`
- `scrapedAt`
- `pageUrl`
- `quickEvalSubmissionCount`
- `processedSubmissionCount`
- `submissions`

`grading-worklist.example.json`

- `schemaVersion`
- `generatedAt`
- `submissionMapPath`
- `registryPath`
- `itemCount`
- `unmappedCount`
- `items`

`grading-repo-queue.example.json`

- `schemaVersion`
- `generatedAt`
- `worklistPath`
- `repoRoot`
- `itemCount`
- `errorCount`
- `items`

`grading-runner.example.json`

- `schemaVersion`
- `generatedAt`
- `repoQueuePath`
- `worklistPath`
- `registryPath`
- `courseRoot`
- `runRoot`
- `itemCount`
- `errorCount`
- `items`

### 5. Path examples should match runtime sanitization
Prompt and report file names should reflect the current sanitization logic used by the runner. If student names are written with dashes in runtime output, the examples should show dashes too.
