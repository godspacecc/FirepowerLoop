using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Shared.UnityTools
{
    /// <summary>按工程配置枚举场景，并保存该工具的本地搜索词。</summary>
    internal static class SceneSearch
    {
        internal static IReadOnlyList<string> Find(params string[] roots)
        {
            string[] existing = roots?.Where(AssetDatabase.IsValidFolder).ToArray();
            if (roots?.Length > 0 && existing.Length == 0)
                return Array.Empty<string>();
            string[] matches = existing?.Length > 0
                ? AssetDatabase.FindAssets("t:Scene", existing)
                : AssetDatabase.FindAssets("t:Scene");
            return matches.Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static string Load(string preference) => EditorPrefs.GetString(preference, string.Empty);

        internal static string Save(string preference, string value)
        {
            value ??= string.Empty;
            if (Load(preference) != value)
                EditorPrefs.SetString(preference, value);
            return value;
        }

        internal static bool Matches(string path, string search) =>
            string.IsNullOrWhiteSpace(search) || path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
