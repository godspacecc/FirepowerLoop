# 三种特殊丧尸独立整体审查

审查范围：已确认的 `Docs/Plans/幸存者特殊丧尸-20261101.md`，阶段1→阶段2顺序增量的 `full-review.patch`，最终工作区实现及两个阶段的原始验证证据。只做静态审查和证据核读，没有操作 Unity、重跑测试、改生产代码或操作 index。root 另行负责三段 GIF 的实际有效帧和同框外观目检。

## 结论

规则与正式资源链符合已确认要求。最终修复复审通过：原唯一 Important/P2 已关闭，没有发现该最小修复引入新的 Critical/Important；可继续更新清单、隔离提交及推送。本结论基于下述代码与原始限定测试证据，不是重跑整个套件。

## 已关闭 Important / P2：非预警自爆与法术敌人追击朝向被旧方向覆盖

位置：`Client/Assets/Game/Scripts/System/Arcade/SurvivorSystem.SpecialEnemies.cs:142`，消费端 `Client/Assets/Game/Scripts/Controller/View/Map/EnemyView.SpecialZombies.cs:13`。

修复前：`SpecialEnemyState.Facing` 初始为 `Vector2.up`，自爆与法术仅在预警起手（第70行）更新它。距离超过触发距离时（第64行），规则会继续追击移动，却不更新该方向。`ProjectSpecialEnemy` 无条件传递这个旧方向，随后视图无条件用其覆盖 `EnemyCtrl.TrySetEnemy` 本帧已经计算的追击朝向。结果：从侧面或下方接近的自爆、法术敌人会横移或倒退滑行；法术敌人发射后，玩家换位或退出射程时，模型也保持上次施法方向。盾牌限速朝向需要覆盖普通追踪，但该覆盖被扩展到了另外两种敌人的非攻击阶段。

已核读 `stage2/fix.patch` 和最终源码：在 `ProjectSpecialEnemy` 中，盾牌或当前预警阶段继续使用 `state.Facing`；其余特殊敌人仅在目标距离非零时传当前追击方向 `(player-enemy.Position).normalized`。零距离保留既有方向。此修改只选择显示快照方向，不改状态方向、射击目标、投射物速度、计时、平衡值和资源；预警仍锁住起手方向，盾牌仍使用限速后的权威方向。爬出最终方向由 `LateTickCombatFeedback → ApplyGroundEntry` 恢复为 `groundForward`，因此不将爬出受击几何错误列为本项影响。

新增 `SurvivorSpecialFacingTests` 精确5项补齐规则到显示端口的方向选择覆盖，核读原始 `stage2/facing-red.json` 为2通过、3失败（两种横向追击及施法后冷却朝向），`stage2/facing-green.json` 为5/5通过。测试同时断言法术前摇方向与弹道仍锁点、盾牌0.1秒只转12度及零距离方向安全。修复与测试针对原触发点闭环，未发现新破坏；本审查没有重跑。

## 最终样片证据补充

原 `attack-1.gif` 漏拍火球粒子，不能作为飞行火球可见证据。已核读 `RuntimeSampleFireball.cs`：改动位于 Backup 采样工具，仅在真实规则/视图调用后按帧显式推进粒子再渲染，没有修改正式效果或生产运行逻辑；来源仍明确为真实系统接口受控采样，不等同未干预整局实录。root 已独立目检修正版 `sample-1/009.png`、`010.png`，确认清楚可见飞行火球，修正版 GIF 已覆盖原文件。

`runtime-fireball.txt` 和 `start-fireball.txt` 记录补采样的真实 YooAssetResourceLoader、StartAsync=True，以及Stop/End后敌弹、敌爆的活动和池均为0；工具在开始前注入同批目录内的金币和结果双临时存档。此次受控采样不替代之前 `natural-restart-final.txt` 的正常注册生命周期/活动状态重开证据，两者用途及边界清楚。

## 已核对的规则与职责

- 权威 CSV 保持六档已确认权重、240/360秒解锁及首轮参数；只读敌池映射含行为参数和有限值验证。双端生成证据为阶段1 `generation-with-hitposes.log`，没有把生成代码当权威源。
- 自爆预警接管移动，玩家击杀移除特殊状态；自毁通过独立延迟移除入口，不经过击杀、金币和经验事务。重复伤害入口先检查实体仍在字典，避免重复奖励。
- 法术前摇起手保存玩家位置，释放直线弹；线段圆与实体矩形比较首次命中，墙优先处理相同接触时刻，飞行到期只扫描剩余寿命距离。共用已有受击保护。
- 盾牌直接弹丸传负速度方向的来源点，范围强化爆炸传爆心，统一扣血入口减伤；无方向燃烧不套盾。侧后正常、正面80%、最少1及转速符合配置。
- 主循环暂停/选卡门禁冻结规则时钟；爬出分支阻断特殊攻击。死亡、休眠和回池重置预警及动作状态；Stop 清空规则集合，End/地图释放清理敌弹、敌爆及其空闲池。
- 规则保持在 SurvivorSystem，模型视图及 ProjectileCtrl 只消费显示快照；新状态不写玩家持久化。未引入额外逐敌人 Update。

## 资源与运行证据

阶段2 `collection.txt`、`tests.json` 的真实 BeginCollect 结果覆盖精确的三个 `Prefabs/Characters/ArcadeZombie*` 地址及 `Prefabs/Combat/GrayboxBullet`，地址唯一、实际路径正确、标签为 arcade；完整 DependAssets 查询包含三敌材质、Fireball 和 ArcadeDroneExplosion。敌池独立入口合并身份预加载，弹体模板为短弹 Prefab 的序列化依赖，沿现有资源事务持有句柄；未以 AssetDatabase 存在代替收集证据。

`real-load-release.txt` 记录三个正式敌人地址经 YooAssetResourceLoader 真实加载，且逐个显式 Release。`natural-restart-final.txt` 记录真实 StartAsync 成功后由正常注册生命周期观察到预警、火球、敌爆、特殊状态与法术前摇；在敌弹1、敌爆1活动时真实 RestartAsync 成功，重开后的敌弹、敌爆活动/池和特殊状态全部为0。早先失败或全False记录不计入通过；没有把受控采样 GIF 说成未干预整局实录。

核读阶段1已有79项规则结果及阶段2限定4项正式资源结果，均通过；本审查没有重跑。身体沿 Shambler 同骨架、controller 和根尺寸，新增装备为骨骼子物体，未发现破坏已有爬出身体采样的代码证据。明显穿模与 GIF 可见性仍以 root 目检为准。

验证结论仅覆盖真实源收集与当前编辑器运行环境，不声称旧发布包、其他平台或已发布设备包含新资源；未打包属于授权边界，不列为缺陷。

## 提交边界

共享 Molotov 变更须沿阶段基线和当前最终暂存树区别，不可整份按 HEAD 差异夹带提交。本审查不要求为本任务回退或重做其它聊天的实现，也未更改总清单或宣称提交推送已完成。原 P2 修复及证据已闭环，由 root 完成清单、最终暂存树检查、中文提交及推送。
