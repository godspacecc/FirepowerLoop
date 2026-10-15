using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Game
{
    public enum LocalizedTextScanSource
    {
        Scene,
        Prefab,
        Code
    }

    public enum LocalizedTextScanStatus
    {
        ChineseText,
        InternalKeyLeak,
        ReadableKeyMode,
        PreviewTextMismatch,
        KeyModeInvalid,
        CodeText
    }

    public sealed class LocalizedTextScanCandidate
    {
        public bool Selected = true;
        public LocalizedTextScanSource Source;
        public string AssetPath;
        public string ObjectPath;
        public int Line;
        public string Text;
        public TextMeshProUGUI SceneText;
        public bool HasLocalizedText;
        public string LocalizedTextKey;
        public int LocalizedTextCount;
        public bool HasComponentTexts;
        public string ExpectedDefaultText;
        public string Diagnostic;
        public LocalizedTextScanStatus Status;
        public bool CanAddComponent => Status == LocalizedTextScanStatus.ChineseText &&
                                       Source != LocalizedTextScanSource.Code &&
                                       !HasLocalizedText;
    }

    public sealed class LocalizedTextScannerWindow : EditorWindow
    {
        private const string MenuPath = "Tools/本地化/扫描本地化文本";
        private const string DefaultLanguageCode = "zh-CN";
        private static readonly Regex CSharpStringRegex =
            new Regex("\"(?<text>(?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled);
        private static readonly Regex UiKeyRegex =
            new Regex("^ui\\.[a-z0-9_.-]+$", RegexOptions.Compiled);

        private readonly List<LocalizedTextScanCandidate> candidates = new List<LocalizedTextScanCandidate>();
        private Vector2 scrollPosition;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<LocalizedTextScannerWindow>("多语言文本扫描");
            window.Scan();
        }

        public static bool ContainsChinese(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return text.Any(character => character >= 0x4E00 && character <= 0x9FFF);
        }

        public static List<LocalizedTextScanCandidate> ScanGameObject(
            GameObject root,
            string assetPath,
            LocalizedTextScanSource source)
        {
            var result = new List<LocalizedTextScanCandidate>();
            if (root == null)
            {
                return result;
            }

            var context = LocalizationScanContext.Load();
            foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (!TryCreateTextCandidate(root, text, assetPath, source, context, out var candidate))
                {
                    continue;
                }

                result.Add(candidate);
            }

            return result;
        }

        public static bool AddComponentOwnedText(TextMeshProUGUI text, string languageCode)
        {
            if (text == null || !ContainsChinese(text.text))
            {
                return false;
            }

            var sourceText = text.text;
            var localizedText = text.GetComponent<LocalizedText>();
            if (localizedText == null)
            {
                localizedText = Undo.AddComponent<LocalizedText>(text.gameObject);
            }

            Undo.RecordObject(localizedText, "Add localized text");
            localizedText.SetComponentText(languageCode, sourceText);
            text.text = sourceText;
            EditorUtility.SetDirty(localizedText);
            EditorUtility.SetDirty(text);
            return true;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("扫描", GUILayout.Width(96f)))
            {
                Scan();
            }

            if (GUILayout.Button("加入多语言组件到选中项", GUILayout.Width(180f)))
            {
                AddSelectedComponentTexts();
            }

            GUILayout.Label($"候选 {candidates.Count} 条");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "场景和 Prefab TMP 文本可一键加入 LocalizedText 组件。代码字符串只作为报告显示，需要后续改为 Loc.T(...)。",
                MessageType.Info);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (var candidate in candidates)
            {
                DrawCandidate(candidate);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawCandidate(LocalizedTextScanCandidate candidate)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            candidate.Selected = EditorGUILayout.Toggle(candidate.Selected, GUILayout.Width(20f));
            GUILayout.Label(candidate.Source.ToString(), GUILayout.Width(52f));
                    GUILayout.Label(candidate.CanAddComponent ? "可加入" : GetStatus(candidate), GUILayout.Width(96f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("文本", candidate.Text);
            EditorGUILayout.LabelField("来源", FormatLocation(candidate));
            if (!string.IsNullOrWhiteSpace(candidate.Diagnostic))
            {
                EditorGUILayout.LabelField("诊断", candidate.Diagnostic);
            }

            EditorGUILayout.EndVertical();
        }

        private void Scan()
        {
            candidates.Clear();
            candidates.AddRange(ScanOpenSceneObjects());
            candidates.AddRange(ScanPrefabAssets());
            candidates.AddRange(ScanCodeStrings());
            Repaint();
            Debug.Log($"[LocalizedTextScanner] 扫描完成，候选 {candidates.Count} 条。");
        }

        private void AddSelectedComponentTexts()
        {
            var changedCount = 0;
            foreach (var candidate in candidates.Where(candidate => candidate.Selected && candidate.CanAddComponent))
            {
                if (candidate.Source == LocalizedTextScanSource.Scene)
                {
                    if (AddComponentOwnedText(candidate.SceneText, DefaultLanguageCode))
                    {
                        changedCount++;
                    }

                    continue;
                }

                if (candidate.Source == LocalizedTextScanSource.Prefab &&
                    AddComponentOwnedTextToPrefab(candidate.AssetPath, candidate.ObjectPath, DefaultLanguageCode))
                {
                    changedCount++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Scan();
            Debug.Log($"[LocalizedTextScanner] 已加入或更新 {changedCount} 处 LocalizedText 组件。");
        }

        private static IReadOnlyList<LocalizedTextScanCandidate> ScanOpenSceneObjects()
        {
            var result = new List<LocalizedTextScanCandidate>();
            var context = LocalizationScanContext.Load();
            foreach (var text in FindObjectsOfType<TextMeshProUGUI>(true))
            {
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(text.gameObject)) &&
                    TryCreateTextCandidate(text.transform.root.gameObject, text, text.transform.root.gameObject.scene.path,
                        LocalizedTextScanSource.Scene, context, out var candidate))
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

        private static IReadOnlyList<LocalizedTextScanCandidate> ScanPrefabAssets()
        {
            var result = new List<LocalizedTextScanCandidate>();
            var context = LocalizationScanContext.Load();
            foreach (var prefabPath in Directory.GetFiles("Assets/Game/Prefabs/UI", "*.prefab", SearchOption.AllDirectories)
                         .Where(path => !path.EndsWith(".meta", StringComparison.Ordinal)))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    continue;
                }

                foreach (var text in prefab.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (TryCreateTextCandidate(prefab, text, prefabPath, LocalizedTextScanSource.Prefab, context, out var candidate))
                    {
                        result.Add(candidate);
                    }
                }
            }

            return result;
        }

        private static IReadOnlyList<LocalizedTextScanCandidate> ScanCodeStrings()
        {
            var result = new List<LocalizedTextScanCandidate>();
            foreach (var scriptPath in Directory.GetFiles("Assets/Game/Scripts", "*.cs", SearchOption.AllDirectories)
                         .Where(path => !path.EndsWith(".meta", StringComparison.Ordinal)))
            {
                var lines = File.ReadAllLines(scriptPath);
                for (var index = 0; index < lines.Length; index++)
                {
                    foreach (Match match in CSharpStringRegex.Matches(lines[index]))
                    {
                        var text = Regex.Unescape(match.Groups["text"].Value);
                        if (!ContainsChinese(text))
                        {
                            continue;
                        }

                        result.Add(new LocalizedTextScanCandidate
                        {
                            Source = LocalizedTextScanSource.Code,
                            AssetPath = scriptPath,
                            Line = index + 1,
                            Text = text,
                            Status = LocalizedTextScanStatus.CodeText,
                            Selected = false
                        });
                    }
                }
            }

            return result;
        }

        private static bool TryCreateTextCandidate(
            GameObject root,
            TextMeshProUGUI text,
            string assetPath,
            LocalizedTextScanSource source,
            LocalizationScanContext context,
            out LocalizedTextScanCandidate candidate)
        {
            candidate = null;
            if (root == null || text == null)
            {
                return false;
            }

            var localizedTexts = text.GetComponents<LocalizedText>();
            var enabledLocalizedTexts = localizedTexts.Where(item => item != null && item.enabled).ToArray();
            var keyMode = enabledLocalizedTexts.Length == 1 &&
                          !string.IsNullOrWhiteSpace(enabledLocalizedTexts[0].Key);
            var hasComponentTexts = enabledLocalizedTexts.Any(item => item.Texts != null && item.Texts.Count > 0);
            var textValue = text.text ?? string.Empty;
            var isUiKey = UiKeyRegex.IsMatch(textValue);
            var containsChinese = ContainsChinese(textValue);
            var diagnostics = new List<string>();
            LocalizedTextScanStatus status;
            string expectedDefaultText = string.Empty;

            if (isUiKey)
            {
                diagnostics.Add("内部 Key 泄漏：TMP 默认文本保存了 ui.*");
            }

            if (enabledLocalizedTexts.Length > 1)
            {
                diagnostics.Add("同对象存在多个启用的 LocalizedText");
            }
            else if (enabledLocalizedTexts.Length == 1)
            {
                var localizedText = enabledLocalizedTexts[0];
                var key = localizedText.Key ?? string.Empty;
                var hasTexts = localizedText.Texts != null && localizedText.Texts.Count > 0;
                if (string.IsNullOrWhiteSpace(key))
                {
                    if (!hasTexts)
                    {
                        diagnostics.Add("LocalizedText Key 为空且 Texts 为空");
                    }
                }
                else if (hasTexts)
                {
                    diagnostics.Add("Key 非空时 Texts 必须为空");
                }
                else if (!context.IsValid)
                {
                    diagnostics.Add(context.Error);
                }
                else if (!context.ContainsAllLanguages(key))
                {
                    diagnostics.Add("任一正式语言缺少 Key");
                }
                else
                {
                    expectedDefaultText = context.GetDefaultText(key);
                    if (textValue != expectedDefaultText)
                    {
                        diagnostics.Add("TMP 默认文本与默认语言不一致");
                    }
                }
            }

            if (isUiKey)
            {
                status = LocalizedTextScanStatus.InternalKeyLeak;
            }
            else if (diagnostics.Count > 0)
            {
                status = expectedDefaultText.Length > 0 && diagnostics.Count == 1 &&
                         diagnostics[0] == "TMP 默认文本与默认语言不一致"
                    ? LocalizedTextScanStatus.PreviewTextMismatch
                    : LocalizedTextScanStatus.KeyModeInvalid;
            }
            else if (keyMode)
            {
                status = LocalizedTextScanStatus.ReadableKeyMode;
                diagnostics.Add("可读 Key 模式");
            }
            else if (containsChinese)
            {
                status = LocalizedTextScanStatus.ChineseText;
            }
            else
            {
                return false;
            }

            candidate = new LocalizedTextScanCandidate
            {
                Source = source,
                AssetPath = assetPath,
                ObjectPath = GetTransformPath(root.transform, text.transform),
                Text = textValue,
                SceneText = source == LocalizedTextScanSource.Scene ? text : null,
                HasLocalizedText = enabledLocalizedTexts.Length > 0,
                LocalizedTextKey = keyMode ? enabledLocalizedTexts[0].Key : string.Empty,
                LocalizedTextCount = enabledLocalizedTexts.Length,
                HasComponentTexts = hasComponentTexts,
                ExpectedDefaultText = expectedDefaultText,
                Diagnostic = string.Join("；", diagnostics),
                Status = status,
                Selected = status == LocalizedTextScanStatus.ChineseText
            };
            return true;
        }

        private static bool AddComponentOwnedTextToPrefab(string prefabPath, string objectPath, string languageCode)
        {
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var target = FindTransformByPath(prefabRoot.transform, objectPath);
                var text = target != null ? target.GetComponent<TextMeshProUGUI>() : null;
                if (!AddComponentOwnedText(text, languageCode))
                {
                    return false;
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static string GetTransformPath(Transform root, Transform target)
        {
            var parts = new Stack<string>();
            var current = target;
            while (current != null)
            {
                parts.Push(current.name);
                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            return string.Join("/", parts);
        }

        private static Transform FindTransformByPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            var parts = path.Split('/');
            var current = root;
            var startIndex = parts.Length > 0 && parts[0] == root.name ? 1 : 0;
            for (var index = startIndex; index < parts.Length; index++)
            {
                current = current.Find(parts[index]);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        private static string GetStatus(LocalizedTextScanCandidate candidate)
        {
            if (candidate.Source == LocalizedTextScanSource.Code)
            {
                return "代码文案";
            }

            switch (candidate.Status)
            {
                case LocalizedTextScanStatus.InternalKeyLeak: return "内部 Key 泄漏";
                case LocalizedTextScanStatus.ReadableKeyMode: return "可读 Key";
                case LocalizedTextScanStatus.PreviewTextMismatch: return "预览不一致";
                case LocalizedTextScanStatus.KeyModeInvalid: return "Key 模式异常";
                case LocalizedTextScanStatus.ChineseText: return candidate.HasLocalizedText ? "已有组件" : "不可加入";
                default: return "不可加入";
            }
        }

        private static string FormatLocation(LocalizedTextScanCandidate candidate)
        {
            if (candidate.Source == LocalizedTextScanSource.Code)
            {
                return $"{candidate.AssetPath}:{candidate.Line}";
            }

            return $"{candidate.AssetPath} :: {candidate.ObjectPath}";
        }

        [MenuItem("CONTEXT/TextMeshProUGUI/添加本地化文本")]
        private static void AddLocalizedTextFromContext(MenuCommand command)
        {
            if (command.context is TextMeshProUGUI text)
            {
                AddComponentOwnedText(text, DefaultLanguageCode);
            }
        }

        private sealed class LocalizationScanContext
        {
            private readonly Dictionary<string, Dictionary<string, string>> tables;

            private LocalizationScanContext(
                string defaultLanguageCode,
                Dictionary<string, Dictionary<string, string>> tables,
                string error)
            {
                DefaultLanguageCode = defaultLanguageCode;
                this.tables = tables ?? new Dictionary<string, Dictionary<string, string>>();
                Error = error ?? string.Empty;
            }

            public string DefaultLanguageCode { get; }
            public string Error { get; }
            public bool IsValid => string.IsNullOrEmpty(Error);

            public static LocalizationScanContext Load()
            {
                const string directory = "Assets/Game/Config/Localization";
                var languagesPath = Path.Combine(directory, "languages.json");
                if (!File.Exists(languagesPath))
                {
                    return Invalid("缺少 languages.json");
                }

                if (!RuntimeJson.TryParseObject(File.ReadAllText(languagesPath), out var root))
                {
                    return Invalid("languages.json 无效");
                }

                var defaultLanguage = RuntimeJson.GetString(root, "default");
                var languages = root["languages"] is JArray array
                    ? array.Select(item => RuntimeJson.GetString(item, "code"))
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                    : Array.Empty<string>();
                if (string.IsNullOrWhiteSpace(defaultLanguage) || languages.Length == 0)
                {
                    return Invalid("languages.json 缺少默认语言或语言列表");
                }

                var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                foreach (var language in languages)
                {
                    var tablePath = Path.Combine(directory, $"{language}.json");
                    if (!File.Exists(tablePath))
                    {
                        return Invalid($"缺少语言表 {language}.json");
                    }

                    if (!RuntimeJson.TryParseStringTable(File.ReadAllText(tablePath), out var table))
                    {
                        return Invalid($"语言表 {language}.json 无效");
                    }

                    tables[language] = table;
                }

                if (!tables.ContainsKey(defaultLanguage))
                {
                    return Invalid($"默认语言表不存在：{defaultLanguage}");
                }

                return new LocalizationScanContext(defaultLanguage, tables, string.Empty);
            }

            public bool ContainsAllLanguages(string key)
            {
                return tables.Count > 0 && tables.Values.All(table => table.ContainsKey(key));
            }

            public string GetDefaultText(string key)
            {
                return tables.TryGetValue(DefaultLanguageCode, out var table) &&
                       table.TryGetValue(key, out var text)
                    ? text
                    : string.Empty;
            }

            private static LocalizationScanContext Invalid(string error)
            {
                return new LocalizationScanContext(LocalizedTextScannerWindow.DefaultLanguageCode, null, error);
            }
        }
    }
}

