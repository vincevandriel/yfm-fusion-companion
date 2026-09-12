@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install_dependencies.ps1"
set "result=%ERRORLEVEL%"
echo.
if not "%result%"=="0" echo Dependency check failed with exit code %result%.
pause
exit /b %result%
