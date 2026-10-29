# 配置清理子任务记录

本记录只声明本代理执行的静态审计与源码修改。没有调用Unity MCP，没有运行Unity测试，没有调用Config/gen.bat，没有提交/推送。生成、Editor复验、资源GUID审查由主执行者统一执行。

## 权威源差异

- `item.csv` 原第3–6、10–11、13–15、26–34、44行删除：ID 1/2/3/4/8/9/16/17/18/29/30/31/32/33/34/35/36/37/48，共19项。
- `weapon.csv`、`weapon_appearance.csv` 删除16/17/18/32/33/34/35，共7项。保留5/6/15及19–25现役武器与目录契约；7/26/27/28为其弹药依赖。
- `loot.csv` 删除上述19个Item对应掉落；新增`loot.arcade.drop → TestResourcePickup → Item49 → Amount1`。49仅承担现役街机地面表现，不恢复金属、材料采集或背包玩法。现有通用ItemType.Material用于适配，不代表有材料获得入口。
- `enemy.csv` 删除graybox.basic/archer、han.melee/archer四行；七个现役enemy.arcade行清空LootConfigId、GuaranteedWeaponLootConfigId、GuaranteedAmmoLootConfigId。伤害/血量/间隔/武器身份/投射物/弹匣和禁紫资格不改。
- `arcade_weapon.csv` 未修改：敌人短剑、弓、巨剑、爪身份全部保留。玩家`player.graybox.default`、GrayboxPlayer正式资源、正式轮次与出生配置不改。
- `Config/Localization/arcade-drop-carrier.json` 为新增49的权威中英文文本，gen.bat增加发布入口，不手改客户端语言JSON。
- 退役`WeaponConfig.TreeHarvestEfficiency/StoneHarvestEfficiency`、`Item.BurnSeconds`、`ItemActionKind.ToggleTorch`和`ItemUseEffect.RestorePower/RestoreWater`从Schema及对应CSV/手写Definition/Provider同步移除。Guardian挑战的BurnSeconds独立字段保留。

## 手写边界

- `ArcadeRunDefaults.DropPresentationLootConfigId` 改为明确的街机表现身份，避免为了现役掉落保留旧metal-parts物品。
- Provider对GuaranteedAmmoLootConfigId改为可选，非空时仍校验有效引用；真正战斗使用的ProjectileConfigId、AmmoCapacity、MagazineCapacity、ReloadSeconds照常校验。
- ConfigLocalizationTests使用现役弓而非旧金属材料作语言测试，补全当前Item二进制字段；清除瓶水文案断言，保留语言切换、Fallback和生成边界意图。更新陈旧26/20张表清单为当前35/30张目标，新增退役配置缺失/街机承载/Provider有效性回归。

## 资源协作映射

需由主执行者按GUID反向依赖审查的旧图标名：IconMetalParts、IconCloth、IconMedkit、IconRation、IconBottledWater、icon_wood、icon_food_mushroom、IconDriedFood、icon_stone、IconHanForgedAxe、IconHanForgedPick、IconHanSteelBlade、IconHanBow、IconHanGrainPouch、IconHanWaterPot、IconHandheldTorch、icon_equip_knife、icon_equip_axe、icon_equip_pickaxe。

对应旧Prefab名：KnifeShared、AxeShared、PickaxeShared、HanForgedAxe、HanForgedPick、HanSteelBlade、HanBow、HanForgedAxePickup、HanForgedPickPickup、HanSteelBladePickup、HanBowPickup、HanGrainPouchPickup、HanWaterPotPickup、HandheldTorchPickup。代码代理负责CharacterWeaponCatalog删除刀/斧/镐/汉代/火把目录项。

保留TestResourcePickup现役通用街机承载、GrayboxShortSword/GrayboxBow/GreatswordShared、Arcade七枪Shared、五种现役投射物、所有现役角色及衣物；不因名称graybox直接删除。

## 验证入口

- 根执行者运行`Config/gen.bat`双端生成，检查客户端/服务端目标数及Provider真实生成配置校验。
- Unity EditMode真实短class：`ConfigLocalizationTests`，当前7项，上限7，includePassingTests=true。
- 配合代码/资源代理修改，建议根执行者覆盖`ArcadeEnemyVisualTests`、`ArcadeMeleePresentationTests`（由其报告最终数量），以及旧局回退定向测试。
- 空map_display源表与MapDisplayConfig/TbMapDisplay及MapDisplayDefinition/TryGetMapDisplay整条零消费者契约已删除。MapScenePart现役多场景加载契约保留。Config/README.md当前表数更新为35/30，共41源表，历史验证段不改。
- `config-schema-audit.json`覆盖41源表的XML/JSON解析、CSV字段集合/列数及数据行宽检查，错误0；`removed-config-items.csv`提供原始行号、ID、名称、图标映射。
- `config-static-audit.json`记录已执行的CSV列宽与直接ID引用静态核查：24 Item、10 Weapon、10 Appearance、24 Loot、7 Enemy、5 Projectile，错误0。不能将其称为生成或运行时通过。

完整方法清单（同一class，共7项，可整class限定运行，最大7）：

1. Game.Tests.ConfigLocalizationTests.ItemConfig_GeneratesLocalizationKeysAndBinaryData
2. Game.Tests.ConfigLocalizationTests.ConfigLocalization_UsesCurrentLanguageWithoutCaching
3. Game.Tests.ConfigLocalizationTests.ConfigLocalization_FallsBackToLegacyTextThenFallbackId
4. Game.Tests.ConfigLocalizationTests.LanguageTables_ContainItemKeysInEveryLanguage
5. Game.Tests.ConfigLocalizationTests.LubanGenerationBoundary_UsesSingleEntryAndKeepsClientServerTablesSeparate
6. Game.Tests.ConfigLocalizationTests.GeneratedCatalog_ExcludesRetiredItemsAndKeepsArcadePresentationCarrier
7. Game.Tests.ConfigLocalizationTests.PlayerConfig_ExposesGeneratedAndCompatibilityUnarmedRules

## 追加：退役文案权威收口

根确认已删除旧MobileJoystick/PocketSlotBox及Pause隐藏BtnReturnCamp后，先前两个序列化例外也纳入清理，最终权威清单`Config/Localization/retired-keys.json`为525个唯一键。基线两语言各1413键，集合一致；按清单发布后预期各888键（若本批没有其他新增）。不手改客户端语言表或TextsGenerated。

`publish-localization.ps1`新增RetiredKeysSource参数，默认读取此清单，拒绝空/重复键及与增补源冲突；只删除明确行，保留其余键顺序、格式和换行。删除尾键后去除悬空逗号，支持清空表后新增键；两语言输入及输出全部解析通过后才写入，相同内容不覆盖。仍由Config/gen.bat现有调用统一执行。Config/README补充权威及正式Texts生成入口。根执行者随后需调用`Game.LocalizationTextGenerator.GenerateTexts()`，不能手改自动生成成员。

只执行静态检查：publisher PowerShell语法解析0错误、清单525键且无重复、与所有Config增补源冲突0。未执行发布脚本、Unity验证或测试。之前报告7方法为追加前状态，现`ConfigLocalizationTests`总9项，上限9、includePassingTests=true，新增完整方法：

8. Game.Tests.ConfigLocalizationTests.RetiredLocalizationManifest_RemovesOnlyRetiredKeysFromPublishedTablesAndTexts
9. Game.Tests.ConfigLocalizationTests.LocalizationPublisher_PrunesFirstAdjacentAndLastKeysWithoutReorderingAndIsIdempotent

另清除TestCmdTests/TestCmdUiTests中已退役物品提供者及无实际调用的异常注入，删除UiLocalizationOwnershipTests完全无人实例化的InventoryTestConfigProvider。保留通用诊断菜单夹具与历史测试意图。`TestCmdUsesReadableLabelsResultsAndIgnoresLanguageSwitch`旧克隆物品/清空背包命令本身已退役，改为真实#unitbar on/off检验可读结果和语言切换时选项/历史不变；不再为无用物品保留正式语言键。

建议根额外定向运行完整方法，各上限1、includePassingTests=true：

- Game.Tests.TestCmdUiTests.TestCmdUsesReadableLabelsResultsAndIgnoresLanguageSwitch
- Game.Tests.TestCmdTests.CommandWritesExpectedRuntimeLogSeverity
