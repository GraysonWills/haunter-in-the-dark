@echo off
if not exist "%~dp0Builds\Windows\HaunterInTheDark.exe" (
    echo Build the game first using Build.ps1 or the Haunter menu in Unity.
    echo For the ready-to-play download, visit:
    echo https://github.com/GraysonWills/haunter-in-the-dark/releases/latest
    pause
    exit /b 1
)
start "Haunter in the Dark" "%~dp0Builds\Windows\HaunterInTheDark.exe"
