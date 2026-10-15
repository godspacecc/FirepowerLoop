using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class ConfigLocalizationTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new LocalizationResourceLoader());
            LocalizationLanguages.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
        }

        [Test]
        public void ItemConfig_GeneratesLocalizationKeysAndBinaryData()
        {
            var item = LoadGeneratedItem();

            Assert.That(typeof(global::Item).GetField("NameKey"), Is.Not.Null);
            Assert.That(typeof(global::Item).GetField("DescKey"), Is.Not.Null);
            Assert.That(item.Id, Is.EqualTo(ItemIds.MetalParts));
            Assert.That(item.NameKey, Is.EqualTo("item.metal_parts.name"));
            Assert.That(item.DescKey, Is.EqualTo("item.metal_parts.desc"));
        }

        [Test]
        public void ConfigLocalization_UsesCurrentLanguageWithoutCaching()
        {
            var item = LoadGeneratedItem();

            Assert.That(ConfigLocalization.GetItemName(item), Is.EqualTo("金属零件"));
            Assert.That(ConfigLocalization.GetItemDesc(item), Is.EqualTo("常见的金属制作材料"));

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });

            Assert.That(ConfigLocalization.GetItemName(item), Is.EqualTo("Metal Parts"));
            Assert.That(ConfigLocalization.GetItemDesc(item), Is.EqualTo("Common metal crafting material."));
        }

        [Test]
        public void ConfigLocalization_FallsBackToLegacyTextThenFallbackId()
        {
            var missingKeys = CreateItem(
                1001,
                "Legacy Name",
                "Legacy Desc",
                "missing.item.name",
                "missing.item.desc");
            var blankName = CreateItem(1002, "", "", "", "");

            Assert.That(ConfigLocalization.GetItemName(missingKeys, "fallback-id"), Is.EqualTo("Legacy Name"));
            Assert.That(ConfigLocalization.GetItemDesc(missingKeys), Is.EqualTo("Legacy Desc"));
            Assert.That(ConfigLocalization.GetItemName(blankName, "fallback-id"), Is.EqualTo("fallback-id"));
            Assert.That(ConfigLocalization.GetItemDesc(blankName), Is.EqualTo(string.Empty));
            Assert.That(ConfigLocalization.GetItemName(missingKeys), Is.Not.EqualTo("missing.item.name"));
        }

        [Test]
        public void LanguageTables_ContainItemKeysInEveryLanguage()
        {
            var zh = File.ReadAllText(Path.Combine(ProjectRoot, "Assets/Game/Config/Localization/zh-CN.json"));
            var en = File.ReadAllText(Path.Combine(ProjectRoot, "Assets/Game/Config/Localization/en-US.json"));

            Assert.That(zh, Does.Contain("\"item.metal_parts.name\""));
            Assert.That(zh, Does.Contain("\"item.metal_parts.desc\""));
            Assert.That(en, Does.Contain("\"item.metal_parts.name\""));
            Assert.That(en, Does.Contain("\"item.metal_parts.desc\""));
            Assert.That(zh, Does.Contain("\"ui.inventory.use_failed_water\""));
            Assert.That(en, Does.Contain("\"ui.inventory.use_failed_water\""));

            var zhWaterFailure = Loc.T("ui.inventory.use_failed_water");
            Assert.That(zhWaterFailure, Is.EqualTo("水分已满或当前状态无法使用瓶装水"));
            Assert.That(zhWaterFailure, Is.Not.EqualTo("ui.inventory.use_failed_water"));

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            var enWaterFailure = Loc.T("ui.inventory.use_failed_water");
            Assert.That(enWaterFailure, Is.EqualTo("Water is full or bottled water cannot be used in the current state"));
            Assert.That(enWaterFailure, Is.Not.EqualTo("ui.inventory.use_failed_water"));
        }

        [Test]
        public void LubanGenerationBoundary_UsesSingleEntryAndKeepsClientServerTablesSeparate()
        {
            var repoRoot = Path.GetFullPath(Path.Combine(ProjectRoot, ".."));
            Assert.That(File.Exists(Path.Combine(repoRoot, "Config/gen.bat")), Is.True);
            Assert.That(File.Exists(Path.Combine(repoRoot, "Config/gen_anotherday.bat")), Is.False);

            var clientTableNames = typeof(Tables)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .Where(name => name.StartsWith("Tb"))
                .OrderBy(name => name)
                .ToArray();

            Assert.That(clientTableNames, Is.EquivalentTo(new[]
            {
                "TbEnemy",
                "TbExtraction",
                "TbCraftRecipe",
                "TbItem",
                "TbLoot",
                "TbMap",
                "TbMapScenePart",
                "TbMapSpawn",
                "TbNpc",
                "TbPlayer",
                "TbProjectile",
                "TbTempBuilding",
                "TbTempBuildingManualAttack",
                "TbTestCmdCategory",
                "TbTestCmdEntry",
                "TbWeapon",
                "TbWeaponAppearance"
            }));

            var configAssembly = typeof(Tables).Assembly;
            Assert.That(configAssembly.GetType("StartMachineConfig"), Is.Null);
            Assert.That(configAssembly.GetType("StartProcessConfig"), Is.Null);
            Assert.That(configAssembly.GetType("StartSceneConfig"), Is.Null);
            Assert.That(configAssembly.GetType("StartZoneConfig"), Is.Null);
            Assert.That(configAssembly.GetType("AIConfig"), Is.Null);
            Assert.That(configAssembly.GetType("UnitConfig"), Is.Null);

            var serverBytes = Directory.GetFiles(
                    Path.Combine(repoRoot, "Server/GameServer/Config/Luban"),
                    "*.bytes")
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToArray();

            Assert.That(serverBytes, Is.EquivalentTo(new[]
            {
                "tbaiconfig.bytes",
                "tbenemy.bytes",
                "tbmapspawn.bytes",
                "tbplayer.bytes",
                "tbstartmachine.bytes",
                "tbstartprocess.bytes",
                "tbstartscene.bytes",
                "tbstartzone.bytes",
                "tbunitconfig.bytes",
                "tbweapon.bytes"
            }));

            var serverConfigRoot = Path.Combine(
                repoRoot,
                "Server/GameServer/Runtime/Model/Generated/Config");

            var serverGeneratedCs = Directory.GetFiles(serverConfigRoot, "*.cs")
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToArray();

            Assert.That(serverGeneratedCs, Is.EquivalentTo(new[]
            {
                "AIConfig.cs",
                "EnemyAttackKind.cs",
                "EnemyConfig.cs",
                "MapSpawnConfig.cs",
                "MapSpawnObjectType.cs",
                "PlayerConfig.cs",
                "StartMachineConfig.cs",
                "StartProcessConfig.cs",
                "StartSceneConfig.cs",
                "StartZoneConfig.cs",
                "Tables.cs",
                "TbAIConfig.cs",
                "TbEnemy.cs",
                "TbMapSpawn.cs",
                "TbPlayer.cs",
                "TbStartMachine.cs",
                "TbStartProcess.cs",
                "TbStartScene.cs",
                "TbStartZone.cs",
                "TbUnitConfig.cs",
                "TbWeapon.cs",
                "UnitConfig.cs",
                "WeaponConfig.cs",
                "WeaponKind.cs"
            }));

            var serverRuntimeConfigRoot = Path.Combine(
                repoRoot,
                "Server/GameServer/Runtime/Model/Server/Config");
            Assert.That(File.Exists(Path.Combine(serverRuntimeConfigRoot, "ServerConfig.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(serverRuntimeConfigRoot, "StartConfigRuntime.cs")), Is.True);

            var serverConfigText = File.ReadAllText(Path.Combine(serverRuntimeConfigRoot, "ServerConfig.cs"));
            Assert.That(serverConfigText, Does.Not.Contain("StartConfig/"));
        }

        [Test]
        public void PlayerConfig_ExposesGeneratedAndCompatibilityUnarmedRules()
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(
                ProjectRoot,
                "Assets/Game/Config",
                $"{name}.bytes"))));
            global::PlayerConfig generated = tables.TbPlayer.Get("player.graybox.default");

            Assert.That(generated.UnarmedAttackDamage, Is.EqualTo(3));
            Assert.That(generated.UnarmedAttackRange, Is.EqualTo(1.4f));
            Assert.That(generated.UnarmedAttackCooldownSeconds, Is.EqualTo(0.6f));
            Assert.That(generated.UnarmedMeleeArcDegrees, Is.EqualTo(75f));

            var compatible = new PlayerConfigDefinition(
                "player.test", string.Empty, 100, 9, 2f, 0.2f, 3f, 180f, 5f);
            Assert.That(compatible.UnarmedAttackDamage, Is.EqualTo(3));
            Assert.That(compatible.UnarmedAttackRange, Is.EqualTo(1.4f));
            Assert.That(compatible.UnarmedAttackCooldownSeconds, Is.EqualTo(0.6f));
            Assert.That(compatible.UnarmedMeleeArcDegrees, Is.EqualTo(75f));
        }

        private static global::Item LoadGeneratedItem()
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(
                ProjectRoot,
                "Assets/Game/Config",
                $"{name}.bytes"))));
            return tables.TbItem.Get(ItemIds.MetalParts);
        }

        private static global::Item CreateItem(
            int id,
            string name,
            string desc,
            string nameKey,
            string descKey)
        {
            var byteBuf = new ByteBuf();
            byteBuf.WriteInt(id);
            byteBuf.WriteString(name);
            byteBuf.WriteString(desc);
            byteBuf.WriteString(nameKey);
            byteBuf.WriteString(descKey);
            byteBuf.WriteString(string.Empty);
            byteBuf.WriteInt((int)ItemType.Material);
            byteBuf.WriteInt(20);
            byteBuf.WriteInt((int)ItemUseEffect.None);
            byteBuf.WriteInt(0);
            byteBuf.WriteFloat(0f);
            return new global::Item(byteBuf);
        }

        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static void ResetArchitecture()
        {
            var field = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            field?.SetValue(null, null);
        }

        private sealed class LocalizationResourceLoader : IResourceLoader
        {
            private readonly Dictionary<string, TextAsset> assets = new Dictionary<string, TextAsset>
            {
                [LocalizationLanguages.LanguagesAddress] = new TextAsset(
                    "{\"default\":\"zh-CN\",\"codeComment\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}"),
                ["zh-CN.json"] = new TextAsset(
                    "{\"item.metal_parts.name\":\"金属零件\",\"item.metal_parts.desc\":\"常见的金属制作材料\",\"ui.inventory.use_failed_water\":\"水分已满或当前状态无法使用瓶装水\"}"),
                ["en-US.json"] = new TextAsset(
                    "{\"item.metal_parts.name\":\"Metal Parts\",\"item.metal_parts.desc\":\"Common metal crafting material.\",\"ui.inventory.use_failed_water\":\"Water is full or bottled water cannot be used in the current state\"}")
            };

            public T LoadAsset<T>(string path) where T : Object
            {
                return assets.TryGetValue(path, out var asset) ? asset as T : null;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded) where T : Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}
