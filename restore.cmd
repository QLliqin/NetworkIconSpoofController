@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\restore.ps1"
if errorlevel 1 (
    echo.
    echo Restore failed.
) else (
    echo.
    echo Restore completed.
)
pause
