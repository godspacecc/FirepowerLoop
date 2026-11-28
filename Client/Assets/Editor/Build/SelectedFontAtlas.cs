using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

namespace Shared.UnityTools
{
    /// <summary>只向正式字体追加已确认缺字，失败时恢复原资产和图集。</summary>
    internal static class SelectedFontAtlas
    {
        private const string CheckMenu = "Tools/本地化/检查所选字体图集";
        private const string CompleteMenu = "Tools/本地化/补全所选字体图集";

        [MenuItem(CheckMenu)]
        private static void Check() => Run(font =>
        {
            uint[] missing = MissingCodePoints(font, RequiredText());
            if (missing.Length == 0)
                Debug.Log($"[{font.name}] 所选图集覆盖全部语言表字符。", font);
            else
                Debug.LogWarning($"[{font.name}] 缺少 {missing.Length} 个字符。{Describe(missing)}", font);
        });

        [MenuItem(CompleteMenu)]
        private static void CompleteFromMenu() => Run(font =>
        {
            string required = RequiredText();
            uint[] missing = MissingCodePoints(font, required);
            if (missing.Length == 0)
            {
                Debug.Log($"[{font.name}] 图集无需补全。", font);
                return;
            }
            if (!EditorUtility.DisplayDialog("补全所选字体图集",
                $"所选图集缺少 {missing.Length} 个字符。\n{Describe(missing)}", "补全", "取消"))
                return;
            int added = Complete(font, required);
            Debug.Log($"[{font.name}] 已补全并复验 {added} 个字符。", font);
        });

        [MenuItem(CheckMenu, true)]
        private static bool CanCheck() =>
            !EditorApplication.isPlayingOrWillChangePlaymode && Selection.activeObject is TMP_FontAsset;

        [MenuItem(CompleteMenu, true)]
        private static bool CanComplete() => CanCheck() && Selection.activeObject is TMP_FontAsset font &&
            font.atlasPopulationMode == AtlasPopulationMode.Dynamic && font.sourceFontFile != null &&
            AssetDatabase.GetAssetPath(font).StartsWith("Assets/", StringComparison.Ordinal);

        public static uint[] MissingCodePoints(TMP_FontAsset font, string text)
        {
            var available = font.characterTable.Select(character => character.unicode).ToHashSet();
            var missing = new SortedSet<uint>();
            for (int index = 0; index < text.Length; index++)
            {
                if (char.IsControl(text, index))
                    continue;
                uint point = (uint)char.ConvertToUtf32(text, index);
                if (!available.Contains(point))
                    missing.Add(point);
                if (char.IsHighSurrogate(text[index]))
                    index++;
            }
            return missing.ToArray();
        }

        [MenuItem("Tools/本地化/重建所选字体图集")]
        private static void RebuildMenu() => Run(font =>
            PreviewRebuild(font, font.sourceFontFile, RequiredText, text => WriteAtlas(font, text, rebuild: true)));

        [MenuItem("Tools/本地化/重建所选字体图集", true)]
        private static bool CanRebuild() => CanComplete();

        internal static void PreviewRebuild(TMP_FontAsset font, Font source, Func<string> readText, Action<string> apply, bool? multiAtlas = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || font == null || source == null)
                throw new InvalidOperationException("请选中可重建的字体并确认源字体文件。");
            string path = AssetDatabase.GetAssetPath(font);
            string sourcePath = AssetDatabase.GetAssetPath(source);
            RequireSaved(path);
            string text = readText();
            var points = CodePoints(text);
            if (points.Length == 0)
                throw new InvalidOperationException("没有可预览的字符。");
            bool useMultipleAtlases = multiAtlas ?? font.isMultiAtlasTexturesEnabled;
            CheckSource(font, source, points, useMultipleAtlases);
            var inputs = new[] { path, path + ".meta", sourcePath, sourcePath + ".meta" };
            string fingerprint = PreparedBuildFingerprint.Capture(inputs);
            var temporary = TMP_FontAsset.CreateFontAsset(source, (int)font.faceInfo.pointSize, font.atlasPadding,
                font.atlasRenderMode, font.atlasWidth, font.atlasHeight, AtlasPopulationMode.Dynamic,
                useMultipleAtlases);
            if (temporary == null)
                throw new InvalidOperationException("无法创建临时字体。");
            Texture2D preview = null;
            try
            {
                if (!temporary.TryAddCharacters(points, out uint[] missing))
                    throw new InvalidOperationException("临时图集无法完整重建。" + Describe(missing ?? points));
                if (font.material != null && temporary.material != null)
                {
                    temporary.material.CopyPropertiesFromMaterial(font.material);
                    temporary.material.mainTexture = temporary.atlasTextures[0];
                }
                temporary.atlasPopulationMode = AtlasPopulationMode.Static;
                preview = RenderTextPreview(temporary, points);
                var window = EditorWindow.CreateInstance<SharedFontPreview>();
                window.titleContent = new GUIContent("预览字体重建");
                window.SetPreview(preview, points.Length, () =>
                {
                    RequireSaved(path);
                    if (PreparedBuildFingerprint.Capture(inputs) != fingerprint || readText() != text)
                        throw new InvalidOperationException("字体或语言表已变化，请重新预览。");
                    apply(text);
                });
                preview = null;
                window.ShowUtility();
            }
            finally
            {
                if (preview != null) Object.DestroyImmediate(preview);
                foreach (var texture in temporary.atlasTextures)
                    if (texture != null) Object.DestroyImmediate(texture);
                if (temporary.material != null) Object.DestroyImmediate(temporary.material);
                Object.DestroyImmediate(temporary);
            }
        }

        private static uint[] CodePoints(string text)
        {
            var points = new SortedSet<uint>();
            for (int index = 0; index < text.Length; index++)
            {
                if (char.IsControl(text, index))
                    continue;
                points.Add((uint)char.ConvertToUtf32(text, index));
                if (char.IsHighSurrogate(text[index])) index++;
            }
            return points.ToArray();
        }

        private static void RequireSaved(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || string.IsNullOrWhiteSpace(path) ||
                !path.StartsWith("Assets/", StringComparison.Ordinal) || !File.Exists(path) ||
                AssetDatabase.LoadAllAssetsAtPath(path).Any(EditorUtility.IsDirty))
                throw new InvalidOperationException("正式字体有未保存编辑，或当前不可修改。");
        }

        private static Texture2D RenderTextPreview(TMP_FontAsset font, uint[] points)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                var item = new GameObject("字体临时预览");
                preview.AddSingleGO(item);
                var label = item.AddComponent<TextMeshPro>();
                label.font = font;
                label.fontSize = 7;
                label.color = Color.white;
                label.alignment = TextAlignmentOptions.Center;
                label.rectTransform.sizeDelta = new Vector2(16, 7);
                label.text = string.Concat(points.Take(24).Select(value => char.ConvertFromUtf32((int)value))) + "\n" +
                    string.Concat(points.Where(value => value >= 0x3400 && value <= 0x9fff).Take(32)
                        .Select(value => char.ConvertFromUtf32((int)value)));
                label.ForceMeshUpdate();
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = 4;
                preview.camera.transform.position = new Vector3(0, 0, -10);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.08f, 0.08f, 0.08f);
                preview.BeginStaticPreview(new Rect(0, 0, 900, 420));
                preview.camera.Render();
                var image = preview.EndStaticPreview();
                if (!image.GetPixels32().Any(pixel => pixel.r > 80 || pixel.g > 80 || pixel.b > 80))
                {
                    Object.DestroyImmediate(image);
                    throw new InvalidOperationException("预览图没有显示字形，请检查字体资源。");
                }
                return image;
            }
            finally { preview.Cleanup(); }
        }


        public static int Complete(TMP_FontAsset font, string text) => WriteAtlas(font, text, rebuild: false);

        private static int WriteAtlas(TMP_FontAsset font, string text, bool rebuild)
        {
            string path = AssetDatabase.GetAssetPath(font);
            if (EditorApplication.isPlayingOrWillChangePlaymode || font == null ||
                font.atlasPopulationMode != AtlasPopulationMode.Dynamic || font.sourceFontFile == null ||
                !path.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsMainAsset(font) ||
                !AssetDatabase.IsOpenForEdit(font) || EditorUtility.IsDirty(font) ||
                font.atlasTextures.Any(texture => texture != null &&
                    (AssetDatabase.GetAssetPath(texture) != path || EditorUtility.IsDirty(texture))))
                throw new InvalidOperationException("请选中已保存、可编辑且图集纹理归属于同一文件的动态 TMP 字体。");

            uint[] missing = rebuild ? CodePoints(text) : MissingCodePoints(font, text);
            if (missing.Length == 0)
                return 0;
            CheckSource(font, font.sourceFontFile, missing, font.isMultiAtlasTexturesEnabled);

            byte[] original = File.ReadAllBytes(path);
            var originalTextures = font.atlasTextures.Where(texture => texture != null).ToArray();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(new Object[] { font }.Concat(originalTextures).ToArray(), "补全字体图集");
            try
            {
                if (rebuild)
                    font.ClearFontAssetData();
                if (!font.TryAddCharacters(missing, out uint[] remaining))
                    throw new InvalidOperationException($"图集无法容纳全部字符。{Describe(remaining ?? missing)}");

                foreach (var texture in font.atlasTextures.Where(texture => texture != null))
                    EditorUtility.SetDirty(texture);
                EditorUtility.SetDirty(font);
                AssetDatabase.SaveAssetIfDirty(font);
                Reimport(path);
                uint[] stillMissing = MissingCodePoints(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path), text);
                if (stillMissing.Length > 0)
                    throw new InvalidOperationException($"重新读取图集后仍有缺字。{Describe(stillMissing)}");
                Undo.CollapseUndoOperations(undoGroup);
                return missing.Length;
            }
            catch (Exception failure)
            {
                try
                {
                    var addedTextures = font.atlasTextures.Except(originalTextures).Where(texture => texture != null).ToArray();
                    Undo.RevertAllDownToGroup(undoGroup);
                    foreach (var texture in addedTextures)
                        Object.DestroyImmediate(texture, allowDestroyingAssets: true);
                    Restore(path, original);
                    AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path).ReadFontAssetDefinition();
                }
                catch (Exception restoreError)
                {
                    throw new AggregateException($"图集写入失败且无法自动恢复 {path}。", failure, restoreError);
                }
                throw;
            }
        }

        private static void CheckSource(TMP_FontAsset font, Font source, uint[] missing, bool multiAtlas)
        {
            var result = FontEngine.LoadFontFace(source, (int)font.faceInfo.pointSize);
            if (result != FontEngineError.Success)
                throw new InvalidOperationException("无法读取所选图集的源字体。");
            uint[] unsupported = missing.Where(point => !FontEngine.TryGetGlyphIndex(point, out _)).ToArray();
            if (unsupported.Length > 0)
                throw new InvalidOperationException($"源字体不包含所需字符。{Describe(unsupported)}");

            // TMP can keep allocating atlases when a single glyph cannot fit an empty texture.
            if (multiAtlas)
            {
                uint[] oversized = missing.Where(point =>
                    FontEngine.TryGetGlyphWithUnicodeValue(point, GlyphLoadFlags.LOAD_DEFAULT, out var glyph) &&
                    (glyph.metrics.width + 2 * font.atlasPadding >= font.atlasWidth ||
                        glyph.metrics.height + 2 * font.atlasPadding >= font.atlasHeight)).ToArray();
                if (oversized.Length > 0)
                    throw new InvalidOperationException($"字符尺寸超过单张图集容量。{Describe(oversized)}");
            }
        }

        private static void Restore(string path, byte[] original)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, original);
                File.Replace(temporary, path, null);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            Reimport(path);
        }

        private static void Reimport(string path) =>
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        private static string RequiredText()
        {
            string folder = AssetDatabase.IsValidFolder("Assets/Game/Config/Localization")
                ? "Assets/Game/Config/Localization" : "Assets/Boot/Resources/JHJCLocales";
            if (!Directory.Exists(folder))
                throw new InvalidOperationException("找不到本地化语言表：" + folder);
            var values = Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(Path.GetFileName(path), "languages.json", StringComparison.OrdinalIgnoreCase))
                .Select(path => Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path)))
                .SelectMany(table => table["entries"] is Newtonsoft.Json.Linq.JArray entries
                    ? entries.Select(entry => (string)entry["text"])
                    : table.Properties().Where(property => property.Value.Type == Newtonsoft.Json.Linq.JTokenType.String)
                        .Select(property => (string)property.Value))
                .Where(text => text != null)
                .Select(text => System.Text.RegularExpressions.Regex.Replace(text, @"(?<!\{)\{[0-9]+(?:[, :][^}]+)?\}(?!\})", ""));
            return string.Concat(values) + "0123456789.:+-";
        }

        private static string Describe(IEnumerable<uint> points) => string.Join("、",
            points.Select(point => $"{char.ConvertFromUtf32((int)point)} (U+{point:X4})"));

        private static void Run(Action<TMP_FontAsset> action)
        {
            if (!CanCheck())
                return;
            var font = (TMP_FontAsset)Selection.activeObject;
            try { action(font); }
            catch (Exception error) { Debug.LogWarning(error.Message, font); }
        }
    }

    internal sealed class SharedFontPreview : EditorWindow
    {
        private Texture2D image;
        private int characterCount;
        private Action confirm;

        internal void SetPreview(Texture2D result, int count, Action action)
        {
            image = result;
            characterCount = count;
            confirm = action;
            minSize = new Vector2(440, 280);
        }

        private void OnGUI()
        {
            if (image == null) { Close(); return; }
            Rect area = GUILayoutUtility.GetRect(400, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawPreviewTexture(area, image, null, ScaleMode.ScaleToFit);
            EditorGUILayout.LabelField($"预览字形，正式图集将包含 {characterCount} 个字符");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("取消")) { Close(); return; }
                if (GUILayout.Button("确认重建"))
                {
                    try { confirm(); }
                    catch (Exception error) { Debug.LogError("[字体] " + error.GetBaseException().Message); }
                    Close();
                }
            }
        }

        private void OnDestroy()
        {
            if (image != null) Object.DestroyImmediate(image);
        }
    }

}
