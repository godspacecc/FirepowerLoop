# 海岛已确认批次：定向验证

Unity项目：`E:/GitHub/FirepowerLoop/Client`；Boot场景干净、编辑态。未打包、未运行完整测试套件；所有测试包含通过项，过滤为空或编译错误时修正原因，不回退扩大范围。

| 范围 | 最大数量 | 最近结果 |
| --- | ---: | --- |
| IslandPassageRevisionTests：中央30米干地、三通道连通与海岸朝向 | 6 | 6通过 |
| ArcadeIslandLayoutTests：兼容起点、三出口、船壳网格尺寸 | 3 | 3通过 |
| IslandSeaApproachTests：12实际通道、旧顺序局、拥挤／击退／接触与边界 | 16 | 16通过 |
| IslandRoundFlowTests筛选 Mountain、Passage、OneActiveSegment、EntryBatchDoes、ExitContact | 15 | 15通过 |
| 同类筛选 MountainFoot、OrdinaryGunWall、ResourceWait、RoundWaitsForBlack、CoastBoundaryProjection、RestoredCoast、StartPreparesMap | 12 | 12通过 |
| IslandRoundPresentationTests：真实资源、释放、加载失败重试、通道边界、倒序回调与单变体实例 | 11 | 11通过 |
| ArcadeObstacleVisualTests：资源来源、模板、碰撞与原场地恢复 | 6 | 6通过 |
| ArcadeRoundLayoutRuntimeChecks：真实生成80行转换、连通与非法配置 | 3 | 3通过 |
| ArcadeIslandGeometryChecks：四轮轮廓、边界、海岸及旧模式 | 4 | 4通过 |
| IslandPassageOnline：真实服务器PrepareBattle、12通道生成／移动、活动时钟、加载／复活／区域冻结和出口 | 6 | 6通过 |
| IslandEnemyChaseOnline：四图与旧模式连续追击、接触和位移 | 7 | 7通过 |

控制台检查命令（固定短类和最大数）：

```powershell
dotnet run --project Tools/ArcadeRoundLayoutChecks/ArcadeRoundLayoutChecks.csproj -- --testClass ArcadeRoundLayoutRuntimeChecks --maxTests 3 --includePassingTests true
dotnet run --project Tools/ArcadeIslandChecks/ArcadeIslandChecks.csproj -- testClass=ArcadeIslandGeometryChecks maxTests=4 includePassingTests=true
dotnet run --no-restore --project Tools/ArcadeFullRouteOnlineChecks/ArcadeFullRouteOnlineChecks.csproj -- testClass=IslandPassageOnline maxTests=6 includePassingTests=true
dotnet run --no-restore --project Tools/ArcadeFullRouteOnlineChecks/ArcadeFullRouteOnlineChecks.csproj -- testClass=IslandEnemyChaseOnline maxTests=7 includePassingTests=true
```

服务器检查有NU1900提示：NuGet漏洞源无法连接；实际编译和定向检查有独立退出码，不把此网络提示当成测试通过或失败。无真实服务器重启或两客户端实跑。

截图通过`Tools/Unity/ArcadeIslandPassageCapture.cs`调用真实表现fixture准备四张正式资源，在隔离编辑场景用正式主角同框渲染1600×1000图片。它验证资源加载和静态比例，不能替代实际游戏过程。水面Shader编译消息0条。动态视觉未扩项。

审查两项问题均已先以2个新用例复现，再修复转绿；11项资源回归、6项通用障碍回归已通过。本表共89项定向检查通过。`git diff --check`通过；新增资源.meta齐全，22个样板美术依赖均已被Git追踪。截图按最终修复控制器重新渲染，Boot仍为干净编辑场景。

未执行真实玩家存档读写。历史Console保留，缺失涉水材质测试会主动产生预期错误，不能宣称全局Console无错误。
