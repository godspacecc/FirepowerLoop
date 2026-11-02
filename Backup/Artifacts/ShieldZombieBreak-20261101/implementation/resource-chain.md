# 实际资源链检查（静态）

- 配置 enemy.arcade.zombie.shield 的 PrefabName 仍为 ArcadeZombieShield。
- LubanRuntimeConfigProvider / ResourceAddress.CharacterPrefab 解析为 Prefabs/Characters/ArcadeZombieShield。
- AssetBundleCollectorSetting.asset 包含逐文件收集项 Assets/Game/Prefabs/Characters/ArcadeZombieShield.prefab，arcade 标签沿用。
- SurvivorSceneFlowSystem.EnterAsync 经 ArcadeSceneFlowSystem.EnterArenaMapAsync 准备地图；EnemyCtrl.PrepareArcadeAsync 枚举 TryGetSurvivorEnemyRoster 的所有敌人ID，LoadEnemyPrefabAsync 使用 ResourceLoader.LoadAssetAsync(config.PrefabAddress)，不是AssetDatabase替身。
- 取消回调及卸载经既有 ReleaseHandleOnce，临时/提交缓存沿既有生命周期释放。
- 本次没有新增、修改资源寻址或依赖，不改Prefab层级，表现只控制三个现有Renderer。

待root真实Editor验证：SurvivorSpecialVisualTests.ExactAddressesAndSerializedDependenciesAreCollected，正式BeginCollect地址、标签、材质依赖；不是打包。
运行／发布包：本次尚未验证，不打包、不生成模拟清单、不触发Player Build。
