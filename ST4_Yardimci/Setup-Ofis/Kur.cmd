@echo off
REM BETONARME TOOL ofis kurulum — 2026-10-06.6
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Kur.ps1"
echo.
pause
