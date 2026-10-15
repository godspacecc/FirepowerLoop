using UnityEditor;
using UnityEngine;
using TMPro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;

namespace Game
{
    public static class ChineseFontSetup
    {
        private const string CharsetPath = "Assets/Game/Fonts/3500hz.txt";
        private const string SourceFontPath = "Assets/Game/Fonts/Chinese/SourceHanSansCN-Normal.otf";
        private const string FontAssetPath = "Assets/Game/Fonts/Chinese/SourceHanSansCN-Normal SDF.asset";
        private static readonly HashSet<string> TextAssetExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".json",
            ".prefab",
            ".unity",
            ".txt",
            ".csv",
            ".xml",
            ".uss",
            ".uxml"
        };

        [MenuItem("Tools/本地化/选中中文字体", false, 320)]
        public static void SelectChineseFont()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(FontAssetPath);
            if (asset == null)
            {
                Debug.LogError($"[本地化] 缺少字体资源：{FontAssetPath}");
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        [MenuItem("Tools/本地化/扫描字符集", false, 325)]
        public static void ScanAndUpdateCharset()
        {
            var builder = new StringBuilder();
            var charsetAbsolutePath = Path.Combine(Application.dataPath, "..", CharsetPath);
            if (File.Exists(charsetAbsolutePath))
            {
                builder.Append(File.ReadAllText(charsetAbsolutePath));
            }

            foreach (string path in Directory.GetFiles(Application.dataPath, "*", SearchOption.AllDirectories)
                         .Select(path => "Assets" + path.Substring(Application.dataPath.Length).Replace("\\", "/"))
                         .Where(ShouldScanAssetPath))
            {
                var absolutePath = Path.Combine(Application.dataPath, "..", path);
                if (File.Exists(absolutePath))
                {
                    builder.Append(File.ReadAllText(absolutePath));
                }
            }

            File.WriteAllText(charsetAbsolutePath, NormalizeCharset(builder.ToString()), Encoding.UTF8);
            AssetDatabase.Refresh();
            Debug.Log($"[本地化] 已更新字符集：{CharsetPath}");
        }

        [MenuItem("Tools/本地化/更新中文 TMP 字体资源", false, 326)]
        public static void UpdateChineseTmpFontAsset()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fontAsset == null)
            {
                Debug.LogError($"[本地化] 缺少 TMP 字体资源：{FontAssetPath}");
                return;
            }

            var sourceFont = ResolveSourceFont(fontAsset);
            if (sourceFont == null)
            {
                Debug.LogError($"[本地化] 缺少源字体：{SourceFontPath}");
                return;
            }

            var sourceFontPath = AssetDatabase.GetAssetPath(sourceFont);
            if (string.IsNullOrWhiteSpace(sourceFontPath))
            {
                Debug.LogError("[本地化] 无法解析源字体路径。");
                return;
            }

            var charset = LoadNormalizedCharsetFromFile();
            if (string.IsNullOrEmpty(charset))
            {
                Debug.LogError($"[本地化] 字符集为空：{CharsetPath}");
                return;
            }

            var charsetCodePoints = EnumerateCodePoints(charset)
                .Where(IsRenderableCodePoint)
                .Distinct()
                .OrderBy(codePoint => codePoint)
                .ToArray();
            if (charsetCodePoints.Length == 0)
            {
                Debug.LogError($"[本地化] 字符集没有有效码点：{CharsetPath}");
                return;
            }

            if (!TryAssignSourceFont(fontAsset, sourceFont))
            {
                Debug.LogError("[本地化] 未找到 TMP 字体资源的源字体字段。");
                return;
            }

            var previousPopulationMode = fontAsset.atlasPopulationMode;
            var previousMultiAtlas = fontAsset.isMultiAtlasTexturesEnabled;
            var previousCharacters = fontAsset.characterTable.Select(item => (int)item.unicode).ToArray();
            var assetFullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FontAssetPath));
            var backupBytes = File.Exists(assetFullPath) ? File.ReadAllBytes(assetFullPath) : null;
            if (backupBytes == null || backupBytes.Length == 0)
            {
                Debug.LogError($"[本地化] 无法备份 TMP 字体资产：{FontAssetPath}");
                return;
            }

            var settings = fontAsset.creationSettings;
            settings.sourceFontFileGUID = AssetDatabase.AssetPathToGUID(sourceFontPath);
            settings.sourceFontFileName = Path.GetFileName(sourceFontPath);
            settings.characterSequence = charset;
            settings.characterSetSelectionMode = 7;

            try
            {
                fontAsset.creationSettings = settings;
                fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                fontAsset.isMultiAtlasTexturesEnabled = true;
                fontAsset.ClearFontAssetData();

                if (!fontAsset.TryAddCharacters(charset, out var missingCharacters))
                {
                    throw new InvalidOperationException("TryAddCharacters 返回 false。");
                }

                if (!string.IsNullOrEmpty(missingCharacters))
                {
                    throw new InvalidOperationException(
                        $"仍缺少字符：{FormatCodePoints(EnumerateCodePoints(missingCharacters))}");
                }

                var missingCodePoints = GetMissingCodePoints(fontAsset, charsetCodePoints);
                if (missingCodePoints.Length > 0)
                {
                    throw new InvalidOperationException(
                        $"characterTable 缺少码点：{FormatCodePoints(missingCodePoints)}");
                }

                fontAsset.atlasPopulationMode = previousPopulationMode;
                fontAsset.isMultiAtlasTexturesEnabled = true;
                fontAsset.creationSettings = settings;
                MarkFontAssetDirty(fontAsset);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(FontAssetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
                Selection.activeObject = fontAsset;
                EditorGUIUtility.PingObject(fontAsset);
                Debug.Log(
                    $"[本地化] TMP 字体重建成功：有效码点={charsetCodePoints.Length}，characterTable={fontAsset.characterTable.Count}，源字体={sourceFontPath}，目标字体={FontAssetPath}，缺失字符=0");
            }
            catch (Exception exception)
            {
                RestoreFontAssetBackup(
                    assetFullPath,
                    backupBytes,
                    previousPopulationMode,
                    previousMultiAtlas,
                    previousCharacters,
                    exception.Message);
            }
        }

        [MenuItem("Tools/本地化/应用中文 TMP 字体", false, 327)]
        public static void ApplyChineseTmpFont()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fontAsset == null)
            {
                Debug.LogError($"[本地化] 缺少 TMP 字体资源：{FontAssetPath}");
                return;
            }

            foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(text, "应用中文 TMP 字体");
                text.font = fontAsset;
                EditorUtility.SetDirty(text);
            }
        }

        public static string NormalizeCharset(string source)
        {
            return string.Concat(EnumerateCodePoints(source)
                .Where(IsRenderableCodePoint)
                .Distinct()
                .OrderBy(codePoint => codePoint)
                .Select(char.ConvertFromUtf32));
        }

        public static string LoadNormalizedCharsetFromFile()
        {
            var absolutePath = Path.Combine(Application.dataPath, "..", CharsetPath);
            return File.Exists(absolutePath) ? NormalizeCharset(File.ReadAllText(absolutePath)) : string.Empty;
        }

        public static TMP_FontAsset PrepareFontAssetForCharsetUpdate()
        {
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        }

        public static Font ResolveSourceFont()
        {
            return ResolveSourceFont(PrepareFontAssetForCharsetUpdate());
        }

        public static Font ResolveSourceFont(TMP_FontAsset fontAsset)
        {
            if (fontAsset != null)
            {
                var guid = fontAsset.creationSettings.sourceFontFileGUID;
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    var sourcePath = AssetDatabase.GUIDToAssetPath(guid);
                    var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
                    if (sourceFont != null)
                    {
                        return sourceFont;
                    }
                }
            }

            return AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        }

        public static IEnumerable<int> EnumerateCodePoints(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                yield break;
            }

            for (var index = 0; index < source.Length; index++)
            {
                var current = source[index];
                if (char.IsHighSurrogate(current) &&
                    index < source.Length - 1 &&
                    char.IsLowSurrogate(source[index + 1]))
                {
                    yield return char.ConvertToUtf32(current, source[index + 1]);
                    index++;
                    continue;
                }

                if (!char.IsSurrogate(current))
                {
                    yield return current;
                }
            }
        }

        public static bool IsRenderableCodePoint(int codePoint)
        {
            if (codePoint == 0xFEFF ||
                codePoint == 0xFFFD)
            {
                return false;
            }

            if (IsPrivateUse(codePoint))
            {
                return false;
            }

            if (codePoint <= char.MaxValue)
            {
                var character = (char)codePoint;
                if (char.IsControl(character))
                {
                    return false;
                }

                if (char.IsWhiteSpace(character) && codePoint != 0x20)
                {
                    return false;
                }

                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    return false;
                }
            }

            return codePoint >= 0 && codePoint <= 0x10FFFF;
        }

        private static void RestoreFontAssetBackup(
            string assetFullPath,
            byte[] backupBytes,
            AtlasPopulationMode previousPopulationMode,
            bool previousMultiAtlas,
            int[] previousCharacters,
            string reason)
        {
            try
            {
                File.WriteAllBytes(assetFullPath, backupBytes);
                AssetDatabase.ImportAsset(FontAssetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
                var restored = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
                var restoredCharacters = restored == null
                    ? Array.Empty<int>()
                    : restored.characterTable.Select(item => (int)item.unicode).ToArray();
                var characterTableRestored = previousCharacters
                    .OrderBy(codePoint => codePoint)
                    .SequenceEqual(restoredCharacters.OrderBy(codePoint => codePoint));
                if (restored == null ||
                    restored.atlasPopulationMode != previousPopulationMode ||
                    !characterTableRestored)
                {
                    Debug.LogError(
                        $"[本地化] TMP 字体重建失败且恢复校验异常，请人工检查：{FontAssetPath}，原因：{reason}");
                    return;
                }

                restored.isMultiAtlasTexturesEnabled = previousMultiAtlas;
                Debug.LogError(
                    $"[本地化] TMP 字体重建失败，已恢复原字体资产：{FontAssetPath}，原因：{reason}");
            }
            catch (Exception restoreException)
            {
                Debug.LogError(
                    $"[本地化] TMP 字体重建失败且恢复失败，请人工检查：{FontAssetPath}，原因：{reason}，恢复错误：{restoreException.Message}");
            }
        }

        private static int[] GetMissingCodePoints(TMP_FontAsset fontAsset, IEnumerable<int> requiredCodePoints)
        {
            var actual = fontAsset.characterTable
                .Select(item => (int)item.unicode)
                .ToHashSet();
            return requiredCodePoints
                .Where(codePoint => !actual.Contains(codePoint))
                .Distinct()
                .OrderBy(codePoint => codePoint)
                .ToArray();
        }

        private static string FormatCodePoints(IEnumerable<int> codePoints)
        {
            return string.Join(", ", codePoints
                .Distinct()
                .OrderBy(codePoint => codePoint)
                .Select(codePoint => $"{char.ConvertFromUtf32(codePoint)} U+{codePoint:X4}"));
        }

        private static bool TryAssignSourceFont(TMP_FontAsset fontAsset, Font sourceFont)
        {
            var serializedObject = new SerializedObject(fontAsset);
            var assigned = false;
            foreach (var propertyName in new[] { "m_SourceFontFile_EditorRef", "m_SourceFontFile" })
            {
                var property = serializedObject.FindProperty(propertyName);
                if (property == null ||
                    property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                property.objectReferenceValue = sourceFont;
                assigned = true;
            }

            if (assigned)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            return assigned;
        }

        private static void MarkFontAssetDirty(TMP_FontAsset fontAsset)
        {
            EditorUtility.SetDirty(fontAsset);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(FontAssetPath))
            {
                EditorUtility.SetDirty(asset);
            }
        }

        private static bool IsPrivateUse(int codePoint)
        {
            return codePoint >= 0xE000 && codePoint <= 0xF8FF ||
                codePoint >= 0xF0000 && codePoint <= 0xFFFFD ||
                codePoint >= 0x100000 && codePoint <= 0x10FFFD;
        }

        public static bool ShouldScanAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            var normalized = assetPath.Replace("\\", "/");
            var extension = Path.GetExtension(normalized);
            if (normalized.StartsWith("Assets/Boot/") ||
                normalized.StartsWith("Packages/") ||
                normalized.Contains("/Editor/") ||
                normalized.EndsWith(".meta") ||
                !TextAssetExtensions.Contains(extension))
            {
                return false;
            }

            return normalized.StartsWith("Assets/Game/Config/Localization/") ||
                   normalized.StartsWith("Assets/Game/Prefabs/UI/") ||
                   normalized.StartsWith("Assets/Game/Scenes/");
        }
    }
}
