$ErrorActionPreference = 'Stop'

$runnerPath = 'C:\grading\grading-runner.json'
$codexPath = 'C:\Users\Rob011235\AppData\Roaming\npm\codex.cmd'

if (-not (Test-Path $runnerPath)) {
    throw "Runner file not found: $runnerPath"
}

if (-not (Test-Path $codexPath)) {
    throw "Codex CLI not found: $codexPath"
}

$runner = Get-Content -Raw -LiteralPath $runnerPath | ConvertFrom-Json
$results = New-Object System.Collections.Generic.List[object]

foreach ($item in $runner.items) {
    $workingDirectory = if (-not [string]::IsNullOrWhiteSpace($item.repoPath)) {
        $item.repoPath
    }
    else {
        $item.selectedFolderPath
    }

    if (-not $workingDirectory -or -not (Test-Path $workingDirectory)) {
        $results.Add([pscustomobject]@{
                Index      = $item.index
                Student    = $item.student
                Activity   = $item.activityName
                ExitCode   = -1
                Status     = 'missing-working-directory'
                ReportPath = $item.reportPath
            })
        continue
    }

    if (-not (Test-Path $item.promptPath)) {
        $results.Add([pscustomobject]@{
                Index      = $item.index
                Student    = $item.student
                Activity   = $item.activityName
                ExitCode   = -1
                Status     = 'missing-prompt'
                ReportPath = $item.reportPath
            })
        continue
    }

    $promptText = Get-Content -Raw -LiteralPath $item.promptPath
    $reportDir = Split-Path -Parent $item.reportPath
    New-Item -ItemType Directory -Force -Path $reportDir | Out-Null

    if (Test-Path $item.reportPath) {
        Remove-Item -LiteralPath $item.reportPath -Force
    }

    $lastMessagePath = Join-Path $env:TEMP ("instk-codex-last-message-{0}-{1}.txt" -f $item.index, [guid]::NewGuid().ToString('N'))
    $args = @(
        'exec',
        '-',
        '--full-auto',
        '--skip-git-repo-check',
        '--output-last-message',
        $lastMessagePath,
        '--add-dir',
        $reportDir,
        '--color',
        'never'
    )

    if ($promptText -match '(?m)^Course root: (.+)$') {
        $courseRoot = $Matches[1].Trim()
        if (Test-Path $courseRoot) {
            $args += @('--add-dir', $courseRoot)
        }
    }

    if ($item.selectedFolderPath -and (Test-Path $item.selectedFolderPath) -and ($item.selectedFolderPath -ne $workingDirectory)) {
        $args += @('--add-dir', $item.selectedFolderPath)
    }

    Write-Host ("[{0}/{1}] {2} - {3}" -f ($item.index + 1), $runner.items.Count, $item.student, $item.activityName)

    Push-Location $workingDirectory
    try {
        $null = $promptText | & $codexPath @args 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ((-not (Test-Path $item.reportPath) -or ((Get-Item $item.reportPath).Length -eq 0)) -and (Test-Path $lastMessagePath)) {
        $lastMessage = Get-Content -Raw -LiteralPath $lastMessagePath
        if (-not [string]::IsNullOrWhiteSpace($lastMessage)) {
            Set-Content -LiteralPath $item.reportPath -Value $lastMessage
        }
    }

    $results.Add([pscustomobject]@{
            Index      = $item.index
            Student    = $item.student
            Activity   = $item.activityName
            ExitCode   = $exitCode
            Status     = if (Test-Path $item.reportPath) { 'report-written' } else { 'no-report' }
            ReportPath = $item.reportPath
        })

    Remove-Item -LiteralPath $lastMessagePath -ErrorAction SilentlyContinue
}

$results | Format-Table -AutoSize
