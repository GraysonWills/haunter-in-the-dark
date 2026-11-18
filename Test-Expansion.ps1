$ErrorActionPreference = 'Stop'
$player = Join-Path $PSScriptRoot 'Builds\Windows\HaunterInTheDark.exe'
if (-not (Test-Path -LiteralPath $player -PathType Leaf)) { throw 'Build the Windows player with Build.ps1 first.' }
$logs = Join-Path $PSScriptRoot 'Logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$log = Join-Path $logs 'expansion-smoke.log'
$reportPath = Join-Path $PSScriptRoot 'Builds\Windows\ExpansionEvidence\expansion-report.json'
$startedAt = [DateTime]::UtcNow
$arguments = '-haunterExpansionSmoke -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "{0}"' -f $log
$process = Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(180000)) {
    Stop-Process -Id $process.Id
    throw "The expansion test timed out. Read $log"
}
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf) -or
    (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -lt $startedAt) {
    throw "The expansion test did not produce a fresh report. Read $log"
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
Get-Content -LiteralPath $reportPath
if ($process.ExitCode -ne 0 -or $report.failed.Count -gt 0 -or $report.errors.Count -gt 0) {
    throw "Expansion checks failed. Read $reportPath"
}
