# 海岛 low poly v2 正式接入实拍

2026-10-22 用户对 v2 四轮预览明确回复“采用这版，正式接入”。中央保持完全开阔，仅沙地层次和低矮装饰；不恢复已暂缓竞速。

`round-1.png` 至 `round-4.png`、`four-rounds-overview.png`、`detail.png` 均由实际 Controller 加载正式资源后隔离渲染，未再应用临时替换。近景包含正式主角和 AK。以上六张文件的 SHA256 均与已采用 v2 对应候选完全一致；这证明本次同机位接入还原，不代表完整自然对局或真人双端验收。

正式资源：`Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeIslandVisualSet.prefab`；既有 `ArcadeObstacleVisualSet.prefab` 的 Island 为嵌套 Prefab，自动继承四轮修改。网格和材质持久化到 `Assets/Game/Art/Arcade/Island/LowPolyV2/IslandLowPolyV2.asset`，旧素材、原 Prefab GUID 与配置均保留。每轮地表细节按轮显隐，纯色沙地/深海/浅水/岸线从当前轮资源读取；未引入新水面动态。

工具：同时编译 `Tools/Unity/IslandLowPolyPreview.cs` 和 `IslandLowPolyAuthoring.cs`，调用 `IslandLowPolyPreview.Bake()` 接入已确认版本；重复执行不再次改变尺寸或叠加装饰。调用 `RunApplied()` 只拍摄实际正式加载结果。工具位于 Assets 外，不进入运行时程序集；只保存本任务资源。

验证：新增 `ApprovedLowPolyPaletteAndDetailsLoadForAllFourRounds` 首次在旧写实沙地上失败，接入后通过。`IslandRoundPresentationTests` 12/12、`IslandPassageRevisionTests` 6/6 通过；覆盖正式四轮持久依赖/深浅海色差/中央低矮细节、权威山脚碰撞贴合、通道出口、中央通行、异步就绪/晚回调/释放和旧共享图隔离。仅 EditMode 定向检查，无完整套件、Player Build 或打包。

Boot 未脏，编辑态、无脚本编译失败、TimeScale=1；测试和编辑器暂存导致的无关旧沙材质、字体和 Quality/Time 文件变化已还原，不纳入任务提交。

独立只读审查未发现 P1/P2/P3：103 个库内引用可解析，22 个山脚 Transform 保留，无新增 Collider；原集合嵌套依赖及资源生命周期保持。全部已确认制作已交付，竞速按用户要求继续暂停，发布准备仍排后续。
