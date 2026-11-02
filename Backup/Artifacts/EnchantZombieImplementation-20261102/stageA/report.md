# 附魔丧尸阶段 A 规则实现

阶段 A 可审查；整体怪物功能未交付，等待阶段 B 具体视觉采用与真实资源接入。未提交、未推送、未操作 index，未触发任何 Player Build、资源构建或模拟构建。没有改正式 Prefab、材质或可见 View 表现。

## 实现

- Config 权威源新增三个独立身份 enemy.arcade.zombie.enchant.health/speed/shield；SpecialKind 为 EnchantHealth/EnchantSpeed/EnchantShield，独立资源名 ArcadeZombieEnchantHealth/Speed/Shield。生命80、抓挠10、速度1.76，复制 Caster 权威 hitpose 行到各自独立地址，没有加载替身。
- survivor_enemy 首列主键原样保留。360 秒三种各3%；六档比例依确认调整，出生预算不改。7个附魔字段全部来自权威表，正式 Config/gen.bat 一次双端生成，退出码0，见 gen.log。生成代码和 bytes 未手改。
- SurvivorSystem.Enchantments.cs 专用状态，出生时保存基础最大生命和速度；活着且爬出后开始4秒周期，半径6米，按ID排除自己；其它活着已爬出实体均可接收，包括支持者；无视线条件，离开范围及源死亡不撤销效果。
- 生命每层基础上限20%，5层上限，只补本次上限差，到顶不回血；速度按出生基础速度每层10%、5层上限，不反复乘当前速度，冰霜仍折算移动时间；大数饱和避免溢出。
- 附魔盾20/次、当前量上限100，无终身次数限制；所有伤害先扣附魔盾，再在未来攻击扣原生盾、最后生命。每次破盾攻击完整吸收不跨池溢出；原生盾破损不会因附魔或完整投影恢复。
- 活动主循环推进，暂停/选卡阻断；死亡、Stop清理，重开不复用层数。任务模式不推进附魔，不改闯关、联机和玩家强化。
- 金币默认使用出生基础 MaxHealth（EnchantBirthHealth），避免生命附魔抬高0.01收益；其它掉落规则保持。此默认供 root 审查。

## 最小无表现端口

SurvivorEnchantSnapshot 是完整不可变副本：HealthStacks、SpeedStacks、ShieldRemaining、BaseMaxHealth、BaseSpeed。System.TryGetEnchantSnapshot 查询权威状态。IArcadeCombatViewPort.SetSurvivorEnemyEnchantment(int, snapshot) 接收完整状态；PlaySurvivorEnchantPulse(int,string,Vector2,float) 接收脉冲；两者默认空实现，原生盾端口独立。ProjectSpecialEnemy 会补投影附魔状态，但绝不补层或补盾。阶段 B 只消费快照和通知，不能在显示层保存权威层数/推进时钟。

## 验证证据

- 修改前保存1327份实际文件，含已有脏工作树的权威源、运行代码、生成 C#、双端bytes，见 baseline/ 与 baseline.json；没有使用HEAD覆盖用户改动。implementation/ 是本批最终快照，changes.json 含前后SHA256，review.patch 为相对该实际基线的文本差异。
- 使用 code-role、luban-config、resource-load-check、TDD、verification-before-completion。11个初始新测试先RED：全部因缺 AdvanceEnchantments 断言失败，见 tests-red.json；扩展配置/护盾边界测试后18项GREEN。
- tests-enchant-green.json：SurvivorEnchantTests，18/18通过（15方法，坏周期NaN/Infinity/-1/0展开）。
- tests-special-green.json：SurvivorSpecialEnemyTests，21/21通过，包含原生盾全方向/持续伤害/完整投影及原怪攻击行为。
- tests-roster-green.json：SurvivorEnemyRosterTests，24/24通过，含0/120/240/360/480/600秒边界及坏比例。
- 每次仅真实非空短class，事先枚举方法并按源码声明最大展开数，includePassingTests=true；总计最终63项通过，未运行完整套件，无过滤回退。测试使用独立临时Game架构、真实Luban bytes和无表现端口，没有启动正式金币钱包或结果存档。
- 本轮可发现正式 mcp__unity_firepowerloop 工具，直接用标准 rql-query/script-execute/tests-run；未采用endpoint脚本，也未自造测试入口。初始确认正确工程，但isPlaying=true，因此退出Play。一次GREEN期间外部再次进入Play导致180秒超时且工具租约残留；退出Play后仅对该过期request调用插件现有 ClearActiveTestRun 清租约，随后仍通过正式tests-run得到18/18。见 tests-green-timeout.json、tests-active-rejected.json。未修改插件文件。
- 最终 editor：E:/GitHub/FirepowerLoop/Client，Play=false，compiling=false；Boot场景干净。Console有既存外部工具错误和本轮已修复的临时编译/测试租约错误；未清空Console或声称历史Console为空。

## 资源验证边界及继续工作

精确新地址为 Prefabs/Characters/ArcadeZombieEnchantHealth、Prefabs/Characters/ArcadeZombieEnchantSpeed、Prefabs/Characters/ArcadeZombieEnchantShield。真实 BeginCollect("DefaultPackage",true,false) 查询三名称均0条，见 collector-stageA.json。因此三新资源源收集、标签、运行/发布包加载均尚未通过，阶段 A 不宣称可运行加载。

现有 EnemyCtrl.PrepareArcade 会把 SurvivorRoster 全部加入敌人预加载集合（包括普通闯关准备流程），逐一 LoadEnemyPrefabAsync、ValidateGroundEntry/ValidateElementFeedback；失败会返回准备失败，不降级替身。因此阶段 A 配置必须与阶段 B 正式资源一起完成验证后再集成。阶段 B 须制作三独立Prefab/.meta、核对真实collector/arcade下载标签/准备事务缓存键和资源释放，连接采用后的表现，另行提供可播放实拍。本轮没有新增正式资源或把缺失地址标为忽略。

本批基线比对发现 SurvivorRuntimeTests.cs 在并发会话中被改变，本worker未编辑，已排除在 changes.json 和 review.patch 外。未夹带其它工作树更改。
