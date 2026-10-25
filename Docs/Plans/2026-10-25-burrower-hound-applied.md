# 潜地怪与恶犬采用版正式接入 Implementation Plan

> **For agentic workers:** 使用 executing-plans 在当前会话逐项执行；最后用 requesting-code-review 独立审查。用户已明确要求本会话正式接入，无需再次审批技术方案。

**Goal:** 把 Creatures v1 已采用外形与动作接入SpecialKind4/5，单人与联机沿相同正式表现入口采样。

**Architecture:** 已采用临时几何与绝对时间姿态烘焙为持久Mesh/Material/AnimationClip与两个正式Prefab，嵌入既有ObstacleVisualSet的Content模板树，沿原加载事务持有资源；不新增加载通道或Update。RoundEnemyVisual拥有演员，消费权威年龄、锁向、位置距离与原近战进度。联机新增只读年龄/朝向字段，经Tools/gen-protocol.ps1生成双端协议。

**Tech Stack:** Unity URP、AnimationClip手动采样、现有QFramework表现端口、MemoryPack协议、限定EditMode和服务器工具检查。

**Spec:** 本会话2026-10-25先后两次“可以”：先采用展示的两条最终GIF与原速／慢放，随后针对“现在是否也授权本会话继续正式接入这两种怪物？”明确授权。参考 Backup/Artifacts/HengeVolcanoPolish-v1-20261025/Creatures/README.md 与 verification.json。

## Global Constraints

- 当前共享工作区，不创建worktree；不动其他会话的场景候选或输出。
- 保留技能周期7.5秒、预警1.2秒等全部Config源数值及判伤／位移／人数／单双人规则；动画适配实际阶段长度，不反改规则以适配9.6／8秒展示时间轴。
- 核心手写类、函数与字段补中文注释；新资产及meta保留。正式美术放Client/Assets/Game/Art，报告／回放仍存Backup/Artifacts/HengeVolcanoPolish-v1-20261025/Creatures/Applied。
- 所有Unity MCP调用继续使用本批原子锁，持锁先查工程／场景／Console，隔离additive场景try/finally清理，Boot不保存。
- 测试只用真实非空短类／完整方法，includePassingTests=true；本项新类CreaturePresentationTests最多14例，邻接ArcadeRoundContentViewTests最多7例，相关业务规则类以预查实际数量为上限。含RED及必要修复总计最多80次用例；不完整套件、不打包。
- 维持总会话统一汇总／提交分工：本会话不git add/commit/push，总清单只读，确认与进度保存在自身status.txt及本计划。

## Review Focus

- 重复／乱序年龄不能倒播，首次晚快照应直接采样当前地下或扑咬阶段。
- 暂停时既有表现Tick不能推动技能时间；停止位移不能原地跑。
- 潜地原位免伤仍由权威裁决；模型根不可被动画移动。
- 狗预警／冲进朝向消费权威锁向，不能转向本机玩家；普通近战沿既有进度。
- 身份替换、移除、切轮及聚合模板异步晚回调不残留演员或释放共享Mesh。

## Task 1：正式资源与单人表现

Files: 新建Controller/View/Map/ArcadeCreatureView.cs、Controller/Arcade/ArcadeCombatViewController.Creatures.cs、Tests/EditMode/CreaturePresentationTests.cs；修改RoundContent.cs与ArcadeCombatViewController.cs；新建Tools/Unity/BurrowerHoundBake.cs与两种正式美术／Prefab，添加到现有Content模板。

Interfaces: `SetCreatureState(int id,float age,Vector2 facing)`只接收只读状态；`ArcadeCreatureView.SamplePose(string pose,float seconds)`只采样正式片段。位置由RecordRoundEnemyPosition投影，步态由实际累计距离驱动。

- [x] 写真实Prefab与controller集成测试，先运行RED，验证未接入时无法生成正式四足演员。
- [x] 从采用源烘焙Idle、Move、Sink、Underground、Emerge、Windup、Pounce与Bite／Recovery片段；持久化自有网格材质，绑定两个正式Prefab；编辑聚合模板并验证无Collider／Update。
- [x] controller按权威阶段选择片段，实际位移匹配步频，接入原近战，重复与旧年龄保护，身份与地图清理。
- [x] 限定新类GREEN与邻接类回归；实拍同框并和采用片段的关节关键点比较。

## Task 2：联机映射与交付

Files: Config/Proto/OnlineOuter_C_12001.proto；Tools/gen-protocol.ps1生成的双端消息；OnlineArcadeRunContracts.cs、OnlineArcadeRunSystem.cs、OnlineArcadePresentationSystem.cs、ArcadeCombatSystem.RoundContent.cs；Server的OnlineArcadeRunSystem.cs快照创建／复制；限定服务器检查工具。

Interfaces: EnemyInfo新增ContentAge=32、ContentFacingX=33、ContentFacingZ=34；快照构造追加同名可选参数，旧测试调用不破坏。年龄来自ContentElapsed／ContentClock-BornAt，方向来自真实锁向或目标，客户端不积分技能年龄。

- [x] 先写快照创建、克隆、协议编解码与客户端消费映射的失败检查。
- [x] 只改协议源并生成双端，再接单人／联机同一端口；不修改业务状态推进或配置。
- [x] GREEN、定向相关规则回归、四条正式原速／慢放完整解码，检查编译、清理、资源差异。
- [x] 独立只读审查并修复实际发现；更新自身交付及status供总会话汇总，禁止自行提交其他会话内容。

- 审查修正范围：独立审查发现模板晚于场地时的就绪/孤立节点问题、犬技能姿态覆盖真实近战口部问题。新增依赖顺序检查并把原近战检查覆盖两种怪物，新类最大14例；先运行两个完整方法共3例RED，再修复与GREEN。重复烘焙的临时clip释放也随同修正。

执行结果：正式资源、共用端口、双端生成和映射已完成。最终客户端14+7+2=23项、服务端4项通过；四条正式MP4全解码，采用姿态抽样误差位置≤1.543毫米／角度≤0.172459度。独立审查2项P2与1项P3已修复并复验。总计54次已执行用例（Unity50、服务端4），未超过80；无完整套件、打包或真人双端验收。按分工未提交推送，交总会话汇总；证据见Creatures/Applied/README.md。
