using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game
{
    public sealed class MapTerrainGeneratorWindow : EditorWindow
    {
        [SerializeField] private MapTerrainGenerationProfile profile;
        private MapTerrainValidationResult validation;
        private MapTerrainPreflightResult preflight;
        private MapTerrainGenerationResult generation;
        private MapTerrainSurfaceRefreshResult surfaceRefresh;
        private Vector2 scroll;

        [MenuItem("Tools/地图/通用地形工具/地图地形生成")]
        private static void OpenWindow()
        {
            GetWindow<MapTerrainGeneratorWindow>("地图地形生成");
        }

        private void OnEnable()
        {
            validation = MapTerrainGenerator.ValidateProfile(profile);
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            profile = (MapTerrainGenerationProfile)EditorGUILayout.ObjectField("生成配置", profile,
                typeof(MapTerrainGenerationProfile), false);
            if (EditorGUI.EndChangeCheck())
            {
                validation = MapTerrainGenerator.ValidateProfile(profile);
                preflight = null;
                generation = null;
                surfaceRefresh = null;
            }

            using (new EditorGUI.DisabledScope(validation?.Success != true ||
                                                profile == null || profile.SurfaceLayers == null ||
                                                profile.SurfaceLayers.Length == 0))
            {
                if (GUILayout.Button("刷新现有地图表面")) ExecuteRefreshSurface();
            }
            EditorGUILayout.LabelField("只更新 TerrainLayer 与混合权重，不修改高度或场景。",
                EditorStyles.miniLabel);

            ShowMessage(validation?.Success == true ? "配置字段校验通过。" : validation?.Error,
                validation?.Success == true ? MessageType.Info : MessageType.Error);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(validation?.Success != true))
                {
                    if (GUILayout.Button("检查")) ExecutePreflight();
                }
                using (new EditorGUI.DisabledScope(!CanGenerate()))
                {
                    if (GUILayout.Button("生成")) ExecuteGenerate();
                }
            }

            if (preflight != null)
            {
                var text = preflight.Success
                    ? "将创建场景数：" + preflight.CreateCount + "\n最终目录：" + preflight.OutputDirectory
                    : preflight.Error + "\n冲突或目标目录：" + preflight.OutputDirectory;
                ShowMessage(text, preflight.Success ? MessageType.Info : MessageType.Error);
            }
            if (generation != null)
            {
                ShowMessage(generation.Success ? "生成成功。" : generation.Error,
                    generation.Success ? MessageType.Info : MessageType.Error);
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(220f));
                foreach (var path in generation.ScenePaths) EditorGUILayout.SelectableLabel(path, GUILayout.Height(18f));
                EditorGUILayout.EndScrollView();
            }
            if (surfaceRefresh != null)
            {
                var message = surfaceRefresh.Success
                    ? "地形表面刷新成功，更新块数：" + surfaceRefresh.UpdatedCount +
                      "\n目录：" + surfaceRefresh.OutputDirectory
                    : surfaceRefresh.Error;
                ShowMessage(message, surfaceRefresh.Success ? MessageType.Info : MessageType.Error);
            }
        }

        private bool CanGenerate()
        {
            return validation?.Success == true && preflight?.Success == true;
        }

        private MapTerrainPreflightResult ExecutePreflight()
        {
            validation = MapTerrainGenerator.ValidateProfile(profile);
            preflight = MapTerrainGenerator.Preflight(profile);
            generation = null;
            surfaceRefresh = null;
            Repaint();
            return preflight;
        }

        private MapTerrainGenerationResult ExecuteGenerate()
        {
            preflight = MapTerrainGenerator.Preflight(profile);
            if (!preflight.Success)
            {
                generation = new MapTerrainGenerationResult(false, preflight.Error,
                    new List<string>(), new List<string>());
            }
            else
            {
                generation = MapTerrainGenerator.Generate(profile);
                preflight = MapTerrainGenerator.Preflight(profile);
            }
            Repaint();
            return generation;
        }

        private MapTerrainSurfaceRefreshResult ExecuteRefreshSurface()
        {
            validation = MapTerrainGenerator.ValidateProfile(profile);
            surfaceRefresh = MapTerrainGenerator.RefreshSurface(profile);
            Repaint();
            return surfaceRefresh;
        }

        private static void ShowMessage(string message, MessageType type)
        {
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, type);
        }
    }
}
