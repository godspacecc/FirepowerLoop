using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game
{
    public sealed class LocalizationJsonLanguage
    {
        public LocalizationJsonLanguage(string code, string label)
        {
            Code = code;
            Label = label;
        }

        public string Code { get; set; }
        public string Label { get; set; }
        public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Code : $"{Label} ({Code})";
    }

    public sealed class LocalizationJsonRow
    {
        private readonly Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.Ordinal);

        public LocalizationJsonRow(string key)
        {
            Key = key;
            MemberName = string.Empty;
        }

        public string Key { get; set; }
        public string MemberName { get; set; }

        public string GetText(string languageCode)
        {
            return texts.TryGetValue(languageCode, out var text) ? text : string.Empty;
        }

        public void SetText(string languageCode, string text)
        {
            texts[languageCode] = text ?? string.Empty;
        }

        public IReadOnlyDictionary<string, string> Texts => texts;
    }

    public sealed class LocalizationJsonEditorState
    {
        public LocalizationJsonEditorState(
            List<LocalizationJsonLanguage> languages,
            List<LocalizationJsonRow> rows,
            string defaultLanguageCode,
            string commentLanguageCode)
        {
            Languages = languages;
            Rows = rows;
            DefaultLanguageCode = defaultLanguageCode;
            CommentLanguageCode = commentLanguageCode;
        }

        public List<LocalizationJsonLanguage> Languages { get; }
        public List<LocalizationJsonRow> Rows { get; }
        public string DefaultLanguageCode { get; set; }
        public string CommentLanguageCode { get; set; }

        public IReadOnlyDictionary<string, string> ToJsonByLanguage()
        {
            return Languages.ToDictionary(
                language => language.Code,
                language => LocalizationJsonEditorUtility.BuildLanguageJson(Rows, language.Code),
                StringComparer.Ordinal);
        }
    }

    public static class LocalizationJsonEditorUtility
    {
        public const string LocalizationAssetRoot = LocalizationEditorPaths.ConfigFolder;
        public const string LanguageConfigPath = LocalizationEditorPaths.LanguageConfigPath;

        private static readonly Regex LanguageCodeRegex = new Regex(@"^[a-z]{2,3}(?:-[A-Z]{2})?$", RegexOptions.Compiled);

        public static LocalizationJsonEditorState LoadStateFromProject()
        {
            var languagesJson = LoadAssetText(LanguageConfigPath);
            var languageConfig = ParseLanguageConfig(languagesJson);
            var jsonByLanguage = languageConfig.Languages.ToDictionary(
                language => language.Code,
                language => LoadAssetText(GetLanguageAssetPath(language.Code)),
                StringComparer.Ordinal);
            return LoadState(languagesJson, jsonByLanguage);
        }

        public static LocalizationJsonEditorState LoadStateForTests(
            string languagesJson,
            IReadOnlyDictionary<string, string> jsonByLanguage)
        {
            return LoadState(languagesJson, jsonByLanguage);
        }

        public static IList FilterRowsForTests(IList rows, string query)
        {
            return FilterRows(rows.Cast<LocalizationJsonRow>(), query).Cast<object>().ToList();
        }

        public static List<LocalizationJsonRow> FilterRows(IEnumerable<LocalizationJsonRow> rows, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return rows.ToList();
            }

            var queryTokens = query
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeSearchText)
                .Where(token => token.Length > 0)
                .ToArray();

            if (queryTokens.Length == 0)
            {
                return rows.ToList();
            }

            return rows
                .Where(row => MatchesRow(row, queryTokens))
                .ToList();
        }

        public static void RefreshMemberNames(IEnumerable<LocalizationJsonRow> rows)
        {
            var rowList = rows.ToList();
            var nameByKey = LocalizationTextGenerator.BuildMemberNameMap(rowList.Select(row => row.Key));
            foreach (var row in rowList)
            {
                row.MemberName = nameByKey.TryGetValue(row.Key, out var name) ? name : string.Empty;
            }
        }

        public static void SaveStateToProject(LocalizationJsonEditorState state)
        {
            ValidateStateForSave(state);
            var jsonByLanguage = state.ToJsonByLanguage();
            var report = LocalizationValidator.ValidateJsonTables(jsonByLanguage);
            if (!report.IsValid)
            {
                throw new InvalidDataException(string.Join("\n", report.Issues.Select(issue => issue.ToString())));
            }

            Directory.CreateDirectory(LocalizationAssetRoot);
            File.WriteAllText(LanguageConfigPath, BuildLanguagesJson(state), Encoding.UTF8);
            foreach (var pair in jsonByLanguage)
            {
                File.WriteAllText(GetLanguageAssetPath(pair.Key), pair.Value, Encoding.UTF8);
            }

            // 语言文件已落盘；后续失败需如实报告阶段，不能声称代码已生成。
            try
            {
                AssetDatabase.Refresh();
                LocalizationTextGenerator.GenerateTextsOrThrow();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"文本表已保存，但文本常量同步未完成：{exception.Message}", exception);
            }
        }

        public static string BuildLanguagesJson(LocalizationJsonEditorState state)
        {
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine($"  \"default\": \"{EscapeJson(state.DefaultLanguageCode)}\",");
            builder.AppendLine($"  \"codeComment\": \"{EscapeJson(state.CommentLanguageCode)}\",");
            builder.AppendLine("  \"languages\": [");
            for (var i = 0; i < state.Languages.Count; i++)
            {
                var language = state.Languages[i];
                builder.AppendLine("    {");
                builder.AppendLine($"      \"code\": \"{EscapeJson(language.Code)}\",");
                builder.AppendLine($"      \"label\": \"{EscapeJson(language.Label)}\"");
                builder.Append("    }");
                if (i < state.Languages.Count - 1)
                {
                    builder.Append(",");
                }

                builder.AppendLine();
            }

            builder.AppendLine("  ]");
            builder.Append("}");
            return builder.ToString();
        }

        public static string BuildLanguageJson(IEnumerable<LocalizationJsonRow> rows, string languageCode)
        {
            var orderedRows = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Key))
                .OrderBy(row => row.Key, StringComparer.Ordinal)
                .ToList();

            var builder = new StringBuilder();
            builder.AppendLine("{");
            for (var i = 0; i < orderedRows.Count; i++)
            {
                var row = orderedRows[i];
                builder.Append($"  \"{EscapeJson(row.Key)}\": \"{EscapeJson(row.GetText(languageCode))}\"");
                if (i < orderedRows.Count - 1)
                {
                    builder.Append(",");
                }

                builder.AppendLine();
            }

            builder.Append("}");
            return builder.ToString();
        }

        private static LocalizationJsonEditorState LoadState(
            string languagesJson,
            IReadOnlyDictionary<string, string> jsonByLanguage)
        {
            var languageConfig = ParseLanguageConfig(languagesJson);
            var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var language in languageConfig.Languages)
            {
                jsonByLanguage.TryGetValue(language.Code, out var json);
                tables[language.Code] = ParseTable(json);
            }

            var keys = tables.Values
                .SelectMany(table => table.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            var rows = new List<LocalizationJsonRow>();
            foreach (var key in keys)
            {
                var row = new LocalizationJsonRow(key);
                foreach (var language in languageConfig.Languages)
                {
                    var text = tables.TryGetValue(language.Code, out var table) && table.TryGetValue(key, out var value)
                        ? value
                        : string.Empty;
                    row.SetText(language.Code, text);
                }

                rows.Add(row);
            }

            RefreshMemberNames(rows);
            return new LocalizationJsonEditorState(
                languageConfig.Languages,
                rows,
                languageConfig.DefaultLanguageCode,
                languageConfig.CommentLanguageCode);
        }

        private static LanguageConfig ParseLanguageConfig(string json)
        {
            if (!RuntimeJson.TryParseObject(json, out var root) || root["languages"] is not JArray languageNodes)
            {
                return CreateFallbackLanguageConfig();
            }

            var languages = new List<LocalizationJsonLanguage>();
            var usedCodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in languageNodes)
            {
                if (token is not JObject node)
                {
                    continue;
                }

                var code = RuntimeJson.GetString(node, "code");
                var label = RuntimeJson.GetString(node, "label");
                if (string.IsNullOrWhiteSpace(code) || usedCodes.Contains(code))
                {
                    continue;
                }

                languages.Add(new LocalizationJsonLanguage(code, string.IsNullOrWhiteSpace(label) ? code : label));
                usedCodes.Add(code);
            }

            if (languages.Count == 0)
            {
                return CreateFallbackLanguageConfig();
            }

            var defaultCode = NormalizeConfiguredLanguage(RuntimeJson.GetString(root, "default"), languages, languages[0].Code);
            var commentCode = NormalizeConfiguredLanguage(RuntimeJson.GetString(root, "codeComment"), languages, defaultCode);
            return new LanguageConfig(languages, defaultCode, commentCode);
        }

        private static LanguageConfig CreateFallbackLanguageConfig()
        {
            var languages = new List<LocalizationJsonLanguage>
            {
                new LocalizationJsonLanguage("zh-CN", "简体中文"),
                new LocalizationJsonLanguage("en-US", "English")
            };
            return new LanguageConfig(languages, "zh-CN", "zh-CN");
        }

        private static string NormalizeConfiguredLanguage(
            string languageCode,
            IReadOnlyCollection<LocalizationJsonLanguage> languages,
            string fallback)
        {
            return languages.Any(language => language.Code == languageCode) ? languageCode : fallback;
        }

        private static Dictionary<string, string> ParseTable(string json)
        {
            if (!RuntimeJson.TryParseStringTable(json, out var table))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return new Dictionary<string, string>(table, StringComparer.Ordinal);
        }

        private static void ValidateStateForSave(LocalizationJsonEditorState state)
        {
            if (state.Languages.Count == 0)
            {
                throw new InvalidDataException("至少需要一种语言。");
            }

            var languageCodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var language in state.Languages)
            {
                ValidateLanguageCode(language.Code);
                if (!languageCodes.Add(language.Code))
                {
                    throw new InvalidDataException($"语言 code 重复：{language.Code}");
                }
            }

            if (!languageCodes.Contains(state.DefaultLanguageCode))
            {
                throw new InvalidDataException($"默认语言不存在：{state.DefaultLanguageCode}");
            }

            if (!languageCodes.Contains(state.CommentLanguageCode))
            {
                throw new InvalidDataException($"注释语言不存在：{state.CommentLanguageCode}");
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in state.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.Key))
                {
                    throw new InvalidDataException("存在空 key。");
                }

                if (!keys.Add(row.Key))
                {
                    throw new InvalidDataException($"key 重复：{row.Key}");
                }
            }
        }

        public static void ValidateLanguageCode(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode) || !LanguageCodeRegex.IsMatch(languageCode))
            {
                throw new InvalidDataException($"语言 code 格式不正确：{languageCode}");
            }
        }

        private static bool MatchesRow(LocalizationJsonRow row, string[] queryTokens)
        {
            var searchFields = new List<string>
            {
                row.Key,
                row.MemberName,
                $"Texts.{row.MemberName}"
            };
            searchFields.AddRange(row.Texts.Values);

            var normalizedFields = searchFields.Select(NormalizeSearchText).ToArray();
            var combined = string.Concat(normalizedFields);

            return queryTokens.All(token => normalizedFields.Any(field => MatchesNormalized(field, token)) ||
                MatchesNormalized(combined, token));
        }

        private static bool MatchesNormalized(string value, string query)
        {
            return value.Contains(query) || IsSubsequence(value, query);
        }

        private static string NormalizeSearchText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }

            return builder.ToString();
        }

        private static bool IsSubsequence(string value, string query)
        {
            if (query.Length == 0)
            {
                return true;
            }

            var queryIndex = 0;
            foreach (var ch in value)
            {
                if (ch != query[queryIndex])
                {
                    continue;
                }

                queryIndex++;
                if (queryIndex == query.Length)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetLanguageAssetPath(string languageCode)
        {
            return $"{LocalizationAssetRoot}/{languageCode}.json";
        }

        private static string LoadAssetText(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            return asset == null ? string.Empty : asset.text;
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(ch);
                        break;
                }
            }

            return builder.ToString();
        }

        private sealed class LanguageConfig
        {
            public LanguageConfig(
                List<LocalizationJsonLanguage> languages,
                string defaultLanguageCode,
                string commentLanguageCode)
            {
                Languages = languages;
                DefaultLanguageCode = defaultLanguageCode;
                CommentLanguageCode = commentLanguageCode;
            }

            public List<LocalizationJsonLanguage> Languages { get; }
            public string DefaultLanguageCode { get; }
            public string CommentLanguageCode { get; }
        }
    }

    public sealed class LocalizationJsonEditorWindow : EditorWindow
    {
        private const float KeyWidth = 300f;
        private const float MemberWidth = 220f;
        private const float TextWidth = 280f;
        private const float RowHeight = 38f;
        private const float WindowMinWidth = 1160f;
        private const float WindowMinHeight = 620f;
        private const string MenuPath = "Tools/本地化/多语言文本编辑器";

        private LocalizationJsonEditorState state;
        private Vector2 scroll;
        private string search = string.Empty;
        private string newKey = string.Empty;
        private string newLanguageCode = string.Empty;
        private string newLanguageLabel = string.Empty;
        private string status = string.Empty;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<LocalizationJsonEditorWindow>();
            ConfigureWindowForOpen(window);
            window.Show();
        }

        private static void ConfigureWindowForOpen(LocalizationJsonEditorWindow window)
        {
            var minimumSize = new Vector2(WindowMinWidth, WindowMinHeight);
            window.titleContent = new GUIContent("本地化 JSON");
            window.minSize = minimumSize;

            var currentPosition = window.position;
            if (currentPosition.width >= minimumSize.x && currentPosition.height >= minimumSize.y)
            {
                return;
            }

            window.position = new Rect(
                currentPosition.x,
                currentPosition.y,
                Mathf.Max(currentPosition.width, minimumSize.x),
                Mathf.Max(currentPosition.height, minimumSize.y));
        }

        private void OnEnable()
        {
            Reload();
        }

        private void OnGUI()
        {
            if (state == null)
            {
                Reload();
            }

            DrawToolbar();
            DrawLanguageBar();
            DrawAddKeyBar();
            DrawTable();
            DrawStatusBar();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("重载", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                Reload();
            }

            if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                Save();
            }

            if (GUILayout.Button("校验文本表", EditorStyles.toolbarButton, GUILayout.Width(88f)))
            {
                ValidateTables();
            }

            GUILayout.Space(8f);
            GUILayout.Label("默认", GUILayout.Width(32f));
            state.DefaultLanguageCode = DrawLanguagePopup(state.DefaultLanguageCode, 150f);
            GUILayout.Label("注释", GUILayout.Width(32f));
            state.CommentLanguageCode = DrawLanguagePopup(state.CommentLanguageCode, 150f);
            GUILayout.FlexibleSpace();
            GUILayout.Label("搜索", GUILayout.Width(34f));
            search = GUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField, GUILayout.Width(280f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLanguageBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("新增语言", GUILayout.Width(60f));
            newLanguageCode = EditorGUILayout.TextField(newLanguageCode, GUILayout.Width(92f));
            newLanguageLabel = EditorGUILayout.TextField(newLanguageLabel, GUILayout.Width(140f));
            if (GUILayout.Button("添加", GUILayout.Width(52f)))
            {
                AddLanguage();
            }

            GUILayout.Space(12f);
            for (var i = 0; i < state.Languages.Count; i++)
            {
                var language = state.Languages[i];
                language.Label = EditorGUILayout.TextField(language.Label, GUILayout.Width(120f));
                GUILayout.Label(language.Code, EditorStyles.miniLabel, GUILayout.Width(56f));
                if (language.Code != state.DefaultLanguageCode && GUILayout.Button("删", GUILayout.Width(28f)))
                {
                    RemoveLanguage(language.Code);
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAddKeyBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("新增 key", GUILayout.Width(58f));
            newKey = EditorGUILayout.TextField(newKey);
            if (GUILayout.Button("添加", GUILayout.Width(52f)))
            {
                AddKey();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTable()
        {
            var rows = LocalizationJsonEditorUtility.FilterRows(state.Rows, search);
            EditorGUILayout.LabelField($"显示 {rows.Count}/{state.Rows.Count}", EditorStyles.miniLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Texts", EditorStyles.boldLabel, GUILayout.Width(MemberWidth));
            GUILayout.Label("Key", EditorStyles.boldLabel, GUILayout.Width(KeyWidth));
            foreach (var language in state.Languages)
            {
                GUILayout.Label(language.DisplayName, EditorStyles.boldLabel, GUILayout.Width(TextWidth));
            }

            GUILayout.Label(string.Empty, GUILayout.Width(36f));
            EditorGUILayout.EndHorizontal();

            foreach (var row in rows)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.SelectableLabel($"Texts.{row.MemberName}", GUILayout.Width(MemberWidth), GUILayout.Height(RowHeight));

                EditorGUI.BeginChangeCheck();
                row.Key = EditorGUILayout.TextField(row.Key, GUILayout.Width(KeyWidth), GUILayout.Height(RowHeight));
                if (EditorGUI.EndChangeCheck())
                {
                    LocalizationJsonEditorUtility.RefreshMemberNames(state.Rows);
                }

                foreach (var language in state.Languages)
                {
                    row.SetText(
                        language.Code,
                        EditorGUILayout.TextArea(row.GetText(language.Code), GUILayout.Width(TextWidth), GUILayout.Height(RowHeight)));
                }

                if (GUILayout.Button("删", GUILayout.Width(32f), GUILayout.Height(RowHeight)))
                {
                    state.Rows.Remove(row);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawStatusBar()
        {
            if (!string.IsNullOrWhiteSpace(status))
            {
                EditorGUILayout.HelpBox(status, status.StartsWith("失败", StringComparison.Ordinal) ? MessageType.Error : MessageType.Info);
            }
        }

        private string DrawLanguagePopup(string currentCode, float width)
        {
            var codes = state.Languages.Select(language => language.Code).ToArray();
            var labels = state.Languages.Select(language => language.DisplayName).ToArray();
            var index = Mathf.Max(0, Array.IndexOf(codes, currentCode));
            index = EditorGUILayout.Popup(index, labels, GUILayout.Width(width));
            return codes.Length == 0 ? currentCode : codes[index];
        }

        private void AddLanguage()
        {
            try
            {
                var code = newLanguageCode.Trim();
                LocalizationJsonEditorUtility.ValidateLanguageCode(code);
                if (state.Languages.Any(language => language.Code == code))
                {
                    throw new InvalidDataException($"语言已存在：{code}");
                }

                var label = string.IsNullOrWhiteSpace(newLanguageLabel) ? code : newLanguageLabel.Trim();
                state.Languages.Add(new LocalizationJsonLanguage(code, label));
                foreach (var row in state.Rows)
                {
                    row.SetText(code, row.GetText(state.DefaultLanguageCode));
                }

                newLanguageCode = string.Empty;
                newLanguageLabel = string.Empty;
                status = $"已添加语言 {code}，默认复制 {state.DefaultLanguageCode} 文本。";
            }
            catch (Exception exception)
            {
                status = $"失败：{exception.Message}";
            }
        }

        private void RemoveLanguage(string code)
        {
            state.Languages.RemoveAll(language => language.Code == code);
            if (state.CommentLanguageCode == code)
            {
                state.CommentLanguageCode = state.DefaultLanguageCode;
            }

            status = $"已从语言列表移除 {code}。保存后运行时不会再列出它。";
        }

        private void AddKey()
        {
            var key = newKey.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                status = "失败：key 不能为空。";
                return;
            }

            if (state.Rows.Any(row => row.Key == key))
            {
                status = $"失败：key 已存在：{key}";
                return;
            }

            var row = new LocalizationJsonRow(key);
            foreach (var language in state.Languages)
            {
                row.SetText(language.Code, string.Empty);
            }

            state.Rows.Add(row);
            state.Rows.Sort((left, right) => string.Compare(left.Key, right.Key, StringComparison.Ordinal));
            LocalizationJsonEditorUtility.RefreshMemberNames(state.Rows);
            newKey = string.Empty;
            status = $"已添加 key：{key}";
        }

        private void Reload()
        {
            try
            {
                state = LocalizationJsonEditorUtility.LoadStateFromProject();
                status = $"已载入 {state.Languages.Count} 种语言，{state.Rows.Count} 条文本。";
            }
            catch (Exception exception)
            {
                status = $"失败：{exception.Message}";
            }
        }

        private void Save()
        {
            try
            {
                LocalizationJsonEditorUtility.RefreshMemberNames(state.Rows);
                LocalizationJsonEditorUtility.SaveStateToProject(state);
                status = $"已保存 {state.Languages.Count} 种语言，{state.Rows.Count} 条文本，文本常量已同步（内容相同时不重写）。";
            }
            catch (Exception exception)
            {
                status = $"失败：{exception.Message}";
            }
        }

        private void ValidateTables()
        {
            try
            {
                LocalizationJsonEditorUtility.RefreshMemberNames(state.Rows);
                var report = LocalizationValidator.ValidateJsonTables(state.ToJsonByLanguage());
                if (!report.IsValid)
                {
                    status = $"失败：{string.Join("\n", report.Issues.Select(issue => issue.ToString()))}";
                    return;
                }

                status = $"校验通过：{state.Languages.Count} 种语言，{state.Rows.Count} 条文本。";
            }
            catch (Exception exception)
            {
                status = $"失败：{exception.Message}";
            }
        }
    }
}

