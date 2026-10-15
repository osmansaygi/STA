@echo off
REM BETONARME TOOL ofis kurulum — eskiyi sil, tek bundle kur. 2026-10-08
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Kur.ps1"
echo.
pause
