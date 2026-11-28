using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Shared.UnityTools
{
    /// <summary>共用文案字形检测、语言表键集合和格式占位符检查。</summary>
    internal static class TextTableAudit
    {
        internal const string ScanMenu = "Tools/本地化/检查界面本地化";
        internal const string TableMenu = "Tools/本地化/校验文本表";
        private static readonly Regex Placeholders = new(@"(?<!\{)\{\s*([0-9]+)(?:[, :][^}]*)?\}(?!\})");
        private static readonly Regex UiKey = new(@"^ui\.[a-z0-9_.-]+$");

        internal enum TextStatus { None, InternalKeyLeak, PreviewTextMismatch, KeyModeInvalid, ReadableKeyMode, ChineseText }

        internal static (TextStatus Status, string Diagnostic, string Expected) Classify(string text, int bindingCount,
            string key, bool hasOwnedTexts, string tableError, Func<string, bool> containsKey, Func<string, string> defaultText)
        {
            text ??= string.Empty;
            var diagnostics = new List<string>();
            bool leaksKey = UiKey.IsMatch(text);
            if (leaksKey)
                diagnostics.Add("内部 Key 泄漏：TMP 默认文本保存了 ui.*");
            string expected = string.Empty;
            const string mismatch = "TMP 默认文本与默认语言不一致";
            if (bindingCount > 1)
                diagnostics.Add("同对象存在多个启用的 LocalizedText");
            else if (bindingCount == 1)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    if (!hasOwnedTexts) diagnostics.Add("LocalizedText Key 为空且 Texts 为空");
                }
                else if (hasOwnedTexts) diagnostics.Add("Key 非空时 Texts 必须为空");
                else if (!string.IsNullOrEmpty(tableError)) diagnostics.Add(tableError);
                else if (!containsKey(key)) diagnostics.Add("任一正式语言缺少 Key");
                else
                {
                    expected = defaultText(key);
                    if (text != expected) diagnostics.Add(mismatch);
                }
            }
            if (leaksKey) return (TextStatus.InternalKeyLeak, string.Join("；", diagnostics), expected);
            if (diagnostics.Count > 0)
                return (expected.Length > 0 && diagnostics.Count == 1 && diagnostics[0] == mismatch
                    ? TextStatus.PreviewTextMismatch : TextStatus.KeyModeInvalid, string.Join("；", diagnostics), expected);
            if (bindingCount == 1 && !string.IsNullOrWhiteSpace(key))
                return (TextStatus.ReadableKeyMode, "可读 Key 模式", expected);
            return (ContainsChinese(text) ? TextStatus.ChineseText : TextStatus.None, "", expected);
        }

        internal static bool ContainsChinese(string text) =>
            !string.IsNullOrEmpty(text) && text.Any(value => value >= '\u3400' && value <= '\u9fff');

        internal static bool ValidateAndLog(IReadOnlyDictionary<string, string> languageJson)
        {
            try
            {
                string[] issues = Validate(languageJson);
                foreach (string issue in issues)
                    Debug.LogError("[本地化] " + issue);
                return issues.Length == 0;
            }
            catch (Exception error)
            {
                Debug.LogError("[本地化] " + error.GetBaseException().Message);
                return false;
            }
        }

        private static string[] Validate(IReadOnlyDictionary<string, string> languageJson)
        {
            if (languageJson.Count == 0)
                return new[] { "找不到语言表。" };
            var tables = languageJson.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => Read(pair.Value));
            var keys = tables.Values.SelectMany(table => table.Keys).Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal);
            var issues = new List<string>();
            foreach (string key in keys)
            {
                string referenceLanguage = null;
                HashSet<string> reference = null;
                foreach (var language in tables)
                {
                    if (!language.Value.TryGetValue(key, out string text))
                    {
                        issues.Add($"语言 {language.Key} 缺少文字键 {key}。");
                        continue;
                    }

                    var arguments = Placeholders.Matches(text).Cast<Match>()
                        .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
                    if (reference == null)
                    {
                        reference = arguments;
                        referenceLanguage = language.Key;
                    }
                    else if (!reference.SetEquals(arguments))
                        issues.Add($"语言 {language.Key} 的 {key} 占位符与 {referenceLanguage} 不一致。");
                }
            }
            return issues.ToArray();
        }

        internal static Dictionary<string, string> Read(string json)
        {
            var parsed = JObject.Parse(json, new JsonLoadSettings
                { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            if (parsed["entries"] is JArray entries)
            {
                foreach (var entry in entries)
                    Add((string)entry["key"], entry["text"]);
            }
            else
            {
                foreach (var entry in parsed.Properties())
                    Add(entry.Name, entry.Value);
            }
            return table;

            void Add(string key, JToken text)
            {
                if (string.IsNullOrWhiteSpace(key) || text?.Type != JTokenType.String || table.ContainsKey(key))
                    throw new InvalidDataException("语言表含有空键、重复键或非文本值：" + key);
                table.Add(key, (string)text);
            }
        }
    }
}
