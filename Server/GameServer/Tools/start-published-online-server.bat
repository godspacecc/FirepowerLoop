@echo off
setlocal

for %%I in ("%~dp0.") do set "GAME_SERVER_ROOT=%%~fI"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%GAME_SERVER_ROOT%\Tools\start-local-online-server.ps1" -GameServerRoot "%GAME_SERVER_ROOT%" -Port 31011
set "SERVER_EXIT_CODE=%ERRORLEVEL%"

endlocal & exit /b %SERVER_EXIT_CODE%
