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
            Assert.That(item.Id, Is.EqualTo(6));
            Assert.That(item.NameKey, Is.EqualTo("item.weapon.bow.name"));
            Assert.That(item.DescKey, Is.EqualTo("item.weapon.bow.desc"));
        }

        [Test]
        public void ConfigLocalization_UsesCurrentLanguageWithoutCaching()
        {
            var item = LoadGeneratedItem();

            Assert.That(ConfigLocalization.GetItemName(item), Is.EqualTo("弓"));
            Assert.That(ConfigLocalization.GetItemDesc(item), Is.EqualTo("可靠的远程武器"));

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });

            Assert.That(ConfigLocalization.GetItemName(item), Is.EqualTo("Bow"));
            Assert.That(ConfigLocalization.GetItemDesc(item), Is.EqualTo("A reliable ranged weapon."));
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

            Assert.That(zh, Does.Contain("\"item.weapon.bow.name\""));
            Assert.That(zh, Does.Contain("\"item.weapon.bow.desc\""));
            Assert.That(en, Does.Contain("\"item.weapon.bow.name\""));
            Assert.That(en, Does.Contain("\"item.weapon.bow.desc\""));
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

            Assert.That(clientTableNames, Is.EquivalentTo(new[] { "TbItem", "TbWeapon", "TbWeaponAppearance", "TbProjectile", "TbMap", "TbMapScenePart", "TbMapSpawn", "TbPlayer", "TbEnemy", "TbLoot", "TbTestCmdCategory", "TbTestCmdEntry", "TbArcadeRule", "TbArcadeWeapon", "TbArcadeArena", "TbArcadeEnemyGroup", "TbArcadeBoss", "TbArcadeChallenge", "TbArcadeTreasureRoom", "TbArcadeDrop", "TbArcadePowerup", "TbArcadeDrone", "TbArcadeSupportVisual", "TbArcadeUpgrade", "TbArcadeReward", "TbArcadeRoundLayout", "TbArcadeSlayerMartItem", "TbArcadeSlayerMartRegion", "TbArcadeSlayerMartAbility", "TbArcadeSlayerChallenge", "TbArcadeRoundReward", "TbArcadeWeaponUpgrade", "TbArcadeEarlyStage", "TbArcadeScoreShop" }));

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

            Assert.That(serverBytes, Is.EquivalentTo(new[] { "tbprojectile.bytes", "tbenemy.bytes", "tbarcaderule.bytes", "tbarcadeweapon.bytes", "tbarcadearena.bytes", "tbarcadeenemygroup.bytes", "tbarcadeboss.bytes", "tbarcadechallenge.bytes", "tbarcadetreasureroom.bytes", "tbarcadedrop.bytes", "tbarcadepowerup.bytes", "tbarcadedrone.bytes", "tbarcadesupportvisual.bytes", "tbarcadeupgrade.bytes", "tbarcadereward.bytes", "tbaiconfig.bytes", "tbunitconfig.bytes", "tbstartmachine.bytes", "tbstartprocess.bytes", "tbstartzone.bytes", "tbstartscene.bytes", "tbarcaderoundlayout.bytes", "tbarcadeslayermartitem.bytes", "tbarcadeslayermartregion.bytes", "tbarcadeslayermartability.bytes", "tbarcadeslayerchallenge.bytes", "tbarcaderoundreward.bytes", "tbarcadeweaponupgrade.bytes", "tbarcadeearlystage.bytes", "tbarcadescoreshop.bytes" }));

            var serverConfigRoot = Path.Combine(
                repoRoot,
                "Server/GameServer/Runtime/Model/Generated/Config");

            var serverGeneratedCs = Directory.GetFiles(serverConfigRoot, "*.cs")
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToArray();

            Assert.That(serverGeneratedCs, Does.Contain("TbArcadeRule.cs"));
            Assert.That(serverGeneratedCs, Does.Not.Contain("OnlineWorldRuleConfig.cs"));

            var serverRuntimeConfigRoot = Path.Combine(
                repoRoot,
                "Server/GameServer/Runtime/Model/Server/Config");
            Assert.That(File.Exists(Path.Combine(serverRuntimeConfigRoot, "ServerConfig.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(serverRuntimeConfigRoot, "StartConfigRuntime.cs")), Is.True);

            var serverConfigText = File.ReadAllText(Path.Combine(serverRuntimeConfigRoot, "ServerConfig.cs"));
            Assert.That(serverConfigText, Does.Not.Contain("StartConfig/"));
        }

        [Test]
        public void GeneratedCatalog_ExcludesRetiredItemsAndKeepsArcadePresentationCarrier()
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(
                ProjectRoot, "Assets/Game/Config", $"{name}.bytes"))));
            var retiredItems = new[] { 1, 2, 3, 4, 8, 9, 16, 17, 18, 29, 30, 31, 32, 33, 34, 35, 36, 37, 48 };
            foreach (var id in retiredItems)
            {
                Assert.That(tables.TbItem.GetOrDefault(id), Is.Null, $"退役物品 {id}");
                Assert.That(tables.TbWeapon.GetOrDefault(id), Is.Null, $"退役武器 {id}");
                Assert.That(tables.TbWeaponAppearance.GetOrDefault(id), Is.Null, $"退役外观 {id}");
                Assert.That(tables.TbLoot.DataList.Any(row => row.ItemId == id), Is.False, $"退役掉落 {id}");
            }
            Assert.That(tables.TbEnemy.DataList.All(row => row.Id.StartsWith("enemy.arcade.")), Is.True);
            Assert.That(tables.TbEnemy.DataList.All(row => string.IsNullOrEmpty(row.LootConfigId) &&
                string.IsNullOrEmpty(row.GuaranteedWeaponLootConfigId) && string.IsNullOrEmpty(row.GuaranteedAmmoLootConfigId)), Is.True);
            var carrier = tables.TbLoot.Get(ArcadeRunDefaults.DropPresentationLootConfigId);
            Assert.That(carrier.ItemId, Is.EqualTo(49));
            Assert.That(carrier.PrefabName, Is.EqualTo("TestResourcePickup"));
            Assert.That(tables.TbItem.Get(49).NameKey, Is.EqualTo("item.arcade_drop.name"));
            var provider = new LubanRuntimeConfigProvider(() => tables);
            Assert.That(provider.TryGetArcadeCatalog(out _, out var error), Is.True, error);
        }

        [Test]
        public void RetiredLocalizationManifest_RemovesOnlyRetiredKeysFromPublishedTablesAndTexts()
        {
            var repoRoot = Path.GetFullPath(Path.Combine(ProjectRoot, ".."));
            var manifest = File.ReadAllText(Path.Combine(repoRoot, "Config/Localization/retired-keys.json"));
            var retired = JsonUtility.FromJson<RetiredLocalizationKeys>("{\"Keys\":" + manifest + "}").Keys;
            Assert.That(retired.Length, Is.EqualTo(525));
            Assert.That(retired.Distinct().Count(), Is.EqualTo(retired.Length));
            var generated = File.ReadAllText(Path.Combine(ProjectRoot,
                "Assets/Game/Scripts/Utility/Localization/TextsGenerated.cs"));
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(
                ProjectRoot, "Assets/Game/Config", $"{name}.bytes"))));
            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                Assert.That(RuntimeJson.TryParseStringTable(File.ReadAllText(Path.Combine(ProjectRoot,
                    "Assets/Game/Config/Localization", language + ".json")), out var translations), Is.True);
                foreach (var key in retired)
                {
                    Assert.That(translations.ContainsKey(key), Is.False, language + ": " + key);
                    Assert.That(generated, Does.Not.Contain("Loc.T(\"" + key + "\""), key);
                }
                // 现役配置的动态名称不能被前缀清理误删，包括衣物、敌人共用武器与街机承载。
                foreach (var item in tables.TbItem.DataList)
                {
                    Assert.That(translations.ContainsKey(item.NameKey), Is.True, language + ": " + item.NameKey);
                    Assert.That(translations.ContainsKey(item.DescKey), Is.True, language + ": " + item.DescKey);
                }
                foreach (var key in new[] { "map.arcade.name", "tip.item_gained",
                    "ui.optional.expedition.preparing", "ui.optional.expedition.failed", "ui.loading.stage.Scene" })
                    Assert.That(translations.ContainsKey(key), Is.True, language + ": " + key);
            }
        }

        [Test]
        public void LocalizationPublisher_PrunesFirstAdjacentAndLastKeysWithoutReorderingAndIsIdempotent()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "FirepowerLoopLocalizationPublish", System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var source = Path.Combine(tempRoot, "source.json");
                var retired = Path.Combine(tempRoot, "retired.json");
                var zh = Path.Combine(tempRoot, "zh-CN.json");
                var en = Path.Combine(tempRoot, "en-US.json");
                File.WriteAllText(source, "{\"live.new\":{\"zh-CN\":\"new zh\",\"en-US\":\"new en\"}}");
                File.WriteAllText(retired, "[\"old.first\",\"old.adjacent\",\"old.last\"]");
                File.WriteAllText(zh, "{\r\n  \"old.first\": \"first\",\r\n  \"keep\"   : \"kept\",\r\n  \"literal\": \"keep , } safely\",\r\n  \"old.adjacent\": \"adjacent\",\r\n  \"old.last\": \"last\"\r\n}\r\n");
                File.WriteAllText(en, "{\n  \"old.first\": \"first\",\n  \"old.adjacent\": \"adjacent\",\n  \"old.last\": \"last\"\n}\n");
                Assert.That(RunLocalizationPublisher(source, tempRoot, retired), Is.Zero);
                var publishedZh = File.ReadAllText(zh);
                var publishedEn = File.ReadAllText(en);
                Assert.That(publishedZh, Is.EqualTo("{\r\n  \"keep\"   : \"kept\",\r\n  \"literal\": \"keep , } safely\",\r\n  \"live.new\": \"new zh\"\r\n}\r\n"));
                Assert.That(RuntimeJson.TryParseStringTable(publishedEn, out var enTable), Is.True);
                Assert.That(enTable.Keys, Is.EquivalentTo(new[] { "live.new" }));
                Assert.That(enTable["live.new"], Is.EqualTo("new en"));
                var writeTime = File.GetLastWriteTimeUtc(zh);
                Assert.That(RunLocalizationPublisher(source, tempRoot, retired), Is.Zero);
                Assert.That(File.ReadAllText(zh), Is.EqualTo(publishedZh));
                Assert.That(File.ReadAllText(en), Is.EqualTo(publishedEn));
                Assert.That(File.GetLastWriteTimeUtc(zh), Is.EqualTo(writeTime));
                // 第二种语言损坏时，第一种语言的候选更新也必须全部放弃。
                File.WriteAllText(source, "{\"live.new\":{\"zh-CN\":\"changed\",\"en-US\":\"changed\"}}");
                File.WriteAllText(en, "invalid json");
                Assert.That(RunLocalizationPublisher(source, tempRoot, retired), Is.Not.Zero);
                Assert.That(File.ReadAllText(zh), Is.EqualTo(publishedZh));
                Assert.That(File.ReadAllText(en), Is.EqualTo("invalid json"));
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [System.Serializable]
        private sealed class RetiredLocalizationKeys
        {
            public string[] Keys;
        }

        private static int RunLocalizationPublisher(string source, string destination, string retired)
        {
            var script = Path.GetFullPath(Path.Combine(ProjectRoot, "../Config/publish-localization.ps1"));
            var startInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" -Source \"{source}\" -Destination \"{destination}\" -RetiredKeysSource \"{retired}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                Assert.That(process, Is.Not.Null);
                if (!process.WaitForExit(10000))
                {
                    process.Kill();
                    Assert.Fail("本地化发布脚本超过10秒限定时间");
                }
                return process.ExitCode;
            }
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
            return tables.TbItem.Get(6);
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
            byteBuf.WriteInt((int)ItemActionKind.None);
            byteBuf.WriteString(string.Empty);
            byteBuf.WriteInt((int)ClothingSlot.None);
            byteBuf.WriteString(string.Empty);
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
                    "{\"item.weapon.bow.name\":\"弓\",\"item.weapon.bow.desc\":\"可靠的远程武器\"}"),
                ["en-US.json"] = new TextAsset(
                    "{\"item.weapon.bow.name\":\"Bow\",\"item.weapon.bow.desc\":\"A reliable ranged weapon.\"}")
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
