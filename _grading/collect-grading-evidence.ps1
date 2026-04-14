$runner = Get-Content -Raw 'C:\grading\grading-runner.json' | ConvertFrom-Json
$items = $runner.items | Where-Object { $_.submissionStatus -eq 'ready-to-grade' }
$results = foreach ($item in $items) {
  $target = $item.gradingTargetPath
  $repoPath = $item.repoPath
  $solutions = @()
  $projects = @()
  if ($target -and (Test-Path -LiteralPath $target)) {
    $allFiles = @(Get-ChildItem -LiteralPath $target -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' })
    $solutions = @($allFiles | Where-Object { $_.Extension -in @('.sln', '.slnx') } | Select-Object -ExpandProperty FullName)
    $projects = @($allFiles | Where-Object { $_.Extension -eq '.csproj' } | Select-Object -ExpandProperty FullName)
  }

  $buildTarget = $solutions | Select-Object -First 1
  if (-not $buildTarget) { $buildTarget = $projects | Select-Object -First 1 }

  $buildSuccess = $null
  $buildOutput = $null
  if ($buildTarget) {
    try {
      $buildOutput = & dotnet build "$buildTarget" -p:UseAppHost=false 2>&1 | Out-String
      $buildSuccess = $LASTEXITCODE -eq 0
    }
    catch {
      $buildSuccess = $false
      $buildOutput = $_.Exception.Message
    }
  }

  $csFiles = 0
  $xamlFiles = 0
  $razorFiles = 0
  $docs = @()
  if ($target -and (Test-Path -LiteralPath $target)) {
    $csFiles = @(Get-ChildItem -LiteralPath $target -Recurse -File -Filter *.cs -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' }).Count
    $xamlFiles = @(Get-ChildItem -LiteralPath $target -Recurse -File -Filter *.xaml -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' }).Count
    $razorFiles = @(Get-ChildItem -LiteralPath $target -Recurse -File -Filter *.razor -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' }).Count
    $docs = @(Get-ChildItem -LiteralPath $target -Recurse -File -Include *.md,*.txt,*.docx -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' } | Select-Object -First 8 -ExpandProperty Name)
  }

  [pscustomobject]@{
    student = $item.student
    assignmentKey = $item.assignmentKey
    activityName = $item.activityName
    repoPath = $repoPath
    gradingTargetPath = $target
    selectedBranch = $item.selectedBranch
    buildTarget = $buildTarget
    buildSuccess = $buildSuccess
    buildOutput = $buildOutput
    solutionCount = $solutions.Count
    projectCount = $projects.Count
    csFileCount = $csFiles
    xamlFileCount = $xamlFiles
    razorFileCount = $razorFiles
    sampleDocs = $docs
    promptPath = $item.promptPath
    reportPath = $item.reportPath
  }
}
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath 'C:\grading\grading-evidence-pass.json'
