@echo off
setlocal

for %%I in ("%~dp0.") do set "GAME_SERVER_ROOT=%%~fI"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%GAME_SERVER_ROOT%\Publish-win-x64.ps1" -GameServerRoot "%GAME_SERVER_ROOT%"
set "PUBLISH_EXIT_CODE=%ERRORLEVEL%"

endlocal & exit /b %PUBLISH_EXIT_CODE%
