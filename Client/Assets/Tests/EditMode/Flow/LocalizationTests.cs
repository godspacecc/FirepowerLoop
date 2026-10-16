using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class LocalizationTests
    {
        private LocalizationResourceLoader resourceLoader;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            resourceLoader = new LocalizationResourceLoader();
            ResourceLoader.SetLoaderForTests(resourceLoader);
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
        public void Languages_ParseDefaultAndAllLanguages_FromResourceLoader()
        {
            Assert.That(LocalizationLanguages.Default.Code, Is.EqualTo("zh-CN"));
            Assert.That(LocalizationLanguages.All.Select(language => language.Code), Is.EquivalentTo(new[] { "zh-CN", "en-US" }));
        }

        [Test]
        public void LocalizationLanguage_LocalizedLabelFollowsCurrentLanguageAndFallsBackToNativeLabel()
        {
            var zhCn = LocalizationLanguages.FindOrDefault("zh-CN");
            var enUs = LocalizationLanguages.FindOrDefault("en-US");
            var missing = new LocalizationLanguage("fr-FR", "Français");

            Assert.That(Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value, Is.EqualTo("zh-CN"));
            Assert.That(zhCn.LocalizedLabel, Is.EqualTo("简体中文"));
            Assert.That(enUs.LocalizedLabel, Is.EqualTo("英语"));
            Assert.That(missing.LocalizedLabel, Is.EqualTo("Français"));

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });

            Assert.That(zhCn.LocalizedLabel, Is.EqualTo("Simplified Chinese"));
            Assert.That(enUs.LocalizedLabel, Is.EqualTo("English"));
            Assert.That(missing.LocalizedLabel, Is.EqualTo("Français"));
        }

        [Test]
        public void Utility_TranslatesMissingKeysAndFormatsPlaceholders()
        {
            var utility = new LocalizationUtility();

            Assert.That(utility.Translate("zh-CN", "ui.common.confirm"), Is.EqualTo("确认"));
            Assert.That(utility.Translate("en-US", "tip.item_gained", "Wood", 2), Is.EqualTo("Got Wood x2"));
            Assert.That(utility.Translate("en-US", "missing.key"), Is.EqualTo("missing.key"));
        }

        [Test]
        public void Validator_ReportsMissingKeyAndPlaceholderMismatch()
        {
            var report = LocalizationValidator.ValidateJsonTables(new Dictionary<string, string>
            {
                ["zh-CN"] = "{\"same\":\"值 {0}\",\"only_cn\":\"只在中文\"}",
                ["en-US"] = "{\"same\":\"Value\"}"
            });

            Assert.IsFalse(report.IsValid);
            Assert.That(report.Issues.Any(issue => issue.Type == LocalizationValidationIssueType.MissingKey), Is.True);
            Assert.That(report.Issues.Any(issue => issue.Type == LocalizationValidationIssueType.PlaceholderMismatch), Is.True);
        }

        [Test]
        public void GameArchitecture_RegistersLocalizationModelAndUtility()
        {
            var model = Game.Interface.GetModel<LocalizationModel>();
            var utility = Game.Interface.GetUtility<LocalizationUtility>();

            Assert.That(model.CurrentLanguageCode.Value, Is.EqualTo("zh-CN"));
            Assert.That(utility.Translate("en-US", "ui.common.cancel"), Is.EqualTo("Cancel"));
        }

        [Test]
        public void RegisteredUtilityInitializesConfiguredTablesOnlyOnce()
        {
            var utility = Game.Interface.GetUtility<LocalizationUtility>();

            utility.InitializeConfiguredTables();
            utility.InitializeConfiguredTables();
            Assert.That(utility.Translate("en-US", "ui.common.confirm"), Is.EqualTo("Confirm"));

            Assert.That(Game.Interface.GetUtility<LocalizationUtility>(), Is.SameAs(utility));
            Assert.That(resourceLoader.GetLoadCount("zh-CN.json"), Is.EqualTo(1));
            Assert.That(resourceLoader.GetLoadCount("en-US.json"), Is.EqualTo(1));
        }

        [Test]
        public void SetLanguageCommand_StoresLanguageAndSendsEvent()
        {
            LanguageChangedEvent received = default;
            var gotEvent = false;
            var unregister = Game.Interface.RegisterEvent<LanguageChangedEvent>(e =>
            {
                received = e;
                gotEvent = true;
            });

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });

            unregister.UnRegister();
            Assert.IsTrue(gotEvent);
            Assert.That(received.PreviousLanguageCode, Is.EqualTo("zh-CN"));
            Assert.That(received.CurrentLanguageCode, Is.EqualTo("en-US"));
            Assert.That(Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value, Is.EqualTo("en-US"));
        }

        [Test]
        public void LocalizedText_RefreshesImmediately_WhenLanguageChanges()
        {
            var gameObject = new GameObject("LocalizedText");
            var text = gameObject.AddComponent<TextMeshProUGUI>();
            var localizedText = gameObject.AddComponent<LocalizedText>();
            localizedText.Key = "ui.common.confirm";
            localizedText.enabled = true;

            localizedText.Refresh();
            Assert.That(text.text, Is.EqualTo("确认"));

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });

            Assert.That(text.text, Is.EqualTo("Confirm"));
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void LocalizedText_LoadPrefabContentsWithoutConfiguredLoader_PreservesPreviewWithoutResourceErrors()
        {
            const string prefabPath = "Assets/Game/Prefabs/UI/Craft/Craft.prefab";
            var errors = new List<string>();
            GameObject root = null;
            try
            {
                ResourceLoader.ResetForTests();
                LocalizationLanguages.ResetForTests();

                using (Log.CaptureForTests((type, message) =>
                {
                    if (type == LogType.Error)
                    {
                        errors.Add(message);
                    }
                }))
                {
                    root = PrefabUtility.LoadPrefabContents(prefabPath);
                }

                Assert.That(root, Is.Not.Null);
                var localizedText = root.GetComponentsInChildren<LocalizedText>(true)
                    .Single(item => item.Key == "ui.craft.title");
                Assert.That(localizedText.GetComponent<TextMeshProUGUI>().text, Is.EqualTo("打造"));
                Assert.That(errors.Any(message =>
                    message.Contains("[ResourceLoader]") && message.Contains("DefaultPackage 为空")), Is.False);
            }
            finally
            {
                if (root != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                ResourceLoader.SetLoaderForTests(resourceLoader);
                LocalizationLanguages.ResetForTests();
            }
        }

        [Test]
        public void ResourceLoaderConfigurationState_FollowsUseAndReset()
        {
            var errors = new List<string>();
            try
            {
                Assert.That(ResourceLoader.IsConfigured, Is.True);

                ResourceLoader.ResetForTests();
                Assert.That(ResourceLoader.IsConfigured, Is.False);

                using (Log.CaptureForTests((type, message) =>
                {
                    if (type == LogType.Error)
                    {
                        errors.Add(message);
                    }
                }))
                {
                    ResourceLoader.LoadAsset<TextAsset>("missing-runtime-resource");
                }

                Assert.That(errors.Any(message =>
                    message.Contains("[ResourceLoader]") && message.Contains("DefaultPackage 为空")), Is.True);

                ResourceLoader.Use(null);
                Assert.That(ResourceLoader.IsConfigured, Is.False);

                ResourceLoader.SetLoaderForTests(resourceLoader);
                Assert.That(ResourceLoader.IsConfigured, Is.True);
            }
            finally
            {
                ResourceLoader.SetLoaderForTests(resourceLoader);
                LocalizationLanguages.ResetForTests();
            }
        }

        [Test]
        public void LocalizedText_SettingResetKeyRefreshesImmediately_WhenLanguageChanges()
        {
            var gameObject = new GameObject("SettingResetLocalizedText");
            var text = gameObject.AddComponent<TextMeshProUGUI>();
            var localizedText = gameObject.AddComponent<LocalizedText>();
            localizedText.Key = "ui.settings.reset";
            localizedText.enabled = true;

            localizedText.Refresh();
            Assert.That(text.text, Is.EqualTo("重置"));

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            Assert.That(text.text, Is.EqualTo("Reset"));

            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void LocalizationRuntime_DoesNotUseResourcesLoad()
        {
            var root = System.IO.Path.Combine(Application.dataPath, "Game", "Scripts", "Utility", "Localization");
            var source = string.Join("\n", System.IO.Directory.GetFiles(root, "*.cs").Select(System.IO.File.ReadAllText));

            Assert.That(source, Does.Not.Contain("Resources.Load"));
        }

        [Test]
        public void EditorTools_UseConfirmedGameNamespace()
        {
            Assert.That(System.Type.GetType("Game.LocalizationTextGenerator, Assembly-CSharp-Editor"), Is.Not.Null);
            Assert.That(System.Type.GetType("Game." + "EditorTools.LocalizationTextGenerator, Assembly-CSharp-Editor"), Is.Null);
        }

        [Test]
        public void LocalizationTextGenerator_Keeps2DMemberAndPlaceholderRules()
        {
            var type = System.Type.GetType("Game.LocalizationTextGenerator, Assembly-CSharp-Editor");
            var method = type?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(item => item.Name == "GenerateSourceForTests" && item.GetParameters().Length == 1);
            Assert.That(method, Is.Not.Null);

            var json = "{\"ui.start.title\":\"Start\",\"format.only_second\":\"Value {1}\"}";
            var source = (string)method.Invoke(null, new object[] { json });

            Assert.That(source, Does.Contain("public static string StartTitle"));
            Assert.That(source, Does.Not.Contain("UiStartTitle"));
            Assert.That(source, Does.Contain("FormatOnlySecond(object arg0, object arg1)"));
            Assert.That(source, Does.Contain("Loc.T(\"format.only_second\", arg0, arg1)"));
            Assert.That(source, Does.Contain("public static class Texts"));
            Assert.That(source, Does.Not.Contain("partial class Texts"));
        }

        [Test]
        public void ChineseFontSetup_Exposes2DWorkflow()
        {
            var type = System.Type.GetType("Game.ChineseFontSetup, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null);
            foreach (var methodName in new[]
                     {
                         "ScanAndUpdateCharset",
                         "UpdateChineseTmpFontAsset",
                         "ApplyChineseTmpFont",
                         "NormalizeCharset",
                         "LoadNormalizedCharsetFromFile",
                         "PrepareFontAssetForCharsetUpdate",
                         "ResolveSourceFont",
                         "ShouldScanAssetPath",
                         "EnumerateCodePoints",
                         "IsRenderableCodePoint"
                     })
            {
                Assert.That(type.GetMethods().Any(item => item.Name == methodName), Is.True, methodName);
            }
        }

        [Test]
        public void ChineseFontSetup_NormalizesCharsetByUnicodeCodePoint()
        {
            var type = RequireEditorType("Game.ChineseFontSetup");
            var source = "B\tA\n A\uFEFF\u0001\uE000😀中";
            var normalized = (string)InvokeStatic(type, "NormalizeCharset", source);

            Assert.That(normalized, Is.EqualTo(" AB中😀"));
            Assert.That((string)InvokeStatic(type, "NormalizeCharset", normalized), Is.EqualTo(normalized));
        }

        [Test]
        public void ChineseFontSetup_KeepsRenderableSymbolsAndScansOnlyContentAssets()
        {
            var type = RequireEditorType("Game.ChineseFontSetup");
            var normalized = (string)InvokeStatic(type, "NormalizeCharset", "A\u2717\U0001F600");

            Assert.That(normalized, Does.Contain(char.ConvertFromUtf32(0x2717)));
            Assert.That(normalized, Does.Contain(char.ConvertFromUtf32(0x1F600)));
            Assert.That(InvokeStatic(type, "ShouldScanAssetPath", "Assets/Game/Scripts/Sample.cs"), Is.False);
            Assert.That(InvokeStatic(type, "ShouldScanAssetPath", "Assets/Game/Config/Localization/zh-CN.json"), Is.True);
            Assert.That(InvokeStatic(type, "ShouldScanAssetPath", "Assets/Game/Prefabs/UI/Test.prefab"), Is.True);
            Assert.That(InvokeStatic(type, "ShouldScanAssetPath", "Assets/Game/Scenes/Main.unity"), Is.True);
            Assert.That(InvokeStatic(type, "ShouldScanAssetPath", "Assets/Game/Scenes/SurvivalSampleMap/NavMesh.asset"), Is.False);
        }

        [Test]
        public void GeneratedTexts_MatchesProjectLanguageTables()
        {
            var utility = RequireEditorType("Game.LocalizationJsonEditorUtility");
            var generator = RequireEditorType("Game.LocalizationTextGenerator");
            var state = InvokeStatic(utility, "LoadStateFromProject");
            var jsonByLanguage = (IReadOnlyDictionary<string, string>)state.GetType()
                .GetMethod("ToJsonByLanguage")
                .Invoke(state, null);
            var expected = (string)InvokeStatic(
                generator,
                "GenerateSourceForTests",
                jsonByLanguage,
                GetProperty(state, "DefaultLanguageCode"),
                GetProperty(state, "CommentLanguageCode"));
            var actual = File.ReadAllText(Path.Combine(
                Application.dataPath,
                "Game/Scripts/Utility/Localization/TextsGenerated.cs"));

            Assert.That(NormalizeGeneratedSource(actual), Is.EqualTo(NormalizeGeneratedSource(expected)));
        }

        [Test]
        public void ChineseFontAsset_CoversCompleteCharsetAndChineseLocalizationTables()
        {
            var setup = RequireEditorType("Game.ChineseFontSetup");
            var charset = (string)InvokeStatic(setup, "LoadNormalizedCharsetFromFile");
            var fontAsset = (TMP_FontAsset)InvokeStatic(setup, "PrepareFontAssetForCharsetUpdate");
            Assert.That(fontAsset, Is.Not.Null);

            var charsetRequired = ((IEnumerable<int>)InvokeStatic(setup, "EnumerateCodePoints", charset))
                .Where(codePoint => (bool)InvokeStatic(setup, "IsRenderableCodePoint", codePoint))
                .ToHashSet();
            var localizationRequired = new HashSet<int>(EnumerateRenderableZhCnLocalizationCodePoints());
            var charsetCodePoints = ((IEnumerable<int>)InvokeStatic(setup, "EnumerateCodePoints", charset)).ToHashSet();
            var fontCodePoints = fontAsset.characterTable.Select(item => (int)item.unicode).ToHashSet();
            var missingLocalizationFromCharset = localizationRequired.Where(codePoint => !charsetCodePoints.Contains(codePoint)).ToArray();
            var missingCharsetFromFont = charsetRequired.Where(codePoint => !fontCodePoints.Contains(codePoint)).ToArray();
            var missingLocalizationFromFont = localizationRequired.Where(codePoint => !fontCodePoints.Contains(codePoint)).ToArray();

            Assert.That(charsetCodePoints, Does.Contain(0x8FFD), "U+8FFD must be present in 3500hz.txt");
            Assert.That(fontCodePoints, Does.Contain(0x8FFD), "U+8FFD must be present in TMP font asset");
            Assert.That(missingLocalizationFromCharset, Is.Empty, FormatCodePoints(missingLocalizationFromCharset));
            Assert.That(missingCharsetFromFont, Is.Empty, FormatCodePoints(missingCharsetFromFont));
            Assert.That(missingLocalizationFromFont, Is.Empty, FormatCodePoints(missingLocalizationFromFont));
        }

        [Test]
        public void ChineseFontAsset_CreationSettingsMatchSourceFontAndFullCharset()
        {
            var setup = RequireEditorType("Game.ChineseFontSetup");
            var charset = (string)InvokeStatic(setup, "LoadNormalizedCharsetFromFile");
            var fontAsset = (TMP_FontAsset)InvokeStatic(setup, "PrepareFontAssetForCharsetUpdate");
            var sourceFont = (Font)InvokeStatic(setup, "ResolveSourceFont", fontAsset);
            var sourcePath = AssetDatabase.GetAssetPath(sourceFont);
            var settings = fontAsset.creationSettings;

            Assert.That(sourceFont, Is.Not.Null);
            Assert.That(settings.sourceFontFileGUID, Is.EqualTo(AssetDatabase.AssetPathToGUID(sourcePath)));
            Assert.That(settings.sourceFontFileName, Is.EqualTo(Path.GetFileName(sourcePath)));
            Assert.That(settings.characterSequence, Is.EqualTo(charset));
            Assert.That(settings.characterSetSelectionMode, Is.EqualTo(7));
        }

        [Test]
        public void ChineseFontSetup_RebuildHasFailureRestoreBoundary()
        {
            var source = File.ReadAllText(Path.Combine(
                Application.dataPath,
                "Editor/Localization/ChineseFontSetup.cs"));

            Assert.That(source, Does.Contain("File.ReadAllBytes(assetFullPath)"));
            Assert.That(source, Does.Contain("RestoreFontAssetBackup"));
            Assert.That(source, Does.Contain("File.WriteAllBytes(assetFullPath, backupBytes)"));
            Assert.That(source, Does.Contain("ImportAssetOptions.ForceUpdate"));
            Assert.That(source, Does.Contain("GetMissingCodePoints"));
            Assert.That(source, Does.Contain("missingCharacters"));
        }

        [Test]
        public void MissingCharCollector_IgnoresNonTargetLogsAndWritesNormalizedCodePoints()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                MissingCharCollector.ResetForTests();
                File.WriteAllText(tempPath, " B", Encoding.UTF8);
                MissingCharCollector.SetCharsetPathForTests(tempPath);
                MissingCharCollector.Init();

                Assert.That(MissingCharCollector.TryCollectMissingCharacterFromLogForTests(
                    "Unicode value \\u4E2D was not found in [SourceHanSansCN-Normal SDF]",
                    LogType.Error), Is.False);
                Assert.That(MissingCharCollector.TryCollectMissingCharacterFromLogForTests(
                    "Unicode value \\u4E2D was not found in [Other Font]",
                    LogType.Warning), Is.False);

                Assert.That(MissingCharCollector.TryCollectMissingCharacterFromLogForTests(
                    "Unicode value \\u4E2D was not found in [SourceHanSansCN-Normal SDF]",
                    LogType.Warning), Is.True);
                Assert.That(MissingCharCollector.TryCollectMissingCharacterFromLogForTests(
                    "Unicode value \\u4E2D was not found in [SourceHanSansCN-Normal SDF]",
                    LogType.Warning), Is.False);
                Assert.That(MissingCharCollector.TryCollectMissingCharacterFromLogForTests(
                    "Unicode value \\u2717 was not found in [SourceHanSansCN-Normal SDF]",
                    LogType.Warning), Is.True);
                Assert.That(MissingCharCollector.TryCollectMissingCharacterFromLogForTests(
                    "Unicode value \\u2717 was not found in [SourceHanSansCN-Normal SDF]",
                    LogType.Warning), Is.False);
                Assert.That(MissingCharCollector.TryCollectMissingCharacterFromLogForTests(
                    "Unicode value \\u8FFD was not found in [SourceHanSansCN-Normal SDF]",
                    LogType.Warning), Is.True);
                Assert.That(MissingCharCollector.TryCollectMissingCharacterFromLogForTests(
                    "Unicode value \\uD83D\\uDE00 was not found in [SourceHanSansCN-Normal SDF]",
                    LogType.Warning), Is.True);

                foreach (var ignored in new[] { "\\u0001", "\\uFEFF", "\\uE000" })
                {
                    MissingCharCollector.TryCollectMissingCharacterFromLogForTests(
                        $"Unicode value {ignored} was not found in [SourceHanSansCN-Normal SDF]",
                        LogType.Warning);
                }

                var setup = RequireEditorType("Game.ChineseFontSetup");
                var normalized = (string)InvokeStatic(setup, "NormalizeCharset", File.ReadAllText(tempPath));

                Assert.That(normalized, Is.EqualTo(" B\u2717\u4E2D\u8FFD\U0001F600"));
                Assert.That(normalized.Count(item => item == '\u4E2D'), Is.EqualTo(1));
                Assert.That(((IEnumerable<int>)InvokeStatic(setup, "EnumerateCodePoints", normalized)).ToArray(),
                    Is.EqualTo(new[] { 0x20, 0x42, 0x2717, 0x4E2D, 0x8FFD, 0x1F600 }));
            }
            finally
            {
                MissingCharCollector.ResetForTests();
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        [Test]
        public void LocalizationJsonEditor_LoadsLanguagesRowsAndSearchesTranslations()
        {
            var utility = RequireEditorType("Game.LocalizationJsonEditorUtility");
            var state = InvokeStatic(utility, "LoadStateForTests",
                "{\"default\":\"zh-CN\",\"codeComment\":\"zh-CN\",\"languages\":[" +
                "{\"code\":\"zh-CN\",\"label\":\"简体中文\"}," +
                "{\"code\":\"en-US\",\"label\":\"English\"}]}",
                new Dictionary<string, string>
                {
                    ["zh-CN"] = "{\"ui.start.title\":\"开始界面\"}",
                    ["en-US"] = "{\"ui.start.title\":\"Start Screen\"}"
                });
            var languages = ((IEnumerable)GetProperty(state, "Languages")).Cast<object>().ToArray();
            var rows = ((IEnumerable)GetProperty(state, "Rows")).Cast<object>().ToArray();

            Assert.That(languages.Select(item => GetProperty(item, "Code")),
                Is.EqualTo(new[] { "zh-CN", "en-US" }));
            Assert.That(GetProperty(state, "DefaultLanguageCode"), Is.EqualTo("zh-CN"));
            Assert.That(GetProperty(state, "CommentLanguageCode"), Is.EqualTo("zh-CN"));
            Assert.That(GetProperty(rows.Single(), "MemberName"), Is.EqualTo("StartTitle"));
            Assert.That(((ICollection)InvokeStatic(utility, "FilterRows", GetProperty(state, "Rows"), "Texts.StartTitle")).Count, Is.EqualTo(1));
            Assert.That(((ICollection)InvokeStatic(utility, "FilterRows", GetProperty(state, "Rows"), "start screen")).Count, Is.EqualTo(1));
        }

        [Test]
        public void LocalizationJsonEditor_BuildsConfigAndLanguageTables()
        {
            var utility = RequireEditorType("Game.LocalizationJsonEditorUtility");
            var state = InvokeStatic(utility, "LoadStateForTests",
                "{\"default\":\"zh-CN\",\"languages\":[" +
                "{\"code\":\"zh-CN\",\"label\":\"简体中文\"}]}",
                new Dictionary<string, string>
                {
                    ["zh-CN"] = "{\"ui.common.confirm\":\"确认\"}"
                });

            var configJson = (string)InvokeStatic(utility, "BuildLanguagesJson", state);
            var languageJson = (string)InvokeStatic(utility, "BuildLanguageJson", GetProperty(state, "Rows"), "zh-CN");

            Assert.That(configJson, Does.Contain("\"codeComment\": \"zh-CN\""));
            Assert.That(languageJson, Does.Contain("\"ui.common.confirm\": \"确认\""));
        }

        [Test]
        public void LocalizedTextScanner_ScansChineseTmpAndAddsComponentOwnedText()
        {
            var activeBefore = SceneManager.GetActiveScene();
            GameObject root = null;
            GameObject child = null;
            try
            {
                EditModeSceneIsolation.ThrowIfDirtyProductionScenes();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                resourceLoader = new LocalizationResourceLoader();
                ResourceLoader.SetLoaderForTests(resourceLoader);
                LocalizationLanguages.ResetForTests();

                root = new GameObject("Root", typeof(RectTransform));
                child = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
                child.transform.SetParent(root.transform, false);
                var text = child.GetComponent<TextMeshProUGUI>();
                text.text = "开始游戏";

                var scanner = RequireEditorType("Game.LocalizedTextScannerWindow");
                var sourceType = RequireEditorType("Game.LocalizedTextScanSource");
                var prefabSource = System.Enum.Parse(sourceType, "Prefab");
                var candidates = ((IEnumerable)InvokeStatic(scanner, "ScanGameObject",
                    root, "Assets/Test.prefab", prefabSource)).Cast<object>().ToArray();

                Assert.That(candidates.Length, Is.EqualTo(1));
                Assert.That(GetField(candidates[0], "ObjectPath"), Is.EqualTo("Root/Title"));
                Assert.That(GetProperty(candidates[0], "CanAddComponent"), Is.True);
                Assert.That(InvokeStatic(scanner, "AddComponentOwnedText", text, "zh-CN"), Is.True);
                Assert.That(text.GetComponent<LocalizedText>(), Is.Not.Null);
                Assert.That(text.text, Is.EqualTo("开始游戏"));
            }
            finally
            {
                Object.DestroyImmediate(child, true);
                Object.DestroyImmediate(root, true);
                if (activeBefore.IsValid() && !string.IsNullOrEmpty(activeBefore.path))
                {
                    EditorSceneManager.OpenScene(activeBefore.path, OpenSceneMode.Single);
                }
                else
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
            }
        }

        [Test]
        public void LocalizedTextScanner_ReportsCodeCandidatesButCannotAutoModifyThem()
        {
            var candidateType = RequireEditorType("Game.LocalizedTextScanCandidate");
            var sourceType = RequireEditorType("Game.LocalizedTextScanSource");
            var candidate = System.Activator.CreateInstance(candidateType);
            SetField(candidate, "Source", System.Enum.Parse(sourceType, "Code"));
            SetField(candidate, "AssetPath", "Assets/Game/Scripts/Sample.cs");
            SetField(candidate, "Line", 12);
            SetField(candidate, "Text", "中文文案");

            Assert.That(GetProperty(candidate, "CanAddComponent"), Is.False);
        }

        [Test]
        public void LocalizationEditorTypesMenusAndPaths_UseFirepowerLoopContracts()
        {
            Assert.That(System.Type.GetType("Game.LocalizationJsonEditorUtility, Assembly-CSharp-Editor"), Is.Not.Null);
            Assert.That(System.Type.GetType("Game.LocalizationJsonEditorWindow, Assembly-CSharp-Editor"), Is.Not.Null);
            Assert.That(System.Type.GetType("Game.LocalizedTextScannerWindow, Assembly-CSharp-Editor"), Is.Not.Null);
            Assert.That(System.Type.GetType("Game.LocalizedTextScanSource, Assembly-CSharp-Editor"), Is.Not.Null);
            Assert.That(System.Type.GetType("Game." + "EditorTools.LocalizationJsonEditorUtility, Assembly-CSharp-Editor"), Is.Null);
            Assert.That(System.Type.GetType("Rift2D.EditorTools.LocalizationJsonEditorUtility, Assembly-CSharp-Editor"), Is.Null);

            var localizationEditorRoot = Path.Combine(Application.dataPath, "Editor", "Localization");
            var source = string.Join("\n", Directory.GetFiles(localizationEditorRoot, "*.cs").Select(File.ReadAllText));

            Assert.That(source, Does.Contain("Tools/本地化/多语言文本编辑器"));
            Assert.That(source, Does.Not.Contain("Tools/本地化/编辑 JSON 文本表"));
            Assert.That(source, Does.Contain("Tools/本地化/扫描本地化文本"));
            Assert.That(source, Does.Contain("Assets/Game/Config/Localization"));
            Assert.That(source, Does.Not.Contain("Assets/Game/Resources/Localization"));
            Assert.That(source, Does.Not.Contain("SimpleJSON"));
        }

        [Test]
        public void LocalizationTextGenerator_ResolvesNameConflictsAndUsesCommentLanguage()
        {
            var generator = RequireEditorType("Game.LocalizationTextGenerator");
            var source = (string)InvokeStatic(generator, "GenerateSourceForTests",
                new Dictionary<string, string>
                {
                    ["zh-CN"] = "{\"ui.test-name\":\"中文一\",\"ui.test_name\":\"中文二\",\"format.only_second\":\"值 {1}\"}",
                    ["en-US"] = "{\"ui.test-name\":\"English One\",\"ui.test_name\":\"English Two\",\"format.only_second\":\"Value {1}\"}"
                },
                "zh-CN",
                "en-US");

            Assert.That(source, Does.Contain("public static string TestName => Loc.T(\"ui.test-name\")"));
            Assert.That(source, Does.Contain("public static string TestName2 => Loc.T(\"ui.test_name\")"));
            Assert.That(source, Does.Contain("        /// English One"));
            Assert.That(source, Does.Contain("FormatOnlySecond(object arg0, object arg1)"));
            Assert.That(source, Does.Contain("public static class Texts"));
            Assert.That(source, Does.Not.Contain("partial class Texts"));
        }

        [Test]
        public void LocalizationTextGenerator_WritesMultilineXmlCommentsSafely()
        {
            var generator = RequireEditorType("Game.LocalizationTextGenerator");
            var source = (string)InvokeStatic(generator, "GenerateSourceForTests",
                new Dictionary<string, string>
                {
                    ["zh-CN"] = "{\"ui.multiline\":\"第一行\\n第二行 <xml> & \\\"quote\\\"\"}"
                },
                "zh-CN",
                "zh-CN");

            Assert.That(source, Does.Contain("        /// <summary>"));
            Assert.That(source, Does.Contain("        /// 第一行"));
            Assert.That(source, Does.Contain("        /// 第二行 &lt;xml&gt; &amp; &quot;quote&quot;"));
            Assert.That(source, Does.Contain("        /// </summary>"));
            Assert.That(source, Does.Not.Contain("<summary>第一行\n第二行"));
        }

        [Test]
        public void LocalizationValidatorMenu_LoadsTablesFromEditorStateWithoutWriting()
        {
            var paths = new[]
            {
                "Assets/Game/Config/Localization/languages.json",
                "Assets/Game/Config/Localization/zh-CN.json",
                "Assets/Game/Config/Localization/en-US.json"
            };
            var before = paths.ToDictionary(path => path, ComputeAssetFileHash);

            var validatorMenu = RequireEditorType("Game.LocalizationValidatorMenu");
            var jsonByLanguage = (IReadOnlyDictionary<string, string>)InvokeStatic(
                validatorMenu, "LoadAllLanguageJsonForTests");
            var after = paths.ToDictionary(path => path, ComputeAssetFileHash);
            var report = LocalizationValidator.ValidateJsonTables(jsonByLanguage);

            Assert.That(jsonByLanguage.Keys, Is.EquivalentTo(new[] { "zh-CN", "en-US" }));
            Assert.That(jsonByLanguage.Keys, Does.Not.Contain("languages"));
            Assert.That(report.IsValid, Is.True);
            Assert.That(after, Is.EqualTo(before));
        }

        private static void ResetArchitecture()
        {
            var field = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            field?.SetValue(null, null);
        }

        private static System.Type RequireEditorType(string typeName)
        {
            var type = System.Type.GetType(typeName + ", Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, typeName);
            return type;
        }

        private static object InvokeStatic(System.Type type, string methodName, params object[] parameters)
        {
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(item => item.Name == methodName)
                .First(item => item.GetParameters().Length == parameters.Length);
            return method.Invoke(null, parameters);
        }

        private static object GetProperty(object target, string propertyName)
        {
            return target.GetType().GetProperty(propertyName)?.GetValue(target);
        }

        private static object GetField(object target, string fieldName)
        {
            return target.GetType().GetField(fieldName)?.GetValue(target);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName)?.SetValue(target, value);
        }

        private static string NormalizeGeneratedSource(string source)
        {
            return (source ?? string.Empty)
                .TrimStart('\uFEFF')
                .Replace("\r\n", "\n");
        }

        private static IEnumerable<int> EnumerateRenderableZhCnLocalizationCodePoints()
        {
            var setup = RequireEditorType("Game.ChineseFontSetup");
            var path = Path.Combine(Application.dataPath, "Game/Config/Localization/zh-CN.json");
            foreach (var codePoint in (IEnumerable<int>)InvokeStatic(
                         setup,
                         "EnumerateCodePoints",
                         File.ReadAllText(path)))
            {
                var renderable = (bool)InvokeStatic(setup, "IsRenderableCodePoint", codePoint);
                if (renderable)
                {
                    yield return codePoint;
                }
            }
        }

        private static string FormatCodePoints(IEnumerable<int> codePoints)
        {
            return string.Join(", ", codePoints.Select(
                codePoint => $"{char.ConvertFromUtf32(codePoint)} U+{codePoint:X}"));
        }

        private sealed class LocalizationResourceLoader : IResourceLoader
        {
            private readonly Dictionary<string, int> loadCounts = new Dictionary<string, int>();
            private readonly Dictionary<string, TextAsset> assets = new Dictionary<string, TextAsset>
            {
                [LocalizationLanguages.LanguagesAddress] = new TextAsset(
                    "{\"default\":\"zh-CN\",\"codeComment\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}"),
                ["zh-CN.json"] = new TextAsset(
                    "{\"language.zh-CN.name\":\"简体中文\",\"language.en-US.name\":\"英语\",\"ui.hotkey.unknown\":\"未知动作\",\"ui.common.confirm\":\"确认\",\"ui.common.cancel\":\"取消\",\"ui.settings.reset\":\"重置\",\"tip.item_gained\":\"获得 {0} x{1}\"}"),
                ["en-US.json"] = new TextAsset(
                    "{\"language.zh-CN.name\":\"Simplified Chinese\",\"language.en-US.name\":\"English\",\"ui.hotkey.unknown\":\"Unknown Action\",\"ui.common.confirm\":\"Confirm\",\"ui.common.cancel\":\"Cancel\",\"ui.settings.reset\":\"Reset\",\"tip.item_gained\":\"Got {0} x{1}\"}")
            };

            public T LoadAsset<T>(string path) where T : Object
            {
                loadCounts[path] = GetLoadCount(path) + 1;
                return assets.TryGetValue(path, out var asset) ? asset as T : null;
            }

            public int GetLoadCount(string path)
            {
                return loadCounts.TryGetValue(path, out var count) ? count : 0;
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

        private static string ComputeAssetFileHash(string assetPath)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(File.ReadAllBytes(fullPath));
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var item in hash)
            {
                builder.Append(item.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
