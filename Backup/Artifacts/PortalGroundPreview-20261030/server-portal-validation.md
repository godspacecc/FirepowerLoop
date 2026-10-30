# 服务端等待传送门射击回归

2026-10-30。仅调整 `Server/GameServer/Runtime/Hotfix/Server/Online/Arcade/OnlineArcadeRunSystem.cs` 两处：允许清场等待出口时射击，并在等待出口分支推进投射物及短暂武器效果。敌人生成、敌人行动和机关分支仍不执行。

验证使用实际服务端 Hotfix 工程及配置 bytes，构造第一关第四轮、两名加载就绪玩家，调用真实 `TickBattle`。未启动网络服务、未读写玩家存档、未触发 Unity 或 Player Build。

固定执行范围为 `PortalExitServerChecks`，最多 8 项，输出全部通过项；不提供完整套件或无效过滤回退：

```powershell
dotnet run --project Tools/PortalExitChecks/Server/PortalExitServerChecks.csproj -- --testClass=PortalExitServerChecks --maxTests=8 --includePassingTests=true
```

修复前：4/8 通过。等待出口开火、弹丸运动/回收、死亡队友旁的存活玩家开火、宝藏房队友旁的场内玩家开火按预期失败，见 [RED 日志](server-portal-red.log)。

修复后：8/8 通过，见 [GREEN 日志](server-portal-green.log)。覆盖双人开火，弹丸运动和寿命回收，传送/结算/加载/断线门禁，死亡/宝藏房/商店限制，冷却和输入过期限制，以及清场后不恢复刷怪和机关时钟。实际服务端依赖编译成功，`git diff --check` 通过。

验证限于服务端权威行为；客户端与可视化由主任务验证。未提交或推送。新增检查工程 `.csproj` 受到仓库忽略规则影响，归档提交时需显式 `git add -f Tools/PortalExitChecks/Server/PortalExitServerChecks.csproj`。
