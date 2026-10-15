# FirepowerLoop 搜打撤内容统一依赖分析与清理边界

日期：2026-10-15。状态：用户已确认功能范围并授权实施；源码与配置已清理，验证结果见实施记录。

## 1. 目标与约束

清除街机项目中仍可执行的基地、远征、撤离、搜刮背包、仓库、制造、文明发展、局外奖励钱包及联机开放世界业务。保留现有单人街机、双人街机、本局金币、装备、强化、商店、挑战、宝藏、分数与本局结果处理，不借此次清理改变现有街机规则。

遵循根 AGENTS.md：保留资源及 `.meta`，不读取或迁移 AnotherDay 玩家数据；只在当前工程维护，不默认建 worktree、不提交、不推送、不触发 Unity 打包或 Player Build。配置只能改 Config 权威源后通过 `Config/gen.bat` 双端生成；协议只能由源文件经 `Tools/gen-protocol.ps1` 生成。

当前三个 DLL `.meta` 已有修改，属于分析开始前的工作区状态，不纳入清理：

- `Client/Assets/Game/Dlls/HybridCLRAssemblyList.bytes.meta`
- `Client/Assets/Game/Dlls/System.Runtime.CompilerServices.Unsafe.dll.bytes.meta`
- `Client/Assets/Game/Dlls/UnityEngine.CoreModule.dll.bytes.meta`

## 2. 盘点范围与证据局限

配套文件：`2026-10-15-extraction-cleanup-dependencies.json`。该文件提供全部客户端脚本路径、客户端类型词引用来源、序列化资源中的脚本 GUID 引用，以及配置、服务端联机、测试、Editor、工具和协议路径清单。

| 范围 | 当前盘点数量 | 方法 |
| --- | ---: | --- |
| Game 客户端 C# | 688 | 文件枚举、类型声明和类型词反向引用 |
| Assets 中 prefab/unity/asset | 440 | `m_Script` GUID 对应脚本 `.meta` |
| Luban 表 | 53 | `Config/Defines/tables.xml` |
| 服务端 Online C# | 88 | Runtime/Hotfix 与 Runtime/Model 的 Server/Online 路径枚举，重点入口人工阅读 |
| Unity 测试源码 | 319 | 文件枚举，重点夹具人工阅读；不是测试用例数量 |
| Editor C# | 61 | 文件枚举，资源收集与校验入口人工阅读 |
| Tools/Unity C# | 65 | 路径盘点，旧业务 authoring/preview 工具需同步清理 |
| 协议源 | 7 | Config/Proto 路径盘点，Online 内外协议与生成器人工阅读 |

客户端分类：102 个旧业务候选、33 个混合边界、34 个旧 UI 序列化 View 契约、16 个共用 Controller/Base 框架文件、124 个生成文件、379 个其他保留或待核对文件。分类用于定位工作，不是自动删除白名单；同一文件可能承担多种职责。

类型词扫描包含注释、字符串及同名短类型带来的保守误报，不具备 C# 编译器的重载、命名空间和控制流语义。JSON 中 incomingClientSources 仅覆盖 Game 客户端脚本，不覆盖服务端、Editor、测试和工具；这些外部调用方必须随实施变更继续核对。GUID 扫描是文本证据，不代替 Unity 反序列化验证，也不能证明所有运行时字符串地址均已覆盖。

会话没有可调用的 Unity MCP；只发现 `.mcp.example.json` 示例，没有查询到运行中的 Unity 进程。因此未取得 Unity 项目连接、当前场景或 Console 的运行时证据。未启动 Unity、未执行测试、未生成配置/协议、未构建。

## 3. 已确认的依赖链

### 3.1 启动与架构注册仍包含旧业务

`Boot → Main/GameStart → Game.Init → Models/Utilities/Controllers/Systems`。

- `Game.cs` 仍注册 SaveModel、ExpeditionModel、ExtractionModel、WarehouseModel、CurrencyModel、基地准备、文明、搜刮、临时建筑和 OnlineWorld 等模型/系统，并初始化 CivilisationSceneCtrl。
- `GameStart.cs` 当前启动界面已经走街机，但退出逻辑仍读取 SaveModel、捕获基地姿态并 Flush SaveSystem；文件后半部还保留待结算远征与返回基地的静态流程。
- `StartUICtrl.cs` 的新建/继续已调用 ArcadeResumeUICtrl，联机入口已走街机大厅；同文件仍保留 EnterBaseFromStartupAsync、EnterBaseRuntimeAsync 和 ResumePendingExpeditionAsync。
- `PauseUICtrl.cs` 仍包含返回营地、远征资格判断与结算流程。需保留街机暂停/返回菜单能力，去掉旧分支。

不能只删注册：旧代码仍会编译，退出入口、回调与调试命令也可能继续调用它们。

### 3.2 地图与表现是共用边界

`ArcadeSceneFlowSystem → MapSceneSystem.LoadArcadeMapAsync → ArcadePlayerOnly → ProjectileCtrl / PlayerCtrl / EnemyCtrl / LootCtrl`。

- `Config/DataCsv/map.csv` 中 `map.another-day.arcade` 仍指向 `TaskSampleMap`。不能因为场景名字含 Task 就删除或搬走。
- `MapSceneSystem.cs` 同时承担基地、远征、程序化河谷、OnlinePlayersOnly 和 ArcadePlayerOnly；MapPreparationContext 与 MapSceneModel 也承载混合契约。
- 街机当前明确准备投射物、玩家、敌人和掉落四个控制器。要保留它们的异步准备、提交、取消、资源所有权和释放流程，仅裁掉旧玩法捕获/恢复与联机开放世界分支。
- 玩家输入、移动、相机、武器、交互及移动端控制需要逐分支核对；文件名不带 Expedition 也可能引用旧业务。
- ArcadeSceneFlowSystem.ReturnToBaseAsync 当前实际是卸载地图并回主菜单，属于遗留命名，不是加载基地。应与调用方一起改成返回主菜单语义，不能直接删除。

### 3.3 存档文件混合了独立街机所需的公共类型

- `ArcadeRunSystem` 使用 ArcadeRunFileStore，不依赖基地建档；`OnlineArcadeResultFileStore` 独立保存按玩家区分的分数/结果回执。
- 但 ISaveClock、UnitySaveClock 定义在 `System/Save/SaveSystem.cs`，ArcadeRunSystem 直接使用。删除 SaveSystem 前需要把通用时钟类型独立保留。
- SaveFileStatus、SaveReadResult<T> 定义在 `Model/Save/SaveGameData.cs`，与 BaseSaveData、ExpeditionSaveData 共文件。删除基地/远征 DTO 前需要保留街机读档结果契约。
- SaveSystem、SaveModel、SaveFileStore、IncrementalSaveStore/Codec 及远征/基地存档分区存在一整条旧事务链；需先切断所有当前入口再移除。
- `Model/Arcade/ArcadeRewardWalletModel.cs` 虽在 Arcade 目录内，实际是 BaseOnly 的局外奖励钱包，且当前 Game.cs 未注册，应列为旧业务删除候选。
- 宝藏中的金条、宝石、炸弹、冲刺和本局商店装备属于现有街机内容，不按 Reward、Equipment、Inventory 等词直接删除。

保留本项目当前街机安全节点、防覆盖和结果确认语义。本次不额外改变街机恢复策略，也不触碰磁盘上的任何历史玩家文件。

### 3.4 配置不仅有数据，还存在全表加载与强校验

- 53 张源表中 c=25、e=22、s=6；当前生成目标是客户端 47、服务端 28 张。
- LubanRuntimeConfigProvider 的 EnsureValid/校验链同时依赖搜打撤的任务、撤离、制造、基地准备、临时建筑、文明及物品等表。删除表之前必须裁掉旧接口、适配和校验。
- 服务端 ServerConfig 也读取和校验 OnlineWorldRule、旧地图出生点、武器、物品、掉落及临时建筑；其中部分表现配置仍为街机所用，需要按消费关系收窄。
- `Config/gen.bat` 硬编码了 47/28 的数量检查及旧业务必需文件名；需随着最终 schema 调整。
- 生成入口会删除过时 C# 及其 `.meta`，却未对客户端过时 bytes 做对称清理。不能直接跑“删表后生成”并假定产物已收敛：应先调整发布阶段，使生成集合与目标集合一致，同时遵守现有 `.meta` 保留约束，避免残留或无关资源误删。
- `Config/README.md` 中服务端生成路径描述与当前 gen.bat 的 Runtime/Model/Generated/Config 不一致，应以实际入口为准同步文档。

建议分类：

| 配置 | 清理边界 |
| --- | --- |
| civilisation_*、base_camp_layout、base_preparation_rule、map_preparation_rule、item_preparation、expedition_task、extraction、craft_recipe、dialogue_*、npc、shop/shop_item | 旧业务表候选；同步清除 schema/bean/enum、接口、适配与校验后再生成 |
| online_world_rule、temp_building、temp_building_manual_attack | 与服务端开放世界链一起移除；先断开账号和配置初始化的引用 |
| map/map_scene_part/map_spawn/map_display | 按街机实际场景、加载和展示消费保留行；不整表盲删 |
| item/weapon/weapon_appearance/player/enemy/loot/projectile | 按街机表现和配置交叉引用保留最小集合 |
| arcade_* | 保留本局规则；arcade_reward 中 scrap/core/token 是局外奖励候选，需要和消费逻辑一致去除，不能伤及 treasure 奖励 |
| server_start_*、server_ai/server_unit | 保留账号、网关、房间、聊天和街机实际基础设施；移除不再启动的 OnlineWorld 项与对应强校验 |
| test_cmd_* | 保留街机和共用诊断命令，移除旧业务命令行及处理器 |

最终表数量由保留 schema 计算，不预先猜定。

### 3.5 双人街机与 OnlineWorld 不是同一个业务

必须保留 `OnlineAccount / OnlineRoom / OnlineRoomChat / OnlineArcadeRun / OnlineArcadePresentation` 和服务端对应的 Gate、Account、Player、Room、Chat、Arcade 流程。

可移除的目标是开放世界 World、尸体搜刮、联机背包、临时建筑与相关推送。但以下共用入口需先裁剪：

- `OnlinePlayerActorSystem.cs` 同时处理会话、下线、房间、街机和世界进入/离开；登录与断线仍调用世界连接状态同步。
- `G2P_GetOrCreatePlayerHandler.cs` 拷贝 OnlineInventory，`OnlineAccountComponentSystem.cs` 在建号、加载、保存和校验时使用背包及 OnlineWorldRule。
- `ServerConfig.cs` 校验 OnlineWorld 配置和启动场景唯一性，`server_start_scene.csv` 仍启动 OnlineWorld。
- Gate 与 Player 的 World handlers、Inventory/Projectile 推送，以及 World 专属测试必须成组移除。

账号、房间、街机的会话 epoch、重连、掉线宽限、房间解锁与结果确认不能随着世界业务一起删除。不得把 Online 目录整体视为搜打撤。

### 3.6 协议删除会影响后续消息编号

OnlineOuter_C_12001.proto、OnlineInner_S_22001.proto 同时包含账号、房间、聊天、世界和街机协议。`Tools/ProtocolGenerator/ProtocolGenerator.cs` 逐 message 递增分配 opcode；删除中间世界消息会改变后续保留消息的编号。

集中清理必须核对生成前后保留协议的编号与字段，双端同步生成并验证，不手改生成文件、不假定旧 DLL/旧服务端仍兼容。若需要继续兼容已运行的旧端，需先在协议源/生成器层明确稳定编号策略；当前没有运行端兼容需求的证据，不能擅自声称兼容。

### 3.7 资源、UI、收集器、Editor 与工具

- AssetBundleCollectorSetting 已把 TaskSampleMap 和街机资源纳入收集；旧资源收集审计 ResourceModuleAuditUtility 和相关测试仍含基地/文明/expedition 预期，需要同步收窄。
- 34 个旧 UI 生成 View 文件归为序列化契约保留项。其中部分作为其他 UI 的字段类型，即使自身没有直接 m_Script 挂载也不能漏删引用关系。可清除对应业务 UICtrl 和入口，保留资源与生成 View，避免 Missing Script。
- CivilisationPointView 仍绑定业务 ViewCtrl；不能只删 ViewCtrl。需要保留原序列化字段和 `.meta`，去除可执行业务绑定，使未收集的旧资源保持可反序列化，而非保留整套文明业务。
- DayNightView、BaseWeatherView、MapProceduralSkyView、MapSkyboxView 挂在现有资源上，要核对是否被街机场景依赖；不按 Environment 目录整删。
- Controller/Base 是 BaseUI、UIRoot、LocalizedText、SafeAreaFitter 等共用框架，明确保留。
- Tools/Unity 的基地、河谷、文明、衣物/背包专属 authoring 与 preview 代码要与被删类型同步处理；街机及通用 UI/资源工具保留。美术源、图片、模型及 `.meta` 保留。
- Docs 中旧路线图、基地/远征/库存说明和历史计划需从当前文档入口撤下或标为历史；不把历史设计当作恢复旧业务的依据。`.agents/skills` 属于约定技能来源，不随业务代码清理。

## 4. 集中清理设计

推荐采用一次完整工作区变更、内部按依赖顺序实施的方式。单纯移除注册会留下可执行残留；直接按关键词删目录会破坏共用控制器、配置加载及序列化契约。

1. 固定街机保留边界和回归夹具，提取共用存档结果/时钟类型；裁剪 Game、GameStart、Start/Pause 和旧业务调试入口。
2. 收窄地图上下文、控制器、输入/相机/武器及生命周期到现有单人/双人街机；保持异步候选准备、提交回滚和释放责任。
3. 裁剪服务端 Account/Player/ServerConfig 的混合逻辑，再删除 World 专属处理器、模型、推送和旧客户端系统。
4. 以最终调用关系修改 Config schema/CSV 与协议源，调整生成入口的验证/收敛规则，双端生成后核查产物差异。
5. 集中删除引用归零的纯业务源码和过时业务测试/工具；保留资源、`.meta`、生成 View 与必要的无业务序列化契约。同步修正收集器、资源审计、文档索引及有效测试。
6. 做静态残留检查、编译、定向测试和 Unity Console/资源引用验证；任何失败先在明确范围定位，不回退完整套件。

实施期间不为搜打撤建立共享框架、不创建长期兼容业务系统、不添加迁移或旧玩家数据清理机制。最终报告分别列出：物理移除业务、共用裁剪、因资源契约保留的文件，以及未验证事项。

## 5. 验证设计（尚未执行）

Unity MCP 可用后，先查询项目路径、场景和 Console。确认连接 `E:/GitHub/FirepowerLoop/Client` 后才进行编辑器操作。Boot 脏状态可按根约定从磁盘重载。禁止触发 Player Build。

首批只运行以下真实非空过滤；EditMode；每次 `includePassingTests=true`，明确最大数量。短类过滤若命中为空或超出预期就停止排查，不回退全套。

| 过滤 | 最大测试数 | 验证目标 |
| --- | ---: | --- |
| testClass=`FirepowerLoopRunTests` | 9 | 独立开局/恢复、防覆盖、死亡/放弃/确认及按玩家隔离结果 |
| testMethod=`Game.Tests.OnlineArcadeRunTests.ServerCreationBroadcastsRunSnapshotAfterRoomLock` | 1 | 街机建局和房间锁 |
| testMethod=`Game.Tests.OnlineArcadeRunTests.SafeShopApisCarryCurrentRevisionAndExplicitIntent` | 1 | 本局商店与修订号 |
| testMethod=`Game.Tests.OnlineArcadeRunTests.PausedBattleCopiesReconnectAndSoloEndDeadlines` | 1 | 双人掉线与重连契约 |
| testMethod=`Game.Tests.ArcadeUiTests.ArcadePrefabs_HaveCollectorGeneratedViewAndBoundReferences` | 1 | 正式 UI 反序列化绑定 |
| testMethod=`Game.Tests.BootSafetyTests.BootSceneBindsLauncherAndAllHotUpdateFields` | 1 | Boot 引用 |

总上限 14 个测试。变更引入的新回归测试应另行声明真实短类/完整方法及最大数量。涉及其他街机规则时才追加对应定向用例。

服务端使用受影响 DotNet 项目的源码编译，并选取已有 ArcadeTransactionChecks/ArcadeOpeningChecks 等定向程序验证；不启动或引用另一项目服务器和数据。最终清理要覆盖服务器建号/登录、双人入房/开局/重连/结果与资源加载；纯文本测试不替代实际双人试玩。

静态验收包括：被删类型在客户端、服务端、Editor、测试、Tools 中无有效引用；保留资源 GUID 与 `.meta` 不丢失；生成产物与源定义一致；旧业务不注册、不被启动/退出/暂停/调试入口调用；街机宝藏和本局强化未被误清理。

## 6. 当前产物与下一步

分析时仅新增本报告和依赖 JSON；后续已按用户确认范围实施。依赖 JSON 是清理前盘点快照，不代表当前仍有对应文件。最新编译、定向测试和限制见 ../plans/2026-10-15-extraction-cleanup.md。

用户已确认保留的功能并授权集中清理。报告前文保留分析时证据，不能当作当前产品仍有旧业务的描述。
