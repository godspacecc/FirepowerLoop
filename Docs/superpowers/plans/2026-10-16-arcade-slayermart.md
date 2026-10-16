# SlayerMart 实施计划

> **For agentic workers:** 使用 `executing-plans` 在当前工作区逐项实施；本计划不要求派发子代理。Steps use checkbox (`- [ ]`) syntax for tracking。用户已授权确定内容直接推进，只询问真实玩法决策；不因计划模板重复索取笼统实施许可。

**Goal:** 在实际第 11／31 轮后提供单人／双人一致的特殊区域商店，实现 24 项候选、个人 16 卡库存、等额个人现金、完整能力和轮级事务。

**Architecture:** 双端同源纯规则计算库存、资格、报价、消费候选和能力参数，现有 `ArcadeCombatSystem`／服务端 Actor 持有工作态，现有 Run 系统原子提交下一轮全量节点。保持旧普通道具槽，商店多实体使用稳定实例 ID；UI 只展示快照和发送意图，不增加第二个战斗循环。

**Tech Stack:** Unity、QFramework、C#、ET 服务端、Luban 双端配置、现有协议生成器与 UI ReferenceCollector 生成器。

**Spec:** [正式设计及最新补充](../specs/2026-10-16-arcade-slayermart-design.md)，尤其第 14—16 节。状态：用户已确认全部玩法决定并授权按计划持续实施、分段提交推送。任务 1 的纯交易核心已开始，后续按顺序接续。

## Global Constraints

- 当前工作区 `E:/GitHub/FirepowerLoop`，Unity 根 `Client`；不默认创建 worktree、不还原既有未提交修改。
- 不 Unity 打包、不 Player Build、按用户新授权分段提交并推送，不自动发布；核心手写类、字段和函数补中文注释，保留资源及 `.meta`。
- 只开发街机局内；新商店是本次明确新增范围，不恢复旧逐场交易、基地、远征、局外钱包，不读 AnotherDay 玩家数据。
- 普通掉落原基础数值和规则保留；旧局不开新商店、旧 RunGold 不兑换购物现金。新局具有明确 `SlayerMartRulesVersion=1` 才启用。
- 24 项能力全部就绪才开启正式入口；展示 16 个不同商品、不以旧商品或未实现能力填空。
- Config 是唯一数值源，仅 `Config/gen.bat` 双端生成；协议仅 `Tools/gen-protocol.ps1` 生成。实际服务端生成路径为 `Server/GameServer/Runtime/Model/Generated/Config`。
- 先用 Unity MCP 确认项目、场景和 Console；不可用时可推进纯规则及独立检查，Unity/Prefab/实连项目保持未验，不用旧日志替代。
- 每项测试先创建并核实真实短类／完整方法，再运行；提前声明范围与最大项数、`includePassingTests=true`。没有过滤结果就停止修正，不回退完整套件。
- 下列新增类型、方法、文件和测试均为计划产物；未创建、未编译、未执行前不能声称存在或通过。

## Review Focus

1. 商店工作态泄漏到其他房间的保存：任务 2 验证另一局保存后，本局重启仍恢复原轮前全部状态。
2. 重抽／宝石通过改 OperationId 或崩溃变相刷结果：任务 1、2 验证种子独立于请求 ID，相同节点恢复得到相同结果。
3. 下轮加命、补空库存与保存失败互相放大：任务 2、5 验证失败重试不重复加命／补给，零命成员按原复起流程接续。
4. 组合商品只部分成功或同类普通拾取覆盖长时支援：任务 3、4 验证候选原子性、实例容量、次数和时长独立。
5. 最后一秒购买、重连旧包和双方冲突选择：任务 5、6 验证权威截止、代次、重放、个人隐私及全队商品只扣一笔。

---

## 文件与职责

以下路径相对仓库根；写入前重新检查 git 状态和当前实现，路径变化以实际源码为准。

| 类别 | 修改／新增 | 职责 |
| --- | --- | --- |
| 配置源 | 修改 `Config/Defines/{enums,beans,tables}.xml`、`Config/DataCsv/arcade_rule.csv`、`Config/gen.bat`；新增 `arcade_slayermart_item.csv`、`arcade_slayermart_region.csv`、`arcade_slayermart_ability.csv` | 24 商品、两区域、可调能力、显式发布门禁 |
| 配置适配 | 修改 `Client/Assets/Game/Scripts/Utility/Config/{IRuntimeConfigProvider,RuntimeConfigDefinitions,LubanRuntimeConfigProvider}.cs`、`Server/GameServer/Runtime/Model/Server/Config/ServerConfig.cs` | 只读配置和双端引用校验 |
| 共享纯规则 | 新增 `Client/Assets/Game/Scripts/Utility/Arcade/ArcadeSlayerMart{Contracts,Rules,AbilityRules}.cs`；修改 `Server/GameServer/DotNet/Model/DotNet.Model.csproj` | 稳定身份、纯候选计算、确定性抽取和能力计算，服务端显式编入同源文件 |
| 单人状态／保存 | 修改 `Model/Arcade/{ArcadeRunContracts,ArcadeRunModel,ArcadeCombatModel}.cs`、`System/Arcade/{ArcadeRunSystem,ArcadeCombatSystem.RoundLayouts,ArcadeCombatSystem.IslandRounds}.cs`、`Utility/Save/ArcadeRunFileStore.cs`（均在 `Client/Assets/Game/Scripts`） | 工作态、深拷贝、轮前节点、旧局版本门禁 |
| 单人战斗 | 新增 `Client/Assets/Game/Scripts/System/Arcade/ArcadeCombatSystem.SlayerMart.cs`、`ArcadeCombatSystem.SlayerMartAbilities.cs`；修改现有 `ArcadeCombatSystem.cs` | 区域进入、动作裁决、实例推进、伤害与主动输入接点 |
| 双人领域 | 修改 `Server/GameServer/Runtime/Model/Server/Online/Arcade/OnlineArcadeRunComponent.cs` 及现有 Actor／Manager；新增 `Runtime/Hotfix/Server/Online/Arcade/OnlineArcadeRunSystem.SlayerMart.cs`、`.SlayerMartAbilities.cs` | 全员事务、权威时间、个人库存、就绪／断线规则 |
| 协议 | 修改 `Config/Proto/{OnlineOuter_C_12001,OnlineInner_S_22001}.proto`；修改现有 `OnlineArcadeHandlers.cs`、`Gate/P2G_ArcadeHandlers.cs`、`Player/G2P_ArcadeHandlers.cs` | 新商店动作与能力输入；旧撤销 action 1／2 仍拒绝 |
| 客户端联机 | 修改 `Client/Assets/Game/Scripts/Model/Online/OnlineArcadeRunContracts.cs`、`System/Online/{OnlineArcadeRunSystem,OnlineArcadePresentationSystem}.cs` | 协议转换、当前地图／区域代次过滤、快照投影 |
| UI／表现 | 修改既有 `Prefabs/UI/Arcade/ArcadeShop.prefab`、`Box/ArcadeShopOfferBox.prefab`、`ArcadeHud.prefab`；新增 `Controller/UICtrl/Arcade/ArcadeSlayerMartUICtrl.cs` 与 Controller 的 `.SlayerMart.cs` 表现 partial | 保留 Prefab 资源身份，生成 View、正式列表和独立能力表现 |
| 输入／本地化 | 修改现有 `ArcadeHudActionKind`、`Controller/UICtrl/Arcade/ArcadeHudUICtrl.cs`、正式玩家输入适配、HotKey 默认配置及 `Config/Localization/{zh-CN,en-US}.json` | 能力动作、可重绑 F、手柄组合与移动按钮，所有玩家文案正式本地化 |

配置新增三张 `e` 表后，生成器当前硬校验的客户端 27／服务端 21 应更新为 30／24，并更新 `Config/README.md`；不能只改 schema 让正式生成入口永远失败。数量以实施时新增表前的实际登记复核，避免覆盖其他任务后来新增的表。

## Task 1：配置合同与纯交易规则

**Interfaces（新增，namespace `FirepowerLoop.Arcade`）：**

- `ArcadeSlayerMartCatalog`：24 个只读 `ItemDefinition`、2 个 `RegionDefinition`、`AbilityDefinition` 参数集。
- `ArcadeSlayerMartState`：`RulesVersion`、`RegionInstanceId`、`Generation`、`Revision`、`RemainingSeconds`、原始成员、个人现金／券／库存／抽取序／限购／已领箱／专精／待下轮事件，提供 `Clone()`；不引用 Unity。
- `ArcadeSlayerMartRequest`：动作（SelectEnter/SelectSkip/CollectCash/Buy/Reroll/SetMenuOpen/SetFinished）、操作者、区域身份、代次、预期 revision、OperationId、商品／箱 ID、确认截断值。
- `ArcadeSlayerMartResult`：成功、失败码、候选状态、领域效果列表、已执行操作回执。失败候选为 null，输入不变。
- `ArcadeSlayerMartRules.Evaluate(ArcadeSlayerMartState current, ArcadeSlayerMartRequest request, ArcadeSlayerMartCatalog catalog) : ArcadeSlayerMartResult`。
- `ArcadeSlayerMartRules.Create(long runId, int visit, IReadOnlyList<long> members, ulong seed, ArcadeSlayerMartCatalog catalog) : ArcadeSlayerMartState`。

- [ ] 创建 `Tools/ArcadeSlayerMartChecks` 独立同源检查入口，只接受 `testClass=SlayerMartRules`、`maxTests=24`、`includePassingTests=true`；无匹配或超上限失败。先写 16 卡去重、不能变库存不扣券、满资源不扣款、每人等额现金、同请求重放、同 ID 异参拒绝、全队加命并发只一笔、三鸡容量不足整单拒绝、checked 溢出拒绝的行为用例。
- [ ] 在仅测试／合同代码存在时运行上述真实过滤，记录缺实现造成的预期失败；不是靠缺依赖或配置报错冒充红阶段。
- [ ] 新建三张源表，24 商品 ID 固定 `slayermart.item.01`—`.24`，效果引用独立、价格照设计。区域 `.first/.second` 对应 11／31、300 秒、每箱 2000、9／11 箱，位置必须可达。所有几何和能力试调值进配置；规则代码不嵌入价格和倍率默认值。
- [ ] 实现纯规则，抽样使用保存的稳定 seed+个人抽取序，不以请求 ID 或当前时钟做种子。每次请求先重放查重再检查当前 revision；同 ID 异参永远拒绝。显式前置／限购和效果容量在扣款前全部验证。
- [ ] 更新生成器实际计数门禁后运行唯一 `Config/gen.bat`，验证双端 bytes 散列及配置适配。正式入口保持关闭，纯检查允许直接构造目录验证 24 项。
- [ ] 运行 `dotnet run --project Tools/ArcadeSlayerMartChecks/ArcadeSlayerMartChecks.csproj -- testClass=SlayerMartRules maxTests=24 includePassingTests=true`，逐项通过；本任务完成不等于商店已可玩。

## Task 2：工作态、轮级保存与旧局隔离

**Interfaces：** 消费任务 1 的状态／请求／结果。单人 `ArcadeCombatRuntime` 与双人 `OnlineArcadeBattleRuntime` 新增工作态，`ArcadeRoundCheckpoint` 和联机安全记录保存深拷贝；新局记录 `SlayerMartRulesVersion=1`，旧档缺省 0。

- [ ] 新增真实短类 `SlayerMartCheckpointTests`，最多 16 项，先断言旧档不开商店、实例列表深拷贝、未知版本保留文件、现金与宝石积分同步回滚、下一轮加命恰好一次、保存失败工作态不覆盖原节点。
- [ ] 捕获第 12／32 轮候选时，把商店、已购能力实例、队列、积分／倍率／命数、事件消费标记一起交给 `TrySaveRound`。不添加进店或购买专用文件保存，不原地改已提交节点。
- [ ] 扩展服务端 `CaptureRoundCheckpoint`／`TryCommitRoundTransition`，商店工作态只在成功全员候选中进入 record。深拷贝列表、回执参数、随机序与成员索引；未知状态拒绝恢复，不删主文件。
- [ ] 对同一轮失败重试恢复稳定商店种子、现金箱 ID 和随机序列。第 11 轮前回滚会丢弃该轮及商店全部收益；第 12 节点已提交则恢复全部已购状态、禁止重进第一次商店。
- [ ] 使用临时目录的 `SlayerMartCheckpoint` 独立检查（最多 16 项）验证真实文件失败与备份、另一房间写盘不夹带工作态、结果文件不含现金／券／专精；不碰真实用户档。
- [ ] 仅在 Unity MCP 可用时执行新增的真实 `SlayerMartCheckpointTests`，`includePassingTests=true`、最多 16 项；报告独立检查与 Unity 检查各自结果。

## Task 3：护甲、唯一专精与电击风暴

**Interfaces：** 新增 `ArcadeSlayerMartAbilityState`（背心槽／板、唯一专精集合、充能、活动风暴剩余／周期、复起保护来源）；`ArcadeSlayerMartAbilityRules.ResolveDamage(int baseDamage, DamageSource source, AbilityState state) : int`；`TryBlockHit(HitKind kind, AbilityState state) : BlockResult`。纯规则不直接改运行 HP。

- [ ] 新建 `SlayerMartAbilityRules` 检查范围，最多 24 项；先验证护盾优先于板、无敌不扣板、致命机关不被护甲救下、升级不赠板、专精伤害只乘一次、风暴满 100 杀可用／99 杀不可用、10 秒边界、0.5 秒周期和 Boss 不受控。
- [ ] 按设计 14.2／14.3 在双端实际伤害入口接线；把既有失命机关与普通 HitKind 明确区分，不能因为新增护甲把原通电塔改成扣板。
- [ ] 精英控制使用独立乘数，普通眩晕阻止其移动和攻击；离圈、死亡、切图和效果到期解除。归属击杀从正式 KillEnemy 入口传入，清场删除不充能，风暴自身不自充。
- [ ] 在真正复起转可控时应用复起专精 ×1.5，保存只记剩余与来源；新能力输入消费后才发布快照。默认 F、手柄 LB+RB、移动按钮对应单一 `UseFieldUpgrade` 意图，不借用 Bomb 或 Dash 请求。
- [ ] **死亡规则已确认**：清除支援、队列、护甲板，保留背心、专精和未使用充能；增加实例／护甲／充能的死亡验证。
- [ ] 运行独立能力检查；新增 Unity `SlayerMartAbilityFlowTests` 最多 20 项，验证实际伤害／控制入口，不能只测试纯数学倍率。

## Task 4：多实例支援、队列、几何与表现

**Interfaces：** `ArcadeSlayerMartEffectInstance` 稳定实例 ID、产品 ID、来源、剩余、次数、下一次命中剩余、相位、锚点；`ArcadeSlayerMartEffectQueue` 持有当前份与未启动份。保留现有普通 `Powerups` 字典，商店用独立集合，双端投影同一身份。

- [ ] 新建 `SlayerMartSupportTests` 最多 24 项：三鸡同时各自射击、总上限五只、短拾取不覆盖长实例、爆桶三份依次启动、暂停不消耗队列、转场不补次数、三球接触与双锯共享冷却、组合失败不半发奖；先实际红阶段。
- [ ] 按任务 1 的全部效果候选一次提交；不要在扣款后异步逐件创建领域实体。表现资源失败只重试投影，不重扣款、不重赠效果。
- [ ] 复用现有正式鸡／球／桶／锯资源，建立实例根与每实例 ID；死亡、替换、区域进入和地图卸载释放配对。创建异步回调同时检查地图代次、拥有者和实例 ID。
- [ ] 规则接触与显示共用轨道、半径和相位，精确处理多个敌人／同一敌人共享冷却；不能使用整圈大圆伤害替代双锯／三球接触。活动时钟被冻结时相位和剩余同步冻结。
- [ ] **商品 06 已确认有限次数**：锯300秒，爆桶最多300秒或三次触发先到者结束；全部24项能力就绪前正式入口保持关闭。
- [ ] 运行上述 24 项上限的真实短类；服务端新增 `SlayerMartSupportOnline` 独立过滤最多 18 项，验证实体来源、归属、冷却与队列序列一致。保留原普通道具定向回归，具体真实方法在改动前从测试目录确定，禁止凭空运行旧名称。

## Task 5：主线区域、现金与单人返回

**Interfaces：** `ArcadeCombatSystem.TryOperateSlayerMart(ArcadeSlayerMartRequest request, out string error) : bool`；`GetSlayerMartSnapshot() : ArcadeSlayerMartSnapshot`。快照只读；`ArcadeCombatSystem.SlayerMart.cs` 依现有生命周期推进区域与状态，不新增 MonoBehaviour.Update。

- [ ] 新建 `SlayerMartRegionFlowTests` 最多 20 项：所有路线实际第 11／31 轮清场触发、跳过无现金、区域 9／11 箱、300 秒硬截止、菜单不暂停区域、下一普通轮消费加命、资源慢加载和保存失败不提前推进。
- [ ] 将正式轮次出口确认挂接为选择／准备区域／购物／返回候选；不先调用 `StartContentRound` 再退回商店，不把挑战或主题 Boss 算普通轮。
- [ ] 创建正式墓穴区域表现及现金箱交互，使用配置坐标和稳定领取 ID；同一箱一次原子给所有原始成员等额份额，积分不跟着复制。
- [ ] 资源全部就绪后开始 300 秒。截止设置 closing，拒绝新交易，已接受交易先完成确定结果再返回；单人过程无后台异步写一半状态。
- [ ] 区域失败重试保持现有工作态，下一轮候选失败不重抽／不重领／不重新施加事件。进程恢复按任务 2 的原节点语义执行。
- [ ] 运行真实短类上限 20，输出实际执行数；单人首店完整进出实玩单列，不以注入快照代替。

## Task 6：双人协议、权威裁决与个人快照

**Interfaces：** 新增专用 `C2G_OperateArcadeSlayerMart`／对应响应及内层转发消息；客户端 `OnlineArcadeRunSystem.OperateSlayerMartAsync(ArcadeSlayerMartRequest request)`。具体异步返回包装沿现有网络模式，载荷为任务 1 的领域结果和当前个人快照。UseFieldUpgrade 在现有战斗输入追加新字段，不重用旧撤销 action。

- [ ] 在 `SlayerMartOnline` 独立检查范围写最多 24 项：同箱双方同帧、同商品重复 ID、相同 ID 异参、同 Tick 全队商品、不同个人重抽互不影响、旧 generation 拒绝、同 Tick 截止与新请求按服务器顺序处理。
- [ ] 外／内协议只追加未使用编号，先扫描全部字段和消息编号防冲突，经正式生成器双端生成；检查服务器当前指纹门禁，旧新不匹配明确拒绝，不降级到旧购物路径。
- [ ] 服务器 Actor 串行裁决任务 1 请求；个人库存和余额仅发本人，队友只见购物／完成状态及已生效共享能力。公共战斗效果可见不代表队友可获取原报价。
- [ ] 两人分别确认进入／跳过，相反选择停留可改；双方完成且所有菜单关闭才能提前离开。短断线按已有暂停／宽限策略，不自行发钱、不自动跳过。
- [ ] 将区域资源就绪接入现有地图代次门禁；客户端旧快照不能替换当前区域／重置预测。慢加载冻结全员，服务端 restart 按已提交轮恢复。
- [ ] 运行真实 `SlayerMartOnline` 范围；有版本匹配现成客户端才做真实双客户端实连。缺少可运行双端时标记待验，禁止为测试触发 Player Build。

## Task 7：正式 UI、输入与本地化

**Interfaces：** `ArcadeSlayerMartUICtrl` 消费单人／联机统一 `ArcadeSlayerMartSnapshot` 并发出任务 1 请求；每张卡完整呈现价格、前置、剩余限购、个人／全队作用与拒绝原因。

- [ ] 新建 `SlayerMartUiContractTests` 最多 12 项，验证反序列化后的正式宿主／Box、CustomListView 外部项来源、必需引用、普通入口不出现购物、请求等待状态与重复 Show。
- [ ] 保留既有 Shop／Box Prefab GUID，调整宿主为 16 张商品卡（4×4 网格在目标横屏内全部可见），详细说明放独立详情区。移除旧放弃／结算／挑战按钮；加入现金、券、300 秒、重抽及完成购物。窄屏按正式安全区缩放并验证最小可读字号，不运行时补造布局。
- [ ] 根 `ReferenceCollector` 与独立 Box 更新后依次执行 `Assets/智能更新BoxUI`、`Assets/智能更新UI`，检查生成 View、挂载组件、每条序列化引用均实际存在。Controller 不 Instantiate／Destroy 列表项。
- [ ] 加技能输入与 HUD 充能显示；手柄组合只发一次能力，避免同帧消耗炸弹／冲刺。UI focus 隐藏时清理，语言切换不重抽、不重扣。
- [ ] 中英文正式文案覆盖 24 商品、截止、零命限制、容量不足、截断预览、队友等待与失败重试；固定 TMP 是可读默认语言，动态字段不挂 LocalizedText。
- [ ] 运行真实短类上限 12；正式 Prefab 中文／英文、桌面／移动横屏预览，真实手柄与设备结果单列。没有 Unity MCP 不声称反序列化／生成／画面通过。

## Task 8：发布门禁、定向回归与交接

- [ ] 对照正式设计逐条建立验收映射，任务 1—7 及两项玩法决定全部完成后，配置中才允许新局 `SlayerMartRulesVersion=1` 开放正式商店。不能因 UI 可打开就提前开门。
- [ ] 仅重跑受本次变化影响的真实类，按上面各任务独立范围与上限分批执行；不创建总套件回退、不重复统计同一用例为新增完成数。
- [ ] 实测两条主线的第 11／31 轮进店、跳过、完整购物与最终结果；双人再测慢加载、断线、同时购买、重启回滚。无实机证据保留待验，与纯规则／服务端进程检查分开。
- [ ] 编写 `Docs/Plans/SlayerMart实施验证记录-20261016.md`，记录红绿证据、配置／协议生成、字段兼容、真实环境、结果和剩余项目；更新当前 `Docs/README.md` 的商店事实，不把未验收内容称为完成。
- [ ] 若用户随后要求把此计划交定时任务，更新对应自动化而非另建重复任务；现有“全路线关卡剩余验收”的无新增商店边界不自动扩大。每次一个有界步骤，不打包、不自动提交。

## 本次编制核对与交接

已核对当前真实配置生成路径与 27／21 表门禁、单人 TrySaveRound／死亡路径、联机 CaptureRoundCheckpoint／CommitWithCandidate、普通单实例效果容器、保留 Shop Prefab 与生成 View、现有协议消息和 UI 列表约定。上述计划尚无产品代码、配置、协议或 Prefab 改动，未运行产品测试或生成器。

执行方式保持当前会话由主代理逐项推进；用户“确定的直接推进”不因计划模板变成重复审批。所有玩法决定均已确认，按任务顺序持续实施并分段提交推送。每个任务只在实际证据齐全后勾选。
