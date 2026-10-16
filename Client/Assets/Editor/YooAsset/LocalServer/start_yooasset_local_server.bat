@echo off
setlocal
set "PORT=%~1"
set "ROOT=%~2"
set "LAN_ARG="
if /I "%~3"=="lan" set "LAN_ARG=-ListenOnLan"
title FirepowerLoop Resource Server - %PORT%
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0start_yooasset_local_server.ps1" -Port %PORT% -Root "%ROOT%" %LAN_ARG%
exit /b %errorlevel%
