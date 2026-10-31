# 幸存者场内爬出正式接入

2026-10-31用户查看[采用版v3](../SurvivorGroundEmerge-v3-20261031/README.md)回复“可以”，确认该动画与正式接入。本批替换幸存者场外沿通道走入；地形仍以幸存者加载图为方向、正式地形候选另待采用，属性界面待确认。

[正式EnemyView隔离回放](applied-ground-crawl.gif) · [关键帧](contact-sheet.png)

场内出生点在玩家8米以外抽样，整个撑身爬行路径保持可行走，并检查敌人体型和玩家的连通区；土块及支撑动作留有避墙空间。没有安全点时等待下次刷新，不冻结本局、不取消既有无存活上限规则。约0.65秒翻土提示、3.65秒撑地/抬膝/起身后进入正常追击；暂停、升级暂停和失效场地不消耗动作时间。土下站立碰撞体关闭，站稳后恢复。翻土网格每个池中实例只创建一次，回池隐藏、最终释放。

复用三种正式丧尸各自的DockSeaEntry片段及已采用比例，场内动作、下沉和真实身体采样使用同一年龄；地下身体不挡弹、不被自动锁定，实际身体露出后可在站稳前受击。爬出期间不近战。死亡沿当前爬出姿态进入既有骨骼物理，不先跳回地面站姿。经验在死亡的场内可达位置产生，已有同坐标合并和隐藏名字规则保留。

## 验证

限定EditMode，全部includePassingTests=true，未全套、未打包：

|范围|最终结果|证据|
|---|---|---|
|SurvivorGroundEntryTests|11/11|ground-final.json|
|SurvivorRuntimeTests|37/37（最大40）|runtime-final.json|
|SurvivorEnemyCombatTests|14/14|enemy-combat.json|
|DockSeaEntryVisualTests|13/13|dock-final.json|
|GameplayResourceDistributionTests.SurvivorRosterDependenciesAreCollectedBeforeEntry|1/1|resource-source.json|

共76项不同检查。Runtime当前包括其他会话正在开发的5项弹夹/换弹检查，仅作为共享工作区回归证据，不将该功能纳入本次提交。最早8项真实RED（缺少正式状态/行为），随后通过；增加死亡/身体/连通性后11项通过。中间一次露出身体检查使用固定横轴穿过脚底，随机前倾方向可让弹道绕过身体；改为沿出生锁定朝向穿过身体后单方法及全11项通过。Runtime第一次31/32的失败来自新等待逻辑下测试仍调用未注册AppState的Tick，改为再次Spawn验证无安全点时等待；之后33项时新增换弹用例未完成而失败，最终当前37项全部通过。旧错误与连接超时均未记作通过。

正式回放200帧/20fps/10秒逐帧解码通过，正式View采样与土块网格，临时测试地面及主角原尺寸同框，近景慢放已目视检查。编辑态使用既有码头工具同样的骨矩阵蒙皮代理刷新，避免关闭Animator后的编辑器蒙皮缓存；不是新增游戏运行时蒙皮算法。不是自然对局或最终地形实拍。FormalReplay.cs.txt可重现。

## 资源链与边界

实际地址沿独立SurvivorEnemyRoster → Enemy配置PrefabAddress → EnemyCtrl.PrepareArcadeAsync → LoadEnemyPrefabAsync →现有提交/回滚事务，不新增地址或加载句柄。三个地址Prefabs/Characters/ArcadeZombieShambler、ArcadeZombieGaunt、ArcadeZombieBloated均由真实BeginCollect验证，标签arcade；既有入场下载arcade及预加载链保持。新增材质由正式Prefab序列化依赖持有；准备阶段校验材质和动作引用，失败通过已有地图准备事务退出。

另实际调用BeginCollect("DefaultPackage",false,false)只查询源收集依赖，不构建。三种主资源各47项DependAssets，均含Art/Survivor/GroundEntry/SurvivorGroundSoil.mat及各自Shambler/Gaunt/BloatedDockSeaEntry.anim，见source-dependencies.txt。常规测试用BeginCollect("DefaultPackage",true,false)核对主地址与标签并回读源引用。二者均属于源收集验证；未构建/更新模拟清单、未Player Build、未验证已发布包或设备实际加载。

独立只读审查两轮无剩余阻断问题，第二轮覆盖碰撞体恢复，见review.md。Boot从磁盘重载了获准丢弃的临时脏状态；未保留场景改动。地形和属性界面剩余工作在总清单保留。
