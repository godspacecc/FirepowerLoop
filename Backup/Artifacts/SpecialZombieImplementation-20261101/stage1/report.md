# 特殊丧尸阶段1交付

阶段1规则已实施，正式视觉资源及真实资源收集验证留阶段2。本阶段未提交、未操作index、未打包/构建/模拟构建，未写真实金币或结果。使用code-role、luban-config、resource-load-check、TDD及verification-before-completion技能。

## 本次行为

- 自爆：240活动秒后混入，HP60、速度2.97（普通2.2的1.35倍），1.7米触发，停步预警1秒，半径1.5、伤害25；死亡取消；自毁不走击杀/金币/经验事务。
- 法术：360活动秒后混入，HP80、速度1.76，射程10，前摇0.8秒，发射后冷却3秒，火球速度7、寿命2.5秒、伤害15。前摇起手锁定目标点，释放后直飞，可躲；以线段首次接触比较玩家圆与实体矩形障碍，薄墙不会漏判，水/仅行走障碍不挡。
- 盾牌：240活动秒后混入，HP150、速度1.43、抓挠10，正面半角60度减伤80%，转向120度/秒，减伤后至少1。所有直接步枪、无人机、分裂、穿透及爆炸在统一扣血入口按来袭源处理；燃烧和其他持续火区无方向，不二次套盾。
- 六档每120秒比例（普通/瘦削/笨重/自爆/法术/盾牌）：100/0/0/0/0/0；60/40/0/0/0/0；45/30/15/5/0/5；30/30/20/7/6/7；20/30/25/8/8/9；10/30/30/10/10/10。总生成预算未增加。
- 普通与尸潮出生共用敌池；爬出期间不启动特殊攻击，冰霜仍影响特殊敌人的移动。暂停/选卡由现有主Tick门禁冻结全部特殊时钟。Stop清空方向、预警、敌弹、独立爆炸与待移除身份。

## 配置及资源边界

权威源为Config/DataCsv/enemy.csv、survivor_enemy.csv、arcade_dock_hit_pose.csv及Defines/beans.xml。首字段Id保持。特殊参数加入已有幸存者敌池，不增加表/加载入口。FromTables映射并检查有限值、合法行为、各行为必填参数，保留每档100及未解锁权重0的校验。数字只在CSV定义。

通过Config/gen.bat双端生成，见generation-with-hitposes.log。生成的本任务输出为双端SurvivorEnemyConfig.cs、tbenemy.bytes、tbsurvivorenemy.bytes、tbarcadedockhitpose.bytes。

新增身份及实际地址：enemy.arcade.zombie.exploder→Prefabs/Characters/ArcadeZombieExploder；caster→ArcadeZombieCaster；shield→ArcadeZombieShield。闯关敌组不引用它们。阶段2尚未创建正式Prefab，因此未宣称这些地址已收集、下载或可加载。

三种新增hit pose地址复制已有Shambler权威采样；阶段2已明确沿Shambler同骨架/同尺寸复制，仅增加装备，故身体爬出采样一致。若阶段2修改身体骨架/动画/缩放，必须重新采样并生成，不能沿用此假设。

## 阶段2精确接口

IArcadeCombatViewPort新增默认空实现，阶段2实现具体表现：

- SetSurvivorEnemySpecial(int id,string kind,Vector2 direction,bool warning,float progress,Vector2 target,float radius)。kind严格为Exploder/Caster/Shield；direction为权威平面朝向（盾牌必须用于真实模型转向，不能由SetEnemy自动跟玩家覆盖）；warning仅前摇阶段true；progress为0至1归一前摇进度；target为起手锁定玩家位置；radius为自爆范围，其他0。每个近场显示同步重发全状态，远场恢复不能依赖先前触发事件。普通敌人不调用。
- SetSurvivorHostileProjectile(int id,Vector2 position,Vector2 direction) / RemoveSurvivorHostileProjectile(int id)。只投影火球，业务不带资源对象；碰墙/玩家/到期/Stop均Remove，End还应兜底清理。首次业务创建后本帧推进并投影，重复Set必须更新而非重复实例。
- SetSurvivorEnemyBlast(int id,Vector2 position,float radius,float progress,int capacity) / RemoveSurvivorEnemyBlast(int id)。自爆使用独立specialBlasts列表与容量，不挤占玩家强化爆炸；阶段2应建立独立owner池，可以复用SurvivorBlast模板。不能直接接到玩家survivorBlasts.Count总容量，否则仍会抢占玩家表现预算。表现持续复用已有ModifierBlastSeconds=0.4秒，伤害发生在预警结束帧且不受表现容量约束。

所有id来自现有本局identity统一递增，敌人/玩家弹/敌弹/双方爆炸不会冲突；新局归零，故End/回池必须完整清理。生命周期owner为SurvivorSystem及现有地图表现事务，不另开MonoBehaviour.Update。迟到加载须遵守地图代际、隐藏候选提交及对称释放。Suspend/RemoveEnemy/End应回收预警；近场恢复重新消费完整特殊状态。自毁RemoveEnemy(id,false)，玩家击杀沿原RemoveEnemy(id,true)。

## 验证

限定EditMode短类，includePassingTests=true；参数展开上限与结果：专项17/17、敌池24/24、近战15/15、元素23/23。见green2.json、roster.json、combat.json、elements.json。测试使用独立架构与现有临时文件fixture，不运行正式地图、不调用真实钱包/结果。

专项覆盖六档所有权重、独立身份、起手锁点、自爆一次/无奖励、死亡不再爆炸、快弹扫掠/墙挡、真实步枪/无人机命中盾、爆炸入射源、正面/侧面/背面、限速转身、最少1、无方向伤害、爬出门禁、暂停/Stop。旧元素测试验证原燃烧多目标死亡、经验事务及暂停行为保持。

先写10项测试，首次Refresh受到外部并行Molotov缺字段/方法的编译阻断；后遇新身份缺hit pose导致配置准备失败（red2.json），补权威采样再生成后获得真实规则红灯：10项1通过9失败（缺TickSpecialEnemy、敌弹字段、方向伤害参数）。该轮原始完整回执在会话工具输出，未另持久化；这里不将red2.json冒充规则红灯。然后实现规则获得green1.json的10/10；追加覆盖后green2.json的17/17。部分新增扩展测试是实现后补充，并非每一项都单独经历红灯。

最终专项重跑曾被其他聊天SurvivorRuntimeTests.cs:120的Assert.Multiple编译错误短暂阻断；对方恢复编译后，按同一17项短类重新验证，最终17/17通过，见special-final.json。git diff --check针对本任务源码及CSV通过。

## 基线与审查

baseline/为实际开工复制，包含手写文件、所有双端生成代码/bytes及后发现会修改的hit pose源；baseline.json记录初始批次散列。changes.json仅列本任务涉及路径及当前散列，review.patch按真实基线比对。共享文件beans.xml、SurvivorSystem.cs、ArcadeCombatViewController.cs在执行期间还出现其他聊天Molotov改动，patch可能含其并发差异；已标sharedConcurrentChanges，不能据此将他人改动归入本任务。双端其他新生成Molotov字段也不属于本任务。

阶段2未完成前，不能宣称整项正式接入或真实资源加载完成。未执行真实BeginCollect、运行/发布包检查、Play实机、联机、Player Build或资源构建。
