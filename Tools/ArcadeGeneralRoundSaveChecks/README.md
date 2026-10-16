# 通用轮级存档定向检查

使用正式 ArcadeRunContracts、ArcadeRunModel、ArcadeRunSystem 与 ArcadeRunFileStore 源码。文件测试直接编译 Unity 的 RoundCheckpointTests；独立事务检查仅替代 Unity、QFramework 接入与配置查询端口，不能替代 Unity 场景或真实配置验收。

```powershell
dotnet run --project Tools/ArcadeGeneralRoundSaveChecks/ArcadeGeneralRoundSaveChecks.csproj -- testClass=RoundCheckpointTests maxTests=19 includePassingTests=true
dotnet run --project Tools/ArcadeGeneralRoundSaveChecks/ArcadeGeneralRoundSaveChecks.csproj -- testClass=RoundCheckpointTransaction maxTests=9 includePassingTests=true
```

两种范围严格匹配且必须非空，不提供完整套件回退。所有文件I/O使用独立临时子目录，结束删除。不会读取用户存档，不启动 Unity Player Build。