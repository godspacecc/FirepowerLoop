# 阶段2：特殊丧尸正式表现与整体集成

已完成正式三敌人资源及接口接入，等待root目检和独立整体审查。本阶段未提交、未推送、未操作index；未运行资源构建、模拟构建或Player Build。使用code-role、luban-config（仅边界核查，本阶段无配置修改/生成）、resource-load-check、continuing-confirmed-work及verification-before-completion。

## 实现

- 独立ArcadeZombieExploder/Caster/Shield复制Shambler，身体骨架/Animator controller/根尺寸保持一致；本阶段不修改身体动作/骨架/比例，因此阶段1权威hitpose复用条件保持。胸前炸药挂Torso、施法球挂Middle1.R、盾牌挂Middle1.L；装备随动作骨骼移动，不以模型根固定。颜色、形状、大小沿已确认Preview.cs；盾牌在手前留出距离。
- EnemyView.SpecialZombies消费完整权威快照，地面红圈按自爆范围与进度显示；施法前摇沿已有抓挠骨架采样，视图不裁决伤害。死亡（动画及物理）、休眠、回池显式重置。EnemyCtrl不新增Update生命周期。
- 特殊快照在Horde投影中移到通用近战之后：否则法术抬手会被通用空闲近战同帧清零。盾牌最终转向来自业务direction，通用近战同样使用SpecialFacing，不覆盖为追踪玩家。
- ProjectileCtrl拥有独立敌弹与敌爆字典及池；复用Fireball与ArcadeDroneExplosion，敌爆不会借用玩家survivorBlasts预算。短弹Prefab通过序列化依赖随既有ProjectileCtrl准备事务预载；缺依赖时入口失败回滚。地图退出、End、ReleaseAll、重新提交前销毁实例再释放句柄；本阶段表现同步不额外发起迟到异步实例化。
- 正式三敌地址进入逐文件collector，arcade标签；Boot.StartupResourceTags既有startup+arcade下载链保持；EnemyCtrl.PrepareArcadeAsync从幸存者独立敌池合并ID预加载，非依赖闯关敌组碰巧引用。ProjectileCtrl既有prepare/commit/rollback持有Fireball/Explosion序列化依赖，未引入另一个下载链。

## 验证与证据

- `tests.json`：限定EditMode短类SurvivorSpecialVisualTests，预先枚举3个Prefab参数项+1个真实收集项，上限4，includePassingTests=true，4/4通过。检查身体根尺寸与原controller、骨骼挂载、业务朝向、回池关圈；真实BeginCollect(DefaultPackage,true,false)精确四地址唯一、路径、arcade标签，并以false查询DependAssets核对三敌正式材质、Fireball.prefab与ArcadeDroneExplosion.prefab依赖。`collection.txt`为原始地址/包名/依赖证据。未以AssetDatabase存在代替收集结果。
- `start.txt`首轮正常Boot Play ->真实SurvivorSceneFlowSystem.StartAsync成功。runtime.txt记录Game.YooAssetResourceLoader及真实三敌加载；`real-load-release.txt`另用真实LoadAssetAsync逐地址加载并finally Release句柄，未替换加载器。地图准备本身也从真实loader持有资源句柄。首轮采样额外同步LoadAsset没有暴露句柄，以后续独立async回执证明显式释放；退出Play清除该轮同步探针环境。
- `natural-restart-final.txt`为最终连贯正常Play证据：真实StartAsync=True；只注入三个临时测试敌人并禁止玩家开火，随后不调用Tick，由正常注册生命周期观察2.2秒，warning/fireball/blast/specialStates/casterWindup全部True，UI=False且无LastError。随后在存在敌弹1、敌爆1时真实RestartAsync=True；重开后敌弹/敌爆活动及池、特殊状态全部0。临时金币与结果store均先注入stage2/runtime-save，TryPrepare保留注入，不写玩家账本。
- `runtime.txt`首轮受控采样还验证Stop/End后敌弹/敌爆活动及池均0；`restart.txt`第一次真实重开成功。规则层的爬出门禁、暂停、死亡取消、伤害及墙体扫掠由stage1已审查79项覆盖，本阶段未扩大重跑全套。
- `lineup.png`：正式Prefab与正式主角同框，在临时隔离场景采样静态Idle，源为正式资源，不是概念图。`attack-0/1/2.gif`：正常Play加载后，用真实SurvivorSystem特殊方法→真实ViewCtrl/EnemyCtrl/ProjectileCtrl受控0.1秒采样，字幕明确来源，不标为未干预完整对局实录。对应原始sample-0/1/2各40 PNG与RuntimeSample.cs。最终自然生命周期回执补充证明正式运行调用链能触发三种表现及法术抬手。
- 已目检lineup、爆炸预警侧视、盾牌侧视：主角与敌人同尺度，装备跟骨骼，盾牌在手前。root仍须独立目检三段GIF及整体代码。
- 先前natural-restart.txt自然观察全False：脚本前运行环境已结束/受阻，不算通过。natural-restart-final-error.txt保留另一次NullResourceLoader/YooAssets未初始化失败（疑似其它聊天重置；非确定归因），干净Boot后同一个async连贯完成的-final.txt全部成功。失败不混入最终通过数量。
- 当前验证为编辑器正常Play环境与真实源收集结果；未验证任何旧发布包、其他平台安装包或已发布设备，不以本次源检查声称旧包已包含资源。

## 交付边界

changes.json列本阶段47项路径与真实开工基线散列，review.patch按stage2/baseline真实基线生成，供root只挑本任务变更。GrayboxArrow仅留基线未修改、不列变更。Config/生成代码/bytes本阶段没有修改。共享EnemyCtrl/EnemyView/ProjectileView/SurvivorSystem.Horde等可能包含前序任务状态，patch相对于本阶段真实基线，不应按HEAD整文件吞入别的聊天改动。

剩余仅root独立目检、整体审查与必要问题修复；之后由root更新总清单、中文提交并推送origin当前分支。本代理未执行这些操作。


## 最终统一修复轮（P2及法术可见样片）

最终审查指出非预警自爆/法术的追击方向被旧state.Facing覆盖。已在ProjectSpecialEnemy最小修正：Shield或Warning保留权威方向；其余目标距离非零时使用当前追击方向，零距离保留原方向。前摇仍锁点且盾牌仍限速，未修改平衡参数、攻击裁决或正式效果资源。

限定新增SurvivorSpecialFacingTests共5项（LateralChaseProjectsCurrentTarget两参数exploder/caster、CasterLocksWindupThenFacesNewTargetDuringCooldown、ShieldProjectionKeepsAuthorityTurnLimit、CoincidentTargetKeepsSafeFacing），EditMode真实短class筛选includePassingTests=true。先复现2通过3失败，修正后5/5通过，原始facing-red.json及facing-green.json；没有全套或无效过滤回退。测试直接消费真实系统投影端口，覆盖横向追击、施法锁向/锁定弹道、发射后冷却换位、盾牌12度/0.1秒及零距离。

原attack-1.gif没有拍出飞行火球，只可见手持球及抬手，不能作为火球可见质量证据。已用RuntimeSampleFireball.cs重新通过干净正常Boot ->真实YooAssetResourceLoader ->真实StartAsync，开局前注入同批runtime-save双账本；正式火球资源及正式运行时代码未为截图调整。唯一采样调整是在每次真实规则/视图投影后，以0.1秒年龄显式Simulate火球ParticleSystem再渲染，维持“真实正式资源/系统接口受控采样”来源，不冒充整局实录。

修正版sample-1/008.png、009.png、010.png清楚可见火球从施法者向主角飞行，009最清楚；011已命中移除。attack-1.gif已由修正版40帧覆盖，字幕仍标记controlled step sample。runtime-fireball.txt和start-fireball.txt记录本次真实loader/Start成功及Stop/End后敌弹、敌爆活动/池全部0；runtime-fireball-error.txt保留第一次紧接测试后的未初始化失败，后一次成功由上述无error文件明确记录，不混算。自爆预警有效帧仍为sample-0/000–009，爆燃010–012（011最清楚），024已经结束。盾牌样片维持原样。

fix.patch为本轮相对fix-baseline增量（最小正式代码、新5项测试及meta、纠正采样工具、报告）；changes.json和review.patch同步加入本阶段后续P2及测试，实际47文件。未改index、未提交、未构建或发布。Unity采样完成后已退出Play。
