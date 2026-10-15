# Android Host 代码与资源更新耗时分析

## 结论

`Tools/打包/Android/Host代码+资源更新` 的主要耗时在 YooAsset 资源构建，不在热更新 DLL 编译。2026-09-28 17:44 的一次更新共用时 247.32 秒，其中编译热更新 DLL 用时 24.52 秒，构建 Android Host 资源包用时 221.28 秒。

资源构建有两个需要分别看待的耗时点：

| 阶段 | 当次耗时 | 通俗解释 |
| --- | ---: | --- |
| `TaskGetBuildMap` | 约 108 秒 | 整理要打包的资源及其依赖。后续一次更新仍用了约 61 秒，是反复出现的开销。 |
| `TaskBuilding` | 约 110 秒 | 实际生成 Bundle。当次其中约 75.76 秒集中在 `unityshaders.bundle` 的一次序列化写入；该 Bundle 哈希不变的后续一次更新，此阶段降到约 9 秒。 |

## 已确认的事实

- 项目有单独的 `unityshaders.bundle`。当次报告列出其中 11 个 Shader 资源，文件约 4.13 MiB。75.76 秒的 `Writing AssetBundleWriteOperation` 对应这个 Bundle；Bundle 归档压缩阶段另用时约 8.76 秒。因此，不能把这 75.76 秒简单解释成最终文件压缩或磁盘写入。
- 与 2026-09-24 的报告相比，该 Shader Bundle 从 8 个资源增至 11 个，新增两个 glTF Shader Graph 和一个 URP Unlit Shader，文件哈希也变化了。现有记录不能把耗时准确分摊到某个 Shader 或具体变体。
- 2026-09-28 18:21 的再次更新仍包含 411 个资源，Shader Bundle 哈希与 17:44 的构建相同；Unity 日志记录 `TaskGetBuildMap` 约 61 秒、`TaskBuilding` 约 9 秒。这说明构建映射仍慢，而 Bundle 写入耗时会随变更和缓存状态显著波动。
- 当前构建报告记录 `UseAssetDependencyDB=false`。YooAsset 在该设置下不读取持久化依赖数据库，但每次仍调用 `AssetDatabase.GetAllAssetPaths()`，为全项目资源建立依赖信息。这一工作范围大于报告中的 411 个打包资源。

## 优化方向与尚待验证的判断

1. **优先测构建映射内部耗时。** 记录全项目依赖扫描、资源收集和依赖整理各占多少，再决定是否试用 YooAsset 的持久化依赖数据库。该选项仍会检查全项目资源哈希，实际收益必须用首次运行和连续热缓存运行对比，不能预设会消除全部 61～108 秒。
2. **保持并观察 Bundle 构建缓存。** 当 Shader Bundle 未变化时，后续构建已明显变快。若未来再次出现几十秒的 Shader 写入，先比对 Shader Bundle 的资源清单、哈希与 SBP 缓存命中记录，再判断是否需要精查 Shader 变体。
3. **不要仅因耗时删除 Shader 或关闭自动收集。** 新增的 glTF Shader Graph 与资源变化同期出现，但现有记录没有证明某个 Shader 单独造成了 75.76 秒；删除依赖还可能影响运行表现。

后续若只想掌握大方向，比较工作流总耗时、`TaskGetBuildMap`、`TaskBuilding` 和 Shader Bundle 哈希即可。需要定位构建映射内部原因时，再增加少量分段计时。任何正式效果验证都需要在相同资源与 Editor 条件下由用户手动运行该菜单；本次分析没有触发打包或修改构建代码。

## 证据位置

- 当次 YooAsset 报告：`Client/Bundles/Android/DefaultPackage/20260928174502/DefaultPackage_20260928174502.report`
- 当次 SBP 时间记录：`Client/Bundles/Android/DefaultPackage/20260928174502/buildlogtep.json`
- 后续对照版本：`Client/Bundles/Android/DefaultPackage/20260928182126/`
- 项目构建入口：`Client/Assets/Editor/Build/AndroidBuildUtility.cs`、`Client/Assets/Editor/YooAsset/YooAssetBuildUtility.cs`
- 当前 YooAsset 依赖扫描实现：`Client/Library/PackageCache/com.tuyoogame.yooasset@2.3.17/Editor/AssetBundleCollector/AssetDependencyDatabase.cs`
