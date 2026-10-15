using UnityEditor;
using UnityEngine;

namespace Game
{
    /// <summary>管理局部地图地物参考的 Scene 预览与编辑器回调；不保存或修改地图内容。</summary>
    public sealed class MapFeatureReferenceOverlayWindow : EditorWindow
    {
        public const string MenuPath = "Tools/地图/局部散图制作/地物参考";
        public const float DefaultOpacity = 0.45f;

        private MapFeatureReferenceOverlay overlay;
        private MapFeatureReferenceOverlay.FeatureLayer layer;
        private float opacity;
        private bool displayEnabled;
        private bool dirty;
        private bool editCallbacksRegistered;
        private bool sceneCallbackRegistered;
        private bool playModeCallbackRegistered;
        private string error;

        public int RegisteredCallbackCount =>
            (editCallbacksRegistered ? 5 : 0) + (sceneCallbackRegistered ? 1 : 0) +
            (playModeCallbackRegistered ? 1 : 0);
        public bool HasDrawableResources => overlay != null && overlay.HasDrawableResources;

        [MenuItem(MenuPath)]
        public static MapFeatureReferenceOverlayWindow OpenWindow()
        {
            var window = GetWindow<MapFeatureReferenceOverlayWindow>();
            window.titleContent = new GUIContent("和家村地物参考");
            window.Show();
            return window;
        }

        private void OnEnable()
        {
            layer = MapFeatureReferenceOverlay.FeatureLayer.Forest;
            opacity = DefaultOpacity;
            displayEnabled = true;
            dirty = true;
            error = string.Empty;
            overlay ??= new MapFeatureReferenceOverlay();
            RegisterPlayModeCallback();
            if (!EditorApplication.isPlayingOrWillChangePlaymode) RegisterEditCallbacks();
        }

        private void OnDisable()
        {
            ShutdownForTests();
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            var newDisplayEnabled = EditorGUILayout.Toggle("显示参考图", displayEnabled);
            var newLayer = (MapFeatureReferenceOverlay.FeatureLayer)EditorGUILayout.EnumPopup("图层", layer);
            var newOpacity = EditorGUILayout.Slider("透明度", opacity, 0.05f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                if (newDisplayEnabled != displayEnabled) SetDisplayEnabled(newDisplayEnabled);
                if (newLayer != layer)
                {
                    layer = newLayer;
                    MarkDirty();
                }
                if (!Mathf.Approximately(newOpacity, opacity))
                {
                    opacity = newOpacity;
                    if (!dirty && overlay != null) overlay.SetOpacity(opacity);
                    SceneView.RepaintAll();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新")) RefreshForTests();
                using (new EditorGUI.DisabledScope(!TryGetTarget(out var target, out _)))
                {
                    if (GUILayout.Button("聚焦地形")) FocusTerrain(target);
                }
            }

            EnsurePreview();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
        }

        public void InitializeForTests()
        {
            overlay ??= new MapFeatureReferenceOverlay();
            RegisterPlayModeCallback();
            if (!EditorApplication.isPlayingOrWillChangePlaymode) RegisterEditCallbacks();
        }

        public void RefreshForTests()
        {
            overlay?.Release();
            dirty = true;
            EnsurePreview();
            Repaint();
            SceneView.RepaintAll();
        }

        public void SetVisibleForTests(bool visible)
        {
            SetDisplayEnabled(visible);
        }

        public void ShutdownForTests()
        {
            UnregisterEditCallbacks();
            if (playModeCallbackRegistered)
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                playModeCallbackRegistered = false;
            }
            overlay?.Release();
            error = string.Empty;
            dirty = true;
        }

        private void RegisterPlayModeCallback()
        {
            if (playModeCallbackRegistered) return;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            playModeCallbackRegistered = true;
        }

        private void RegisterEditCallbacks()
        {
            if (!editCallbacksRegistered)
            {
                Selection.selectionChanged += OnEditorStateChanged;
                EditorApplication.hierarchyChanged += OnEditorStateChanged;
                EditorApplication.projectChanged += OnEditorStateChanged;
                AssemblyReloadEvents.beforeAssemblyReload += ShutdownForTests;
                EditorApplication.quitting += ShutdownForTests;
                editCallbacksRegistered = true;
            }
            UpdateSceneCallback();
        }

        private void UnregisterEditCallbacks()
        {
            if (sceneCallbackRegistered)
            {
                SceneView.duringSceneGui -= OnSceneGui;
                sceneCallbackRegistered = false;
            }
            if (!editCallbacksRegistered) return;
            Selection.selectionChanged -= OnEditorStateChanged;
            EditorApplication.hierarchyChanged -= OnEditorStateChanged;
            EditorApplication.projectChanged -= OnEditorStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= ShutdownForTests;
            EditorApplication.quitting -= ShutdownForTests;
            editCallbacksRegistered = false;
        }

        private void UpdateSceneCallback()
        {
            var shouldRegister = editCallbacksRegistered && displayEnabled &&
                                 !EditorApplication.isPlayingOrWillChangePlaymode;
            if (shouldRegister == sceneCallbackRegistered) return;
            if (shouldRegister) SceneView.duringSceneGui += OnSceneGui;
            else SceneView.duringSceneGui -= OnSceneGui;
            sceneCallbackRegistered = shouldRegister;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                UnregisterEditCallbacks();
                overlay?.Release();
                dirty = true;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                RegisterEditCallbacks();
                MarkDirty();
            }
        }

        private void OnEditorStateChanged()
        {
            MarkDirty();
        }

        private void MarkDirty()
        {
            dirty = true;
            overlay?.Release();
            Repaint();
            SceneView.RepaintAll();
        }

        private void SetDisplayEnabled(bool enabled)
        {
            displayEnabled = enabled;
            if (!displayEnabled)
            {
                overlay?.Release();
                error = string.Empty;
            }
            else dirty = true;
            UpdateSceneCallback();
            Repaint();
            SceneView.RepaintAll();
        }

        private void EnsurePreview()
        {
            if (!displayEnabled || EditorApplication.isPlayingOrWillChangePlaymode || !dirty) return;
            dirty = false;
            if (!TryGetTarget(out var target, out error))
            {
                overlay?.Release();
                return;
            }
            overlay ??= new MapFeatureReferenceOverlay();
            overlay.TryBuild(target, layer, opacity, out error);
        }

        private static bool TryGetTarget(out Terrain target, out string targetError)
        {
            return MapFeatureReferenceOverlay.TryResolveTerrain(Selection.activeGameObject, out target, out targetError);
        }

        private void OnSceneGui(SceneView sceneView)
        {
            EnsurePreview();
            overlay?.Draw(sceneView);
        }

        private static void FocusTerrain(Terrain target)
        {
            if (target == null || target.terrainData == null) return;
            var bounds = target.terrainData.bounds;
            bounds.center += target.transform.position;
            SceneView.lastActiveSceneView?.Frame(bounds, false);
        }
    }
}
