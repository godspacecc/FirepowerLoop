using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game
{
    public static class LocalizationValidatorMenu
    {
        [MenuItem("Tools/本地化/校验文本表", false, 322)]
        public static bool ValidateTables()
        {
            var jsonByLanguage = LoadJsonTables();
            var report = LocalizationValidator.ValidateJsonTables(jsonByLanguage);
            if (report.IsValid)
            {
                Debug.Log("[本地化] 文本表校验通过。");
                return true;
            }

            foreach (var issue in report.Issues)
            {
                Debug.LogError($"[本地化] {issue}");
            }

            return false;
        }

        public static IReadOnlyDictionary<string, string> LoadAllLanguageJsonForTests()
        {
            return LoadJsonTables();
        }

        internal static Dictionary<string, string> LoadJsonTables()
        {
            return new Dictionary<string, string>(
                LocalizationJsonEditorUtility.LoadStateFromProject().ToJsonByLanguage(),
                System.StringComparer.Ordinal);
        }
    }
}
