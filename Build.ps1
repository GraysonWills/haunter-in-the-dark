param([string]$UnityPath)
$ErrorActionPreference = 'Stop'
$versionFile = Join-Path $PSScriptRoot 'ProjectSettings\ProjectVersion.txt'
$editorVersion = ((Get-Content -LiteralPath $versionFile | Select-String '^m_EditorVersion:').Line -split ':', 2)[1].Trim()
if (-not $UnityPath) {
    $editorCandidates = @(
        (Join-Path $env:ProgramFiles "Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"),
        (Join-Path $env:ProgramFiles "Unity $editorVersion\Editor\Unity.exe")
    )
    $UnityPath = $editorCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $UnityPath -or -not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity $editorVersion was not found. Install it with Windows Build Support through Unity Hub, or pass -UnityPath 'C:\path\to\Editor\Unity.exe'."
}
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'Logs') | Out-Null
$buildLog = Join-Path $PSScriptRoot 'Logs\build.log'
$arguments = '-batchmode -quit -projectPath "{0}" -executeMethod Haunter.Editor.PrototypeBuilder.BuildWindows -logFile "{1}"' -f $PSScriptRoot, $buildLog
$buildTask = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($buildTask.ExitCode -ne 0) { throw "Unity build failed. Read $buildLog" }
$playerPath = Join-Path $PSScriptRoot 'Builds\Windows\HaunterInTheDark.exe'
if (-not (Test-Path -LiteralPath $playerPath -PathType Leaf)) { throw "Unity did not create the player. Read $buildLog" }
Write-Output $playerPath
