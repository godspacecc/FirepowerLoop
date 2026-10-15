using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game
{
    /// <summary>局部散图的初版生成入口；显示预检、独立场景位置与打开操作。</summary>
    public sealed class MapLocalPreviewWindow : EditorWindow
    {
        private const string DefaultAnnotationPath =
            "Assets/Game/MapTerrainSources/HeJiaVillage/RegionAnnotations/HeJiaVillage_RegionAnnotations.asset";

        [SerializeField] private MapRegionAnnotationAsset annotation;
        private string status;
        private bool checkedInput;

        [MenuItem("Tools/地图/局部散图制作/生成预览地图")]
        public static MapLocalPreviewWindow OpenWindow()
        {
            return GetWindow<MapLocalPreviewWindow>("生成局部预览地图");
        }

        private void OnEnable()
        {
            annotation ??= AssetDatabase.LoadAssetAtPath<MapRegionAnnotationAsset>(DefaultAnnotationPath);
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            annotation = (MapRegionAnnotationAsset)EditorGUILayout.ObjectField("区域标注", annotation,
                typeof(MapRegionAnnotationAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                checkedInput = false;
                status = string.Empty;
            }
            EditorGUILayout.HelpBox("从现有 DEM 地形提取 768×768 米局部区域，并把已保存的区域标注显示为贴地色块。生成独立预览场景；高度草图尚未接入。",
                MessageType.Info);
            if (GUILayout.Button("检查来源与输出目录"))
            {
                checkedInput = MapLocalPreviewGenerator.TryPreflight(annotation, out status);
                if (checkedInput) status = "来源检查通过；目标目录可用。";
            }
            using (new EditorGUI.DisabledScope(!checkedInput))
            {
                if (GUILayout.Button("生成预览地图"))
                {
                    checkedInput = false;
                    if (MapLocalPreviewGenerator.TryGenerate(annotation, out var result))
                        status = "已生成：" + result;
                    else status = result;
                }
            }
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MapLocalPreviewGenerator.ScenePath);
            using (new EditorGUI.DisabledScope(scene == null))
            {
                if (GUILayout.Button("打开预览场景"))
                {
                    EditorSceneManager.OpenScene(MapLocalPreviewGenerator.ScenePath);
                    SceneView.lastActiveSceneView?.Frame(new Bounds(
                        new Vector3(MapLocalPreviewGenerator.WorldSize / 2f, 165f,
                            MapLocalPreviewGenerator.WorldSize / 2f),
                        new Vector3(MapLocalPreviewGenerator.WorldSize, 330f,
                            MapLocalPreviewGenerator.WorldSize)), false);
                }
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status,
                checkedInput || status.StartsWith("已生成") ? MessageType.Info : MessageType.Warning);
        }
    }
}
