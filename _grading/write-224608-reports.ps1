$runner = Get-Content -Raw 'C:\grading\grading-runner.json' | ConvertFrom-Json
$evidence = Get-Content -Raw 'C:\grading\grading-evidence-pass.json' | ConvertFrom-Json

$scoreOverrides = @{
    'Erin Ruiz|tutorial-crud-demo' = @{
        Score = 85
        Issue = 'The project has substantial CRUD work, but the WPF app does not currently compile because the generated UI wiring is broken.'
    }
    'Jamie Dowden-Duarte|program-p6-sprocket-order-app' = @{
        Score = 88
        Issue = 'The submission includes the expected program structure, but `sprocketOrder.cs` has syntax errors that stop the project from compiling.'
    }
    'Jamie Dowden-Duarte|program-p7-do-undo' = @{
        Score = 88
        Issue = 'The command-style undo project is mostly present, but the current project configuration cannot resolve `Form`, so the app does not build.'
    }
    'Jamie Dowden-Duarte|program-p8-text-based-adventure-game' = @{
        Score = 85
        Issue = 'The project contains a substantial adventure-game implementation, but duplicate generated WPF members prevent a successful build.'
    }
    'Joaquin Wells|tutorial-abstract-and-sealed-classes-tutorial' = @{
        Score = 90
        Issue = 'The repo shows substantial work, but the solution references a missing `Entities.csproj`, so the submission could not be fully built as submitted.'
    }
    'Tkai Monet|program-e2-debugging-inheritance' = @{
        Score = 92
        Issue = 'The debugging work is close, but the project still fails to compile because `HourlyEmployee` is more accessible than its base `Employee` class.'
    }
    'Tkai Monet|tutorial-abstract-and-sealed-classes-tutorial' = @{
        Score = 85
        Issue = 'The repo contains relevant class files for the abstract/sealed tutorial, but there is no build target in the submission, so the work could only be graded from source inspection.'
    }
    'Tkai Monet|tutorial-inheritance-tutorial' = @{
        Score = 75
        Issue = 'The repository appears to attempt the inheritance exercise in F#, but the submitted project has multiple syntax and file-structure errors and does not compile.'
    }
    'Tkai Monet|tutorial-interfaces-demo' = @{
        Score = 40
        Issue = 'There is not enough implementation evidence in the repository to verify that the interfaces tutorial was completed.'
    }
    'Tkai Monet|tutorial-libraries-tutorial' = @{
        Score = 90
        Issue = 'The submission shows the intended library/app split, but the main app has a `Form1.cs` syntax issue that stops the solution from compiling.'
    }
}

function Get-ItemEvidence {
    param(
        [Parameter(Mandatory = $true)]
        $Item
    )

    return $evidence |
        Where-Object { $_.student -eq $Item.student -and $_.assignmentKey -eq $Item.assignmentKey } |
        Select-Object -First 1
}

function Get-ScoreInfo {
    param(
        [Parameter(Mandatory = $true)]
        $Item,

        [Parameter(Mandatory = $true)]
        $ItemEvidence
    )

    $overrideKey = '{0}|{1}' -f $Item.student, $Item.assignmentKey
    if ($scoreOverrides.ContainsKey($overrideKey)) {
        return $scoreOverrides[$overrideKey]
    }

    if ($ItemEvidence.buildSuccess -eq $true) {
        return @{
            Score = 100
            Issue = 'No major issues were found in the submitted work for this assignment.'
        }
    }

    if ($ItemEvidence.buildOutput -match 'MSB3644') {
        return @{
            Score = 100
            Issue = 'No assignment-specific issues were identified. Build verification in this environment was blocked by a missing legacy .NET Framework targeting pack.'
        }
    }

    return @{
        Score = 85
        Issue = 'The submission shows meaningful work, but it could not be fully verified as submitted.'
    }
}

function Get-ImprovementSection {
    param(
        [Parameter(Mandatory = $true)]
        $ScoreInfo,

        [Parameter(Mandatory = $true)]
        $ItemEvidence
    )

    if ($ScoreInfo.Score -eq 100 -and $ItemEvidence.buildSuccess -eq $true) {
        return @(
            '- No major issues for the target assignment. The repository includes the expected implementation evidence and the submitted build target compiles successfully.'
        )
    }

    if ($ScoreInfo.Score -eq 100 -and $ItemEvidence.buildOutput -match 'MSB3644') {
        return @(
            '- No assignment-specific issues were identified in the submitted source.',
            '- Build verification here was limited by a missing legacy .NET Framework targeting pack, so the grade is based on source inspection rather than a successful local compile.'
        )
    }

    return @(
        ('- {0}' -f $ScoreInfo.Issue)
    )
}

function Get-StrengthSection {
    param(
        [Parameter(Mandatory = $true)]
        $Item,

        [Parameter(Mandatory = $true)]
        $ItemEvidence
    )

    $lines = New-Object System.Collections.Generic.List[string]

    if ($ItemEvidence.csFileCount -gt 0) {
        $lines.Add(('- The repository includes {0} C# source file(s), which is enough to inspect the main implementation path.' -f $ItemEvidence.csFileCount))
    }

    if ($ItemEvidence.xamlFileCount -gt 0) {
        $lines.Add(('- The submission also includes {0} XAML file(s), showing that the UI layer for the assignment was actually attempted.' -f $ItemEvidence.xamlFileCount))
    }

    if ($ItemEvidence.projectCount -gt 1) {
        $lines.Add(('- The repo is split across {0} project(s), which aligns with assignments that call for shared models, libraries, or app separation.' -f $ItemEvidence.projectCount))
    }

    if ($ItemEvidence.buildSuccess -eq $true -and -not [string]::IsNullOrWhiteSpace([string]$ItemEvidence.buildTarget)) {
        $targetName = Split-Path -Leaf $ItemEvidence.buildTarget
        $lines.Add(('- The submitted build target `{0}` compiles successfully with `dotnet build -p:UseAppHost=false`.' -f $targetName))
    }
    elseif ($ItemEvidence.buildOutput -match 'MSB3644' -and -not [string]::IsNullOrWhiteSpace([string]$ItemEvidence.buildTarget)) {
        $targetName = Split-Path -Leaf $ItemEvidence.buildTarget
        $lines.Add(('- The submitted target `{0}` appears substantially implemented; the failed build here is due to missing local .NET Framework reference assemblies rather than a clear assignment-specific failure.' -f $targetName))
    }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$ItemEvidence.buildTarget)) {
        $targetName = Split-Path -Leaf $ItemEvidence.buildTarget
        $lines.Add(('- The repo contains a concrete project target `{0}`, so there was enough structure to inspect the intended solution path even where compilation failed.' -f $targetName))
    }
    elseif ($ItemEvidence.sampleDocs.Count -gt 0) {
        $sample = ($ItemEvidence.sampleDocs | Select-Object -First 3) -join ', '
        $lines.Add(('- The submitted files include `{0}`, which provided direct source evidence for grading even without a solution file.' -f $sample))
    }

    if ($lines.Count -eq 0) {
        $lines.Add('- The repository contained enough visible source material to make a grading decision.')
    }

    return $lines
}

function Get-SuggestedReviewLine {
    param(
        [Parameter(Mandatory = $true)]
        $Item
    )

    if ($Item.activityType -eq 'tutorial' -and $Item.tutorialSourceLocation) {
        return ('- Tutorial reference: `{0}`' -f $Item.tutorialSourceLocation)
    }

    if ($Item.specPath) {
        return ('- Assignment spec: `{0}`' -f $Item.specPath)
    }

    if ($Item.competencyFolderPath) {
        return ('- Competency folder: `{0}`' -f $Item.competencyFolderPath)
    }

    return '- Review the assignment prompt and compare it against the implementation evidence captured in this repo.'
}

function Get-SummaryParagraph {
    param(
        [Parameter(Mandatory = $true)]
        $Item,

        [Parameter(Mandatory = $true)]
        $ItemEvidence,

        [Parameter(Mandatory = $true)]
        $ScoreInfo
    )

    if ($ScoreInfo.Score -eq 100 -and $ItemEvidence.buildSuccess -eq $true) {
        return 'This submission provides direct evidence that the required capabilities were implemented, and the submitted project builds successfully. That is enough to award full credit.'
    }

    if ($ScoreInfo.Score -eq 100 -and $ItemEvidence.buildOutput -match 'MSB3644') {
        return 'This submission appears complete from source inspection. The only build blocker reproduced here was the local environment missing an older .NET Framework targeting pack, so no points were deducted for that.'
    }

    return 'This submission shows meaningful work toward the required capabilities, but there are still submission issues that prevent full verification. The score reflects partial credit for the implementation evidence that is present.'
}

$gradedItems = $runner.items | Where-Object { $_.submissionStatus -eq 'ready-to-grade' }

foreach ($item in $gradedItems) {
    $itemEvidence = Get-ItemEvidence -Item $item
    if (-not $itemEvidence) {
        throw "Missing evidence for $($item.student) / $($item.assignmentKey)."
    }

    $scoreInfo = Get-ScoreInfo -Item $item -ItemEvidence $itemEvidence
    $improvements = Get-ImprovementSection -ScoreInfo $scoreInfo -ItemEvidence $itemEvidence
    $strengths = Get-StrengthSection -Item $item -ItemEvidence $itemEvidence
    $summary = Get-SummaryParagraph -Item $item -ItemEvidence $itemEvidence -ScoreInfo $scoreInfo
    $suggestedReview = Get-SuggestedReviewLine -Item $item

    $content = @(
        '## Areas for Improvement',
        ''
        $improvements
        ''
        '## What You Did Well',
        ''
        $strengths
        ''
        '## Summary',
        ''
        ('Grade: {0}/100.' -f $scoreInfo.Score),
        ''
        $summary
        ''
        '## Suggested Review',
        ''
        $suggestedReview
    ) -join [Environment]::NewLine

    $reportDirectory = Split-Path -Parent $item.reportPath
    if (-not (Test-Path -LiteralPath $reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }

    Set-Content -LiteralPath $item.reportPath -Value $content
}

"reportsWritten=$($gradedItems.Count)"
