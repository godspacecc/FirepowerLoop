@echo off
setlocal enabledelayedexpansion

set "WORKSPACE=%~dp0.."
set "CONF_ROOT=%~dp0"
set "LUBAN_EXE=%~dp0Luban\Luban.exe"
set "CLIENT_CODE_DIR=%WORKSPACE%\Client\Assets\Game\Scripts\Model\Config"
set "CLIENT_DATA_DIR=%WORKSPACE%\Client\Assets\Game\Config"
set "SERVER_CODE_DIR=%WORKSPACE%\Server\GameServer\Runtime\Model\Generated\Config"
set "SERVER_DATA_DIR=%WORKSPACE%\Server\GameServer\Config\Luban"
set "STAGE_ROOT=%TEMP%\FirepowerLoopLubanGen_%RANDOM%_%RANDOM%"
set "CLIENT_CODE_STAGE=%STAGE_ROOT%\client_code"
set "CLIENT_DATA_STAGE=%STAGE_ROOT%\client_data"
set "SERVER_CODE_STAGE=%STAGE_ROOT%\server_code"
set "SERVER_DATA_STAGE=%STAGE_ROOT%\server_data"

echo === FirepowerLoop Luban generation ===
echo Config: "%CONF_ROOT%"
echo Stage: "%STAGE_ROOT%"

if not exist "%LUBAN_EXE%" (
    echo Error: Luban.exe not found at "%LUBAN_EXE%"
    exit /b 1
)

mkdir "%CLIENT_CODE_STAGE%" "%CLIENT_DATA_STAGE%" "%SERVER_CODE_STAGE%" "%SERVER_DATA_STAGE%" >nul 2>nul
if errorlevel 1 (
    echo Error: failed to create stage directories
    exit /b 1
)

echo.
echo === Generate client ===
"%LUBAN_EXE%" ^
  -t client ^
  -c cs-bin ^
  -d bin ^
  --validationFailAsError ^
  --conf "%CONF_ROOT%\luban.conf" ^
  -x outputCodeDir="%CLIENT_CODE_STAGE%" ^
  -x outputDataDir="%CLIENT_DATA_STAGE%"
if errorlevel 1 exit /b %errorlevel%

dir /b "%CLIENT_CODE_STAGE%\*.cs" >nul 2>nul || (echo Error: client code generation is empty & exit /b 1)
dir /b "%CLIENT_DATA_STAGE%\*.bytes" >nul 2>nul || (echo Error: client data generation is empty & exit /b 1)
set /a CLIENT_TABLE_COUNT=0
for %%F in ("%CLIENT_DATA_STAGE%\*.bytes") do set /a CLIENT_TABLE_COUNT+=1
if not "!CLIENT_TABLE_COUNT!"=="41" (
    echo Error: client table count expected 41 but got !CLIENT_TABLE_COUNT!
    exit /b 1
)
dir /b "%CLIENT_CODE_STAGE%\*Start*.cs" >nul 2>nul && (echo Error: client stage contains server StartConfig code & exit /b 1)
dir /b "%CLIENT_DATA_STAGE%\tbstart*.bytes" >nul 2>nul && (echo Error: client stage contains server StartConfig bytes & exit /b 1)
dir /b "%CLIENT_CODE_STAGE%\AIConfig.cs" >nul 2>nul && (echo Error: client stage contains server AIConfig & exit /b 1)
dir /b "%CLIENT_CODE_STAGE%\UnitConfig.cs" >nul 2>nul && (echo Error: client stage contains server UnitConfig & exit /b 1)

echo.
echo === Generate server ===
"%LUBAN_EXE%" ^
  -t server ^
  -c cs-bin ^
  -d bin ^
  --validationFailAsError ^
  --conf "%CONF_ROOT%\luban.conf" ^
  -x outputCodeDir="%SERVER_CODE_STAGE%" ^
  -x outputDataDir="%SERVER_DATA_STAGE%"
if errorlevel 1 exit /b %errorlevel%

dir /b "%SERVER_CODE_STAGE%\*.cs" >nul 2>nul || (echo Error: server code generation is empty & exit /b 1)
dir /b "%SERVER_DATA_STAGE%\*.bytes" >nul 2>nul || (echo Error: server data generation is empty & exit /b 1)
set /a SERVER_TABLE_COUNT=0
for %%F in ("%SERVER_DATA_STAGE%\*.bytes") do set /a SERVER_TABLE_COUNT+=1
if not "!SERVER_TABLE_COUNT!"=="37" (
    echo Error: server table count expected 37 but got !SERVER_TABLE_COUNT!
    exit /b 1
)
for %%F in (ArcadeWeaponUpgradeConfig TbArcadeWeaponUpgrade ArcadeRoundRewardConfig TbArcadeRoundReward ArcadeDroneConfig TbArcadeDrone ArcadeSlayerChallengeConfig TbArcadeSlayerChallenge ArcadeRuleConfig ArcadeWeaponConfig ArcadeArenaConfig ArcadeEnemyGroupConfig ArcadeBossConfig ArcadeChallengeConfig ArcadeDropConfig ArcadePowerupConfig ArcadeSupportVisualConfig ArcadeUpgradeConfig ArcadeRewardConfig TbArcadeRule TbArcadeWeapon TbArcadeArena TbArcadeEnemyGroup TbArcadeBoss TbArcadeChallenge TbArcadeDrop TbArcadePowerup TbArcadeSupportVisual TbArcadeUpgrade TbArcadeReward ArcadeTreasureRewardConfig ArcadeTreasureRewardKind ArcadeTreasureRoomConfig TbArcadeTreasureRoom) do if not exist "%SERVER_CODE_STAGE%\%%F.cs" (echo Error: server stage missing %%F.cs & exit /b 1)
for %%F in (tbarcadeweaponupgrade tbarcaderoundreward tbarcadedrone tbarcadeslayerchallenge tbarcaderule tbarcadeweapon tbarcadearena tbarcadeenemygroup tbarcadeboss tbarcadechallenge tbarcadedrop tbarcadepowerup tbarcadesupportvisual tbarcadeupgrade tbarcadereward tbarcadetreasureroom) do if not exist "%SERVER_DATA_STAGE%\%%F.bytes" (echo Error: server stage missing %%F.bytes & exit /b 1)

echo.
echo === Publish generated files ===
if not exist "%CLIENT_CODE_DIR%" mkdir "%CLIENT_CODE_DIR%"
if not exist "%CLIENT_DATA_DIR%" mkdir "%CLIENT_DATA_DIR%"
if not exist "%SERVER_CODE_DIR%" mkdir "%SERVER_CODE_DIR%"
if not exist "%SERVER_DATA_DIR%" mkdir "%SERVER_DATA_DIR%"

rem Publish data before code so the previous Tables records delimit retired files. Preserve Unity meta.
powershell -NoProfile -ExecutionPolicy Bypass -File "%CONF_ROOT%publish-generated.ps1" -Source "%CLIENT_DATA_STAGE%" -Destination "%CLIENT_DATA_DIR%" -Pattern "*.bytes" -PreviousTables "%CLIENT_CODE_DIR%\Tables.cs" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%CONF_ROOT%publish-generated.ps1" -Source "%SERVER_DATA_STAGE%" -Destination "%SERVER_DATA_DIR%" -Pattern "*.bytes" -PreviousTables "%SERVER_CODE_DIR%\Tables.cs" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%CONF_ROOT%publish-generated.ps1" -Source "%CLIENT_CODE_STAGE%" -Destination "%CLIENT_CODE_DIR%" -Pattern "*.cs" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%CONF_ROOT%publish-generated.ps1" -Source "%SERVER_CODE_STAGE%" -Destination "%SERVER_CODE_DIR%" -Pattern "*.cs" || exit /b 1

echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%CONF_ROOT%publish-localization.ps1" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%CONF_ROOT%publish-localization.ps1" -Source "%CONF_ROOT%Localization\arcade-exit.json" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%CONF_ROOT%publish-localization.ps1" -Source "%CONF_ROOT%Localization\arcade-details.json" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%CONF_ROOT%publish-localization.ps1" -Source "%CONF_ROOT%Localization\arcade-score-shop.json" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%CONF_ROOT%publish-localization.ps1" -Source "%CONF_ROOT%Localization\arcade-next-three.json" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%CONF_ROOT%publish-localization.ps1" -Source "%CONF_ROOT%Localization\arcade-drop-carrier.json" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%CONF_ROOT%publish-localization.ps1" -Source "%CONF_ROOT%Localization\survivor.json" || exit /b 1
echo === Generation complete ===
echo Client code: "%CLIENT_CODE_DIR%"
echo Client data: "%CLIENT_DATA_DIR%"
echo Server code: "%SERVER_CODE_DIR%"
echo Server data: "%SERVER_DATA_DIR%"
exit /b 0
