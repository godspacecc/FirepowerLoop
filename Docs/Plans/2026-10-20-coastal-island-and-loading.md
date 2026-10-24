# 首轮东岸场地与加载背景

用户确认右侧海岸、码头、其余三侧连接岛内的布局预览后实施。另将用户提供的战斗概念图去除全部 HUD 和文字，保留角色与战斗内容，接入现有加载页。

## 实施范围

- 单机首场第一轮改为东侧浅海，另外三侧为陆地进场。共享轮廓保留原可行走外缘；陆地延伸网格仅补镜头背景。
- 正式海岛模板新增码头、木箱、外围岩石与棕榈。三条陆路留空；新增装饰没有碰撞，并位于可行走区域外。
- 第一轮的陆地敌人从原边界进入，不再向深海偏移；东侧仍保留涉水进场。其他轮次及 round=0 的联机原图维持原规则。
- 四方向离场的既有去向规则保留。没有新增场景、改变加载事务或关卡数值。
- 首次进局沿用完整加载页，局内普通切轮沿用淡入淡出。Loading.prefab 仅更换背景 Sprite，旧图及 .meta 保留。
- 实际场景沿用项目现有低多边形资源；概念图用于布局参考和加载插画，不代表实际游戏画质。

## 视觉核对

- [正式控制器第一轮布局预览](../../Backup/Artifacts/CoastalIsland-20261020/island-round-1.png)：编辑器隔离渲染，无战斗、无存档写入。
- [正式加载 Prefab 预览](../../Backup/Artifacts/CoastalIsland-20261020/loading-preview.png)：示例进度 55%，不是实际加载耗时或进度测量。
- 原图的分数、倍率、波次、状态、技能栏、角落注释等已去除，加载文字由原有 UI 单独呈现。

## 验证

Unity 连接确认路径为 E:/GitHub/FirepowerLoop/Client/Assets。最终编辑态 Boot 场景干净，未编译或更新，Console 错误条目 0。没有运行 Player Build。

本次连接仅注册 script-execute 等少量 MCP 工具，原 tests-run 工具不可用，因此通过 Unity 原生 TestRunnerApi 做限定 EditMode 测试。先发现真实类并核对数量，再按完整 fixture 名运行，includePassingTests=true，所有通过和失败结果均保存。未运行全套测试，未使用空过滤或失败回退。

| 测试类 | 最大数量 | 实际通过/总数 |
| --- | ---: | ---: |
| ArcadeIslandRegionTests | 3 | 3/3 |
| IslandSeaApproachTests | 14 | 11/11 |
| IslandRoundPresentationTests | 14 | 8/8 |
| ArcadeRoundTransitionTests | 5 | 5/5 |
| ArcadeIslandLayoutTests | 15 | 3/3 |
| UnifiedMainMenuTests | 25 | 22/24 |

共 54 项，52 项通过。海岸轮廓和陆地进场的新断言先在旧实现下失败，再在本次实现下通过；新增陆地背景的创建、切轮释放及装饰显隐检查通过。

加载类的两项失败不由新背景引起：仅在内存中将 Loading 的 Sprite 换回旧图，再按以下两个完整 testMethod 精确复测（最大 2，includePassingTests=true），失败位置相同，随后恢复新图，未保存临时旧图引用。

- Game.Tests.UnifiedMainMenuTests.Loading_LaterIslandTransitionReopensLoadingWithoutAnotherSceneLoad：第 96 行仍要求普通切轮显示完整加载页，与既有淡入淡出逻辑冲突。
- Game.Tests.UnifiedMainMenuTests.Loading_PreparationFailureKeepsDetailsAndRetryOnLoadingScreen：第 122 行，失败后进入重试准备时，既有提前返回未刷新错误区域。此加载控制器问题不在本次换图范围，未修改业务逻辑或绕过断言。

完整结果位于 [验证附件目录](../../Backup/Artifacts/CoastalIsland-20261020)，其中 LoadingOldArtworkBaseline.txt 保存旧图对照复测。独立只读审查未发现本次地图和背景改动的重要问题。

制作工具：ArcadeCoastAuthoring、ArcadeCoastLoadingPreview、ArcadeCoastTestRunner。仅提交本任务文件，保留同工作区其他任务的修改。

## 2026-10-21：码头与靠岸木箱阻挡修复

初版码头与两只靠岸木箱只有装饰模型，实际移动未阻挡。现已在 `Config/DataCsv/arcade_round_layout.csv` 的首轮追加与正式模型占地一致的矩形阻挡，并通过 `Config/gen.bat` 生成双端数据；两端轮布局 bytes 的 SHA256 一致。单人海岛改为读取正式轮级表，移动、绕障、弹丸遮挡与场景 BoxCollider 共用配置。原模型不新增独立碰撞规则，碰撞根的通用栅栏外观在本轮隐藏，释放时恢复。

码头按不可穿越实体处理，玩家从旁边绕行。另修正旧节点强化锚点的靠海侧投影：原最近推出方向若被岸线压回实体，改选同时满足岸线和全部障碍约束的候选落点，保留效果剩余时长。其他三轮及旧顺序局的配置不变。

通过 Unity MCP 确认本次工程路径为 `F:/GitHub/FirepowerLoop/Client/Assets`。先观察到旧实现的 6 个移动／冲刺用例和 1 个资源占地用例失败；追加投影检查中 2/4 失败，正式旧节点锚点恢复用例 1/1 失败，再实施相应修复。最终限定 EditMode 结果如下，均 `includePassingTests=true`，没有运行完整套件或无效过滤回退。

| 测试类 | 最大数量 | 最终通过／总数 |
| --- | ---: | ---: |
| IslandRoundFlowTests | 46 | 43/43 |
| IslandRoundPresentationTests | 12 | 9/9 |
| ArcadeRoundLayoutTests | 5 | 5/5 |
| ArcadeCombatSystemTests | 32 | 28/28 |
| 合计 | | 85/85 |

证据见 [PierCollision-20261021](../../Backup/Artifacts/PierCollision-20261021)。测试覆盖模型与碰撞占地一致、普通移动与长距离冲刺阻挡、靠海侧位置修正、真实节点锚点恢复、切轮释放和出口／武器点连通性。独立审查发现的靠海侧投影问题已修正并复审。以上为定向规则与正式资源隔离检查，不代表真机或真实双客户端实玩验收；未执行 Player Build。
