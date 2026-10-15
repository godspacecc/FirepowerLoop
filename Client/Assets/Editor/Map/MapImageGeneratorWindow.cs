#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game
{
    public sealed class MapImageGeneratorWindow : EditorWindow
    {
        private string selectedMapId = MapIds.Base;
        [MenuItem("Tools/地图/地图资料/生成地图资料图片")]
        public static void Open() => GetWindow<MapImageGeneratorWindow>("地图资料图片");

        private void OnGUI()
        {
            selectedMapId = EditorGUILayout.TextField("MapId", selectedMapId);
            if (GUILayout.Button("生成选中地图")) Generate(new[] { selectedMapId });
            if (GUILayout.Button("生成全部地图")) GenerateAllForAutomation();
        }

        public static void GenerateAllForAutomation()
        {
            var displays = ReadRows("map_display.csv");
            Generate(displays.Select(row => row[0]).ToArray());
        }

        /// <summary>仅生成指定地图的正式资料图和图标，并恢复原场景；不运行打包流程。</summary>
        public static void GenerateForMap(string mapId) => Generate(new[] { mapId });

        private static void Generate(IEnumerable<string> mapIds)
        {
            var original = EditorSceneManager.GetSceneManagerSetup();
            var errors = new List<string>();
            try
            {
                foreach (var mapId in mapIds)
                {
                    try { GenerateOne(mapId); }
                    catch (Exception e) { errors.Add($"{mapId}: {e.Message}"); Debug.LogError($"[MapImageGenerator] {mapId}: {e}"); }
                }
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(original);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            if (errors.Count > 0) throw new InvalidOperationException("地图图片生成未全部完成：" + string.Join(" | ", errors));
            Debug.Log("[MapImageGenerator] 全部地图资料图片生成完成。");
        }

        private static void GenerateOne(string mapId)
        {
            var map = Single(ReadRows("map.csv"), row => row[0] == mapId, mapId, "map.csv");
            var display = Single(ReadRows("map_display.csv"), row => row[0] == mapId, mapId, "map_display.csv");
            // 同一地图允许多个历史地图道具 ID，图片只需按不同图标名各生成一次。
            var iconNames = ReadRows("item.csv")
                .Where(row => row.Length > 12 && row[11] == "OpenMap" && row[12] == mapId)
                .Select(row => row[5]).Distinct(StringComparer.Ordinal).ToArray();
            if (iconNames.Length == 0) throw new InvalidOperationException($"{mapId} 在 item.csv OpenMap 中没有地图道具");
            var mainPath = SceneAssetPath(map[1]);
            RequireScene(mainPath, mapId);
            EditorSceneManager.OpenScene(mainPath, OpenSceneMode.Single);
            foreach (var part in ReadRows("map_scene_part.csv").Where(row => row[1] == mapId).OrderBy(row => int.Parse(row[3], CultureInfo.InvariantCulture)))
            {
                var partPath = SceneAssetPath(part[2]); RequireScene(partPath, mapId); EditorSceneManager.OpenScene(partPath, OpenSceneMode.Additive);
            }

            var centerX = Parse(display[2]); var centerZ = Parse(display[3]); var width = Parse(display[4]); var height = Parse(display[5]);
            if (width <= 0 || height <= 0) throw new InvalidOperationException("显示边界必须为正");
            var contentRoots = UnityEngine.Object.FindObjectsOfType<MapContext>(true)
                .Where(context => context != null && context.MapContentRoot != null && context.gameObject.scene.isLoaded)
                .Select(context => context.MapContentRoot).Distinct().ToArray();
            var contentRootStates = contentRoots.Select(root => root.activeSelf).ToArray();
            foreach (var root in contentRoots) root.SetActive(true);
            PlaceTerrainTiles(contentRoots);
            if (!TryWorldYBounds(out var minY, out var maxY))
            {
                RestoreActiveStates(contentRoots, contentRootStates);
                throw new InvalidOperationException("完整场景组没有可渲染 Terrain 或 Renderer");
            }
            var cameraObject = new GameObject("__MapImageCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var lightObject = new GameObject("__MapImageLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var render = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
            Texture2D capture = null;
            var previous = RenderTexture.active;
            try
            {
                // 河谷图标取现有固定场景的可读局部；资料底图另外生成，不把旧地形套到随机布局。
                var valley = string.Equals(mapId, MapIds.CivilisationValley, StringComparison.Ordinal);
                camera.orthographic = true; camera.orthographicSize = valley ? 30f : height * 0.5f;
                camera.aspect = valley ? 1f : width / height;
                camera.transform.SetPositionAndRotation(new Vector3(centerX, maxY + 100f, centerZ), Quaternion.Euler(90f, 0f, 0f));
                camera.nearClipPlane = 10f; camera.farClipPlane = Mathf.Max(120f, maxY - minY + 120f);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(32, 37, 43, 255);
                camera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI")); camera.targetTexture = render;
                // 局部地图俯视相机覆盖 768 米，正常 LOD 会将植被全部裁掉；只在离线拍摄期间强制显示。
                var foliageLods = string.Equals(mapId, MapIds.HeJiaVillage, StringComparison.Ordinal)
                    ? contentRoots.SelectMany(root => root.GetComponentsInChildren<LODGroup>(true)).ToArray()
                    : Array.Empty<LODGroup>();
                try
                {
                    foreach (var lod in foliageLods) lod.ForceLOD(0);
                    camera.Render();
                }
                finally
                {
                    foreach (var lod in foliageLods) if (lod != null) lod.ForceLOD(-1);
                }
                RenderTexture.active = render; capture = new Texture2D(1024, 1024, TextureFormat.RGB24, false, false);
                capture.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); capture.Apply(false, false);
                if (valley) WriteValleyBlankMap($"Assets/Game/Art/UI/Map/{display[1]}.png");
                else WriteTexture(capture, $"Assets/Game/Art/UI/Map/{display[1]}.png", 1024, false);
                foreach (var iconName in iconNames)
                    WriteTexture(capture, $"Assets/Game/Art/UI/Items/{iconName}.png", 256, true);
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                if (capture != null) DestroyImmediate(capture); render.Release(); DestroyImmediate(render);
                DestroyImmediate(lightObject); DestroyImmediate(cameraObject);
                RestoreActiveStates(contentRoots, contentRootStates);
            }
        }

        /// <summary>随机河谷的中性资料底图只含网格，不提供旧固定地形或营地位置；运行时按附近观察覆盖。</summary>
        private static void WriteValleyBlankMap(string path)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
            try
            {
                var pixels = new Color32[1024 * 1024];
                for (var y = 0; y < 1024; y++) for (var x = 0; x < 1024; x++)
                    pixels[y * 1024 + x] = x % 64 == 0 || y % 64 == 0
                        ? new Color32(39, 46, 51, 255) : new Color32(32, 37, 43, 255);
                texture.SetPixels32(pixels); texture.Apply(false);
                WriteTexture(texture, path, 1024, false);
            }
            finally { DestroyImmediate(texture); }
        }

        private static void WriteTexture(Texture2D source, string assetPath, int size, bool sprite)
        {
            var directory = Path.GetDirectoryName(assetPath); if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            Texture2D output = source;
            RenderTexture scaled = null;
            var previous = RenderTexture.active;
            if (source.width != size)
            {
                scaled = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, scaled); RenderTexture.active = scaled;
                output = new Texture2D(size, size, TextureFormat.RGB24, false, false);
                output.ReadPixels(new Rect(0, 0, size, size), 0, 0); output.Apply(false, false);
            }
            File.WriteAllBytes(assetPath, output.EncodeToPNG());
            if (!ReferenceEquals(output, source)) DestroyImmediate(output);
            if (scaled != null) RenderTexture.ReleaseTemporary(scaled); RenderTexture.active = previous;
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.spriteImportMode = sprite ? SpriteImportMode.Single : SpriteImportMode.None;
            importer.sRGBTexture = true; importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false; importer.maxTextureSize = size; importer.SaveAndReimport();
        }

        private static bool TryWorldYBounds(out float minY, out float maxY)
        {
            minY = float.PositiveInfinity; maxY = float.NegativeInfinity;
            foreach (var terrain in UnityEngine.Object.FindObjectsOfType<Terrain>(true))
            {
                if (!terrain.gameObject.scene.isLoaded || terrain.terrainData == null) continue;
                minY = Mathf.Min(minY, terrain.transform.position.y); maxY = Mathf.Max(maxY, terrain.transform.position.y + terrain.terrainData.size.y);
            }
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
            {
                if (!renderer.gameObject.scene.isLoaded) continue;
                minY = Mathf.Min(minY, renderer.bounds.min.y); maxY = Mathf.Max(maxY, renderer.bounds.max.y);
            }
            return !float.IsInfinity(minY) && !float.IsInfinity(maxY);
        }

        private static void PlaceTerrainTiles(IReadOnlyCollection<GameObject> contentRoots)
        {
            var tiles = contentRoots.SelectMany(root => root.GetComponentsInChildren<GeneratedTerrainTile>(true)).ToArray();
            if (tiles.Length <= 1) return;
            var first = tiles.SingleOrDefault(tile => tile.Row == 1 && tile.Column == 1);
            if (first == null || tiles.Length != first.TotalRows * first.TotalColumns || first.TileWorldSize <= 0f)
                throw new InvalidOperationException("多场景 Terrain 元数据不完整");
            var origin = first.GetComponentInParent<MapContext>(true).MapContentRoot.transform.position;
            foreach (var tile in tiles)
            {
                if (tile.TotalRows != first.TotalRows || tile.TotalColumns != first.TotalColumns ||
                    !Mathf.Approximately(tile.TileWorldSize, first.TileWorldSize))
                    throw new InvalidOperationException("多场景 Terrain 元数据不一致");
                var context = tile.GetComponentInParent<MapContext>(true);
                context.MapContentRoot.transform.position = origin + new Vector3(
                    (tile.Column - 1) * first.TileWorldSize, 0f, (tile.Row - 1) * first.TileWorldSize);
            }
        }

        private static void RestoreActiveStates(GameObject[] roots, bool[] states)
        {
            for (var index = 0; index < roots.Length; index++)
                if (roots[index] != null) roots[index].SetActive(states[index]);
        }

        private static string SceneAssetPath(string source) => "Assets/Game/Scenes/" + source.Trim('/') + ".unity";
        private static void RequireScene(string path, string mapId) { if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) throw new FileNotFoundException($"{mapId} 场景不存在", path); }
        private static float Parse(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        private static string[] Single(IEnumerable<string[]> rows, Func<string[], bool> predicate, string mapId, string source)
        { var found = rows.Where(predicate).ToArray(); if (found.Length != 1) throw new InvalidOperationException($"{mapId} 在 {source} 中匹配 {found.Length} 行"); return found[0]; }
        private static List<string[]> ReadRows(string fileName)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
            var path = Path.Combine(projectRoot, "Config", "DataCsv", fileName);
            if (!File.Exists(path)) throw new FileNotFoundException("CSV 不存在", path);
            return File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
                .Select(line => line.Split(',').Skip(1).Select(value => value.Trim()).ToArray()).ToList();
        }
    }
}
#endif
