using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 在固定卫星底图上采集用户人工笔触；只编辑独立标注资产和派生遮罩，不接触任何场景对象。
    /// </summary>
    public sealed class MapRegionAnnotationWindow : EditorWindow
    {
        public const string MenuPath = "Tools/地图/局部散图制作/区域标注（和家村卫星图）";
        public const string DefaultAssetDirectory =
            "Assets/Game/MapTerrainSources/HeJiaVillage/RegionAnnotations";
        public const string DefaultAssetPath =
            DefaultAssetDirectory + "/HeJiaVillage_RegionAnnotations.asset";
        public const string DefaultExportProjectPath = "Docs/Plans/RegionAnnotations";
        public const string DefaultBaseImageProjectPath =
            "Client/Assets/Game/Art/Map/HeJiaVillage/HeJiaVillage_GaodeSatellite_112.192228_27.634943_z17_558x699_20260927.png";
        public const string DefaultBaseImageSha256 =
            "8368c080bdd0a7c48555d0aad018793a90971a3fdb7c5b60317c2047f48207b8";
        public const string PreferencesKeyPrefix = "AnotherDay.MapRegionAnnotation.";

        private readonly Dictionary<MapRegionLayerKind, Texture2D> overlayTextures =
            new Dictionary<MapRegionLayerKind, Texture2D>();
        private readonly Dictionary<MapRegionLayerKind, bool> layerVisibility =
            new Dictionary<MapRegionLayerKind, bool>();
        private readonly List<Vector2> activeStroke = new List<Vector2>();

        private MapRegionAnnotationAsset annotation;
        private Texture2D baseTexture;
        private Texture2D activeOverlayTexture;
        private MapRegionLayerKind selectedLayer;
        private MapRegionStrokeMode strokeMode;
        private float brushRadiusPixels = 14f;
        private float overlayOpacity = 0.48f;
        private float zoom = 1f;
        private Vector2 pan;
        private bool drawing;
        private bool panning;
        private string status;
        private string currentAssetPath;

        public MapRegionAnnotationAsset Annotation => annotation;
        public Texture2D BaseTexture => baseTexture;

        [MenuItem(MenuPath)]
        public static MapRegionAnnotationWindow OpenWindow()
        {
            var window = GetWindow<MapRegionAnnotationWindow>();
            window.titleContent = new GUIContent("和家村区域标注");
            window.minSize = new Vector2(760f, 560f);
            window.Show();
            return window;
        }

        private void OnEnable()
        {
            foreach (MapRegionLayerKind kind in Enum.GetValues(typeof(MapRegionLayerKind)))
                if (!layerVisibility.ContainsKey(kind)) layerVisibility.Add(kind, true);
            LoadPreferencesForTests(PreferencesKeyPrefix);
            Undo.undoRedoPerformed += OnUndoRedo;
            LoadLastOrWaitForBase();
        }

        private void OnDisable()
        {
            SavePreferencesForTests(PreferencesKeyPrefix);
            Undo.undoRedoPerformed -= OnUndoRedo;
            ReleaseTextures();
            if (annotation != null && !EditorUtility.IsPersistent(annotation)) DestroyImmediate(annotation);
            annotation = null;
        }

        private void OnGUI()
        {
            DrawHeader();
            if (annotation == null || baseTexture == null)
            {
                EditorGUILayout.HelpBox(string.IsNullOrEmpty(status) ? "无法加载已确认的和家村卫星底图。" : status,
                    MessageType.Error);
                return;
            }

            DrawToolSettings();
            var canvasRect = GUILayoutUtility.GetRect(320f, 10000f, 280f, 10000f,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawCanvas(canvasRect);
            DrawFooter();
            HandleSaveShortcut();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(annotation != null && EditorUtility.IsPersistent(annotation)
                    ? "已保存资产"
                    : "新标注（尚未创建资产）", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("打开标注资产", EditorStyles.toolbarButton)) SelectExistingAsset();
                using (new EditorGUI.DisabledScope(annotation == null || baseTexture == null))
                {
                    if (GUILayout.Button("保存标注", EditorStyles.toolbarButton)) SaveForTests();
                    if (GUILayout.Button("另存为", EditorStyles.toolbarButton)) SaveAsForTests();
                    if (GUILayout.Button("导出全部遮罩", EditorStyles.toolbarButton)) ExportAllMasksForTests();
                }
                if (GUILayout.Button("复位视图", EditorStyles.toolbarButton))
                {
                    zoom = 1f;
                    pan = Vector2.zero;
                }
            }
        }

        private void DrawToolSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var names = GetLayerNames();
                    var selectedIndex = Mathf.Clamp((int)selectedLayer, 0, names.Length - 1);
                    selectedLayer = (MapRegionLayerKind)EditorGUILayout.Popup("当前图层", selectedIndex, names,
                        GUILayout.MinWidth(240f));
                    strokeMode = (MapRegionStrokeMode)GUILayout.Toolbar((int)strokeMode,
                        new[] { "画笔", "橡皮" }, GUILayout.Width(140f));
                    if (GUILayout.Button("清空当前图层", GUILayout.Width(110f))) ClearSelectedLayer();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    brushRadiusPixels = EditorGUILayout.Slider("画笔半径（底图像素）", brushRadiusPixels, 2f, 100f);
                    var newOpacity = EditorGUILayout.Slider("标注透明度", overlayOpacity, 0.1f, 0.9f);
                    if (!Mathf.Approximately(newOpacity, overlayOpacity))
                    {
                        overlayOpacity = newOpacity;
                        InvalidateOverlays();
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("显示：", GUILayout.Width(40f));
                    foreach (var layer in annotation.Layers)
                    {
                        var visible = GUILayout.Toggle(layerVisibility[layer.Kind], layer.DisplayName,
                            EditorStyles.miniButton);
                        if (visible != layerVisibility[layer.Kind]) layerVisibility[layer.Kind] = visible;
                    }
                }
                EditorGUILayout.HelpBox("左键刷区域；右键、中键或 Alt+左键拖动平移；滚轮缩放。每次按下到抬起是一条可撤销笔触，Ctrl+Z/Ctrl+Y 可撤销或重做。",
                    MessageType.Info);
                EditorGUILayout.LabelField("底图锁定", annotation.BaseImageProjectPath +
                    "  |  SHA-256 " + annotation.BaseImageSha256.Substring(0, 12) + "…", EditorStyles.miniLabel);
            }
        }

        private void DrawCanvas(Rect canvasRect)
        {
            EditorGUI.DrawRect(canvasRect, new Color(0.075f, 0.075f, 0.075f, 1f));
            GUI.BeginGroup(canvasRect);
            var localCanvas = new Rect(Vector2.zero, canvasRect.size);
            var imageRect = CalculateImageRect(localCanvas);
            GUI.DrawTexture(imageRect, baseTexture, ScaleMode.StretchToFill, false);
            foreach (var layer in annotation.Layers)
            {
                if (!layerVisibility[layer.Kind]) continue;
                var overlay = GetOverlay(layer.Kind);
                if (overlay != null) GUI.DrawTexture(imageRect, overlay, ScaleMode.StretchToFill, true);
            }
            HandleCanvasInput(localCanvas, imageRect);
            GUI.EndGroup();

            if (canvasRect.Contains(Event.current.mousePosition)) Repaint();
        }

        private void DrawFooter()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"底图：{annotation.ImageWidth} × {annotation.ImageHeight}px  |  缩放：{zoom:0.00}x",
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(status)) GUILayout.Label(status, EditorStyles.miniLabel);
            }
        }

        private void HandleCanvasInput(Rect canvas, Rect imageRect)
        {
            var current = Event.current;
            if (!canvas.Contains(current.mousePosition) && !drawing && !panning) return;

            if (current.type == EventType.ScrollWheel && canvas.Contains(current.mousePosition))
            {
                ZoomAt(current.mousePosition, canvas, current.delta.y);
                current.Use();
                return;
            }

            var panButton = IsPanInputForTests(current.button, current.alt);
            if (current.type == EventType.MouseDown && panButton && canvas.Contains(current.mousePosition))
            {
                panning = true;
                current.Use();
                return;
            }
            if (current.type == EventType.MouseDrag && panning)
            {
                pan += current.delta;
                current.Use();
                return;
            }
            if (current.type == EventType.MouseUp && panning)
            {
                panning = false;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0 && !current.alt &&
                imageRect.Contains(current.mousePosition))
            {
                // 避免在隐藏的当前图层上画出用户看不到的笔触。
                layerVisibility[selectedLayer] = true;
                drawing = true;
                activeStroke.Clear();
                activeStroke.Add(ToImagePoint(current.mousePosition, imageRect));
                InvalidateActiveOverlay();
                current.Use();
                return;
            }
            if (current.type == EventType.MouseDrag && drawing)
            {
                var point = ToImagePoint(current.mousePosition, imageRect);
                var minimumSpacing = Mathf.Max(0.75f, brushRadiusPixels * 0.15f);
                if (Vector2.Distance(activeStroke[activeStroke.Count - 1], point) >= minimumSpacing)
                {
                    activeStroke.Add(point);
                    InvalidateActiveOverlay();
                }
                current.Use();
                return;
            }
            if (current.type == EventType.MouseUp && drawing)
            {
                drawing = false;
                var endpoint = ToImagePoint(current.mousePosition, imageRect);
                if (Vector2.Distance(activeStroke[activeStroke.Count - 1], endpoint) >= 0.5f)
                    activeStroke.Add(endpoint);
                if (activeStroke.Count > 0)
                {
                    Undo.RecordObject(annotation, strokeMode == MapRegionStrokeMode.Paint ? "刷区域标注" : "擦除区域标注");
                    annotation.AddStroke(selectedLayer, strokeMode, brushRadiusPixels, activeStroke);
                    EditorUtility.SetDirty(annotation);
                    InvalidateOverlays();
                    status = "已记录一条笔触；点击“保存标注”写入磁盘。";
                }
                activeStroke.Clear();
                current.Use();
            }
        }

        /// <summary>右键和中键直接平移；Alt+左键保留为触控板/单键鼠标后备操作。</summary>
        public static bool IsPanInputForTests(int mouseButton, bool alt)
        {
            return mouseButton == 1 || mouseButton == 2 || mouseButton == 0 && alt;
        }

        /// <summary>把本机交互参数保存到 EditorPrefs；不污染共享标注资产。</summary>
        public void SavePreferencesForTests(string keyPrefix)
        {
            if (string.IsNullOrEmpty(keyPrefix)) keyPrefix = PreferencesKeyPrefix;
            EditorPrefs.SetInt(keyPrefix + "SelectedLayer", (int)selectedLayer);
            EditorPrefs.SetInt(keyPrefix + "StrokeMode", (int)strokeMode);
            EditorPrefs.SetFloat(keyPrefix + "BrushRadiusPixels", brushRadiusPixels);
            EditorPrefs.SetFloat(keyPrefix + "OverlayOpacity", overlayOpacity);
            EditorPrefs.SetFloat(keyPrefix + "Zoom", zoom);
            EditorPrefs.SetFloat(keyPrefix + "PanX", pan.x);
            EditorPrefs.SetFloat(keyPrefix + "PanY", pan.y);
            foreach (MapRegionLayerKind kind in Enum.GetValues(typeof(MapRegionLayerKind)))
            {
                var visible = !layerVisibility.TryGetValue(kind, out var stored) || stored;
                EditorPrefs.SetBool(keyPrefix + "LayerVisible." + kind, visible);
            }
        }

        /// <summary>恢复本机交互参数，并对旧版本或异常值作安全限幅。</summary>
        public void LoadPreferencesForTests(string keyPrefix)
        {
            if (string.IsNullOrEmpty(keyPrefix)) keyPrefix = PreferencesKeyPrefix;
            selectedLayer = (MapRegionLayerKind)Mathf.Clamp(
                EditorPrefs.GetInt(keyPrefix + "SelectedLayer", (int)selectedLayer), 0,
                Enum.GetValues(typeof(MapRegionLayerKind)).Length - 1);
            strokeMode = (MapRegionStrokeMode)Mathf.Clamp(
                EditorPrefs.GetInt(keyPrefix + "StrokeMode", (int)strokeMode), 0,
                Enum.GetValues(typeof(MapRegionStrokeMode)).Length - 1);
            brushRadiusPixels = Mathf.Clamp(EditorPrefs.GetFloat(keyPrefix + "BrushRadiusPixels", brushRadiusPixels),
                2f, 100f);
            overlayOpacity = Mathf.Clamp(EditorPrefs.GetFloat(keyPrefix + "OverlayOpacity", overlayOpacity),
                0.1f, 0.9f);
            zoom = Mathf.Clamp(EditorPrefs.GetFloat(keyPrefix + "Zoom", zoom), 0.35f, 8f);
            pan = new Vector2(EditorPrefs.GetFloat(keyPrefix + "PanX", pan.x),
                EditorPrefs.GetFloat(keyPrefix + "PanY", pan.y));
            foreach (MapRegionLayerKind kind in Enum.GetValues(typeof(MapRegionLayerKind)))
                layerVisibility[kind] = EditorPrefs.GetBool(keyPrefix + "LayerVisible." + kind,
                    !layerVisibility.TryGetValue(kind, out var stored) || stored);
        }

        private Rect CalculateImageRect(Rect canvas)
        {
            var fitScale = Mathf.Min(canvas.width / baseTexture.width, canvas.height / baseTexture.height);
            var size = new Vector2(baseTexture.width, baseTexture.height) * fitScale * zoom;
            return new Rect(canvas.center - size * 0.5f + pan, size);
        }

        private void ZoomAt(Vector2 mouse, Rect canvas, float wheelDelta)
        {
            var oldRect = CalculateImageRect(canvas);
            var imagePoint = new Vector2((mouse.x - oldRect.x) / oldRect.width,
                (mouse.y - oldRect.y) / oldRect.height);
            zoom = Mathf.Clamp(zoom * Mathf.Exp(-wheelDelta * 0.08f), 0.35f, 8f);
            var newFitScale = Mathf.Min(canvas.width / baseTexture.width, canvas.height / baseTexture.height);
            var newSize = new Vector2(baseTexture.width, baseTexture.height) * newFitScale * zoom;
            var desiredTopLeft = mouse - Vector2.Scale(imagePoint, newSize);
            pan = desiredTopLeft - (canvas.center - newSize * 0.5f);
        }

        private Vector2 ToImagePoint(Vector2 guiPoint, Rect imageRect)
        {
            return new Vector2(Mathf.Clamp((guiPoint.x - imageRect.x) / imageRect.width * annotation.ImageWidth,
                    0f, annotation.ImageWidth - 1f),
                Mathf.Clamp((guiPoint.y - imageRect.y) / imageRect.height * annotation.ImageHeight,
                    0f, annotation.ImageHeight - 1f));
        }

        private Texture2D GetOverlay(MapRegionLayerKind kind)
        {
            if (drawing && kind == selectedLayer && activeStroke.Count > 0)
            {
                if (activeOverlayTexture == null)
                    activeOverlayTexture = MapRegionAnnotationRasterizer.CreateOverlayWithStroke(annotation, kind,
                        overlayOpacity, strokeMode, brushRadiusPixels, activeStroke);
                return activeOverlayTexture;
            }
            if (overlayTextures.TryGetValue(kind, out var texture) && texture != null) return texture;
            texture = MapRegionAnnotationRasterizer.CreateOverlay(annotation, kind, overlayOpacity);
            overlayTextures[kind] = texture;
            return texture;
        }

        private string[] GetLayerNames()
        {
            var names = new string[annotation.Layers.Count];
            for (var index = 0; index < names.Length; index++) names[index] = annotation.Layers[index].DisplayName;
            return names;
        }

        private void ClearSelectedLayer()
        {
            var layer = annotation.GetLayer(selectedLayer);
            if (layer == null || layer.Strokes.Count == 0)
            {
                status = "当前图层没有笔触。";
                return;
            }
            Undo.RecordObject(annotation, "清空区域标注图层");
            annotation.ClearLayer(selectedLayer);
            EditorUtility.SetDirty(annotation);
            InvalidateOverlays();
            status = "已清空“" + layer.DisplayName + "”；可用 Ctrl+Z 撤销。";
        }

        private void HandleSaveShortcut()
        {
            var current = Event.current;
            if (current.type != EventType.KeyDown || current.keyCode != KeyCode.S ||
                !current.control && !current.command) return;
            SaveForTests();
            current.Use();
        }

        private void LoadLastOrWaitForBase()
        {
            ReleaseTextures();
            var saved = AssetDatabase.LoadAssetAtPath<MapRegionAnnotationAsset>(DefaultAssetPath);
            if (saved != null)
            {
                LoadAssetForTests(saved);
                return;
            }
            if (!CreateBlankForTests(DefaultBaseImageProjectPath)) return;
            if (!string.Equals(annotation.BaseImageSha256, DefaultBaseImageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                status = "已确认底图的 SHA-256 与项目文件不一致，已停止编辑。";
                ReleaseTextures();
                return;
            }
            status = "已加载用户确认的正式卫星底图；点击“保存标注”创建默认资产。";
        }

        public bool LoadAssetForTests(MapRegionAnnotationAsset asset)
        {
            if (asset == null)
            {
                status = "标注资产为空。";
                return false;
            }
            if (!asset.Validate(out status)) return false;
            if (!TryLoadBaseTexture(asset.BaseImageProjectPath, asset.BaseImageSha256,
                    out var candidateTexture, out status)) return false;
            if (asset.ImageWidth != candidateTexture.width || asset.ImageHeight != candidateTexture.height)
            {
                status = "底图尺寸与标注资产锁定值不一致；为避免坐标错位已停止编辑。";
                DestroyImmediate(candidateTexture);
                return false;
            }
            // 新资产校验完成后再替换当前草图；失败时保留原画布和未保存笔触。
            var previous = annotation;
            ReleaseTextures();
            annotation = asset;
            baseTexture = candidateTexture;
            currentAssetPath = AssetDatabase.GetAssetPath(asset);
            if (previous != null && previous != asset && !EditorUtility.IsPersistent(previous))
                DestroyImmediate(previous);
            status = "已校验底图尺寸与 SHA-256 指纹，可继续编辑。";
            InvalidateOverlays();
            return true;
        }

        public bool CreateBlankForTests(string projectPath)
        {
            if (annotation != null && !EditorUtility.IsPersistent(annotation)) DestroyImmediate(annotation);
            annotation = null;
            ReleaseTextures();
            if (!TryLoadBaseTexture(projectPath, null, out baseTexture, out status)) return false;
            var absolutePath = ToAbsoluteProjectPath(projectPath);
            annotation = CreateInstance<MapRegionAnnotationAsset>();
            annotation.hideFlags = HideFlags.HideAndDontSave;
            annotation.Initialize(projectPath, ComputeSha256(absolutePath), baseTexture.width, baseTexture.height);
            currentAssetPath = string.Empty;
            status = "已建立空标注；点击“保存标注”创建默认资产。";
            return true;
        }

        public bool SaveForTests()
        {
            if (annotation == null || baseTexture == null || !annotation.Validate(out status)) return false;
            if (!EditorUtility.IsPersistent(annotation))
            {
                EnsureAssetFolder(DefaultAssetDirectory);
                if (AssetDatabase.LoadMainAssetAtPath(DefaultAssetPath) != null)
                {
                    status = "默认标注资产已存在。请点击“打开标注资产”继续编辑，或使用“另存为”保留当前草图。";
                    return false;
                }
                annotation.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(annotation, DefaultAssetPath);
                currentAssetPath = DefaultAssetPath;
            }
            else currentAssetPath = AssetDatabase.GetAssetPath(annotation);
            EditorUtility.SetDirty(annotation);
            AssetDatabase.SaveAssets();
            status = "标注已保存：" + currentAssetPath;
            return true;
        }

        public bool SaveAsForTests()
        {
            if (annotation == null || baseTexture == null || !annotation.Validate(out status)) return false;
            EnsureAssetFolder(DefaultAssetDirectory);
            var suggestedName = string.IsNullOrEmpty(annotation.name)
                ? "HeJiaVillage_RegionAnnotations_副本.asset"
                : annotation.name + "_副本.asset";
            var selectedPath = EditorUtility.SaveFilePanelInProject("区域标注另存为", suggestedName,
                "asset", "另存为会创建独立副本，并继续编辑新副本。", DefaultAssetDirectory);
            if (string.IsNullOrEmpty(selectedPath))
            {
                status = "已取消另存为；当前标注未改变。";
                return false;
            }
            var currentPath = EditorUtility.IsPersistent(annotation) ? AssetDatabase.GetAssetPath(annotation) : string.Empty;
            if (string.Equals(selectedPath, currentPath, StringComparison.Ordinal)) return SaveForTests();

            var uniquePath = AssetDatabase.GenerateUniqueAssetPath(selectedPath);
            var previous = annotation;
            var copy = Instantiate(previous);
            copy.name = Path.GetFileNameWithoutExtension(uniquePath);
            copy.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(copy, uniquePath);
            annotation = copy;
            currentAssetPath = uniquePath;
            EditorUtility.SetDirty(annotation);
            AssetDatabase.SaveAssets();
            if (!EditorUtility.IsPersistent(previous)) DestroyImmediate(previous);
            InvalidateOverlays();
            status = "标注已另存并切换到新副本：" + uniquePath;
            return true;
        }

        public bool ExportAllMasksForTests()
        {
            if (!SaveForTests()) return false;
            var exportDirectory = ToAbsoluteProjectPath(DefaultExportProjectPath);
            Directory.CreateDirectory(exportDirectory);
            foreach (var layer in annotation.Layers)
            {
                var prefix = string.IsNullOrEmpty(annotation.name) ? "HeJiaVillage_RegionAnnotations" : annotation.name;
                var filename = prefix + "_" + layer.Kind + "_Mask.png";
                File.WriteAllBytes(Path.Combine(exportDirectory, filename),
                    MapRegionAnnotationRasterizer.EncodeBinaryMaskPng(annotation, layer.Kind));
            }
            status = "已导出 5 张黑白遮罩：" + DefaultExportProjectPath;
            AssetDatabase.Refresh();
            return true;
        }

        private void OnUndoRedo()
        {
            InvalidateOverlays();
            status = "已应用撤销/重做；保存后写入磁盘。";
            Repaint();
        }

        private void InvalidateOverlays()
        {
            if (activeOverlayTexture != null) DestroyImmediate(activeOverlayTexture);
            activeOverlayTexture = null;
            foreach (var texture in overlayTextures.Values)
                if (texture != null) DestroyImmediate(texture);
            overlayTextures.Clear();
            Repaint();
        }

        private void InvalidateActiveOverlay()
        {
            if (activeOverlayTexture != null) DestroyImmediate(activeOverlayTexture);
            activeOverlayTexture = null;
            Repaint();
        }

        private void ReleaseTextures()
        {
            InvalidateOverlays();
            if (baseTexture != null) DestroyImmediate(baseTexture);
            baseTexture = null;
        }

        private void SelectExistingAsset()
        {
            var selected = EditorUtility.OpenFilePanel("打开和家村区域标注资产",
                Path.GetFullPath(Path.Combine(Application.dataPath, "Game/MapTerrainSources/HeJiaVillage/RegionAnnotations")),
                "asset");
            if (string.IsNullOrEmpty(selected)) return;
            var assetsRoot = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(selected);
            if (!fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                status = "标注资产必须位于 Client/Assets 内。";
                return;
            }
            var assetPath = "Assets/" + fullPath.Substring(assetsRoot.Length).Replace('\\', '/');
            var selectedAsset = AssetDatabase.LoadAssetAtPath<MapRegionAnnotationAsset>(assetPath);
            if (selectedAsset == null)
            {
                status = "选择的文件不是和家村区域标注资产。";
                return;
            }
            if (selectedAsset == annotation) return;
            if (annotation != null && EditorUtility.IsDirty(annotation))
            {
                if (!EditorUtility.DisplayDialog("保存当前标注后打开？",
                        "当前标注有未保存修改。保存后再打开所选资产。", "保存并打开", "取消") ||
                    !SaveForTests()) return;
            }
            LoadAssetForTests(selectedAsset);
        }

        private static bool TryLoadBaseTexture(string projectPath, string expectedSha256,
            out Texture2D texture, out string error)
        {
            texture = null;
            var absolutePath = ToAbsoluteProjectPath(projectPath);
            if (!File.Exists(absolutePath))
            {
                error = "找不到固定卫星底图：" + projectPath;
                return false;
            }
            try
            {
                if (!string.IsNullOrEmpty(expectedSha256))
                {
                    var actualSha256 = ComputeSha256(absolutePath);
                    if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "底图文件内容已变化（SHA-256 不匹配）；请恢复与标注资产匹配的原文件。";
                        return false;
                    }
                }
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    name = Path.GetFileNameWithoutExtension(projectPath),
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (ImageConversion.LoadImage(texture, File.ReadAllBytes(absolutePath), true))
                {
                    error = string.Empty;
                    return true;
                }
                DestroyImmediate(texture);
                texture = null;
                error = "卫星底图不是 Unity 可读取的图片：" + projectPath;
                return false;
            }
            catch (Exception exception)
            {
                if (texture != null) DestroyImmediate(texture);
                texture = null;
                error = "加载卫星底图失败：" + exception.Message;
                return false;
            }
        }

        private static string ToAbsoluteProjectPath(string projectPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", projectPath));
        }

        private static string ComputeSha256(string absolutePath)
        {
            using (var stream = File.OpenRead(absolutePath))
            using (var sha256 = SHA256.Create())
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1));
        }
    }
}
