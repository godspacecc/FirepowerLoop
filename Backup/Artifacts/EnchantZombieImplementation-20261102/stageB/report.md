# 附魔丧尸阶段 B：正式资源、表现及整体接入

阶段 B 实施已可供 root 独立审查。没有提交、推送、操作 index、资源构建、模拟构建或 Player Build；root 负责整体审查与最终提交。

## 正式交付

- 三个独立 ArcadeZombieEnchantHealth / Speed / Shield Prefab 及 .meta，沿正式 Caster 相同骨架、动画控制器和根尺寸制作；服装、肩部与手持球采用用户已确认红/绿/蓝。原 Caster 肩部挂 Torso、球挂 Middle1.R，均保留真实骨骼父级，没有固定在角色根节点。
- EnemyView.Enchantments 仅消费完整权威快照：生命层非零显示红环，速度层非零显示绿环，当前附魔盾大于零显示蓝环。三个半径错开，能同时区分；从不修改原生钢板/十字装备。16个现有及新增 EnemyView 角色 Prefab 统一持有共享模板依赖，普通、远程、原生盾等接收者均覆盖。尚未获得任何附魔的零快照不会创建圆环对象。
- 正式 ArcadeCombatViewController 两端口 -> IArcadeEnemyPresentationPort -> EnemyCtrl -> EnemyView 已接线，完整投影不授层、不补盾。死亡、禁用、休眠/回池复用沿既有 ResetSurvivorSpecial 隐藏目标标识。
- 波纹归 EnemyCtrl 当前地图所有，独立最多24个槽，满池轮换复用纯表现；寿命1.3秒，从0.1扩到实际通知半径。源死亡不提前删残波；暂停、选卡及地图隐藏不推进年龄；ClearArcadeEnemies/End/Restart/ReleaseCurrentMap释放池。复用原控制器生命周期，没有新增 MonoBehaviour.Update 或异步资源请求，也没有新的迟到回调或资源句柄所有者。
- 新增依赖为序列化共享 EnchantRing Prefab/白色 Unlit 材质及三服装材质，不增加独立动态加载地址。URP Unlit 通过各实例 MaterialPropertyBlock._BaseColor 正确染色，不污染共享材质；此前仅设 LineRenderer 顶点颜色的问题在正式实拍前修正。

## 资源证据与边界

三个精确请求地址均为 Prefabs/Characters/ArcadeZombieEnchant{Health,Speed,Shield}。逐文件主资源收集器使用 AddressByGameRelativePathNoExtension、PackSeparately、arcade 标签，GUID与各正式.meta匹配。

- source-collection.txt：真实 BeginCollect("DefaultPackage",true,false)，三地址各唯一1项，全部arcade。该dryRun模式未展开DependAssets，未据空依赖宣称完整。
- source-dependencies.txt：真实 BeginCollect("DefaultPackage",false,false) 的165行依赖展开，含各正式角色、共享圆环Prefab/材质、骨架网格/动画/着色器等。只是收集查询，没有调用构建入口。
- PrepareArcadeAsync按当前真实GameLoadMode代码：幸存者准备自己的敌池；普通闯关/联机准备原集合加独立敌池，因此三新地址均沿原LoadEnemyPrefabAsync缓存键config.Id事务提前准备；不忽略失败、不加载Caster替身。新增附魔模板缺失检查会沿原FailPrepared回滚。句柄仍由loading/temporary/committed缓存及ReleaseHandleOnce原事务独占释放；标识与波纹只借其依赖。
- start.txt 与 real-async-load.txt：正常Boot进入Play，SurvivorSceneFlowSystem.StartAsync成功，实际Game.YooAssetResourceLoader三地址异步加载成功，正式EnemyView均有共享模板。探针各自持有句柄并各释放一次；正式地图准备句柄仍归原EnemyCtrl，不由探针释放。
- 运行验证为当前编辑器正常Boot运行环境，不代表旧发布包或设备安装包已包含这些资源。本任务没有打包、更新发布清单或发布。

## 验证

先读实际实现并保全5366个真实文件（含已有脏工作树及.meta），baseline/、baseline.json 可复核。changes.json 限定本任务41项，implementation/与review.patch均相对开始实际基线，不使用HEAD覆盖用户改动。旧Prefab有少量Unity序列化补零字段及空值格式化，核心新增为模板引用；清单未夹带其它并发任务改动。

新增表现测试先RED：2项因缺快照及正式端口失败，tests-red.json。之后扩成4个真实非空[Test]方法、无参数展开（上限4），仅运行SurvivorEnchantPresentationTests，includePassingTests=true；最终 tests-green-raw.json / tests-green.json 为4通过、0失败、0跳过，5.748秒。覆盖三类共存、盾耗尽隐藏/原生盾不复原、回池隐藏、正式双端口覆写、24槽容量/暂停寿命/到时隐藏/清理、三地址真实唯一收集/标签及依赖。未重复阶段A的63项，也未运行完整套件。

natural-restart.txt 为16:01首轮完整自然链证据：正常Boot正式地图，受控注入三来源和原生盾目标后，没有直接调用授予方法，而由真实已注册TickFrame等到首次4秒三类授予（目标180最大生命、生命1层/迅捷1层/盾20）；消耗盾后蓝标识消失，重复完整投影不补盾、不长回钢板；继续自然第二周期补盾蓝恢复；三来源通过共享伤害事务死亡后目标三类标识保留；500ms真实暂停活动波纹年龄不变；带3个活动波纹RestartAsync成功后波纹池和权威附魔状态均为0，旧目标销毁或不活动。

首轮完整自然证据发生于最终颜色属性块修正、零快照分配门禁、准备模板缺失门禁之前；这些后续变化未修改授予、死亡、暂停和重开时钟逻辑。颜色修正后的可见效果由正式系统受控实拍验证，最后四项回归也通过。natural-error.txt 保留后续复跑失败：外部状态改变为Survivor.Active=false、AppState.Paused、视图未就绪，没收到首次授予，不能称作再次通过；按root要求不继续争用编辑器。Natural.cs后加的Suspend/wake检查在该失败复跑中未执行，不能计为运行通过。休眠恢复本轮仅以既有SyncEnemyPresentations完整投影源码及复用回归检查，未声称远场实录。

金币账本从Start前重定向到本批temporary-save；正常TryPrepare会创建并只读原结果文件，Start完成后立即将结果写入对象重定向到temporary-save，再执行任何伤害/Restart结算。首轮Restart的结果写入临时账本，后续失败复跑以Stop无结算结束，没有写用户金币或结果文件。没有迁移AnotherDay数据。

## 正式图片与样片来源

- formal-lineup.png：正式主角与三种正式附魔资源同镜头、同尺寸比较。
- formal-0.gif / formal-1.gif / formal-2.gif：每段4秒，40个10fps采样PNG，GIF合并相同停留帧后15帧但总时长仍4秒。红/绿/蓝正式系统接口受控样片：受控放置实体 -> SurvivorSystem.AdvanceEnchantments -> 实际快照/波纹端口 -> EnemyCtrl；暂停真实游戏后手动推进纯表现年龄，非原始整局实录，也不是临时几何替身。
- formal-coexist.png：正式盾牌目标脚下三色同心环共存，其他附魔源也可接受其它来源强化。
- capture-source.txt、Capture.cs和sample-0/1/2原帧保留。已查看有效帧010和共存图，特效颜色、波纹及脚下目标标识实际可见；摄影隔离层最初漏新建环已在摄影脚本内修复并重拍，不用布尔存在代替视觉检查。

最终查询正确工程E:/GitHub/FirepowerLoop/Client，Play=false、compiling=false，Boot场景isDirty=false。Console包含既有工具错误及本轮已定位的临时采样/复跑错误，未清空历史Console或声称历史无错。未发现待修生产问题；等待root独立审查，审查发现则继续修复。
