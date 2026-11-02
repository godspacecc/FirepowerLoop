# 幸存者强化清理与稳定文案绑定审计

2026-11-02；只读源码审计，未调用 Unity，未运行测试、生成、发布、提交。使用 luban-config 技能。此报告是实施建议，不代表正式资源已修改。

## 精确删除

- Config/DataCsv/survivor_upgrade.csv 删除六行（不是隐藏或占位）：survivor.damage / Damage、survivor.fireRate / FireRate、survivor.rifle / Rifle、survivor.drone.pierce / DronePierce、survivor.drone.explosion / DroneExplosion、survivor.drone.split / DroneSplit。当前22行，删除后16行；新增SlimeBottle后17行。
- Config/DataCsv/survivor_weapon_level.csv 删除 survivor.rifle.2、.3、.4、.5；保留 survivor.rifle.1 作为初始步枪。无人机和燃烧瓶各五级保留。
- Config/Localization/survivor.json 删除 ui.survivor.upgrade.1.title、.1.detail、.2.title、.2.detail。
- 同次迁移将现役数字标题 3/4/5/8/9/17/18/19 分别替换为 Health/MoveSpeed/Pickup/Magazine/Reload/RecoveryStill/RecoverySafe/RecoveryAlways。推荐键 ui.survivor.upgrade.<Kind>.title。
- 数字 detail 3/4/5 当前无正式 consumer（UI统一progress/专项detail），可删除。不要删除 recovery Kind detail、pickup_detail、magazine_detail、reload_detail、progress。
- 因此退休旧数字键共15个：1/2/3/4/5各title与detail（10个），8/9/17/18/19各title（5个）。
- 未发现6/13/14/15等无人机停用项专用文案，不杜撰删除项。settings-visuals.json 的通用 Pierce/Explosion/Split 文案仍被步枪使用，必须保留。
- ui.survivor.weapon.Molotov 保留稳定技术键，仅中文值“燃烧弹”改为“燃烧瓶”；新增 SlimeBottle 三个武器名称/首次/升级说明键，以及投掷路线互斥说明键。

## 文案退休机制

Config/Localization/retired-keys.json 是删除发布目标旧键的权威清单。仅从 survivor.json 删源键并不会删除已发布语言。将上述15个旧数字键加入退休清单，同时移出 survivor.json，避免发布脚本拒绝“退休键与增补源重复”。通过 Config/publish-localization.ps1 -Source Config/Localization/survivor.json 发布，脚本会同步删除 Client/Assets/Game/Config/Localization/zh-CN.json 与 en-US.json 的旧键。然后 Unity 菜单 Tools/本地化/生成文本常量（Game.LocalizationTextGenerator.GenerateTexts）重生 TextsGenerated.cs，禁止手改生成常量。Config/README.md 的退休数量也需同步，当前正文写525。

## 稳定绑定最小方案

无需改造整套本局选择int索引，也无需新增schema标题字段。SurvivorSystem新增TryGetUpgradeKind或TryGetUpgradeDefinition只读查询，边界校验与TryGetUpgradeDetails一致；SurvivorUpgradeBoxCtrl绑定标题使用Kind拼键。选择回调和Model.State仍使用当前配置快照内的int；删除行后索引变化合理，但不能把索引用作文案身份。当前没有为此导入历史玩家数据的必要。

正式依赖：SurvivorConfiguration.FromTables按DataList顺序建立快照；SurvivorSystem.cs 的Offer、EligibleUpgradeIds、Choose、Bonus、TryGetUpgradeDetails、TryGetWeaponUpgrade均用id-1；Modifiers.cs、Recovery.cs、Magazine.cs同样以当前快照索引查询。它们不是跨局稳定ID，不必全局迁移，只需消除UI及测试数字文案绑定。

速度说明增加TryGetMoveSpeedUpgradeDetails：当前速度 config.PlayerMoveSpeed * (1 + 当前Bonus)，下一级同公式替换下一Bonus；直接复用实际TickFrame移动公式。显示当前→下一级 米/秒及本次增量；不要显示累计百分比冒充最终速度。保留强化数值规则。

## 最小实施文件集合

清理/文案/速度：两个CSV、Config/Localization/survivor.json、retired-keys.json、Config/README.md、Client/Assets/Game/Scripts/System/Arcade/SurvivorSystem.cs、Controller/UICtrl/Survivor/SurvivorUpgradeBoxCtrl.cs；测试 SurvivorConfigTests.cs、SurvivorRuntimeTests.cs、SurvivorUiTests.cs。

新增粘液功能还需：Utility/Arcade/SurvivorConfiguration.cs（IsWeapon、允许Kind、0伤害只对SlimeBottle允许、区域减速字段验证）、Config/Defines/beans.xml（如增加正式武器减速字段）、System/Arcade/SurvivorSystem.Molotov.cs或新的投掷/粘液partial、SurvivorSystem.Elements.cs（区域与冰霜减速合并）、生命周期Stop/Tick接点、实际视口与表现consumer及对应真实资源验证。具体外观等待另行预览确认，不能提前接入正式视觉资源。

生成只用Config/gen.bat，实际输出：Client/Assets/Game/Scripts/Model/Config、Client/Assets/Game/Config，Server/GameServer/Runtime/Model/Generated/Config、Server/GameServer/Config/Luban。此次纯删除行不应手改任何生成C#；有新增字段时生成相应双端C#和bytes，保留.meta。

## 粘液已确认边界

SlimeBottle五级，5秒投掷、区域3秒，半径1.2/1.425/1.65/1.875/2.1米（当前同级Molotov），0伤害，减速30/35/40/45/50%，离开即恢复；多个区域不叠加。Molotov与SlimeBottle使用共同且独立的投掷ExclusiveGroup，不使用RifleTip；燃烧弹头可以同时选。现有CanChooseRoute已可共享互斥逻辑，需覆盖选择、候选与刷新。冰霜和粘液取较强减速只是建议，尚未确认。

## 已发现测试与精确验证范围

SurvivorConfigTests.UpgradePoolKeepsSupportAndAmmoWithoutMainWeaponScaling目前预期漏Molotov；LoadsIndependentConfigurationBeforeArcadeCatalog仍断言21行，当前源实际22。必须更新为最终17种实际完整集合，并断言六种退役Kind及步枪2-5不存在。ConfigurationAllowsTwoUnlimitedChoicesAndNoRifleUpgrade现会先人工删三行，应改为直接验证真实配置已不含退役项。

SurvivorRuntimeTests.DisabledMainWeaponChoicesAreNotOfferedOrAccepted使用Single寻找将被删除的三个Kind，删除后必失败，改为缺席断言和越界无效请求。DroneRejectsAllProjectileModifiersAndIgnoresLegacyLevels要检查并移除对正式停用配置行存在的依赖；测试本地构造的无效弹头/旧等级可以作为拒绝输入覆盖，不保留正式CSV占位。

SurvivorUiTests.RecoveryDescriptionsFitExistingCardsInBothLanguages硬编码17/18/19；RifleUpgradeTextFitsExistingCard硬编码8/9，都改Kind。新增实际Bind覆盖，确认删行后Health/MoveSpeed/Pickup/弹匣/恢复标题双语不串位、速度为米/秒。

可直接使用真实完整testMethod，EditMode，每个调用maxTests=1，includePassingTests=true（不得失败回退全集）：
- Game.Tests.SurvivorConfigTests.UpgradePoolKeepsSupportAndAmmoWithoutMainWeaponScaling
- Game.Tests.SurvivorConfigTests.LoadsIndependentConfigurationBeforeArcadeCatalog
- Game.Tests.SurvivorConfigTests.ConfigurationAllowsTwoUnlimitedChoicesAndNoRifleUpgrade
- Game.Tests.SurvivorRuntimeTests.DisabledMainWeaponChoicesAreNotOfferedOrAccepted
- Game.Tests.SurvivorRuntimeTests.DroneRejectsAllProjectileModifiersAndIgnoresLegacyLevels
- Game.Tests.SurvivorRuntimeTests.MolotovBurnsForThreeSecondsWithoutSpendingRifleAmmo
- Game.Tests.SurvivorRuntimeTests.MolotovPauseAndStopClearActiveEffects
- Game.Tests.SurvivorUiTests.RecoveryDescriptionsFitExistingCardsInBothLanguages
- Game.Tests.SurvivorUiTests.RifleUpgradeTextFitsExistingCard
- Game.Tests.SurvivorUiTests.PickupDetailsFitExistingCardInBothLanguages

上列总上限10例，均已在当前源码确认存在。新增粘液测试应先有真实方法再调用：五级权威值、区域边界离开恢复、重叠不叠加、零伤害不触发击杀/掉落、暂停/停止/复用清理、Molotov互斥而RifleFire兼容、刷新不绕过互斥；禁止用尚不存在的方法名调用后回退。当前审计没有运行以上任何测试。

