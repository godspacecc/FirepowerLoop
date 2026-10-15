# ET8.1 Runtime Split Baseline

## Version

- Baseline: ET8.1
- DotNet target framework: `net8.0`
- MemoryPack package version: `1.10.0`

## Delivered Source Baseline

The ET server runtime is physically split from the Unity client. Server runtime source now lives under `Server/GameServer/Runtime/` and is compiled by the existing DotNet projects. Client network source now lives under `Client/Assets/Game/Scripts/Network/` and is compiled into the existing `Game.dll`.

Important retained locations:

- `Server/GameServer/ET.sln`
- `Server/GameServer/DotNet/DotNet.sln`
- `Server/GameServer/DotNet/Directory.Build.props`
- `Server/GameServer/Runtime/Core`
- `Server/GameServer/Runtime/Model`
- `Server/GameServer/Runtime/Hotfix`
- `Server/GameServer/Runtime/ThirdParty`
- `Client/Assets/Game/Scripts/Network/Runtime`
- `Client/Assets/Game/Scripts/Network/Generated`
- `Client/Assets/Game/Scripts/Network/Adapter`
- `Config/Proto`
- `Tools/ProtocolGenerator`
- `Tools/gen-protocol.ps1`

## Excluded Scope

Repository metadata, IDE state, build outputs, logs, runtime data, and caches are not part of the delivered source baseline. Excluded examples include `.git/`, `.idea/`, `.vs/`, `bin/`, `obj/`, root `Bin/`, root `Logs/`, root `Store/`, Unity `Library/`, Unity `Temp/`, Unity `Logs/`, user preference files, and log files.

These transient directories may exist on a developer machine after builds, local runs, or Unity editor activity. The static baseline script validates source structure and required files only; it does not decide synchronization rules or version-control inclusion for local transient content.

## Build Prerequisites

- .NET SDK capable of building `net8.0` projects.
- Unity package resolution for `Client/`, including MemoryPack Unity `1.10.0`.
- Do not commit generated package caches, Unity `Library/`, build outputs, logs, or runtime data.

## Protocol Baseline

- Protocol source: `Config/Proto`
- Protocol generation entry: `Tools/gen-protocol.ps1`
- Protocol generator project: `Tools/ProtocolGenerator/ProtocolGenerator.csproj`
- Generated client messages: `Client/Assets/Game/Scripts/Network/Generated`
- Generated server messages: `Server/GameServer/Runtime/Model/Generated/Message`

The seven formal protocol files live directly under `Config/Proto/`. The old ClientServer generated message output is no longer a runtime artifact. The protocol generator reads the root protocol source as the sole authority and emits only the client and server output sets.

## Runtime Boundary

- `Server/GameServer/Unity/` is not part of the final runtime split baseline.
- `Server/GameServer/Runtime/` must not contain Unity `.meta` files or depend on Unity `PackageCache`.
- `Client/` must not reference `Server/GameServer/` runtime source.
- `Server/GameServer/` must not reference `Client/` runtime source.
- Root `Config/` and `Tools/` remain shared authority inputs and generators, not shared runtime source.
