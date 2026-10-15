# 拾取反馈定向验证

从仓库根运行。以下检查仅使用内存和本项目配置，不启动 Unity、服务器或 Player Build，不读取玩家数据。

合并、奖励隔离、声音限频及清理（`PickupFeedbackChecks`，最多 7 项，输出通过项）：

```powershell
dotnet run --project Tools/PickupFeedbackChecks/PickupFeedbackChecks.csproj -- --testClass=PickupFeedbackChecks --maxTests=7 --includePassingTests=true
```

真实服务端领取事务与协议快照（`PickupFeedbackServerChecks`，最多 7 项，输出通过项）：

```powershell
dotnet build Tools/PickupFeedbackChecks/Server/PickupFeedbackServerChecks.csproj -m:1 -nodeReuse:false
dotnet run --no-build --project Tools/PickupFeedbackChecks/Server/PickupFeedbackServerChecks.csproj -- --testClass=PickupFeedbackServerChecks --maxTests=7 --includePassingTests=true
```

Unity MCP 恢复后，执行 `ArcadeSupplyPickupTests`（最多 13 项）以及 `Game.Tests.NotifyAndHomeUiTests.NotifyFloat_PickupReplacementUpdatesVisibleAndQueuedValues`、`Game.Tests.NotifyAndHomeUiTests.NotifyFloat_MapCleanupRemovesOnlyPickupFeedbackAndItsQueue` 两个完整 testMethod（最多 2 项），均设置 `includePassingTests=true`，不回退完整套件。

Unity 内仍需确认密集拾取时 HUD 可读、主要战斗区域无遮挡，以及音量、静音和快速重试的实际听感。

2026-10-15：Unity MCP 已补跑 `ArcadeSupplyPickupTests`（13/13）和两个新增提示更新、清理方法（各 1/1）。HUD 重叠修正后，`Game.Tests.NotifyAndHomeUiTests.NotifyFloat_FormalPickupStaysBelowArenaHudAndAboveBattleCenter` 通过正式 Prefab 几何验证两种横屏比例及五条提示，另定向验证布局稳定、累计更新和地图清理，每次最多 1 项，`includePassingTests=true`。按用户要求不再执行试玩、截图或主观听感验收。
