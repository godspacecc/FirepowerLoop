# 服务端码头载桶修复验证（2026-10-30）

范围：只改服务端地面桶规则及相关定向回归，不改客户端、配置或协议，不启动Unity、不打包、不启动监听。

根因：`TryHitGroundBarrel` 的敌人遮挡过滤没有使用与后续伤害入口一致的 `DockSeaEntry.CanBeAttacked`，先截短子弹，再跳过海中不可攻击目标。`TickVortexBarrels` 同时在5秒自动卸桶，违反一直背到打爆的规则。

修复：删除计时卸桶循环，保留预置/通道桶吸物；遮挡过滤增加可攻击状态。未改变40桶耐久、36普通弹伤害或5.75秒登岸可攻击界限。旧测试的卸桶断言调整为持续背桶，海路测试补齐正式出生方法新增的可选参数。

真实RED：`DockBarrel` 的4项中，海中遮挡和持续背桶失败，登岸两发破桶和普通敌人挡弹通过（server-red.log）。最小修复后4/4通过（server-green.log）。海中用0、2.1、5.74秒三个阶段，每阶段通过真实TickEnemies/TickProjectiles入口；射线夹具将敌人与后方桶移到无遮挡区域，只隔离命中优先级。

限定验证命令（仓库根运行）：

```powershell
dotnet run --project Tools/DockBarrelChecks -- testClass=DockBarrel maxTests=4 includePassingTests=true
dotnet run --project Tools/ArcadeFullRouteOnlineChecks -- testClass=CarrierBarrelOnline maxTests=5 includePassingTests=true
dotnet run --project Tools/ArcadeFullRouteOnlineChecks -- testClass=DockBarrelOnline maxTests=3 includePassingTests=true
dotnet run --project Tools/DockSeaEntryOnlineChecks -- testClass=DockSeaEntryOnline maxTests=9 includePassingTests=true
```

结果：4/4、5/5、3/3、9/9；合计21项。没有执行完整套件或无效过滤回退。初次海路运行因缺失还原文件未执行测试，正常还原后9项通过。`git diff --check`（本次4个已跟踪文件）通过。

新增测试工程csproj被仓库全局忽略规则匹配，提交时需明确 `git add -f Tools/DockBarrelChecks/DockBarrelChecks.csproj`。本子任务未提交或推送。
