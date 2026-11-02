# 粘液瓶与强化清理实施验证

2026-11-02。正式实施者，不提交、不推送、不操作默认index。授权来自主代理转交用户“可以”，采用 SlimeBottlePreview.gif 与 Preview.cs.txt。使用 continuing-confirmed-work、code-role、luban-config、resource-load-check、unity-ui、TDD、verification-before-completion。

## 已实现

- 移速卡以真实基础移速和原加成公式显示米/秒，初级6.5→7.02，成长规则不变。
- 中文Molotov改燃烧瓶；删除六行Weight0升级与步枪2–5，初始步枪1保留。
- 15个数字旧文案键源删除并加入退休清单，现役标题用Kind稳定键；退休清单实际总548。通用步枪弹头文案保留。移除无人调用GetUpgradeName。Config/gen.bat双端生成成功，Unity正式生成器重生TextsGenerated.cs。
- SlimeBottle五级，5秒投掷，0伤害，区域3秒；半径1.2/1.425/1.65/1.875/2.1，减速30/35/40/45/50%；离开立即恢复，区域和冰霜取强。Throwable互斥与RifleTip独立，候选、选择、刷新沿同一CanChooseRoute门槛。
- 沿既有Content异步事务、地图根、代次防护和有限池实现正式瓶体与绿池；暂停没有自主Update/粒子时间。停止/重局回收领域状态，Content失败或地图释放清空表现及模板。HUD复用瓶子图标并绿色tint、复用投掷冷却环（主代理明确授权）。
- 正式视觉沿已采用预览：约30cm绿瓶、原网格和12颗液滴、0.16秒摊开、同抛物线。正式prefab隔离复拍formal/SlimeBottleFormal.gif，125帧20fps，正式主角同框；演示运动仍是隔离预览，非整局战斗录制。

## 验证证据与边界

起始连接项目Client/Assets，原处Play Main，停止后Boot干净。首次限定runner在Play发生Unity TestRunner异常后timeout及运行锁；新配置真实NUnit反射RED为Expected17/But22，新规则方法缺失RED。反射阶段执行真实NUnit方法，单独记录runtime-nunit.txt、regression-nunit.txt，不冒充runner。运行锁自然恢复后正式runner全部使用EditMode、非空完整testMethod或短testClass、includePassingTests=true；无全集/无无效过滤回退。

正式runner唯一验证范围：新配置1例；新增runtime粘液/速度/暂停5例；燃烧瓶旧行为2例；配置清理旧方法3例；退役与无人机旧方法2例；SurvivorUiTests16例；SurvivorMolotovPresentationTests4例。共33个唯一例全部通过。方法/类原始结果见同目录json（部分早期单例结果仅工具会话输出，后续报告列明，不补造JSON）。最后表现类复验4/4。

资源源验证：BeginCollect(DefaultPackage,true,false)精确地址唯一且arcade标签，real-address.txt；false,false依赖包含正式Slime prefab、3mat、mesh，real-collection.txt。Slime嵌在Content/SurvivorSlimeBottle，无新增直接地址。Boot下载startup+arcade，既有Content异步加载保留代次/Released/root护栏。

运行环境验证：正常Boot Play启动，真实Game.YooAssetResourceLoader加载Content与SurvivorHud，检查Content内正式SlimeBottle瓶子、有效12滴区域、材质网格。boot-play-real-load.txt。不打包、不重建发布清单，不声称既有发布包或用户安装包已更新。未做完整幸存者局长期游玩。

最终Boot编辑态，dirty=false，playing=false；Console errors0、warnings18：15条既存测试代码编译警告（重复using、未使用变量、过时API等），3条MCP日志存储/连接警告，详情console-final.txt。仅检查本次路径git diff --check通过。客户端/服务端两张幸存者生成bytes逐字节一致；15退休键在zh-CN和en-US均不存在，稳定键存在。

## 实施文件清单（仅本次范围，勿夹带其他聊天）

Config/DataCsv/survivor_upgrade.csv；survivor_weapon_level.csv；Config/Localization/survivor.json；retired-keys.json；Config/README.md。

Client/Assets/Game/Scripts/Utility/Arcade/SurvivorConfiguration.cs；System/Arcade/SurvivorSystem.cs、SurvivorSystem.Elements.cs、新增SurvivorSystem.SlimeBottle.cs(+meta)；Controller/Arcade/ArcadeCombatViewController.cs、.Content.cs、.Molotov.cs、新增.SlimeBottle.cs(+meta)、SurvivorSlimeZoneVisual.cs(+meta)；Controller/UICtrl/Survivor/SurvivorUpgradeBoxCtrl.cs、SurvivorHudUICtrl.cs。

Client/Assets/Game/Art/Arcade/VFX/SurvivorSlimeBottle/及目录meta（prefab、SlimePool.asset、BottleGreen.mat、BottleCap.mat、Slime.mat和各meta）；Client/Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab。

Client/Assets/Tests/EditMode/SurvivorConfigTests.cs、SurvivorRuntimeTests.cs、SurvivorUiTests.cs、SurvivorMolotovPresentationTests.cs。

生成输出：Client/Assets/Game/Config/tbsurvivorupgrade.bytes、tbsurvivorweaponlevel.bytes；Server/GameServer/Config/Luban/同名bytes；Client/Assets/Game/Config/Localization/zh-CN.json、en-US.json；正式生成器输出TextsGenerated.cs（沿实际路径发现，不能手改生成）。无schema变更，生成C#模型本次不应有结构差异。

## 私有index注意

默认index既存D+??同文件，勿git add -A、勿reset用户index。按本清单审查工作树相对HEAD的实际内容，只将本次变更放入私有index；存在其他聊天同期修改（enemy、horde、special、settings-visuals、schema、协议等），不能随gen.bat全目录纳入本次提交。主代理负责最终清单、审查、中文提交与origin推送。

补充运行验证边界：本次Boot检查使用真实ResourceLoader.LoadAsset同步入口（内部YooAsset LoadAssetSync），调用者没有取得可释放句柄；检查后退出Play。它证明资源能加载，不作为生产Content异步事务句柄释放的动态验证；异步释放/失败/晚回调路径由独立源码审查核对，未改原句柄所有权。
