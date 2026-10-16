# SlayerMart 定向检查

同源编译正式纯规则，不启动 Unity 或在线服务，不读取玩家存档。

```powershell
dotnet run --project Tools/ArcadeSlayerMartChecks/ArcadeSlayerMartChecks.csproj -- testClass=SlayerMartRules maxTests=24 includePassingTests=true
```

过滤、最大数量与全部通过项输出均为必填。当前24项检查覆盖16卡、个人重抽、购买前置、容量截断确认、等额现金、幂等、全队加命并发、深拷贝、确定性宝石、截止与金额溢出。没有全套或无效过滤回退。

纯规则通过不代表已完成正式商店、战斗能力、持久化或真实双客户端验收。
