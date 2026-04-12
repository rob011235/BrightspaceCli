# Contracts

## Rules
- Each pipeline stage enriches data and should not remove prior fields.
- Keys should remain stable across versions.
- Fields may be null when data is unavailable.
- Errors are per-item, not global.
- JSON uses camelCase.
- Each artifact includes schemaVersion.

## Artifact notes
### submission-map
Required:
- student
- activityName
- evaluationUrl

Optional / nullable:
- repoUrl
- cloneUrl
- branchHint
- subdirHint
- assignmentPathHint
- previewUrl
- error

### grading-worklist
...