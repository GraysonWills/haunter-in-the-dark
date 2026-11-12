$ErrorActionPreference = 'Stop'
$playerPath = Join-Path $PSScriptRoot 'Builds\Windows\HaunterInTheDark.exe'
if (-not (Test-Path -LiteralPath $playerPath)) { throw 'Build the Windows player first with Build.ps1.' }
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'Logs') | Out-Null
$testLog = Join-Path $PSScriptRoot 'Logs\tutorial-smoke.log'
$reportPath = Join-Path $PSScriptRoot 'Builds\Windows\TutorialEvidence\tutorial-report.json'
$startedAt = [DateTime]::UtcNow
$arguments = '--haunter-tutorial-smoke -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "{0}"' -f $testLog
# Run gameplay assertions without opening a window. Captures may be black when hidden;
# rendered presentation is checked separately in a visible interactive player.
$testTask = Start-Process -FilePath $playerPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $testTask.WaitForExit(195000)) { Stop-Process -Id $testTask.Id; throw "Tutorial test timed out. Read $testLog" }
if (-not (Test-Path -LiteralPath $reportPath)) { throw "Tutorial test produced no report. Read $testLog" }
if ((Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $startedAt) { throw "Tutorial test left a stale report. Read $testLog" }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
Get-Content -LiteralPath $reportPath
if ($report.failed.Count -gt 0 -or $report.errors.Count -gt 0) { throw "Tutorial checks failed. Read $reportPath" }
if ($testTask.ExitCode -ne 0) { throw "Tutorial test failed. Read $testLog" }
