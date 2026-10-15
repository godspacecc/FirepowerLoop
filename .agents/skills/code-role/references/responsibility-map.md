# AnotherDay Responsibility Map

Use this map to find authoritative evidence; it is not a substitute for reading the files. Paths are relative to the project root.

## Framework Composition

- Registration and construction: `Client/Assets/Game/Scripts/Game.cs`
- QFramework interfaces and lifecycle base types: `Client/Assets/Game/Scripts/Framework/QFramework.cs`
- Shared lifecycle integration: search `LifeCycle.Register`, `LifeCycle.Unregister`, `OnInit`, and `OnDeinit` in the nearest mature System or Controller.
- View ownership: `Client/Assets/Game/Scripts/Controller/Base/BaseViewCtrl.cs`, `BaseEntityViewCtrl.cs`, `IViewCtrl.cs`, and `ViewCtrlRegistry.cs`
- UI lifecycle: `Client/Assets/Game/Scripts/Controller/Base/BaseUICtrl.cs`, `BasePageCtrl.cs`, `BaseUI.cs`, and `UIRoot.cs`

## Domain Routes

| Domain | Long-term knowledge | Actual-code starting points |
| --- | --- | --- |
| Player, input, movement, camera | `Docs/玩家操控与相机/README.md` | `Model/Player*`, `Controller/Player`, `Controller/View/Player`, `System/Player`, `System/PlayerAttribute`, `System/PlayerCondition` |
| Map and expedition | `Docs/地图与远征流程/README.md` | `System/Map/MapSceneSystem.cs`, `Model/Map`, `System/Expedition`, `Controller/Player`, `Controller/Enemy`, `Controller/Loot`, `Controller/Extraction` |
| Inventory and equipment | `Docs/物资与基地库存/README.md` | `Model/Inventory`, `Model/Equipment`, `System/Inventory`, `System/Equipment`, `System/Loot`, `Controller/Loot` |
| Combat, enemy, projectile | `Docs/战斗与敌人/README.md` | `Model/Combat`, `Model/Projectile`, `System/Combat`, `Controller/Enemy`, `Controller/Projectile`, corresponding ViewCtrl/View files |
| UI | `Docs/UI框架/README.md` | `Controller/Base`, `Controller/UICtrl`, `Controller/UI`, `System/UI`, `Utility/UI` |
| Save and start flow | `Docs/存档与开始流程/README.md` | `Model/Save`, `System/Save`, `System/Application`, `Controller/UICtrl/Home` |

## Cross-Cutting Authorities

- Unit identity and map ownership: `Model/Unit`, `System/Unit/UnitSystem.cs`
- App state: `Model/Application/AppStateModel.cs`, `System/Application/AppStateSystem.cs`
- Runtime configuration boundary: `Utility/Config/IRuntimeConfigProvider.cs`, `LubanRuntimeConfigProvider.cs`, `RuntimeConfigDefinitions.cs`
- Generated configuration: `Client/Assets/Game/Scripts/Model/Config`; treat as output, not source.
- Runtime configuration bytes: `Client/Assets/Game/Config/*.bytes`; treat as output.
- Configuration source/generator: discover the exact synchronized path before naming it. Formal-plan paths describe execution intent and do not prove that the source is present on this device.
- Resource ownership: `Utility/Res` plus the nearest Controller that stores `ResourceLoadHandle`.
- Save section ownership: search `ISaveSectionProvider`; writes and rollback flow through `SaveSystem` and the owning domain System.

## Navigation Rules

1. Start from the public request/event and trace to the authoritative write.
2. Read every consumer before changing an event or public snapshot.
3. For async creation, inspect request start, callback, cancellation, candidate list, commit, rollback, unload, and late-callback release together.
4. For a new class, first prove no existing owner can hold the responsibility without violating its boundary.
5. If synchronized content lacks a referenced source or generation entry, mark it `待确认`; do not infer that it is absent from the full project.
