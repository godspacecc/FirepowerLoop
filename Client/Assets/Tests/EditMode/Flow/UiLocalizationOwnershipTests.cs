using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class UiLocalizationOwnershipTests
    {
        private const string LoginPrefabPath = "Assets/Game/Prefabs/UI/Online/OnlineLogin.prefab";
        private const string RoomPrefabPath = "Assets/Game/Prefabs/UI/Online/OnlineRoom.prefab";
        private const string TestCmdPrefabPath = "Assets/Game/Prefabs/UI/Diagnostics/TestCmd.prefab";
        private const string RuntimeLogPrefabPath = "Assets/Game/Prefabs/UI/Diagnostics/RuntimeLog.prefab";

        private static readonly IReadOnlyDictionary<string, FixedTextExpectation> LoginFixedKeys =
            new Dictionary<string, FixedTextExpectation>
            {
                { "LoginContent/Title", Fixed("ui.online.title", "联机") },
                { "LoginContent/AccountPanel/AnonymousRoot/UserNameLabel", Fixed("ui.online.account", "账号") },
                { "LoginContent/AccountPanel/AnonymousRoot/PasswordLabel", Fixed("ui.online.password", "密码") },
                { "LoginContent/AccountPanel/AnonymousRoot/LoginButton/Text", Fixed("ui.online.login", "登录") },
                { "LoginContent/AccountPanel/AuthenticatedRoot/LogoutButton/Text", Fixed("ui.online.logout", "退出登录") },
                { "LoginContent/AccountPanel/AuthenticatedRoot/RoomButton/Text", Fixed("ui.online.room.open", "房间") }
            };

        private static readonly IReadOnlyDictionary<string, FixedTextExpectation> RoomFixedKeys =
            new Dictionary<string, FixedTextExpectation>
            {
                { "LobbyContent/TitleText", Fixed("ui.online.room.title", "房间") },
                { "LobbyContent/EmptyRoot/DirectJoinSection/RoomCodeLabel", Fixed("ui.online.room.code", "房间号") },
                { "LobbyContent/EmptyRoot/CreateSection/CreateButton/Text", Fixed("ui.online.room.create", "创建") },
                { "LobbyContent/EmptyRoot/DirectJoinSection/JoinButton/Text", Fixed("ui.online.room.join", "加入") },
                { "LobbyContent/JoinedRoot/LeaveButton/Text", Fixed("ui.online.room.leave", "离开房间") },
                { "LobbyContent/JoinedRoot/WorldButton/Text", Fixed("ui.online.world.enter", "进入世界") },
                { "LobbyContent/JoinedRoot/DisconnectHintText", Fixed("ui.online.room.disconnected_hint", "连接已断开，服务端保留房间身份 60 秒") },
                { "LobbyContent/JoinedRoot/ChatPanel/ChatTitleText", Fixed("ui.online.room.chat.title", "房间聊天") },
                { "LobbyContent/JoinedRoot/ChatPanel/ChatSendButton/Text", Fixed("ui.online.room.chat.send", "发送") },
                { "LobbyContent/JoinedRoot/ChatPanel/ChatInput/Placeholder", Fixed("ui.online.room.chat.placeholder", "输入消息（最多 200 个字符）") }
            };

        private static readonly IReadOnlyDictionary<string, string> TestCmdFixedTexts =
            new Dictionary<string, string>
            {
                { "TextTitle", "测试指令" },
                { "TextCategoryTitle", "分类" },
                { "TextEntryTitle", "指令" },
                { "TextDetailsTitle", "指令说明" },
                { "BtnExecute/Text", "执行" },
                { "InputCommand/Placeholder", "输入测试指令" }
            };

        private static readonly IReadOnlyDictionary<string, string> RuntimeLogFixedTexts =
            new Dictionary<string, string>
            {
                { "TitleText", "运行日志" },
                { "BtnAll/Text", "全部" },
                { "BtnNormal/Text", "普通" },
                { "BtnWarning/Text", "警告" },
                { "BtnError/Text", "错误" },
                { "BtnCopy/Text", "复制" },
                { "BtnExport/Text", "导出" },
                { "BtnClear/Text", "清空" }
            };

        private static readonly string[] LoginDynamicFields =
        {
            "AuthenticatedUserText",
            "AuthenticatedPlayerText",
            "ResultText",
            "WorldButtonText"
        };

        private static readonly string[] RoomDynamicFields =
        {
            "CurrentRoomCodeText",
            "ChatStateText",
            "DisconnectHintRoot"
        };

        private static readonly string[] LoginRemovedFields =
        {
            "TitleText",
            "UserNameLabel",
            "PasswordLabel",
            "LoginButtonText",
            "LogoutButtonText",
            "RoomButtonText",
            "CloseButtonText"
        };

        private static readonly string[] RoomRemovedFields =
        {
            "TitleText",
            "RoomCodeLabel",
            "CreateButtonText",
            "JoinButtonText",
            "LeaveButtonText",
            "CloseButtonText",
            "DisconnectHintText",
            "ChatTitleText",
            "ChatSendButtonText"
        };

        private static readonly string[] TestCmdRemovedFields =
        {
            "TextTitle",
            "TextCategoryTitle",
            "TextEntryTitle",
            "TextDetailsTitle"
        };

        private static readonly string[] RuntimeLogRemovedFields =
        {
            "TitleText"
        };



        [TearDown]
        public void TearDown()
        {
            CleanupHarness();
        }

        [Test]
        public void OnlinePrefabs_OwnFixedTextWithLocalizedTextAndExposeOnlyDynamicFields()
        {
            AssertOnlinePrefabContract<OnlineLoginUI>(LoginPrefabPath, LoginFixedKeys, LoginRemovedFields, LoginDynamicFields);
            AssertOnlinePrefabContract<OnlineRoomUI>(RoomPrefabPath, RoomFixedKeys, RoomRemovedFields, RoomDynamicFields);
        }

        [Test]
        public void DiagnosticPrefabs_OwnFixedTextDirectlyAndExposeOnlyDynamicFields()
        {
            AssertDiagnosticPrefabContract<TestCmdUI>(TestCmdPrefabPath, TestCmdFixedTexts, TestCmdRemovedFields);
            AssertDiagnosticPrefabContract<RuntimeLogUI>(RuntimeLogPrefabPath, RuntimeLogFixedTexts, RuntimeLogRemovedFields);
        }

        [Test]
        public void GeneratedTmpFields_DoNotPointToLocalizedTextObjects()
        {
            var failures = new List<string>();
            foreach (var prefabPath in Directory.GetFiles("Assets/Game/Prefabs/UI", "*.prefab", SearchOption.AllDirectories)
                         .Select(path => path.Replace("\\", "/")))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null, prefabPath);
                foreach (var view in prefab.GetComponentsInChildren<MonoBehaviour>(true)
                             .Where(component => component is BaseUI || component is BaseBoxUI))
                {
                    foreach (var field in view.GetType()
                                 .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                 .Where(field => typeof(TMP_Text).IsAssignableFrom(field.FieldType)))
                    {
                        var text = field.GetValue(view) as TMP_Text;
                        if (text == null)
                        {
                            continue;
                        }

                        // 更新公告复用按钮Box的固定标签，不由消息控制器写入动态文字。
                        var fixedUpdateKey = prefabPath == "Assets/Game/Prefabs/UI/Common/ResourceUpdateMessage.prefab" &&
                            view is MessageButtonBoxUI && field.Name == "ButtonText"
                            ? TransformPath(prefab.transform, text.transform) switch
                            {
                                "ResourceUpdateMessage/Panel/Restart/ButtonText" => "ui.start.update.restart_now",
                                "ResourceUpdateMessage/Panel/Later/ButtonText" => "ui.start.update.later",
                                _ => null
                            } : null;
                        if (fixedUpdateKey != null)
                        {
                            var localized = text.GetComponents<LocalizedText>().Where(item => item.enabled).ToArray();
                            Assert.That(localized, Has.Length.EqualTo(1), fixedUpdateKey);
                            Assert.That(localized[0].Key, Is.EqualTo(fixedUpdateKey));
                            Assert.That(localized[0].Texts, Is.Empty, fixedUpdateKey);
                            Assert.That(text.text, Is.EqualTo(fixedUpdateKey == "ui.start.update.restart_now" ? "立即重启更新" : "稍后"), fixedUpdateKey);
                            continue;
                        }

                        if (text.GetComponents<LocalizedText>().Any(item => item.enabled))
                        {
                            failures.Add($"{prefabPath} :: {view.GetType().Name}.{field.Name} points to LocalizedText object {TransformPath(prefab.transform, text.transform)}.");
                        }
                    }
                }
            }

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void AllUiPrefabs_DoNotLeaveBareUiKeysAndKeyModeComponentsAreValid()
        {
            var languageTables = LoadLanguageTables();
            var failures = new List<string>();
            foreach (var prefabPath in Directory.GetFiles("Assets/Game/Prefabs/UI", "*.prefab", SearchOption.AllDirectories)
                         .Select(path => path.Replace("\\", "/")))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null, prefabPath);
                foreach (var text in prefab.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    AddTextOwnershipFailures(
                        prefabPath,
                        prefab.transform,
                        text,
                        languageTables,
                        failures);
                }
            }

            AssertMessageButtonBoxPreview(failures);
            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void UiLocalizationGate_RejectsLocalizedTextComponentStructureAnomalies()
        {
            SetupLocalizationOnlyHarness(
                "{\"ui.online.login\":\"登录\",\"ui.online.logout\":\"退出登录\"}",
                "{\"ui.online.login\":\"Login\",\"ui.online.logout\":\"Log out\"}");
            var languageTables = LoadLanguageTables();
            var root = new GameObject("GateRoot");
            try
            {
                var duplicate = CreateText(root.transform, "Duplicate", "登录");
                duplicate.gameObject.AddComponent<LocalizedText>().Key = "ui.online.login";
                duplicate.gameObject.AddComponent<LocalizedText>().Key = "ui.online.logout";

                var emptyKeyAndTexts = CreateText(root.transform, "EmptyKeyAndTexts", "空配置");
                emptyKeyAndTexts.gameObject.AddComponent<LocalizedText>();

                var componentTexts = CreateText(root.transform, "ComponentTexts", "组件自有");
                componentTexts.gameObject.AddComponent<LocalizedText>().Texts.Add(
                    new LocalizedTextEntry { LanguageCode = "zh-CN", Text = "组件自有" });

                var keyAndTexts = CreateText(root.transform, "KeyAndTexts", "登录");
                var keyAndTextsLocalized = keyAndTexts.gameObject.AddComponent<LocalizedText>();
                keyAndTextsLocalized.Key = "ui.online.login";
                keyAndTextsLocalized.Texts.Add(new LocalizedTextEntry { LanguageCode = "zh-CN", Text = "登录" });

                var missingKey = CreateText(root.transform, "MissingKey", "缺失");
                missingKey.gameObject.AddComponent<LocalizedText>().Key = "ui.tests.missing_key";

                var previewMismatch = CreateText(root.transform, "PreviewMismatch", "退出登录");
                previewMismatch.gameObject.AddComponent<LocalizedText>().Key = "ui.online.login";

                var failures = new List<string>();
                foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    AddTextOwnershipFailures(
                        "Assets/Tests/Temp/GateRoot.prefab",
                        root.transform,
                        text,
                        languageTables,
                        failures);
                }

                AssertHasFailure(failures, "Duplicate", "2 enabled LocalizedText");
                AssertHasFailure(failures, "EmptyKeyAndTexts", "empty Key and empty Texts");
                Assert.That(failures.Any(failure => failure.Contains("ComponentTexts")), Is.False, string.Join(Environment.NewLine, failures));
                AssertHasFailure(failures, "KeyAndTexts", "Texts empty");
                AssertHasFailure(failures, "MissingKey", "missing in");
                AssertHasFailure(failures, "PreviewMismatch", "should be");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Scanner_ReportsReadableKeyModeAndRejectsInternalKeyLeaks()
        {
            ResourceLoader.SetLoaderForTests(new LocalizationTestLoader());
            LocalizationLanguages.ResetForTests();
            var root = new GameObject("ScannerRoot");
            try
            {
                var chinese = CreateText(root.transform, "Chinese", "中文");
                var bare = CreateText(root.transform, "Bare", "ui.online.login");
                var mismatch = CreateText(root.transform, "Mismatch", "退出登录");
                var mismatchLocalized = mismatch.gameObject.AddComponent<LocalizedText>();
                mismatchLocalized.Key = "ui.online.login";
                var valid = CreateText(root.transform, "Valid", "房间");
                var validLocalized = valid.gameObject.AddComponent<LocalizedText>();
                validLocalized.Key = "ui.online.room.open";
                var leak = CreateText(root.transform, "Leak", "ui.online.room.open");
                var leakLocalized = leak.gameObject.AddComponent<LocalizedText>();
                leakLocalized.Key = "ui.online.room.open";
                var duplicate = CreateText(root.transform, "Duplicate", "登录");
                duplicate.gameObject.AddComponent<LocalizedText>().Key = "ui.online.login";
                duplicate.gameObject.AddComponent<LocalizedText>().Key = "ui.online.logout";
                var emptyKeyAndTexts = CreateText(root.transform, "EmptyKeyAndTexts", "空配置");
                emptyKeyAndTexts.gameObject.AddComponent<LocalizedText>();
                var componentTexts = CreateText(root.transform, "ComponentTexts", "组件自有");
                var componentTextsLocalized = componentTexts.gameObject.AddComponent<LocalizedText>();
                componentTextsLocalized.Texts.Add(new LocalizedTextEntry { LanguageCode = "zh-CN", Text = "组件自有" });
                var keyAndTexts = CreateText(root.transform, "KeyAndTexts", "登录");
                var keyAndTextsLocalized = keyAndTexts.gameObject.AddComponent<LocalizedText>();
                keyAndTextsLocalized.Key = "ui.online.login";
                keyAndTextsLocalized.Texts.Add(new LocalizedTextEntry { LanguageCode = "zh-CN", Text = "登录" });
                var missingKey = CreateText(root.transform, "MissingKey", "缺失");
                missingKey.gameObject.AddComponent<LocalizedText>().Key = "ui.tests.missing_key";
                var leakWithDuplicate = CreateText(root.transform, "LeakWithDuplicate", "ui.online.login");
                leakWithDuplicate.gameObject.AddComponent<LocalizedText>().Key = "ui.online.login";
                leakWithDuplicate.gameObject.AddComponent<LocalizedText>().Key = "ui.online.logout";

                var scannerType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("Game.LocalizedTextScannerWindow", false))
                    .FirstOrDefault(type => type != null);
                var sourceType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("Game.LocalizedTextScanSource", false))
                    .FirstOrDefault(type => type != null);
                Assert.That(scannerType, Is.Not.Null);
                Assert.That(sourceType, Is.Not.Null);
                var source = Enum.Parse(sourceType, "Prefab");
                var candidates = (System.Collections.IEnumerable)scannerType
                    .GetMethod("ScanGameObject", BindingFlags.Public | BindingFlags.Static)
                    ?.Invoke(null, new[] { root, "Assets/Tests/Temp/ScannerRoot.prefab", source });
                Assert.That(candidates, Is.Not.Null);
                var byName = candidates.Cast<object>().ToDictionary(
                    candidate => ((string)GetField(candidate, "ObjectPath")).Split('/').Last());

                AssertCandidate(byName["Chinese"], "ChineseText", true);
                AssertCandidate(byName["Bare"], "InternalKeyLeak", false);
                AssertCandidate(byName["Mismatch"], "PreviewTextMismatch", false);
                AssertCandidate(byName["Valid"], "ReadableKeyMode", false);
                AssertCandidate(byName["Leak"], "InternalKeyLeak", false);
                AssertCandidate(byName["Duplicate"], "KeyModeInvalid", false, "多个启用的 LocalizedText");
                AssertCandidate(byName["EmptyKeyAndTexts"], "KeyModeInvalid", false, "Key 为空且 Texts 为空");
                AssertCandidate(byName["ComponentTexts"], "ChineseText", false);
                AssertCandidate(byName["KeyAndTexts"], "KeyModeInvalid", false, "Key 非空时 Texts 必须为空");
                AssertCandidate(byName["MissingKey"], "KeyModeInvalid", false, "任一正式语言缺少 Key");
                AssertCandidate(byName["LeakWithDuplicate"], "InternalKeyLeak", false, "内部 Key 泄漏", "多个启用的 LocalizedText");
            }
            finally
            {
                Object.DestroyImmediate(root);
                ResourceLoader.ResetForTests();
                LocalizationLanguages.ResetForTests();
            }
        }

        [Test]
        public void LocalizedText_EditModeKeepsReadablePreviewWhenKeyFallsBack()
        {
            SetupLocalizationOnlyHarness(
                "{\"ui.common.confirm\":\"确认\"}",
                "{\"ui.common.confirm\":\"Confirm\"}");
            var root = new GameObject("ReadablePreviewGuard");
            try
            {
                var text = root.AddComponent<TextMeshProUGUI>();
                text.text = "可读预览";
                var localizedText = root.AddComponent<LocalizedText>();
                localizedText.Key = "ui.preview.missing";

                localizedText.Refresh();

                Assert.That(text.text, Is.EqualTo("可读预览"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LocalizedText_KeyModeStillRefreshesWhenTranslationExists()
        {
            SetupLocalizationOnlyHarness(
                "{\"ui.common.confirm\":\"确认\"}",
                "{\"ui.common.confirm\":\"Confirm\"}");
            var root = new GameObject("ReadablePreviewRefresh");
            try
            {
                var text = root.AddComponent<TextMeshProUGUI>();
                text.text = "可读预览";
                var localizedText = root.AddComponent<LocalizedText>();
                localizedText.Key = "ui.common.confirm";

                localizedText.Refresh();
                Assert.That(text.text, Is.EqualTo("确认"));

                Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
                Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
                Assert.That(text.text, Is.EqualTo("Confirm"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LanguageChanged_RedrawsOnlineDynamicTextsAndReusedMemberRows()
        {
            SetupHarness();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));

            var loginCtrl = UIHelper.ShowUI<OnlineLoginUICtrl>();
            var loginUi = GetBaseUi<OnlineLoginUI>(loginCtrl);
            var authenticated = new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,
                OnlineOperationState.Idle,
                OnlineAccountResultCode.Success,
                "owner",
                1,
                10,
                2,
                false,
                "owner");
            SetAccountSnapshot(authenticated);

            Assert.That(loginUi.AuthenticatedUserText.text, Does.Contain("owner"));
            Assert.That(loginUi.AuthenticatedPlayerText.text, Does.Contain("10"));

            var roomCtrl = UIHelper.ShowUI<OnlineRoomUICtrl>();
            var roomUi = GetBaseUi<OnlineRoomUI>(roomCtrl);
            var room = new OnlineRoomSnapshot(
                true,
                500,
                "ABC234",
                10,
                1,
                new[]
                {
                    new OnlineRoomMemberSnapshot(10, "owner", true, OnlineRoomMemberConnectionState.Online, 1)
                },
                false,
                OnlineRoomOperationState.Idle,
                OnlineRoomResultCode.Success);
            Game.Interface.GetModel<OnlineRoomModel>().Publish(room);
            var chat = new OnlineRoomChatSnapshot(
                500,
                Array.Empty<OnlineRoomChatMessageSnapshot>(),
                0,
                OnlineRoomChatHistoryState.Ready,
                OnlineRoomChatSendState.Idle,
                OnlineRoomChatResultCode.None,
                false,
                string.Empty);
            Game.Interface.GetModel<OnlineRoomChatModel>().Publish(chat);
            roomUi.ChatInput.text = "keep me";
            var row = roomUi.MemberList.GetContentTransform().Cast<Transform>().First().GetComponent<OnlineRoomMemberBoxUI>();
            Assert.That(row.PlayerIdText.text, Does.Contain("10"));
            Assert.That(roomUi.ChatStateText.text, Is.Not.Empty);

            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));

            Assert.That(loginUi.AuthenticatedUserText.text, Does.Contain("owner"));
            Assert.That(loginUi.AuthenticatedPlayerText.text, Does.Contain("10"));
            Assert.That(roomUi.ChatStateText.text, Is.EqualTo(Loc.T("ui.online.room.chat.empty")));
            Assert.That(roomUi.ChatInput.text, Is.EqualTo("keep me"));
            Assert.That(roomUi.MemberList.totalCount, Is.EqualTo(1));
            Assert.That(row.DisplayNameText.text, Is.EqualTo("owner"));
            Assert.That(row.PlayerIdText.text, Is.EqualTo(Loc.T("ui.online.room.member_player", 10)));
            Assert.That(row.OwnerText.text, Is.EqualTo(Loc.T("ui.online.room.owner")));
            Assert.That(row.ConnectionText.text, Is.EqualTo(Loc.T("ui.online.room.member_online")));
        }

        private static void AssertOnlinePrefabContract<T>(
            string prefabPath,
            IReadOnlyDictionary<string, FixedTextExpectation> fixedKeys,
            string[] removedFields,
            string[] dynamicFields)
            where T : BaseUI
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);
            var collector = prefab.GetComponent<ReferenceCollector>();
            Assert.That(collector, Is.Not.Null, prefabPath);
            var ui = prefab.GetComponent<T>();
            Assert.That(ui, Is.Not.Null, prefabPath);

            var fields = typeof(T)
                .GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(field => field.Name);
            foreach (var removed in removedFields)
            {
                Assert.That(fields.ContainsKey(removed), Is.False, removed);
                Assert.That(collector.data.Any(item => item.key == removed), Is.False, removed);
            }

            foreach (var dynamicField in dynamicFields)
            {
                Assert.That(fields.ContainsKey(dynamicField), Is.True, dynamicField);
                Assert.That(collector.data.Any(item => item.key == dynamicField), Is.True, dynamicField);
            }

            foreach (var pair in fixedKeys)
            {
                var text = prefab.transform.Find(pair.Key)?.GetComponent<TextMeshProUGUI>();
                Assert.That(text, Is.Not.Null, pair.Key);
                Assert.That(text.text, Is.EqualTo(pair.Value.DefaultText), pair.Key);
                var localizedTexts = text.GetComponents<LocalizedText>().Where(item => item.enabled).ToArray();
                Assert.That(localizedTexts.Length, Is.EqualTo(1), pair.Key);
                Assert.That(localizedTexts[0].Key, Is.EqualTo(pair.Value.Key), pair.Key);
                Assert.That(localizedTexts[0].Texts, Is.Empty, pair.Key);
            }
        }

        private static void AssertDiagnosticPrefabContract<T>(
            string prefabPath,
            IReadOnlyDictionary<string, string> fixedTexts,
            string[] removedFields)
            where T : BaseUI
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);
            var collector = prefab.GetComponent<ReferenceCollector>();
            Assert.That(collector, Is.Not.Null, prefabPath);
            Assert.That(prefab.GetComponent<T>(), Is.Not.Null, prefabPath);
            Assert.That(prefab.GetComponentsInChildren<LocalizedText>(true), Is.Empty, prefabPath);

            var fields = typeof(T)
                .GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(field => field.Name);
            foreach (var removed in removedFields)
            {
                Assert.That(fields.ContainsKey(removed), Is.False, removed);
                Assert.That(collector.data.Any(item => item.key == removed), Is.False, removed);
            }

            foreach (var pair in fixedTexts)
            {
                var text = prefab.transform.Find(pair.Key)?.GetComponent<TextMeshProUGUI>();
                Assert.That(text, Is.Not.Null, pair.Key);
                Assert.That(text.text, Is.EqualTo(pair.Value), pair.Key);
            }
        }

        private static LocalizationTableSet LoadLanguageTables()
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var defaultTexts = new Dictionary<string, string>(StringComparer.Ordinal);
            var languagesPath = Path.Combine(Application.dataPath, "Game/Config/Localization/languages.json");
            Assert.That(RuntimeJson.TryParseObject(File.ReadAllText(languagesPath), out var languagesJson), Is.True, languagesPath);
            var defaultLanguage = languagesJson["default"]?.ToString();
            var languages = languagesJson["languages"] != null
                ? languagesJson["languages"]
                    .Select(item => item["code"]?.ToString() ?? string.Empty)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .ToArray()
                : Array.Empty<string>();
            Assert.That(languages, Is.Not.Empty, languagesPath);
            foreach (var languageCode in languages)
            {
                var path = Path.Combine(Application.dataPath, "Game/Config/Localization", $"{languageCode}.json");
                Assert.That(File.Exists(path), Is.True, path);
                Assert.That(RuntimeJson.TryParseStringTable(File.ReadAllText(path), out var table), Is.True, path);
                result[languageCode] = new HashSet<string>(table.Keys, StringComparer.Ordinal);
                if (languageCode == defaultLanguage)
                {
                    defaultTexts = table;
                }
            }

            Assert.That(defaultTexts, Is.Not.Empty);
            return new LocalizationTableSet(result, defaultTexts);
        }

        private static bool IsUiKey(string text)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(text ?? string.Empty, "^ui\\.[a-z0-9_.-]+$");
        }

        private static void AssertHasFailure(IReadOnlyCollection<string> failures, params string[] expectedParts)
        {
            Assert.That(
                failures.Any(failure => expectedParts.All(failure.Contains)),
                Is.True,
                string.Join(Environment.NewLine, failures));
        }

        private static void AddTextOwnershipFailures(
            string prefabPath,
            Transform prefabRoot,
            TextMeshProUGUI text,
            LocalizationTableSet languageTables,
            List<string> failures)
        {
            var path = TransformPath(prefabRoot, text.transform);
            var localizedTexts = text.GetComponents<LocalizedText>().Where(item => item.enabled).ToArray();
            if (IsUiKey(text.text))
            {
                failures.Add($"{prefabPath} :: {path} leaks internal key '{text.text}'.");
            }

            if (localizedTexts.Length > 1)
            {
                failures.Add($"{prefabPath} :: {path} has {localizedTexts.Length} enabled LocalizedText components.");
                return;
            }

            if (localizedTexts.Length != 1)
            {
                return;
            }

            var localizedText = localizedTexts[0];
            var hasKey = !string.IsNullOrWhiteSpace(localizedText.Key);
            var hasTexts = localizedText.Texts != null && localizedText.Texts.Count > 0;
            if (!hasKey)
            {
                if (!hasTexts)
                {
                    failures.Add($"{prefabPath} :: {path} has LocalizedText with empty Key and empty Texts.");
                }

                return;
            }

            if (localizedText.GetComponent<TextMeshProUGUI>() == null)
            {
                failures.Add($"{prefabPath} :: {path} key mode lacks TMP.");
            }

            if (hasTexts)
            {
                failures.Add($"{prefabPath} :: {path} key mode should keep Texts empty.");
            }

            foreach (var table in languageTables.Tables)
            {
                if (!table.Value.Contains(localizedText.Key))
                {
                    failures.Add($"{prefabPath} :: {path} key '{localizedText.Key}' missing in {table.Key}.");
                }
            }

            if (languageTables.DefaultTexts.TryGetValue(localizedText.Key, out var expectedText) &&
                text.text != expectedText)
            {
                failures.Add($"{prefabPath} :: {path} preview '{text.text}' should be '{expectedText}'.");
            }
        }

        private static void AssertCandidate(object candidate, string status, bool canAddComponent)
        {
            AssertCandidate(candidate, status, canAddComponent, Array.Empty<string>());
        }

        private static void AssertCandidate(object candidate, string status, bool canAddComponent, params string[] diagnostics)
        {
            Assert.That(GetField(candidate, "Status").ToString(), Is.EqualTo(status));
            Assert.That((bool)GetProperty(candidate, "CanAddComponent"), Is.EqualTo(canAddComponent));
            var diagnostic = (string)GetField(candidate, "Diagnostic") ?? string.Empty;
            foreach (var expected in diagnostics)
            {
                Assert.That(diagnostic, Does.Contain(expected));
            }
        }

        private static FixedTextExpectation Fixed(string key, string defaultText)
        {
            return new FixedTextExpectation(key, defaultText);
        }

        private static void AssertMessageButtonBoxPreview(List<string> failures)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Notify/Box/MessageButtonBox.prefab");
            var ui = prefab.GetComponent<MessageButtonBoxUI>();
            var text = prefab.transform.Find("ButtonText")?.GetComponent<TextMeshProUGUI>();
            if (ui == null || text == null)
            {
                failures.Add("MessageButtonBox missing UI component or ButtonText.");
                return;
            }

            if (ui.ButtonText == null)
            {
                failures.Add("MessageButtonBoxUI.ButtonText is not bound.");
            }

            if (text.text != "确认")
            {
                failures.Add($"MessageButtonBox/ButtonText preview '{text.text}' should be '确认'.");
            }

            if (text.GetComponents<LocalizedText>().Any(item => item.enabled))
            {
                failures.Add("MessageButtonBox/ButtonText should not have LocalizedText.");
            }
        }

        private sealed class FixedTextExpectation
        {
            public FixedTextExpectation(string key, string defaultText)
            {
                Key = key;
                DefaultText = defaultText;
            }

            public string Key { get; }
            public string DefaultText { get; }
        }

        private sealed class LocalizationTableSet
        {
            public LocalizationTableSet(
                Dictionary<string, HashSet<string>> tables,
                Dictionary<string, string> defaultTexts)
            {
                Tables = tables;
                DefaultTexts = defaultTexts;
            }

            public Dictionary<string, HashSet<string>> Tables { get; }
            public Dictionary<string, string> DefaultTexts { get; }
        }

        private static object GetField(object target, string name)
        {
            return target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
        }

        private static object GetProperty(object target, string name)
        {
            return target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string value)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            gameObject.transform.SetParent(parent, false);
            var text = gameObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            return text;
        }

        private static TMP_Text PrefabPathText(Transform root, string path)
        {
            var child = root.Find(path);
            Assert.That(child, Is.Not.Null, path);
            var text = child.GetComponent<TMP_Text>();
            Assert.That(text, Is.Not.Null, path);
            return text;
        }

        private static string TransformPath(Transform root, Transform target)
        {
            var parts = new List<string>();
            var current = target;
            while (current != null)
            {
                parts.Add(current.name);
                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static T GetBaseUi<T>(BaseUICtrl ctrl)
            where T : BaseUI
        {
            Assert.That(ctrl, Is.Not.Null);
            var ui = typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl) as T;
            Assert.That(ui, Is.Not.Null);
            return ui;
        }

        private static void SetAccountSnapshot(OnlineAccountSnapshot snapshot)
        {
            typeof(OnlineAccountSystem)
                .GetField("lastSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(Game.Interface.GetSystem<OnlineAccountSystem>(), snapshot);
            Game.Interface.GetModel<OnlineAccountModel>().Publish(snapshot);
        }

        private static OnlineRoomSnapshot RoomSnapshot(bool connectionLost)
        {
            return new OnlineRoomSnapshot(
                true,
                500,
                "ABC234",
                10,
                1,
                new[]
                {
                    new OnlineRoomMemberSnapshot(10, "owner", true, OnlineRoomMemberConnectionState.Online, 1)
                },
                connectionLost,
                OnlineRoomOperationState.Idle,
                OnlineRoomResultCode.Success);
        }

        private static void SetupHarness()
        {
            CleanupHarness();
            UIHelper.ResetForTests();
            ResourceLoader.SetLoaderForTests(new LocalizationTestLoader());
            LocalizationLanguages.ResetForTests();
            var game = new Game();
            var localizationModel = new LocalizationModel();
            var accountModel = new OnlineAccountModel();
            var roomModel = new OnlineRoomModel();
            var roomChatModel = new OnlineRoomChatModel();
            var network = new FakeNetwork();
            game.RegisterModel(localizationModel);
            game.RegisterModel(accountModel);
            game.RegisterModel(roomModel);
            game.RegisterModel(roomChatModel);
            game.RegisterUtility<INetworkRuntime>(network);
            game.RegisterUtility<IOnlineAccountPreferenceStore>(new FakePreferenceStore());
            game.RegisterUtility(new LocalizationUtility());
            game.RegisterSystem(new SettingsSystem());
            game.RegisterSystem(new OnlineAccountSystem());
            game.RegisterSystem(new OnlineRoomSystem());
            game.RegisterSystem(new OnlineRoomChatSystem());
            // 大厅复用街机转场入口，语言测试提供其空快照依赖。
            game.RegisterSystem(new OnlineArcadeRunSystem());
            Init(localizationModel);
            Init(accountModel);
            Init(roomModel);
            Init(roomChatModel);
            Init(game.GetSystem<SettingsSystem>());
            Init(game.GetSystem<OnlineAccountSystem>());
            Init(game.GetSystem<OnlineRoomSystem>());
            Init(game.GetSystem<OnlineRoomChatSystem>());
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, game);
            CreateUiRoot();
            new GameObject(SceneRoot.PoolRootName);
        }

        private static void SetupLocalizationOnlyHarness(string zhCnJson, string enUsJson)
        {
            CleanupHarness();
            UIHelper.ResetForTests();
            ResourceLoader.SetLoaderForTests(new InlineLocalizationLoader(zhCnJson, enUsJson));
            LocalizationLanguages.ResetForTests();
            var game = new Game();
            var localizationModel = new LocalizationModel();
            game.RegisterModel(localizationModel);
            game.RegisterUtility(new LocalizationUtility());
            Init(localizationModel);
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, game);
        }

        private static void CleanupHarness()
        {
            UIHelper.ResetForTests();
            Architecture<Game>.OnRegisterPatch = _ => { };
            if (typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.GetValue(null) is IArchitecture architecture)
            {
                architecture.Deinit();
            }

            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
            foreach (var name in new[] { "UIRoot", "EventSystem", SceneRoot.PoolRootName })
            {
                var gameObject = GameObject.Find(name);
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
        }

        private static void Init(ICanInit target)
        {
            target.Init();
            target.Initialized = true;
        }

        private sealed class ActiveInventoryMapHandle : IMapContentHandle
        {
            public ActiveInventoryMapHandle(MapContext context) { Context = context; }
            public string MapId => MapIds.Arcade;
            public MapContext Context { get; }
            public bool IsValid => true;
        }

        private sealed class ActiveInventoryPlayerPort : IPlayerRuntimePortProvider
        {
            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port) { port = null; return false; }
            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port) { port = null; return false; }
            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider) => false;
            public bool TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot snapshot, out string error)
            {
                snapshot = new PlayerMapLocalPoseSnapshot(PlayerModel.LocalPlayerUnitId, 101, Vector3.zero, 0f, true);
                error = null;
                return true;
            }
        }

        private static void CreateUiRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UIRoot));
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
            }

            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            var eventSystem = inputModuleType != null
                ? new GameObject("EventSystem", typeof(EventSystem), inputModuleType)
                : new GameObject("EventSystem", typeof(EventSystem));
            eventSystem.transform.SetParent(root.transform, false);
        }

        private sealed class LocalizationTestLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                    case "en-US.json":
                        return new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization", path))) as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }

        private sealed class InventoryTestConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                if (id == 3)
                {
                    value = new ItemDefinition(id, "Medkit", "Restores health", "item.medkit.name", "item.medkit.desc", string.Empty, global::ItemType.Consumable, 5, global::ItemUseEffect.RestoreHealth, 30);
                    error = null;
                    return true;
                }

                value = new ItemDefinition(id, "Metal Parts", "Metal material", "item.metal_parts.name", "item.metal_parts.desc", string.Empty, global::ItemType.Material, 20, global::ItemUseEffect.None, 0);
                error = null;
                return true;
            }

            public bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
            {
                value = default;
                error = "missing weapon";
                return false;
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, id);
                error = null;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = default;
                error = "missing player";
                return false;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "missing enemy";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = default;
                error = "missing loot";
                return false;
            }
        }

        private sealed class InlineLocalizationLoader : IResourceLoader
        {
            private readonly string zhCnJson;
            private readonly string enUsJson;

            public InlineLocalizationLoader(string zhCnJson, string enUsJson)
            {
                this.zhCnJson = zhCnJson;
                this.enUsJson = enUsJson;
            }

            public T LoadAsset<T>(string path) where T : Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                        return new TextAsset(zhCnJson) as T;
                    case "en-US.json":
                        return new TextAsset(enUsJson) as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }

        private sealed class FakePreferenceStore : IOnlineAccountPreferenceStore
        {
            public string LoadLastUserName()
            {
                return string.Empty;
            }

            public bool SaveLastUserName(string userName, out string error)
            {
                error = null;
                return true;
            }
        }

        private sealed class FakeNetwork : INetworkRuntime
        {
            public bool IsInitialized { get; private set; } = true;
            public NetworkConnectionState ConnectionState { get; private set; } = NetworkConnectionState.Connected;
            public int ConnectionGeneration => 1;
            public void Initialize() => IsInitialized = true;
            public void Update() { }
            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
                where TResponse : class, IResponse
            {
                if (request is C2G_GetCurrentRoom)
                {
                    return Task.FromResult(new G2C_GetCurrentRoom { Error = 0, HasRoom = false } as TResponse);
                }

                if (request is C2G_GetRoomChatHistory)
                {
                    var response = G2C_GetRoomChatHistory.Create();
                    response.Error = 0;
                    response.RoomId = 500;
                    return Task.FromResult(response as TResponse);
                }

                return Task.FromException<TResponse>(new InvalidOperationException("Unexpected network call in UI localization ownership test."));
            }

            public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler)
                where TMessage : class, IMessage
            {
                return new Subscription();
            }

            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler)
            {
                return new Subscription();
            }

            public void Disconnect(NetworkDisconnectReason reason)
            {
                ConnectionState = NetworkConnectionState.Disconnected;
            }

            public void Dispose()
            {
                IsInitialized = false;
            }
        }

        private sealed class Subscription : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
