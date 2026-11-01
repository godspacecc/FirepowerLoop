# 幸存者元素正式视觉接入

2026-11-01，用户对上一批elements-preview.gif回复“可以”，明确采用火焰粒子／冰霜染色冷光、尺寸及启停效果，正式视觉接入已授权。此前玩法提交1cfebf74保持，本文只交付正式视觉和必要验证。

## 正式接入

- 新增EnemyElementFeedback及EnemyView.Elements，接收权威fire/frost状态；SurvivorSystem.SyncEnemyPresentations→既有CombatView→EnemyCtrl→EnemyView投影，隐藏再唤醒恢复当前状态，结束发false。
- 三种ArcadeZombieShambler／Gaunt／Bloated均嵌入共用SurvivorEnemyElements.prefab，材质／纹理／Shader全部序列化引用，借用现有敌人预载句柄，不增代码请求地址／独立收集器。
- 两个ParticleSystem各最多80粒，playOnAwake=false；启动后立即Pause，由EnemyCtrl已有生命周期按活动战斗时间手动Simulate，暂停／升级／加载／UI门禁传零步长。组件无Update，不计算伤害／减速，不独立加载或释放资源。
- 冷色按全部身体材质槽的原色混合24%，使用实例PropertyBlock，不改共享材质、不反复分配材质。真实受击闪红期间归还槽覆盖，结束后继续冷色；冰霜到期、死亡、回池、禁用及切图幂等归还。
- 特效根挂接到ArcadeVisual，采用保持世界姿态的重设父节点，补偿原0.9079756比例，使站立大小与采用图一致；爬出时跟随身体俯仰升降，不浮在地图出生点。
- 幸存者EnemyCtrl预载增加ValidateElementFeedback，缺少粒子、身体、材质、纹理或手动时钟配置时明确拒绝入场。普通敌人没有该组件仍沿原流程。
- Bake.cs.txt是已采用资源的定向制作脚本，保护正确工程、空闲编辑态，限定保存自身资源／三个丧尸Prefab，不全局SaveAssets，保留原.meta。

## 有界验证

全部Unity检查均EditMode、真实短testClass或完整testMethod、includePassingTests=true；未跑全项目套件。

- SurvivorElementVisualTests红10/10因未挂正式效果失败，red.json。最终16/16，visual-green.json，声明最多20项；含三种身体与两种粒子、重复同步、零步长、全部材质槽真实闪红、闪红中到期／继续、死亡淡出再使用、回收、身体跟随、依赖缺失拒绝及无血条引用时不中断粒子。
- SurvivorElementTests：23/23，rules-green.json，声明最多32项；新增权威状态→隐藏→唤醒→到期投影两项，其余玩法不改。
- Game.Tests.GameplayResourceDistributionTests.SurvivorElementDependenciesAreCollectedWithEnemyPrefabs：1/1，collection-green.json，声明最多1项；真实BeginCollect(DefaultPackage,false,false)，无构建。source-collection.txt记录三个实际请求地址、bundle名、arcade标签、共用Prefab／两材质／两纹理／URP ParticlesUnlit.shader的真实DependAssets。
- SurvivorGroundEntryTests：14/14，ground-green.json，最多20项。
- ArcadeEnemyFlameTests：初7项中6过1失败，flame-initial.json；旧夹具specialKind=1已在HEAD改为正式载桶，不再进入RoundEnemyVisual，因此没有它声称验证的专属轮廓火焰。仅将该夹具改specialKind=2，保留专属轮廓生成、火焰击杀和释放检查，完整方法DedicatedEnemyFlameDeathUsesSameTemplateAndReleasesWithMap 1/1，flame-fix.json。未修改旧喷火生产逻辑；声明类最多16项。
- 61个不同限定检查均有通过证据，复验方法不重复计数。没有把旧夹具初次失败写成单次7/7通过。
- 独立审查及两次补测复审无重要阻断；曾指出仅检查首槽和模拟hitFlash布尔的覆盖空洞，现用真实PlayHitFeedback→TickCombatFeedback→TickElementFeedback链路，核对全部槽及死亡淡出／复用。
- 自查源码diff时清除了一条未提交的批量编辑误插入：SetHealthDisplay空血条分支不应重置元素；新增针对性检查通过。
- 独立索引的Game运行C#使用Unity已有响应及Roslyn编译，退出0，scoped-compile.log；未触发Unity资源打包或Player Build。

## 真实加载与视觉证据

正常Boot进入Play，由当前Game.YooAssetResourceLoader异步加载现有地址：Prefabs/Characters/ArcadeZombieShambler、ArcadeZombieGaunt、ArcadeZombieBloated。最终三项均验证组件、身体挂接、序列化粒子／材质／纹理与Shader支持，三个异步句柄均释放（IsValid=false）。runtime-load.txt与RuntimeLoad.cs.txt记录实际结果与检查脚本。

运行环境明确为EditorSimulateMode／DefaultPackage。未调用直接模拟构建或打包入口，未开始测试对局、购买或改写玩家数据；退出后Boot编辑态干净。此证据只说明本机正常编辑器Play的真实加载器链路，不代表发布包／设备或下载服务器验证。

[正式Prefab独立动态回读](formal-elements.gif)与[静态缩略](formal-still.png)：使用实际正式丧尸依赖及组件，在独立临时场景与正式主角同框采样；不是游戏对局实录。FormalReadback.cs.txt手工蒙皮代理仅解决编辑态缓存问题，颜色和粒子读取正式资源。6个时间点与已采用预览的全画面每通道平均差最大约0.542/255，visual-comparison.txt；可见组成与大小保持。

验证边界：没有整局人工试玩、没有发布包或设备验证，没有Player Build。源收集结果、编辑器Play加载和正式Prefab回读分别记录，未用AssetDatabase替代真实加载链宣称发布可用。
