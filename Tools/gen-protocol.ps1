$ErrorActionPreference = "Stop"

$ToolsRoot = Split-Path -Parent $PSCommandPath
$Root = Split-Path -Parent $ToolsRoot

$Project = Join-Path $Root "Tools/ProtocolGenerator/ProtocolGenerator.csproj"
$Source = Join-Path $Root "Config/Proto"
$ClientOutput = Join-Path $Root "Client/Assets/Game/Scripts/Network/Generated"
$ServerOutput = Join-Path $Root "Server/GameServer/Runtime/Model/Generated/Message"

dotnet run --project $Project -- `
    --source $Source `
    --client-output $ClientOutput `
    --server-output $ServerOutput

exit $LASTEXITCODE
