param(
    [string]$UnityPath,
    [ValidateSet('Windows', 'WebGL')][string]$Target = 'Windows'
)
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
    throw "Unity $editorVersion was not found. Install it with the desired build support through Unity Hub, or pass -UnityPath 'C:\path\to\Editor\Unity.exe'."
}
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'Logs') | Out-Null
$buildLog = Join-Path $PSScriptRoot $(if ($Target -eq 'WebGL') { 'Logs\build-webgl.log' } else { 'Logs\build.log' })
$buildMethod = if ($Target -eq 'WebGL') { 'BuildWebGL' } else { 'BuildWindows' }
$buildPlatform = if ($Target -eq 'WebGL') { 'WebGL' } else { 'Win64' }
$arguments = '-batchmode -quit -projectPath "{0}" -buildTarget {1} -executeMethod Haunter.Editor.PrototypeBuilder.{2} -logFile "{3}"' -f $PSScriptRoot, $buildPlatform, $buildMethod, $buildLog
$buildTask = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($buildTask.ExitCode -ne 0) { throw "Unity build failed. Read $buildLog" }
$playerPath = Join-Path $PSScriptRoot $(if ($Target -eq 'WebGL') { 'Builds\WebGL\index.html' } else { 'Builds\Windows\HaunterInTheDark.exe' })
if (-not (Test-Path -LiteralPath $playerPath -PathType Leaf)) { throw "Unity did not create the player. Read $buildLog" }
Write-Output $playerPath
