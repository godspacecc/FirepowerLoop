$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$WorkspaceRoot = Split-Path -Parent (Split-Path -Parent $Root)
$Failures = New-Object System.Collections.Generic.List[string]

function Add-Failure {
    param([string]$Message)
    $Failures.Add($Message) | Out-Null
}

function Require-Path {
    param([string]$RelativePath)
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Failure "Missing required path: $RelativePath"
    }
}

function Require-WorkspacePath {
    param([string]$RelativePath)
    $path = Join-Path $WorkspaceRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Failure "Missing required workspace path: $RelativePath"
    }
}

function Forbid-Path {
    param([string]$RelativePath)
    $path = Join-Path $Root $RelativePath
    if (Test-Path -LiteralPath $path) {
        Add-Failure "Forbidden path remains: $RelativePath"
    }
}

function Forbid-WorkspacePath {
    param([string]$RelativePath)
    $path = Join-Path $WorkspaceRoot $RelativePath
    if (Test-Path -LiteralPath $path) {
        Add-Failure "Forbidden workspace path remains: $RelativePath"
    }
}

function Require-Text {
    param(
        [string]$RelativePath,
        [string]$Pattern,
        [string]$Description
    )
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Failure "Missing required text file: $RelativePath"
        return
    }
    if (-not (Select-String -LiteralPath $path -Pattern $Pattern -Quiet)) {
        Add-Failure "Text contract failed in ${RelativePath}: $Description"
    }
}

function Require-WorkspaceText {
    param(
        [string]$RelativePath,
        [string]$Pattern,
        [string]$Description
    )
    $path = Join-Path $WorkspaceRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Failure "Missing required workspace text file: $RelativePath"
        return
    }
    if (-not (Select-String -LiteralPath $path -Pattern $Pattern -Quiet)) {
        Add-Failure "Text contract failed in ${RelativePath}: $Description"
    }
}

function Forbid-WorkspaceText {
    param(
        [string]$RelativePath,
        [string]$Pattern,
        [string]$Description
    )
    $path = Join-Path $WorkspaceRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Failure "Missing required workspace text file: $RelativePath"
        return
    }
    if (Select-String -LiteralPath $path -Pattern $Pattern -Quiet) {
        Add-Failure "Forbidden text contract failed in ${RelativePath}: $Description"
    }
}

$requiredPaths = @(
    "ET.sln",
    "DotNet/DotNet.sln",
    "DotNet/Directory.Build.props",
    "Runtime/Core",
    "Runtime/Model/Server",
    "Runtime/Model/Share",
    "Runtime/Model/Generated/Config",
    "Runtime/Model/Generated/Message",
    "Runtime/Hotfix/Server",
    "Runtime/Hotfix/Share",
    "Runtime/ThirdParty/DotRecast",
    "Runtime/ThirdParty/ETTask",
    "Runtime/ThirdParty/Kcp",
    "Runtime/ThirdParty/Luban",
    "Runtime/ThirdParty/NativeCollection",
    "Runtime/ThirdParty/TrueSync",
    "Runtime/ThirdParty/Unity.Mathematics",
    "Runtime/ThirdParty/Unity.Mathematics/LICENSE.md",
    "LICENSE",
    "BASELINE.md"
)

foreach ($relativePath in $requiredPaths) {
    Require-Path $relativePath
}

$requiredWorkspacePaths = @(
    "Config/gen.bat",
    "Config/Defines/beans.xml",
    "Config/Defines/tables.xml",
    "Config/DataCsv/server_ai.csv",
    "Config/DataCsv/server_unit.csv",
    "Config/DataCsv/server_start_machine.csv",
    "Config/DataCsv/server_start_process.csv",
    "Config/DataCsv/server_start_zone.csv",
    "Config/DataCsv/server_start_scene.csv",
    "Config/Proto/ClientMessage_C_1000.proto",
    "Config/Proto/OuterMessage_C_10001.proto",
    "Config/Proto/InnerMessage_S_20001.proto",
    "Config/Proto/LockStepOuter_C_11001.proto",
    "Config/Proto/LockStepInner_S_21001.proto",
    "Config/Proto/OnlineOuter_C_12001.proto",
    "Config/Proto/OnlineInner_S_22001.proto",
    "Tools/ProtocolGenerator/ProtocolGenerator.csproj",
    "Tools/ProtocolGenerator/Program.cs",
    "Tools/ProtocolGenerator/ProtocolGenerationOptions.cs",
    "Tools/ProtocolGenerator/ProtocolGenerator.cs",
    "Tools/gen-protocol.ps1",
    "Tools/test-protocol-generator.ps1",
    "Client/Assets/Game/Game.asmdef",
    "Client/Assets/Game/Scripts/Game.cs",
    "Client/Assets/Game/Scripts/ThirdParty/ET/EtMessageRuntime.cs",
    "Client/Assets/Game/Scripts/ThirdParty/Kcp/Kcp.cs",
    "Client/Assets/Game/Scripts/ThirdParty/Kcp/KcpPatial.cs",
    "Client/Assets/Game/Scripts/ThirdParty/Kcp/ikcpc.cs",
    "Client/Assets/Game/Scripts/ThirdParty/Kcp/ikcph.cs",
    "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs",
    "Client/Assets/Game/Scripts/Utility/Network/Serialization/MemoryPackMessageCodec.cs",
    "Client/Assets/Game/Scripts/Utility/Network/Runtime/NetworkClock.cs",
    "Client/Assets/Game/Scripts/Utility/Network/Rpc/RpcMessageDispatcher.cs",
    "Client/Assets/Game/Scripts/Utility/Network/Transport/TcpClientTransport.cs",
    "Client/Assets/Game/Scripts/Utility/Network/Adapter/INetworkRuntime.cs",
    "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkEndpoint.cs",
    "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkConfigProvider.cs",
    "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkRuntime.cs",
    "Client/Assets/StreamingAssets/networkConfig.json",
    "Client/Assets/Game/Scripts/Network/Generated/ClientMessage_C_1000.cs",
    "Client/Assets/Game/Scripts/Network/Generated/LockStepOuter_C_11001.cs",
    "Client/Assets/Game/Scripts/Network/Generated/OnlineOuter_C_12001.cs",
    "Client/Assets/Game/Scripts/Network/Generated/OuterMessage_C_10001.cs"
)

foreach ($relativePath in $requiredWorkspacePaths) {
    Require-WorkspacePath $relativePath
}

$requiredServerConfigFiles = @(
    "Runtime/Model/Generated/Config/AIConfig.cs",
    "Runtime/Model/Generated/Config/StartMachineConfig.cs",
    "Runtime/Model/Generated/Config/StartProcessConfig.cs",
    "Runtime/Model/Generated/Config/StartSceneConfig.cs",
    "Runtime/Model/Generated/Config/StartZoneConfig.cs",
    "Runtime/Model/Generated/Config/Tables.cs",
    "Runtime/Model/Generated/Config/TbAIConfig.cs",
    "Runtime/Model/Generated/Config/TbStartMachine.cs",
    "Runtime/Model/Generated/Config/TbStartProcess.cs",
    "Runtime/Model/Generated/Config/TbStartScene.cs",
    "Runtime/Model/Generated/Config/TbStartZone.cs",
    "Runtime/Model/Generated/Config/TbUnitConfig.cs",
    "Runtime/Model/Generated/Config/UnitConfig.cs",
    "Runtime/Model/Server/Config/ServerConfig.cs",
    "Runtime/Model/Server/Config/StartConfigRuntime.cs",
    "Runtime/ThirdParty/Luban/BeanBase.cs",
    "Runtime/ThirdParty/Luban/ByteBuf.cs",
    "Runtime/ThirdParty/Luban/StringUtil.cs"
)

foreach ($relativePath in $requiredServerConfigFiles) {
    Require-Path $relativePath
}

$requiredServerMessageFiles = @(
    "ClientMessage_C_1000.cs",
    "InnerMessage_S_20001.cs",
    "LockStepInner_S_21001.cs",
    "LockStepOuter_C_11001.cs",
    "OnlineInner_S_22001.cs",
    "OnlineOuter_C_12001.cs",
    "OuterMessage_C_10001.cs"
)

foreach ($fileName in $requiredServerMessageFiles) {
    Require-Path "Runtime/Model/Generated/Message/$fileName"
}

$requiredClientMetaPaths = @(
    "Client/Assets/Game/Scripts/Network.meta",
    "Client/Assets/Game/Scripts/Utility/Network.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Adapter.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Diagnostics.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Rpc.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Runtime.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Serialization.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Transport.meta",
    "Client/Assets/Game/Scripts/ThirdParty/ET.meta",
    "Client/Assets/Game/Scripts/ThirdParty/ET/EtMessageRuntime.cs.meta",
    "Client/Assets/Game/Scripts/ThirdParty/Kcp.meta",
    "Client/Assets/Game/Scripts/ThirdParty/Kcp/Kcp.cs.meta",
    "Client/Assets/Game/Scripts/ThirdParty/Kcp/KcpPatial.cs.meta",
    "Client/Assets/Game/Scripts/ThirdParty/Kcp/ikcpc.cs.meta",
    "Client/Assets/Game/Scripts/ThirdParty/Kcp/ikcph.cs.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Serialization/MemoryPackMessageCodec.cs.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Runtime/NetworkClock.cs.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Rpc/RpcMessageDispatcher.cs.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Transport/TcpClientTransport.cs.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Adapter/INetworkRuntime.cs.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkEndpoint.cs.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkConfigProvider.cs.meta",
    "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkRuntime.cs.meta",
    "Client/Assets/StreamingAssets.meta",
    "Client/Assets/StreamingAssets/networkConfig.json.meta",
    "Client/Assets/Game/Scripts/Network/Generated.meta",
    "Client/Assets/Game/Scripts/Network/Generated/ClientMessage_C_1000.cs.meta",
    "Client/Assets/Game/Scripts/Network/Generated/LockStepOuter_C_11001.cs.meta",
    "Client/Assets/Game/Scripts/Network/Generated/OnlineOuter_C_12001.cs.meta",
    "Client/Assets/Game/Scripts/Network/Generated/OuterMessage_C_10001.cs.meta"
)

foreach ($relativePath in $requiredClientMetaPaths) {
    Require-WorkspacePath $relativePath
}

Forbid-Path "Unity"
Forbid-Path "Share/Tool"
Forbid-WorkspacePath "Config/Proto/test.proto"
Forbid-WorkspacePath "Config/Proto/ET"
Forbid-WorkspacePath "Client/Assets/Game/Scripts/Network/Protocol"
Forbid-WorkspacePath "Client/Assets/Game/Scripts/Network/Adapter"
Forbid-WorkspacePath "Client/Assets/Game/Scripts/Network/Diagnostics"
Forbid-WorkspacePath "Client/Assets/Game/Scripts/Network/Runtime"

Require-Text "README.md" "ET8\.1" "README must declare ET8.1"
Require-Text "DotNet/ThirdParty/DotNet.ThirdParty.csproj" "<TargetFramework>net8\.0</TargetFramework>" "ThirdParty project must target net8.0"
Require-Text "DotNet/ThirdParty/DotNet.ThirdParty.csproj" 'PackageReference Include="MemoryPack" Version="1\.10\.0"' "MemoryPack must remain 1.10.0"
Require-Text "DotNet/ThirdParty/DotNet.ThirdParty.csproj" "Runtime\\ThirdParty\\Unity\.Mathematics" "ThirdParty must compile Unity.Mathematics from Runtime"
Require-Text "DotNet/Core/DotNet.Core.csproj" "Runtime\\Core" "Core project must compile Runtime/Core"
Require-Text "DotNet/Model/DotNet.Model.csproj" "Runtime\\Model\\Generated" "Model project must compile generated Runtime model"
Require-Text "DotNet/Hotfix/DotNet.Hotfix.csproj" "Runtime\\Hotfix" "Hotfix project must compile Runtime hotfix"
Require-Text "Share/Analyzer/Share.Analyzer.csproj" "Runtime[\\/]Core[\\/]Helper[\\/]StringHashHelper\.cs" "Analyzer must reference Runtime StringHashHelper"
Require-Text "Share/Share.SourceGenerator/Share.SourceGenerator.csproj" "Runtime[\\/]Core[\\/]Helper[\\/]StringHashHelper\.cs" "SourceGenerator must reference Runtime StringHashHelper"
Require-WorkspaceText "Tools/ProtocolGenerator/ProtocolGenerator.csproj" "<TargetFramework>net8\.0</TargetFramework>" "ProtocolGenerator project must target net8.0"
Require-WorkspaceText "Tools/ProtocolGenerator/ProtocolGenerator.csproj" "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>" "ProtocolGenerator must treat warnings as errors"
Require-WorkspaceText "Tools/ProtocolGenerator/ProtocolGenerator.cs" "MemoryPack" "ProtocolGenerator must keep MemoryPack generation text"
Require-WorkspaceText "Tools/ProtocolGenerator/ProtocolGenerationOptions.cs" "ServerOutputDirectory" "Protocol generator must expose server output"
Require-WorkspaceText "Tools/ProtocolGenerator/ProtocolGenerationOptions.cs" "ClientOutputDirectory" "Protocol generator must expose client output"
Require-WorkspaceText "Tools/gen-protocol.ps1" 'Config/Proto"' "Protocol wrapper must use root protocol source"
Forbid-WorkspaceText "Tools/gen-protocol.ps1" "Config/Proto/ET" "Protocol wrapper must not use old ET protocol source"
Require-WorkspaceText "Tools/gen-protocol.ps1" "Client/Assets/Game/Scripts/Network/Generated" "Protocol wrapper must output client messages to Client"
Require-WorkspaceText "Tools/gen-protocol.ps1" "Server/GameServer/Runtime/Model/Generated/Message" "Protocol wrapper must output server messages to Runtime"
Require-WorkspaceText "Config/gen.bat" "Server\\GameServer\\Runtime\\Model\\Generated\\Config" "Luban server C# output must target Runtime"
Require-WorkspaceText "Client/Assets/Game/Game.asmdef" '"MemoryPack"' "Game assembly must reference MemoryPack"
Require-WorkspaceText "Client/Assets/Game/Game.asmdef" '"Unity.Mathematics"' "Game assembly must reference Unity.Mathematics"
Require-WorkspaceText "Client/Packages/manifest.json" "com\.cysharp\.memorypack" "Client manifest must include MemoryPack Unity package"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/INetworkRuntime.cs" "bool IsInitialized" "Network runtime contract must expose IsInitialized"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/INetworkRuntime.cs" "void Initialize\(\)" "Network runtime contract must expose Initialize"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/INetworkRuntime.cs" "void Update\(\)" "Network runtime contract must expose Update"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/INetworkRuntime.cs" "ConnectAsync\(NetworkEndpoint" "Network runtime contract must expose TCP ConnectAsync"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/INetworkRuntime.cs" "CallAsync<TResponse>" "Network runtime contract must expose generic RPC calls"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/INetworkRuntime.cs" "RegisterPushHandler" "Network runtime contract must expose push registration"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/INetworkRuntime.cs" "RegisterDisconnectedHandler" "Network runtime contract must expose disconnect registration"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/INetworkRuntime.cs" "void Dispose\(\)" "Network runtime contract must expose Dispose"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkEndpoint.cs" "TryParse" "TCP endpoint must expose deterministic parsing"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkConfigProvider.cs" "networkConfig\.json" "Network config provider must read StreamingAssets network config"
Require-WorkspaceText "Client/Assets/StreamingAssets/networkConfig.json" '"gameGateEndpoint": "127\.0\.0\.1:31011"' "Default network config must target OnlineLocal GameGate"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "class KcpClientTransport" "Client runtime must include KCP transport"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "SocketType\.Dgram" "Client KCP transport must use UDP sockets"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "ProtocolType\.Udp" "Client KCP transport must use UDP protocol"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "new Kcp" "Client KCP transport must own a real KCP dependency"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "remoteEndPoint" "Client KCP transport must not auto-connect without a remote endpoint"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "KcpClientConnectionState" "Client KCP transport must track ET channel connection states"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "SYN = 1" "Client KCP transport must send ET SYN packets"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "ACK = 2" "Client KCP transport must parse ET ACK packets"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "FIN = 3" "Client KCP transport must handle ET FIN packets"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "MSG = 4" "Client KCP transport must wrap KCP output in ET MSG packets"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "ConnectRetryMilliseconds = 300" "Client KCP transport must retain ET connect retry behavior"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "ConnectTimeoutMilliseconds = 20 \* 1000" "Client KCP transport must retain ET connect timeout behavior"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "WriteUInt32\(packet, 1, localConn\)" "Client SYN/MSG must write local connection id after packet type"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "ReadUInt32\(datagram, 5\)" "Client MSG/ACK must validate the ET KCP offset connection id"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "Pump\(long nowMilliseconds\)" "KCP transport must be frame-pumped"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/KcpClientTransport.cs" "IsConnected" "KCP transport must expose connection state without auto-connect"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Serialization/MemoryPackMessageCodec.cs" "HeaderSize = 2" "Client outer packets must be opcode plus full MemoryPack body"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Serialization/MemoryPackMessageCodec.cs" "MemoryPackSerializer\.Serialize" "Client runtime must encode messages with MemoryPack"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Serialization/MemoryPackMessageCodec.cs" "MemoryPackSerializer\.Deserialize" "Client runtime must decode messages with MemoryPack"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Rpc/RpcMessageDispatcher.cs" "TaskCompletionSource<IResponse>" "RPC dispatcher must keep response waiters"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Rpc/RpcMessageDispatcher.cs" "CancellationToken" "RPC dispatcher must support cancellation"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Rpc/RpcMessageDispatcher.cs" "cancellationToken\.IsCancellationRequested" "RPC dispatcher must handle pre-canceled tokens without pending waits"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Rpc/RpcMessageDispatcher.cs" "lock \(gate\)" "RPC dispatcher must serialize pending dictionary access"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Rpc/RpcMessageDispatcher.cs" "BeforeCancellationRegistrationForTest" "RPC dispatcher must expose deterministic cancellation-race coverage"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Rpc/RpcMessageDispatcher.cs" "ActiveCancellationRegistrationCount" "RPC dispatcher must release cancellation registrations"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Rpc/RpcMessageDispatcher.cs" "TimeoutException" "RPC dispatcher must support timeout"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Runtime/NetworkClock.cs" "ElapsedMilliseconds" "Client runtime must maintain frame time"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkRuntime.cs" "new NetworkClock" "NetworkRuntime production constructor must create a real clock"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkRuntime.cs" "new TcpClientTransport" "NetworkRuntime production constructor must create a TCP transport"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkRuntime.cs" "new MemoryPackMessageCodec" "NetworkRuntime production constructor must create a MemoryPack codec"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkRuntime.cs" "new RpcMessageDispatcher" "NetworkRuntime production constructor must create an RPC dispatcher"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkRuntime.cs" "pendingConnect" "NetworkRuntime ConnectAsync must complete from main-thread transport events"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkRuntime.cs" "transport\.Send\(connectionGeneration" "NetworkRuntime must send through the current TCP generation"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkRuntime.cs" "TickTimeouts" "NetworkRuntime Update must drive RPC timeouts"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkRuntime.cs" "TryDequeue" "NetworkRuntime Update must pump inbound TCP transport events"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/TcpClientTransport.cs" "TcpClient" "Client TCP transport must use TcpClient"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/TcpClientTransport.cs" "TcpFrameCodec" "Client TCP transport must frame outbound messages"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/TcpClientTransport.cs" "TcpFrameAssembler" "Client TCP transport must assemble inbound frames"
Require-WorkspaceText "Client/Assets/Game/Scripts/Utility/Network/Transport/TcpClientTransport.cs" "ReleaseIfCurrent" "Client TCP transport must ignore stale generation completions"
Require-WorkspaceText "Client/Assets/Game/Scripts/Game.cs" "new NetworkRuntime" "Game must create the TCP network runtime in production"
Require-WorkspaceText "Client/Assets/Game/Scripts/Game.cs" "RegisterUtility<INetworkRuntime>" "Game must register the TCP network runtime utility"
Require-WorkspaceText "Client/Assets/Game/Scripts/Game.cs" "OnlineAccountLifecycleController" "Game must register exactly one network lifecycle controller"
Require-WorkspaceText "Client/Assets/Game/Scripts/Game.cs" "RegisterModel\(new OnlineAccountModel" "Game must register online account model"
Require-WorkspaceText "Client/Assets/Game/Scripts/Game.cs" "onlineAccountSystem = new OnlineAccountSystem\(\)" "Game must create online account system"
Require-WorkspaceText "Client/Assets/Game/Scripts/Game.cs" "RegisterSystem\(onlineAccountSystem\)" "Game must register online account system"
Forbid-WorkspaceText "Client/Assets/Game/Scripts/Game.cs" "LifeCycle\.RegisterUpdate\(candidateNetworkUpdate\)" "Game must not use legacy lifecycle network update callback"

$protocolProject = Join-Path $WorkspaceRoot "Tools/ProtocolGenerator/ProtocolGenerator.csproj"
if (Test-Path -LiteralPath $protocolProject) {
    $protocolProjectText = Get-Content -LiteralPath $protocolProject -Raw
    if ($protocolProjectText -match "<PackageReference" -or $protocolProjectText -match "<ProjectReference") {
        Add-Failure "ProtocolGenerator must not contain PackageReference or ProjectReference"
    }
}

$serverRuntimeMetaFiles = Get-ChildItem -LiteralPath (Join-Path $Root "Runtime") -Recurse -File -Filter *.meta -ErrorAction SilentlyContinue
foreach ($file in $serverRuntimeMetaFiles) {
    Add-Failure "Server Runtime must not contain Unity meta: $($file.FullName.Substring($Root.Length + 1))"
}

$scanExtensions = @(".sln", ".csproj", ".props", ".targets", ".ps1", ".bat", ".cmd")
$serverScanFiles = Get-ChildItem -LiteralPath $Root -Force -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
        $relative = $_.FullName.Substring($Root.Length + 1).Replace("\", "/")
        $scanExtensions -contains $_.Extension.ToLowerInvariant() -and
        $_.FullName -ne $PSCommandPath -and
        $relative -notmatch '(^|/)(bin|obj|Bin|Logs|Store|Library|Temp)(/|$)'
    }

foreach ($file in $serverScanFiles) {
    $relative = $file.FullName.Substring($Root.Length + 1)
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match 'Unity[\\/](Assets[\\/]Scripts|Library[\\/]PackageCache)|Server[\\/]GameServer[\\/]Unity|Client[\\/]') {
        Add-Failure "Forbidden cross-boundary or old Unity path found in ${relative}"
    }
    if ($content -match 'Model[\\/]Generate[\\/]ClientServer') {
        Add-Failure "Old ClientServer generated output reference found in ${relative}"
    }
}

$workspaceScanFiles = @(
    "Tools/gen-protocol.ps1",
    "Tools/test-protocol-generator.ps1",
    "Config/gen.bat",
    "Client/Assets/Game/Game.asmdef",
    "Client/Assets/Game/Scripts/Game.cs",
    "Client/Assets/Tests/EditMode/Flow/RuntimePhysicalSplitTests.cs"
)

foreach ($relativePath in $workspaceScanFiles) {
    $path = Join-Path $WorkspaceRoot $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        continue
    }
    $content = Get-Content -LiteralPath $path -Raw
    if ($content -match 'Server[\\/]GameServer[\\/]Unity|Unity[\\/]Assets[\\/]Scripts|Unity[\\/]Library[\\/]PackageCache|Model[\\/]Generate[\\/]ClientServer') {
        Add-Failure "Forbidden old Unity path found in ${relativePath}"
    }
}

$pathFileExtensions = @(".sln", ".csproj", ".props", ".targets", ".ps1", ".bat", ".cmd")
$pathFiles = Get-ChildItem -LiteralPath $Root -Force -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
        $relative = $_.FullName.Substring($Root.Length + 1).Replace("\", "/")
        $pathFileExtensions -contains $_.Extension.ToLowerInvariant() -and
        $_.FullName -ne $PSCommandPath -and
        $relative -notmatch '(^|/)(bin|obj|Bin|Logs|Store|Library|Temp)(/|$)'
    }
foreach ($file in $pathFiles) {
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $lineNumber++
        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith("#") -or $trimmed.StartsWith("REM ") -or $trimmed.StartsWith("::")) {
            continue
        }
        if ($line -match '([A-Za-z]:\\|\\\\)') {
            Add-Failure "Absolute path found in $($file.FullName.Substring($Root.Length + 1)): line $lineNumber"
        }
    }
}

$clientMessageDir = Join-Path $WorkspaceRoot "Client/Assets/Game/Scripts/Network/Generated"
$serverMessageDir = Join-Path $Root "Runtime/Model/Generated/Message"
foreach ($dir in @($clientMessageDir, $serverMessageDir)) {
    if (Test-Path -LiteralPath $dir) {
        $csFiles = Get-ChildItem -LiteralPath $dir -Recurse -File -Filter *.cs -ErrorAction SilentlyContinue
        if (-not $csFiles) {
            Add-Failure "Generated message directory has no C# files: $dir"
        }
    }
}

$generatedMessageFiles = @()
if (Test-Path -LiteralPath $clientMessageDir) {
    $generatedMessageFiles += Get-ChildItem -LiteralPath $clientMessageDir -Recurse -File -Filter *.cs -ErrorAction SilentlyContinue
}
if (Test-Path -LiteralPath $serverMessageDir) {
    $generatedMessageFiles += Get-ChildItem -LiteralPath $serverMessageDir -Recurse -File -Filter *.cs -ErrorAction SilentlyContinue
}
if ($generatedMessageFiles) {
    $hasMemoryPackable = $false
    $hasMessageObject = $false
    $hasOpcode = $false
    foreach ($file in $generatedMessageFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        if ($content -match "MemoryPackable") { $hasMemoryPackable = $true }
        if ($content -match "MessageObject") { $hasMessageObject = $true }
        if ($content -match "public\s+const\s+ushort") { $hasOpcode = $true }
    }
    if (-not $hasMemoryPackable) { Add-Failure "Generated messages do not contain MemoryPackable" }
    if (-not $hasMessageObject) { Add-Failure "Generated messages do not contain MessageObject" }
    if (-not $hasOpcode) { Add-Failure "Generated messages do not contain ushort opcode definitions" }
}

if ($Failures.Count -gt 0) {
    Write-Host "ET8 runtime split baseline verification failed:"
    foreach ($failure in $Failures) {
        Write-Host " - $failure"
    }
    exit 1
}

Write-Host "ET8 runtime split baseline verification passed."
exit 0
