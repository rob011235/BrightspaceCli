$ErrorActionPreference = 'Stop'

$runnerPath = 'C:\grading\grading-runner.json'
$outPath = 'C:\grading\grade-summary.md'
$csvPath = 'C:\grading\grade-summary.csv'

if (-not (Test-Path $runnerPath)) {
    throw "Runner file not found: $runnerPath"
}

$runner = Get-Content -Raw -LiteralPath $runnerPath | ConvertFrom-Json
$rows = New-Object System.Collections.Generic.List[object]

function Get-FirstSentence {
    param(
        [string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ''
    }

    $normalized = ($Text -replace '\r?\n', ' ' -replace '\s+', ' ').Trim()
    $match = [regex]::Match($normalized, '^(.+?[.!?])(?:\s|$)')
    if ($match.Success) {
        return $match.Groups[1].Value.Trim()
    }

    return $normalized
}

foreach ($item in $runner.items) {
    $reportText = Get-Content -Raw -LiteralPath $item.reportPath

    $gradeMatch = [regex]::Match($reportText, 'Grade:\s*`?(?<score>\d{1,3})/100`?', 'IgnoreCase')
    $grade = if ($gradeMatch.Success) { [int]$gradeMatch.Groups['score'].Value } else { $null }

    $summaryText = ''
    $summaryMatch = [regex]::Match($reportText, '(?ms)^Summary\s*\r?\n(?<body>.*?)(?:\r?\n\r?\nSuggested Review|\z)')
    if ($summaryMatch.Success) {
        $summaryText = $summaryMatch.Groups['body'].Value.Trim()
    }

    $rows.Add([pscustomobject]@{
            Student          = $item.student
            Activity         = $item.activityName
            ActivityType     = $item.activityType
            Mode             = $item.gradingMode
            Grade            = $grade
            PipelineError    = $item.error
            ReportPath       = $item.reportPath
            SummarySentence  = Get-FirstSentence -Text $summaryText
        })
}

$gradedRows = @($rows | Where-Object { $_.Grade -ne $null })
$average = if ($gradedRows.Count -gt 0) {
    [math]::Round((($gradedRows | Measure-Object -Property Grade -Average).Average), 2)
}
else {
    $null
}

$highest = @($gradedRows | Sort-Object -Property @{ Expression = 'Grade'; Descending = $true }, Student, Activity | Select-Object -First 3)
$lowest = @($gradedRows | Sort-Object -Property Grade, Student, Activity | Select-Object -First 3)
$pipelineErrors = @($rows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.PipelineError) })

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Grade Summary')
$lines.Add('')
$lines.Add(("Generated: {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')))
$lines.Add(("Runner: {0}" -f $runnerPath))
$lines.Add(("Item count: {0}" -f $rows.Count))
$lines.Add(("Average grade: {0}" -f $average))
$lines.Add(("Pipeline issues flagged in runner: {0}" -f $pipelineErrors.Count))
$lines.Add('')
$lines.Add('## Overview')
$lines.Add('')
$lines.Add('| Metric | Value |')
$lines.Add('|---|---:|')
$lines.Add(("| Graded items | {0} |" -f $gradedRows.Count))
$lines.Add(("| Average | {0} |" -f $average))
$lines.Add(("| 90-100 | {0} |" -f (@($gradedRows | Where-Object { $_.Grade -ge 90 }).Count)))
$lines.Add(("| 80-89 | {0} |" -f (@($gradedRows | Where-Object { $_.Grade -ge 80 -and $_.Grade -lt 90 }).Count)))
$lines.Add(("| 70-79 | {0} |" -f (@($gradedRows | Where-Object { $_.Grade -ge 70 -and $_.Grade -lt 80 }).Count)))
$lines.Add(("| Below 70 | {0} |" -f (@($gradedRows | Where-Object { $_.Grade -lt 70 }).Count)))
$lines.Add('')
$lines.Add('## Highest Grades')
$lines.Add('')
$lines.Add('| Student | Activity | Grade |')
$lines.Add('|---|---|---:|')
foreach ($row in $highest) {
    $lines.Add(("| {0} | {1} | {2} |" -f $row.Student, $row.Activity, $row.Grade))
}

$lines.Add('')
$lines.Add('## Lowest Grades')
$lines.Add('')
$lines.Add('| Student | Activity | Grade |')
$lines.Add('|---|---|---:|')
foreach ($row in $lowest) {
    $lines.Add(("| {0} | {1} | {2} |" -f $row.Student, $row.Activity, $row.Grade))
}

$lines.Add('')
$lines.Add('## Grade Sheet')
$lines.Add('')
$lines.Add('| Student | Activity | Type | Mode | Grade | Notes |')
$lines.Add('|---|---|---|---|---:|---|')
foreach ($row in ($rows | Sort-Object Student, Activity)) {
    $note = if (-not [string]::IsNullOrWhiteSpace($row.PipelineError)) {
        "Pipeline: $($row.PipelineError)"
    }
    else {
        $row.SummarySentence
    }

    $safeNote = ($note -replace '\|', '/' -replace '\r?\n', ' ' -replace '\s+', ' ').Trim()
    $lines.Add(("| {0} | {1} | {2} | {3} | {4} | {5} |" -f $row.Student, $row.Activity, $row.ActivityType, $row.Mode, $row.Grade, $safeNote))
}

if ($pipelineErrors.Count -gt 0) {
    $lines.Add('')
    $lines.Add('## Runner Issues')
    $lines.Add('')
    foreach ($row in ($pipelineErrors | Sort-Object Student, Activity)) {
        $lines.Add(("- `{0}` / `{1}`: {2}" -f $row.Student, $row.Activity, $row.PipelineError))
    }
}

$outDir = Split-Path -Parent $outPath
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Set-Content -LiteralPath $outPath -Value $lines -Encoding UTF8

$rows |
    Select-Object Student, Activity, ActivityType, Mode, Grade, PipelineError, SummarySentence, ReportPath |
    Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8

Write-Host "Wrote $outPath"
Write-Host "Wrote $csvPath"
