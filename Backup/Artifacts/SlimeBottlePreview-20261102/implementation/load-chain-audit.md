# 粘液瓶资源链只读审计（2026-11-02）

本报告只读检查代码/配置；未调用 Unity、未执行测试、未修改正式文件。正式修改与 Unity 验证由 implement_slime_and_cleanup 独占执行。遵循 resource-load-check。

## 最小接入路径

- ArcadeCombatViewController.Content.cs:39 请求唯一公共视觉地址 `Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet`；对应 `Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab`。不要为瓶子/绿池另建加载器或逐个动态请求地址。
- 将正式粘液模板作为该 prefab 的 `Content` 子节点序列化依赖，与现有 `Content/SurvivorMolotov` 同级。建议 `Content/SurvivorSlimeBottle/{Bottle,SlimeZone}`，名称由实施端最终确定。嵌套 Prefab、材质、贴图通过主资源依赖收集，不能把层级路径当成 YooAsset 地址。
- 在 Content 代次/根身份验证通过后，与 BindMolotovVisuals 同位置绑定粘液瓶模板。模板完整性应在入场准备阶段验证，缺子节点/无效组件直接沿 Content catch 失败回滚，不能到首投才无声消失。
- Content catch 与 ReleaseContent 都清理粘液活动实例、闲置池、模板引用；先销毁实例再 ReleaseObstacleVisualLoad(contentLoad)。保持 Released 幂等标志、generation 与 expectedRoot 保护。
- 若增加粘液 readiness/error 端口，SurvivorSceneFlowSystem 的入场等待和 SurvivorSystem 的 ConfirmBattlePrepared、失效检查、Tick 门槛须一起覆盖，避免首投发生在模板未就绪时。可以合并到既有投掷物就绪门槛，不需要新准备事务。
- 规则层保持独立 SlimeBottle 武器、Throwable 槽互斥。Config/DataCsv/survivor_weapon_level.csv 的 Molotov 现有五级记录仅为参照；粘液配置只能改 Config 权威源并 gen.bat 双端生成。不要把独立武器身份压成 Molotov 的视觉开关。

## 下载、地址与 HUD

- AssetBundleCollectorSetting.asset:553 的公共视觉主资源是逐文件 CollectorType=0，AddressByGameRelativePathNoExtension，PackSeparately，CollectAll，tag=arcade，GUID=eabe57b37084e214f9602f53724ad16d。
- 地址规则删去 Assets/Game/ 与扩展名，保留 Arcade 子目录。ResourceAddress.SceneObjectPrefab 的文件名入口只能拼 `Prefabs/SceneObjects/`，不适合直接传 `Arcade/...`（文件名入口禁止斜杠）；本链现已用精确常量，沿用即可。
- Boot/Res/ResourceBoot.cs 的 StartupResourceTags 为 startup+arcade；CreateStartupResourceDownloader 使用这两标签。因此公共视觉及其绿池依赖随现有 arcade 启动下载，不必再加 startup。
- 若新增槽位图标，沿用序列化 HUD 图标绑定；实际 UI 请求地址为 `Prefabs/UI/Survivor/SurvivorHud`，源路径 `Assets/Game/Prefabs/UI/Survivor/SurvivorHud.prefab`，通过槽位 Box 的依赖带入新图标。图标本身无需独立地址，除非实际调用改成直接加载（不建议）。
- 当前 Molotov 的瓶/池/图标均为主资源依赖，不是独立运行时地址。新增 SlimeBottle 若遵循本方案，实际新增直接请求地址为零。

## 正式收集验证（不得打包）

1. 仿照 GameplayResourceDistributionTests.CollectAssets 调用 setting.BeginCollect("DefaultPackage", true, false)，枚举 CollectAssets，断言两个精确主地址各唯一、AssetInfo.AssetPath 正确、AssetTags 含 arcade。
2. 依赖证据沿用 SurvivorMolotovPresentationTests.MolotovTemplatesAndIconAreInRealCollectedDependencies 的形式：BeginCollect("DefaultPackage", false, false)，读取每条 CollectAsset 的 DependAssets，核对粘液 Prefab、关键材质/贴图与 HUD 图标的实际 AssetPath。此参数只是收集命令的 SimulateBuild 标志，BeginCollect 源码只返回 CollectResult，不启动构建；不要调用任何构建、清单生成、发布入口。
3. 主资源及其依赖证据分别记录；只通过 AssetDatabase.GetDependencies 不足以证明进入真实收集结果。检查素材/meta/GUID 与真实依赖路径一致。
4. 将报告写本批 implementation 目录。源收集通过只能证明收集链；运行清单/真加载器与旧发布包是否含新依赖要分别说明。不能用替身 ResourceLoader 测试或直接实例化预览宣称运行/发布验证完成。

## 释放、失败与晚回调清单

- 主加载返回 null、缺粘液模板或组件：contentLoadFailed=true；双方投掷物模板/池清空；原场地恢复；唯一 contentLoad 句柄释放一次；入口报告错误并清现场。
- 加载未完成离场：ReleaseContent 先清实例、递增 generation、释放句柄。旧 completion 回来后 state.Released/generation/root 身份任一不符必须直接退出，不能绑定模板或在新场景创建绿池。
- 同对象进入第二局后第一局 callback 到达：旧回调不能覆盖新 contentTemplates、不能清第二局实例或误置新 readiness/error。
- 投掷结束回收瓶；3秒绿池权威年龄结束后回收并重置视觉；暂停不靠自主粒子推进。池复用的 scale、颜色、粒子年龄、半透明/溶解状态不得沿用上个区域。
- 重复清理幂等；活动池与闲置池都释放；Content 失败发生在绑定其他公共模板之后时也应清理粘液状态。
- 不为本任务复制另一套句柄/预加载/缓存服务。

## 可用真实有限测试入口（本审计未运行）

已有短类 SurvivorMolotovPresentationTests 共3个测试，验证旧链不能自动证明粘液已覆盖。建议扩展其中依赖测试覆盖新依赖；或使用实施端新建的真实 SlimeBottle 测试类。

可直接指定完整方法：
- Game.Tests.SurvivorMolotovPresentationTests.MolotovTemplatesAndIconAreInRealCollectedDependencies
- Game.Tests.SurvivorMolotovPresentationTests.MolotovPresentationIsBoundedReusableAndManuallySampled
- Game.Tests.SurvivorMolotovPresentationTests.MolotovSlotUsesBoundIconWithReadyRing
- Game.Tests.GameplayResourceDistributionTests.BootDownloadsCoreAndArcadeTogether

每次声明 EditMode、精确短 testClass 或完整 testMethod、最大数量（例如上述前三项限3，Boot单项限1），includePassingTests=true。不能运行整个 GameplayResourceDistributionTests 类来扩大范围，更不能空过滤回退全套。
