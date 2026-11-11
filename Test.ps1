$ErrorActionPreference = 'Stop'
$playerPath = Join-Path $PSScriptRoot 'Builds\Windows\HaunterInTheDark.exe'
if (-not (Test-Path -LiteralPath $playerPath)) { throw 'Build the Windows player first with Build.ps1.' }
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'Logs') | Out-Null
$testLog = Join-Path $PSScriptRoot 'Logs\smoke.log'
$arguments = '--haunter-smoke -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "{0}"' -f $testLog
# Rendering must use a visible player window; hidden Windows players return black captures.
$testTask = Start-Process -FilePath $playerPath -ArgumentList $arguments -PassThru
if (-not $testTask.WaitForExit(120000)) { Stop-Process -Id $testTask.Id; throw "Smoke test timed out. Read $testLog" }
$reportPath = Join-Path $PSScriptRoot 'Builds\Windows\Evidence\smoke-report.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw "Smoke test produced no report. Read $testLog" }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
Get-Content -LiteralPath $reportPath
if ($report.failed.Count -gt 0 -or $report.errors.Count -gt 0) { throw "Smoke checks failed. Read $reportPath" }
if ($testTask.ExitCode -ne 0) { throw "Smoke test failed. Read $testLog" }
