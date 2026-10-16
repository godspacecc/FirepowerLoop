# 海岛轮级保存合同检查

独立 .NET 8 检查直接编译生产存档合同、文件存储和读取结果源码，仅将 Unity 的默认持久化路径替换为抛错 stub。所有磁盘写入限定在本工具输出目录下的新建随机子目录，结束后删除该测试子目录。

运行范围为短类 `IslandRoundSave`，最多 12 项，并显示通过项目：

```powershell
dotnet run --project Tools/ArcadeRoundSaveChecks -- testClass=IslandRoundSave maxTests=12 includePassingTests=true
```

依赖本工程 Library/PackageCache 中现有 Newtonsoft.Json DLL。此工具验证文件兼容、严格字段校验、深拷贝与节点写入；不替代 Unity 中 `IslandRoundSaveTests` 的七项运行态事务测试和实际场景验收。

后续场地入场状态兼容与校验检查为 `ArenaEntrySave`，最多 6 项：

```powershell
dotnet run --project Tools/ArcadeRoundSaveChecks -- testClass=ArenaEntrySave maxTests=6 includePassingTests=true
```

Unity `ArenaEntrySaveTests` 为四项真实整场事务测试，覆盖最终出口保存、写入失败重试、无效金币与继承来源、后续场地继续继承和放弃清理。
