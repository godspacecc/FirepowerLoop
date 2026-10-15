using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 在用户确认的局部卫星图上绘制相对 DEM 高差；预览和导出均为派生结果，不修改 TerrainData。
    /// </summary>
    public sealed class MapHeightAnnotationWindow : EditorWindow
    {
        public const string MenuPath = "Tools/地图/局部散图制作/高度草图（和家村卫星图）";
        public const string DefaultAssetPath =
            "Assets/Game/MapTerrainSources/HeJiaVillage/RegionAnnotations/HeJiaVillage_HeightAnnotations.asset";
        public const string BaseImageProjectPath =
            "Client/Assets/Game/Art/Map/HeJiaVillage/HeJiaVillage_GaodeSatellite_112.192228_27.634943_z17_558x699_20260927.png";
        public const string BaseImageSha256 =
            "8368c080bdd0a7c48555d0aad018793a90971a3fdb7c5b60317c2047f48207b8";
        public const string DemAssetPath =
            "Assets/Game/MapTerrainSources/HeJiaVillage/HeJiaVillage_Heightmap_RGB48.png";
        public const string DemSha256 =
            "c2502edc692ec340ebb0eeec97aeeabf06dd76b0d51c4c2db43b5b5c11d34d80";
        public const string ExportProjectPath = "Docs/Plans/RegionAnnotations/HeJiaVillage_HeightDeltaMeters.exr";
        public const string PreferencesKeyPrefix = "AnotherDay.MapHeightAnnotation.";

        private readonly List<Vector2> activeStroke = new List<Vector2>();
        private MapHeightAnnotationAsset annotation;
        private Texture2D baseTexture;
        private Texture2D demTexture;
        private Texture2D previewTexture;
        private Texture2D contourTexture;
        private Color[] demBaseColors;
        private float[] previewDelta;
        private readonly List<Vector2> contourLabels = new List<Vector2>();
        private readonly List<int> contourLabelMeters = new List<int>();
        private int toolIndex;
        private float deltaMeters = 2f;
        private float brushRadiusPixels = 12f;
        private float previewOpacity = 0.58f;
        private bool showDemTint = true;
        private bool showContours = true;
        private Vector2? hoveredImagePoint;
        private float zoom = 1f;
        private Vector2 pan;
        private bool drawing;
        private bool panning;
        private bool sourcesValidated;
        private string status;

        public MapHeightAnnotationAsset Annotation => annotation;
        public bool HasValidSources => sourcesValidated && baseTexture != null && demTexture != null && annotation != null;

        [MenuItem(MenuPath)]
        public static MapHeightAnnotationWindow OpenWindow()
        {
            var window = GetWindow<MapHeightAnnotationWindow>();
            window.titleContent = new GUIContent("和家村高度草图");
            window.minSize = new Vector2(760f, 580f);
            window.Show();
            return window;
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            LoadPreferencesForTests(PreferencesKeyPrefix);
            Undo.undoRedoPerformed += OnUndoRedo;
            LoadOrCreateForTests();
        }

        private void OnDisable()
        {
            SavePreferencesForTests(PreferencesKeyPrefix);
            Undo.undoRedoPerformed -= OnUndoRedo;
            sourcesValidated = false;
            ReleaseTextures();
            if (annotation != null && !EditorUtility.IsPersistent(annotation)) DestroyImmediate(annotation);
            annotation = null;
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (!HasValidSources)
            {
                EditorGUILayout.HelpBox(status ?? "无法加载已确认底图或 DEM。", MessageType.Error);
                return;
            }
            DrawSettings();
            var canvas = GUILayoutUtility.GetRect(320f, 10000f, 300f, 10000f,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawCanvas(canvas);
            DrawFooter();
            HandleSaveShortcut();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(annotation != null && EditorUtility.IsPersistent(annotation)
                    ? "已保存高度草图"
                    : "新高度草图", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!HasValidSources))
                {
                    if (GUILayout.Button("保存草图", EditorStyles.toolbarButton)) SaveForTests();
                    if (GUILayout.Button("导出高差 EXR", EditorStyles.toolbarButton)) ExportForTests();
                    if (GUILayout.Button("清空人工高差", EditorStyles.toolbarButton)) ClearEdits();
                }
                if (GUILayout.Button("复位视图", EditorStyles.toolbarButton))
                {
                    zoom = 1f;
                    pan = Vector2.zero;
                }
            }
        }

        private void DrawSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    toolIndex = GUILayout.Toolbar(toolIndex, new[] { "抬高", "降低", "恢复 DEM" }, GUILayout.Width(260f));
                    using (new EditorGUI.DisabledScope(toolIndex == 2))
                        deltaMeters = EditorGUILayout.Slider("每笔高差（米）", deltaMeters, 0.25f, 20f);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    brushRadiusPixels = EditorGUILayout.Slider("画笔半径（底图像素）", brushRadiusPixels, 2f, 100f);
                    var opacity = EditorGUILayout.Slider("DEM 预览透明度", previewOpacity, 0.15f, 0.9f);
                    if (!Mathf.Approximately(opacity, previewOpacity))
                    {
                        previewOpacity = opacity;
                        InvalidatePreview();
                    }
                }
                var tint = EditorGUILayout.ToggleLeft("显示 DEM 色带", showDemTint);
                if (tint != showDemTint)
                {
                    showDemTint = tint;
                    InvalidatePreview();
                }
                showContours = EditorGUILayout.ToggleLeft("显示 DEM 等高线（10 米；50 米加粗并标高）", showContours);
                EditorGUILayout.HelpBox("绿色→黄色→白色表示 DEM 由低到高；红色是人工抬高，蓝色是人工降低。等高线只辅助阅读，不能当作测绘精度。左键绘制，右键、中键或 Alt+左键拖动平移，滚轮缩放。每条笔触可撤销。",
                    MessageType.Info);
            }
        }

        private void DrawCanvas(Rect canvasRect)
        {
            EditorGUI.DrawRect(canvasRect, new Color(0.07f, 0.07f, 0.07f));
            GUI.BeginGroup(canvasRect);
            var localCanvas = new Rect(Vector2.zero, canvasRect.size);
            var imageRect = CalculateImageRect(localCanvas);
            GUI.DrawTexture(imageRect, baseTexture, ScaleMode.StretchToFill, false);
            var preview = GetPreview();
            if (preview != null) GUI.DrawTexture(imageRect, preview, ScaleMode.StretchToFill, true);
            if (showContours)
            {
                if (contourTexture == null) BuildContours();
                GUI.DrawTexture(imageRect, contourTexture, ScaleMode.StretchToFill, true);
                DrawContourLabels(imageRect);
            }
            HandleCanvasInput(localCanvas, imageRect);
            GUI.EndGroup();
        }

        private void DrawFooter()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"底图 {annotation.ImageWidth}×{annotation.ImageHeight}px；DEM 局部范围约 592m×736m；缩放 {zoom:0.00}x",
                        EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(status ?? string.Empty, EditorStyles.miniLabel);
                }
                if (hoveredImagePoint.HasValue)
                {
                    var point = hoveredImagePoint.Value;
                    var x = Mathf.Clamp(Mathf.RoundToInt(point.x), 0, annotation.ImageWidth - 1);
                    var y = Mathf.Clamp(Mathf.RoundToInt(point.y), 0, annotation.ImageHeight - 1);
                    var dem = MapHeightAnnotationRasterizer.SampleDemMeters(annotation, demTexture, point);
                    var delta = previewDelta != null ? previewDelta[y * annotation.ImageWidth + x] : 0f;
                    GUILayout.Label($"光标参考：DEM ≈{dem:0.0}m  人工 {delta:+0.0;-0.0;0.0}m  预览 ≈{dem + delta:0.0}m",
                        EditorStyles.miniLabel);
                }
            }
        }

        private void HandleCanvasInput(Rect canvas, Rect imageRect)
        {
            var current = Event.current;
            if (current.type == EventType.MouseMove || current.type == EventType.MouseDrag ||
                current.type == EventType.MouseLeaveWindow)
            {
                hoveredImagePoint = imageRect.Contains(current.mousePosition)
                    ? ToImagePoint(current.mousePosition, imageRect) : (Vector2?)null;
                Repaint();
            }
            if (!canvas.Contains(current.mousePosition) && !drawing && !panning) return;
            if (current.type == EventType.ScrollWheel && canvas.Contains(current.mousePosition))
            {
                ZoomAt(current.mousePosition, canvas, current.delta.y);
                current.Use();
                return;
            }
            var panInput = IsPanInputForTests(current.button, current.alt);
            if (current.type == EventType.MouseDown && panInput && canvas.Contains(current.mousePosition))
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
                drawing = true;
                activeStroke.Clear();
                activeStroke.Add(ToImagePoint(current.mousePosition, imageRect));
                InvalidatePreview();
                current.Use();
                return;
            }
            if (current.type == EventType.MouseDrag && drawing)
            {
                var point = ToImagePoint(current.mousePosition, imageRect);
                if (Vector2.Distance(activeStroke[activeStroke.Count - 1], point) >=
                    Mathf.Max(0.75f, brushRadiusPixels * 0.15f))
                {
                    activeStroke.Add(point);
                    InvalidatePreview();
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
                    var reset = toolIndex == 2;
                    var signedDelta = toolIndex == 1 ? -deltaMeters : deltaMeters;
                    Undo.RecordObject(annotation, reset ? "恢复 DEM 高度" : "绘制相对 DEM 高差");
                    annotation.AddStroke(reset ? MapHeightStrokeMode.ResetToDem : MapHeightStrokeMode.AddOffset,
                        signedDelta, brushRadiusPixels, activeStroke);
                    EditorUtility.SetDirty(annotation);
                    InvalidatePreview();
                    status = reset ? "已恢复笔刷区域的 DEM 基线。" : $"已记录 {signedDelta:+0.##;-0.##}m 高差笔触。";
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

        /// <summary>把本机高度画笔与视图参数保存到 EditorPrefs，不写入共享高度草图。</summary>
        public void SavePreferencesForTests(string keyPrefix)
        {
            if (string.IsNullOrEmpty(keyPrefix)) keyPrefix = PreferencesKeyPrefix;
            EditorPrefs.SetInt(keyPrefix + "ToolIndex", toolIndex);
            EditorPrefs.SetFloat(keyPrefix + "DeltaMeters", deltaMeters);
            EditorPrefs.SetFloat(keyPrefix + "BrushRadiusPixels", brushRadiusPixels);
            EditorPrefs.SetFloat(keyPrefix + "PreviewOpacity", previewOpacity);
            EditorPrefs.SetBool(keyPrefix + "ShowDemTint", showDemTint);
            EditorPrefs.SetBool(keyPrefix + "ShowContours", showContours);
            EditorPrefs.SetFloat(keyPrefix + "Zoom", zoom);
            EditorPrefs.SetFloat(keyPrefix + "PanX", pan.x);
            EditorPrefs.SetFloat(keyPrefix + "PanY", pan.y);
        }

        /// <summary>恢复本机高度画笔与视图参数，并对旧版本或异常值作安全限幅。</summary>
        public void LoadPreferencesForTests(string keyPrefix)
        {
            if (string.IsNullOrEmpty(keyPrefix)) keyPrefix = PreferencesKeyPrefix;
            toolIndex = Mathf.Clamp(EditorPrefs.GetInt(keyPrefix + "ToolIndex", toolIndex), 0, 2);
            deltaMeters = Mathf.Clamp(EditorPrefs.GetFloat(keyPrefix + "DeltaMeters", deltaMeters), 0.25f, 20f);
            brushRadiusPixels = Mathf.Clamp(EditorPrefs.GetFloat(keyPrefix + "BrushRadiusPixels", brushRadiusPixels),
                2f, 100f);
            previewOpacity = Mathf.Clamp(EditorPrefs.GetFloat(keyPrefix + "PreviewOpacity", previewOpacity),
                0.15f, 0.9f);
            showDemTint = EditorPrefs.GetBool(keyPrefix + "ShowDemTint", showDemTint);
            showContours = EditorPrefs.GetBool(keyPrefix + "ShowContours", showContours);
            zoom = Mathf.Clamp(EditorPrefs.GetFloat(keyPrefix + "Zoom", zoom), 0.35f, 8f);
            pan = new Vector2(EditorPrefs.GetFloat(keyPrefix + "PanX", pan.x),
                EditorPrefs.GetFloat(keyPrefix + "PanY", pan.y));
        }

        public bool LoadOrCreateForTests()
        {
            sourcesValidated = false;
            if (annotation != null && !EditorUtility.IsPersistent(annotation)) DestroyImmediate(annotation);
            annotation = null;
            ReleaseTextures();
            if (!LoadAndVerifyTexture(BaseImageProjectPath, BaseImageSha256, out baseTexture, out status)) return false;
            demTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(DemAssetPath);
            if (demTexture == null || !demTexture.isReadable)
            {
                status = "DEM 高度图缺失或不可读：" + DemAssetPath;
                return false;
            }
            var demAbsolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", DemAssetPath));
            if (!string.Equals(ComputeSha256(demAbsolute), DemSha256, StringComparison.OrdinalIgnoreCase))
            {
                status = "DEM SHA-256 不匹配，已停止高度编辑。";
                return false;
            }
            annotation = AssetDatabase.LoadAssetAtPath<MapHeightAnnotationAsset>(DefaultAssetPath);
            if (annotation == null)
            {
                annotation = CreateInstance<MapHeightAnnotationAsset>();
                annotation.hideFlags = HideFlags.HideAndDontSave;
                annotation.Initialize(BaseImageProjectPath, BaseImageSha256, baseTexture.width, baseTexture.height,
                    DemAssetPath, DemSha256);
            }
            if (!annotation.Validate(out status)) return false;
            if (annotation.ImageWidth != baseTexture.width || annotation.ImageHeight != baseTexture.height ||
                !string.Equals(annotation.BaseImageSha256, BaseImageSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(annotation.DemSha256, DemSha256, StringComparison.OrdinalIgnoreCase))
            {
                status = "高度草图绑定的底图或 DEM 版本不匹配。";
                return false;
            }
            sourcesValidated = true;
            status = "已加载 DEM 基线；尚未修改 Terrain。";
            return true;
        }

        public bool SaveForTests()
        {
            if (!HasValidSources || !annotation.Validate(out status)) return false;
            if (!EditorUtility.IsPersistent(annotation))
            {
                EnsureAssetFolder(Path.GetDirectoryName(DefaultAssetPath)?.Replace('\\', '/'));
                if (AssetDatabase.LoadMainAssetAtPath(DefaultAssetPath) != null)
                {
                    status = "默认高度草图资产已存在，但无法作为有效高度草图加载；请检查资产。";
                    return false;
                }
                annotation.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(annotation, DefaultAssetPath);
            }
            EditorUtility.SetDirty(annotation);
            AssetDatabase.SaveAssets();
            status = "高度草图已保存；Terrain 未改变。";
            return true;
        }

        public bool ExportForTests()
        {
            if (!SaveForTests()) return false;
            var absolute = ToAbsoluteProjectPath(ExportProjectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllBytes(absolute, MapHeightAnnotationRasterizer.EncodeDeltaExr(annotation));
            status = "已导出米制相对高差 EXR；R 通道允许负值。";
            return true;
        }

        private void ClearEdits()
        {
            if (annotation == null || annotation.Strokes.Count == 0) return;
            Undo.RecordObject(annotation, "清空人工高度草图");
            annotation.Clear();
            EditorUtility.SetDirty(annotation);
            InvalidatePreview();
            status = "已清空人工高差，可用 Ctrl+Z 撤销。";
        }

        private Texture2D GetPreview()
        {
            if (previewTexture == null)
            {
                if (demBaseColors == null)
                    demBaseColors = MapHeightAnnotationRasterizer.BuildDemBaseColors(annotation, demTexture);
                if (drawing && activeStroke.Count > 0)
                {
                    var mode = toolIndex == 2 ? MapHeightStrokeMode.ResetToDem : MapHeightStrokeMode.AddOffset;
                    var signedDelta = toolIndex == 1 ? -deltaMeters : deltaMeters;
                    previewDelta = MapHeightAnnotationRasterizer.BuildDeltaMetersWithStroke(annotation,
                        mode, signedDelta, brushRadiusPixels, activeStroke);
                }
                else previewDelta = MapHeightAnnotationRasterizer.BuildDeltaMeters(annotation);
                previewTexture = MapHeightAnnotationRasterizer.CreatePreviewFromDeltaWithMode(annotation, demTexture,
                    previewOpacity, previewDelta, demBaseColors, showDemTint);
            }
            return previewTexture;
        }

        private void BuildContours()
        {
            contourTexture = MapHeightAnnotationRasterizer.CreateContourOverlay(annotation, demTexture);
            contourLabels.Clear();
            contourLabelMeters.Clear();
            // 每条主等高线只在画布约三分之一宽度处放一个标高，避免密集重复文字。
            var x = annotation.ImageWidth / 3;
            var previous = MapHeightAnnotationRasterizer.SampleDemMeters(annotation, demTexture, new Vector2(x, 0));
            for (var y = 1; y < annotation.ImageHeight; y++)
            {
                var current = MapHeightAnnotationRasterizer.SampleDemMeters(annotation, demTexture, new Vector2(x, y));
                var lower = Mathf.CeilToInt(Mathf.Min(previous, current) / 50f);
                var upper = Mathf.FloorToInt(Mathf.Max(previous, current) / 50f);
                for (var level = lower; level <= upper; level++)
                {
                    var meters = level * 50;
                    if (contourLabelMeters.Contains(meters)) continue;
                    contourLabels.Add(new Vector2(x, y));
                    contourLabelMeters.Add(meters);
                }
                previous = current;
            }
        }

        private void DrawContourLabels(Rect imageRect)
        {
            for (var i = 0; i < contourLabels.Count; i++)
            {
                var point = contourLabels[i];
                var screen = new Vector2(imageRect.x + point.x / annotation.ImageWidth * imageRect.width,
                    imageRect.y + point.y / annotation.ImageHeight * imageRect.height);
                var labelRect = new Rect(screen.x - 22f, screen.y - 9f, 44f, 18f);
                GUI.Box(labelRect, contourLabelMeters[i] + "m", EditorStyles.helpBox);
            }
        }

        private void OnUndoRedo()
        {
            InvalidatePreview();
            status = "已应用撤销/重做。";
            Repaint();
        }

        private void InvalidatePreview()
        {
            if (previewTexture != null) DestroyImmediate(previewTexture);
            previewTexture = null;
            previewDelta = null;
            Repaint();
        }

        private void ReleaseTextures()
        {
            InvalidatePreview();
            if (baseTexture != null) DestroyImmediate(baseTexture);
            baseTexture = null;
            demTexture = null;
            demBaseColors = null;
            if (contourTexture != null) DestroyImmediate(contourTexture);
            contourTexture = null;
            contourLabels.Clear();
            contourLabelMeters.Clear();
        }

        private Rect CalculateImageRect(Rect canvas)
        {
            var fit = Mathf.Min(canvas.width / baseTexture.width, canvas.height / baseTexture.height);
            var size = new Vector2(baseTexture.width, baseTexture.height) * fit * zoom;
            return new Rect(canvas.center - size * 0.5f + pan, size);
        }

        private void ZoomAt(Vector2 mouse, Rect canvas, float delta)
        {
            var old = CalculateImageRect(canvas);
            var point = new Vector2((mouse.x - old.x) / old.width, (mouse.y - old.y) / old.height);
            zoom = Mathf.Clamp(zoom * Mathf.Exp(-delta * 0.08f), 0.35f, 8f);
            var fit = Mathf.Min(canvas.width / baseTexture.width, canvas.height / baseTexture.height);
            var size = new Vector2(baseTexture.width, baseTexture.height) * fit * zoom;
            pan = mouse - Vector2.Scale(point, size) - (canvas.center - size * 0.5f);
        }

        private Vector2 ToImagePoint(Vector2 point, Rect imageRect)
        {
            return new Vector2(Mathf.Clamp((point.x - imageRect.x) / imageRect.width * annotation.ImageWidth,
                    0f, annotation.ImageWidth - 1f),
                Mathf.Clamp((point.y - imageRect.y) / imageRect.height * annotation.ImageHeight,
                    0f, annotation.ImageHeight - 1f));
        }

        private void HandleSaveShortcut()
        {
            var current = Event.current;
            if (current.type != EventType.KeyDown || current.keyCode != KeyCode.S ||
                !current.control && !current.command) return;
            SaveForTests();
            current.Use();
        }

        private static bool LoadAndVerifyTexture(string projectPath, string sha256, out Texture2D texture,
            out string error)
        {
            texture = null;
            var absolute = ToAbsoluteProjectPath(projectPath);
            if (!File.Exists(absolute) || !string.Equals(ComputeSha256(absolute), sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "已确认卫星底图缺失或 SHA-256 不匹配。";
                return false;
            }
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            if (ImageConversion.LoadImage(texture, File.ReadAllBytes(absolute), true))
            {
                error = string.Empty;
                return true;
            }
            DestroyImmediate(texture);
            texture = null;
            error = "已确认卫星底图无法读取。";
            return false;
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string ToAbsoluteProjectPath(string path)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", path));
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
