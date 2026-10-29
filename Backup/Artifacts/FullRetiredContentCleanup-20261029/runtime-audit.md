# 退役运行时代码与旧工具清理

2026-10-29，按用户“那就全部清理掉吧”执行，根代理统一操作 Unity、验证、提交推送。此记录仅是子任务静态审计，不声称 Unity 编译或测试通过。

## 脚本与资源依赖

删除前已通知根代理，根代理在 Unity 暂停自动刷新期间协调清理绑定资源，避免中间状态导入 Missing Script。

| 脚本 | 原序列化引用 | 处理 |
| --- | --- | --- |
| NpcView | GrayboxNpc、BaseNpcWarehouseKeeper/Merchant/MapGuide/Crafter/Arcade | 退役资源移除后删除 |
| CivilisationPointView | CivilisationValleyGenerationKit、BaseMap | 退役资源移除后删除 |
| TaskExitView | SceneObjects/TaskExit | 退役资源移除后删除 |
| BaseWeatherView | BaseMap | 退役资源移除后删除 |
| TemporaryBuildingView | 14 个旧 Building Prefab | 移除相机建造预览专属分支及旧测试，再删除 |
| TemporaryManualAttackView | GrayboxArrowTower | 退役资源移除后删除 |
| ValleyGenerationKit | CivilisationValleyGenerationKit | 退役资源移除后删除 |
| CampfireVisualEffect | GrayboxCampfire、Camp_RPGFXCampfire、HandheldTorch、HandheldTorchPickup | 与旧建筑/火把一并退役；未被街机氛围或火焰规则引用 |
| HandheldTorchView | HandheldTorch | 清除 Presenter、第一人称火把粒子分支后删除 |
| MapSkyboxView / MapProceduralSkyView | 何家村预览 / BaseMap | 清除 DayNightView 旧 orbit 注入分支后删除，保留 Main 正午光照数值 |
| PlayerInteractableView | 仅旧 NPC 与 Civilisation 子类 | 子类退役后删除 |
| Editor/MapTerrain 的三个 ScriptableObject | 何家村 MapTerrainSources 配置资产 | 旧地图源资产清理后删除，同时删 HeJiaVillageAnnotationFrame |

MapNearbyCapture、MapImageMemory、MapDisplayProjection 只在彼此之间引用，没有现役调用；ValleyVisibilityCtrl 没有调用者，整组删除。TerrainRenderingCompatibility 仍由 MapSceneSystem 持有，代码与测试保留。

## 现役代码收口

- 保留正式移动、鼠标射击、右摇杆、移动端镜头、第一人称退出 X。删除没有调用者的 H/1/2/E/Shift/B/Z/R 生存输入；不得删除 CancelPressedThisFrame，第一人称退出正在使用。
- ResourcePickupView 保留触发自动拾取及全部街机掉落展示，删除无人消费的按键交互接口、事件；FirstPersonInteractionTargeting、IPlayerInteractable/IPlayerSearchable 随之删除。
- EnemyView 删除空 Interact、固定 false CanInteract、旧 [E] DisplayName 与只写 isFocused；经根代理确认，另删除 corpseSearchTrigger/corpsePromptRoot/corpseProgressFill/corpseLootBoxObject 四个旧搜尸序列化字段及隐藏、朝向分支；根代理同步剥离对应隐藏节点，保留 combatCollider 和 actionProgressView。
- 删除 ResourceAddress 两种临时建筑类别及分支；显式保留后续类别原有枚举数值，避免数值漂移。
- CharacterWeaponCatalog 以及怪物 BruteWeaponCatalog、SoldierWeaponCatalog 三份目录均删除刀/斧/镐、汉代斧/镐/刀/弓及火把 8 项，保留敌人短剑/弓/巨剑与现役枪。AppearanceGallery 姿态调试实现保持。
- PlayerViewCtrl 删除唯一 ToggleTorch 消费者，已通知配置代理可清除旧物品枚举及字段。Guardian 火焰 BurnSeconds 属现役 Boss 规则，保留。

## 旧工具与测试

删除 Tools/MapTerrainData/HeJiaVillage 旧工具及输入数据、Tools/Art/HanItems 与 HanCart 旧生成工具、ShoulderAimPreview、SingleMeleeSlashAuthoring/Preview。文件清单见 runtime-removed.json。删除 SingleMeleeAnimationTests、ValleySkyTests；去掉 AppearanceGallerySceneTests 的旧小刀展示测试及 TopDownOcclusionTests 的旧建造预览测试。

ArcadeMeleePresentationTests、GunLocomotionAnimationTests 的真实表现用例改用保留的短剑/巨剑；保留 ArcadeOpeningFlowTests、OnlineProjection 及服务端 SkullPowerOnlineChecks 中旧武器 ID 的旧局回退检查。CharacterPresentationTests 使用真实现役 enemy.arcade.basic。ArcadeLootEffectsTests 将无人使用的按键交互断言改为渐隐时触发拾取碰撞仍启用。ConfirmedWeaponPresentationTests 同时断言 8 个退役目录项已消失。

## 静态检查与建议限定验证

已执行代码引用回查与 git diff --check，未发现残留已删类型的 C# 引用或空白错误。未调用 Unity MCP，未运行测试，未生成配置，未提交推送。

以下均为实际存在的短 testClass，可由根代理逐类执行 EditMode，includePassingTests=true，禁止无效过滤回退。数量按当前源码 [Test]/[TestCase] 展开统计；如与 Unity 实际枚举不符，应停止核对，不扩大筛选：

| testClass | 最大数量 |
| --- | ---: |
| TopDownOcclusionTests | 10 |
| CharacterPresentationTests | 8 |
| ArcadeMeleePresentationTests | 13 |
| GunLocomotionAnimationTests | 17 |
| ArcadeLootEffectsTests | 7 |
| DynamicResourceAddressTests | 27 |
| AppearanceGallerySceneTests | 26 |

另外建议执行真实完整方法 Game.Tests.ConfirmedWeaponPresentationTests.UnusedMeleeConfigsAreRemovedWhileEnemyWeaponsRemainValid，最大数量 1；旧局兼容 Game.Tests.ArcadeOpeningFlowTests.LegacyMeleeEquipment_UsesBaselineGun，最大数量 5。所有 Unity 结果由根代理的后续验证记录覆盖此处未执行状态。

补查两份怪物目录的作者工具均通过 Instantiate(original)/EditorUtility.CopySerialized(original, catalog) 从正式主目录派生，源目录清理后不会再生成退役项；静态逐项文本比较确认三份目录只去掉8个条目，其余姿态内容保持完全相同。配置代理删掉 MapDisplay 空契约后，MapUiImage 只剩自身定义，已移除对应资源类别与地址映射。
