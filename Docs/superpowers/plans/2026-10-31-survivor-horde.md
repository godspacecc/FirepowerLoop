# 执行结果（2026-10-31）

以下为实际完成记录；后附原计划保留设计依据，其拟定接口、测试fixture和性能矩阵不代表已经逐字执行。用户已确认规则示意并授权接入。

- [x] 任务1：群级账本、地图分布、预留/成功提交/失败延后及移动失效，12项规则检查；独立规则先红后绿。
- [x] 任务2：完整相机地面范围、非死亡休眠、同步资源池唤醒、有限闲置缓存和入口预热。表现与身份事务并入现有SurvivorRuntimeTests，未创建原拟PresentationTests；真实资源收集及Play验证另列。
- [x] 任务3：三类来源及10项配置接入，Config/gen.bat双端生成；8项新集成检查逐方法执行。附近占比落实为每4次原周期1次，未使用概率字段。
- [x] 任务4：近场空间索引，8项限定检查及8项既有战斗回归；动态规则示意获用户采用，正式模型动作沿用。
- [x] 任务5：实际菜单入场、移动采样、最终一万远场受控压力采样、独立代码复审、任务隔离生成及C#编译。没有做完整分辨率矩阵、长局或严格同路线改前改后基准，不宣称覆盖这些指标。提交推送状态以Git回执为准。

实施裁定：远场新生群与保留血量的个体例外分别管理；端口传标量快照而不暴露权威对象。未知相机不休眠，已进入尸潮后丢失相机则暂停报错。常规预算2，可见性紧急恢复允许超预算，因此近场无上限成本单独说明。共享工作区不建worktree；独立索引只包含本任务差异。

全部证据、实际37项测试分布、测量数据和限制见[验证报告](../../../Backup/Artifacts/SurvivorHorde-20261031/README.md)。

---

# 幸存者地图尸潮实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans to implement this plan task-by-task if native execution is selected, or subagent-driven-development if the user selects delegation. Steps use checkbox syntax.

**Goal:** 地图已有敌群、外围成批来潮、少量附近爬出；远处不逐只运行完整角色，保留无存活总上限。

**Architecture:** 现有 SurvivorSystem 持有调度与战斗事务，远场按可达分区保存敌群及必要个体例外，近场沿用 ArcadeEnemyRuntime。EnemyCtrl 管理已预加载资源与表现池，新增独立休眠入口；死亡、掉落与计分仍由原战斗路径负责。

**Tech Stack:** Unity C#、QFramework、Luban、YooAsset、限定 EditMode 检查。

**Spec:** [已确认设计](../specs/2026-10-31-survivor-horde-design.md)。用户2026-10-31回复“继续”确认该设计；用户随后“直接开始执行”，采用当前助手顺序实施。

## Global Constraints

- 只改幸存者模式，不改普通闯关或联机，不默认创建worktree，不触发Player Build。
- 保留“不设存活上限”、敌种解锁、固定属性、暂停与掉落规则；调度预算不能删怪或静默丢弃已生成敌群。
- 保留正式资源与.meta；新表现先提供可播放临时预览，未采用前不改正式动作、镜头或场地。
- 配置只改Config权威源，通过Config/gen.bat双端生成，不手改生成文件。
- 日志、预览和验证报告统一放Backup/Artifacts/SurvivorHorde-20261031/。
- 工作区已有多任务差异，先记录本任务文件基线，最后只提交本任务通过验证的差异并中文提交推送origin。
- Unity操作前用本工程MCP确认项目路径、场景与Console；连接失败先排查，不能将源码检查称为运行验证。

## Review Focus

1. 玩家快速移动或相机宽高比改变：准备带覆盖新视野，无角色突然出现（任务2、4）。
2. 胖体型在围栏窄口：远场推进与展开都遵守相应体型的可达性（任务1、3）。
3. 敌人受伤后离开再回来：ID、血量、攻击状态不重置，不能合并成满血敌群（任务1、2）。
4. 地图卸载时表现请求尚未完成：旧代次不能激活新局角色，句柄仅释放一次（任务2）。
5. 长时间积怪及长暂停：计数不溢出、不丢怪、不补发暂停期间出生、不在一帧清空创建队列（任务1、3、5）。

## 文件与职责

- 新增Utility/Arcade/SurvivorHordeState.cs：远场群、展开请求、例外个体及稳定ID分配状态。
- 新增Utility/Arcade/SurvivorHordeRules.cs：出生分布、远场时间推进、分帧展开和转换纯规则。
- 新增Utility/Arcade/SurvivorEnemySpatialIndex.cs：近场命中候选空间索引。
- 新增System/Arcade/SurvivorSystem.Horde.cs：接入现有生命周期、配置和近远场事务。
- 修改System/Arcade/SurvivorSystem.cs、SurvivorSystem.Enemies.cs、SurvivorSystem.GroundEntry.cs：替换单只调度、保留爬出与既有战斗规则。
- 修改Controller/Arcade/ArcadeCombatViewController.cs、Controller/ViewCtrl/Enemy/EnemyRuntimePorts.cs、Controller/Enemy/EnemyCtrl.cs：相机范围快照与非死亡休眠/唤醒接口；共用接口的既有实现和测试替身同步更新。
- 修改Utility/Arcade/SurvivorConfiguration.cs、Config/Defines/beans.xml、Config/DataCsv/survivor_rule.csv：配置读取、约束和源字段；生成输出按gen.bat实际清单审查。
- 以上C#路径均相对Client/Assets/Game/Scripts/；测试位于Client/Assets/Tests/EditMode/。核心手写类、字段、方法补中文注释。

### 任务1：远场状态、地图分布与预算规则

**Interfaces（新增）**：`SurvivorHordeState` 保存群记录和个体例外；`SurvivorHordeRules.Seed(SurvivorHordeState state, SurvivorHordeSettings settings, int seed, Vector2 initialPlayer, Func<Vector2,float,bool> reachable)`；`Advance(SurvivorHordeState state, float seconds, Vector2 target, Func<Vector2,float,float,Vector2> move)`；`CollectActivationCandidates(SurvivorHordeState state, Rect prepareBounds, int budget, List<SurvivorActivationRequest> output)`。`SurvivorHordeSettings` 为同文件中不可变设置，数值由配置提供；`SurvivorActivationRequest` 包含群ID、种类、位置、稳定个体ID及可选原个体快照。候选只有近场提交成功才从远场扣除。

- [ ] 创建SurvivorHordeRulesTests（最多12项）：同seed与初始地图分区重复可重现；后续移动玩家不移动原铺怪坐标；无安全落点不生成；预算为2时一次输出不超过2；反复取候选未提交不扣数量；提交一次只扣一次；超过旧60只仍可生成；暂停不推进；受伤例外不能并入满血群；窄口大体型拒绝通过；大计数checked溢出有明确错误；重开清空状态。
- [ ] 核实真实fixture和用例数量后，运行限定EditMode，includePassingTests=true、最大12；确认失败来自缺少规则而非测试环境。
- [ ] 实现上述状态与规则。群按地图分区、敌种、相同属性和路径阶段归并；例外个体保留原快照。远场移动调用对应体型共享导航，允许停等，不跨障碍跳跃。失败计数可观测，不每帧刷日志。
- [ ] 重跑同范围至全部通过，保存结果；暂不接入正式运行。

### 任务2：视野准备带、非死亡休眠和表现池

**Interfaces（新增）**：`IArcadeCombatViewPort.TryGetSurvivorViewBounds(out Rect bounds)` 返回地图平面实际相机覆盖；`TryWakeSurvivorEnemy(ArcadeEnemyRuntime enemy, out string error)` 完整同步后返回成功；`SuspendSurvivorEnemy(int id)` 仅回收表现。底层`IArcadeEnemyPresentationPort.SuspendEnemy(int runtimeId)`复用EnemyCtrl已有池，不调用死亡删除。无法获得相机范围时不将现有敌人休眠，入口仍等待战场准备。

- [ ] 创建SurvivorHordePresentationTests（最多10项），覆盖正交/透视及不同宽高比；准备与退出带不同；休眠无死亡动画、掉落或击杀；唤醒保留ID与当前生命；旧地图请求不生效；缺少预加载资源明确失败；池内实例不运行Animator；重复休眠幂等。先限定运行观察失败。
- [ ] 修改端口及EnemyCtrl，实现休眠不销毁配置所有权，唤醒不重新加载每只角色资源；实例池设置闲置缓存预算，超额释放闲置实例而非删除活敌人。
- [ ] 准备带扩展量至少覆盖最大相对速度×预期准备延迟及身体半径；过期相机快照立即刷新，保留退出滞回。展开前检查可达、占地、安全距离和地图代次。
- [ ] 完成相同限定检查。使用resource-load-check追踪实际三种敌人地址、标签、预加载与实例回收；禁止用替身检查代替真实加载证据。

### 任务3：三类出怪接入和配置

**Interfaces（新增私有）**：`InitializeHorde()`、`TickHorde(float dt)`、`ResetHorde()`，位于SurvivorSystem.Horde.cs，由TryStart/TickFrame/Stop调用；只有活动且未暂停的战斗推进。

- [ ] Config新增铺怪群数/每群数量、来潮周期/每潮数量、附近爬出占比、远场更新周期、每帧展开预算、准备余量和退出余量；建立正数、有限值、占比0～1、退出余量大于进入余量的校验。首轮调校值在临时预览中展示，不以未经观察的硬编码值替代Config。
- [ ] 创建SurvivorHordeIntegrationTests（最多12项）：三类来源确实走不同分布；到时间才解锁相应敌种；暂停冻结；重开清空；出生失败不积欠账；已生成未展开不丢失；清除后计分和掉落仅一次；受伤转换不回血；爬出/近战/弹丸威胁不休眠；超过旧上限仍推进；配置错误阻止开局。限定运行并观察失败。
- [ ] 更新权威源与读取器，执行Config/gen.bat，检查双端生成结果，只处理本任务字段。
- [ ] 将单只Spawn计时替换为群调度，附近分支沿用地面爬出，地图与外围分支先形成远场群；保持全部生成位置与经验落点可达。对已激活实体，简化前保留例外快照；入场及攻击状态未完成时继续完整模拟。
- [ ] 针对新fixture最多12项复验；另逐方法执行现有SurvivorRuntimeTests.SpawnContinuesBeyondFormerAliveLimit、GroundSpawnWithNoWalkableFloorWaitsWithoutFailingRun、PauseFreezesClockEnemiesAndInputPresentation，每次最大1，全部includePassingTests=true。先确认方法仍存在，不允许失效过滤回退。

### 任务4：近场碰撞查询与可播放预览

**Interfaces（新增）**：`SurvivorEnemySpatialIndex.Rebuild(IEnumerable<ArcadeEnemyRuntime> enemies)`；`QueryCircle(Vector2 center,float radius,List<ArcadeEnemyRuntime> results)`；`QuerySegment(Vector2 from,Vector2 to,float padding,List<ArcadeEnemyRuntime> results)`。查询只返回候选，最终身体命中和遮挡仍使用原规则；稳定ID排序保证同距离与同弹道候选结果可复现。

- [ ] 创建SurvivorHordeSpatialTests（最多8项）：跨格长弹道、边界体型、相同距离稳定ID、死亡后索引移除、无人机范围、空索引、重复查询无重复候选、与穷举基线命中一致。限定运行确认失败。
- [ ] 实现复用格容器与结果缓冲，将Fire/TickDrone/AdvanceBullets替换为候选查询，避免每弹全图扫描及热路径ToArray；爬出身体扫掠仍使用正式采样。
- [ ] 同范围验证；统计稳态分配，索引扩容与首帧预热单独报告，不以平均值掩盖尖峰。
- [ ] 制作独立临时动态预览：展示地图铺怪、成批靠近、附近少量爬出、快速回头；明确标注模拟预览与正式实拍区别。提供用户可播放文件，涉及新视觉的部分等待采用；独立规则验证继续进行。

### 任务5：真实运行、性能证据与收尾

- [ ] 本工程MCP查询项目路径、当前场景与Console；连接不可用时保存具体错误并排查桥接配置，不操作另一个Unity工程。仅必要时按项目授权重载Boot丢弃临时场景改动。
- [ ] 使用一致路线和敌群输入采集改前/改后，分别观察正常移动、快速回头、靠边围栏、持续积怪及暂停恢复；至少记录主线程p50/p95/p99/最大值、每帧GC、完整角色数、远场群数、展开队列长度/最长等待、内存趋势和安全出生失败数。
- [ ] 在无存活上限条件下比较远场100/1000/10000只的受控场景，声明这是压力输入而非玩法配置；核对计数守恒，完整实例不与远场数量同比增长。近场极端聚集成本单独报告，不隐瞒无上限方案的实际边界。
- [ ] 核对展开队列无持续积压、回头无突然冒怪、未发生回血或重复击杀、地形通路与掉落可达。若不达标，修复再运行相应检查，不宣称完成。
- [ ] 真实资源收集结果与编辑器运行加载分开记录，未构建/未发布包验证明确标注；新视觉确认状态独立记录。
- [ ] 按requesting-code-review做任务范围审查，按verification-before-completion核验记录；更新执行总清单。独立索引或精确差异提交本任务，中文提交推送origin，核对远端提交。问题未解决不提交推送。

## 执行选择

建议由当前助手顺序实施：配置、状态转换和表现事务紧密依赖，当前共享工作区还有其他任务差异，顺序实施便于保护边界。用户也可选择子代理逐任务实施与独立审查。两者均不改变预览及验证要求。

## 自检结果

设计中的三类来源、无总上限、远场轻量化、提前展开、状态保全、空间查询、暂停卸载、资源验证与性能对比均有对应任务。上列新fixture和接口是实施目标，当前尚不存在，不得现在执行这些过滤或声称通过。已核对现有3个回归方法和相关生产路径。未运行Unity测试，未修改产品代码。
