using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Game;
using FirepowerLoop.Arcade;
using Luban;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>在临时加载的正式街机场景中复现外围旧墙；只输出检查记录和候选图，不保存场景或资源。</summary>
public static class ArenaBoundaryAuditPreview
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] WallNames = { "NorthBoundary", "SouthBoundary", "EastBoundary", "WestBoundary" };
    private static string Output => Path.GetFullPath("../Backup/Artifacts/ArenaBoundary-v1-20261023");
    private static void Set(object view, string name, object value) => view.GetType().GetField(name, Hidden).SetValue(view, value);

    /// <summary>磁盘边界同步读取正式Prefab，保留实际控制器创建、显隐和释放行为。</summary>
    private sealed class FormalLoader : IResourceLoader
    {
        public T LoadAsset<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + path + ".prefab");
        public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> complete) where T : UnityEngine.Object
        { complete(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
        public void Release(ResourceLoadHandle handle) { }
    }

    /// <summary>逐一检查全部正式轮布局，再按主题生成同机位现状／仅隐藏旧墙的临时候选。</summary>
    public static string Run() => RunCore(false);

    /// <summary>只记录正式修复自身的效果，不执行候选隐藏，也不覆盖用户已确认的修复前截图。</summary>
    public static string RunApplied() => RunCore(true);

    private static string RunCore(bool applied)
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) return "BUSY: 需空闲编辑态";
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/FirepowerLoop/Client/Assets")) throw new InvalidOperationException("项目路径不匹配");
        Directory.CreateDirectory(Output);
        if (applied) Directory.CreateDirectory(Path.Combine(Output, "Applied"));
        var previousLoader = ResourceLoader.Current;
        var prior = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/TaskSampleMap.unity", OpenSceneMode.Additive);
        var view = new ArcadeCombatViewController();
        var rows = new List<string> { "layout|afterLayout|afterOpeningRelease|candidate|restored" };
        int regressions = 0;
        try
        {
            SceneManager.SetActiveScene(scene);
            ResourceLoader.Use(new FormalLoader());
            var map = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MapContext>(true)).Single();
            map.SetMapContentActive(true);
            var walls = map.GetComponentsInChildren<Renderer>(true).Where(r => WallNames.Contains(r.name)).ToArray();
            var baseRenderers = map.GetComponentsInChildren<Renderer>(true).Where(r => WallNames.Contains(r.name) || r.name == "Ground").ToArray();
            if (walls.Length != 4) throw new InvalidOperationException("正式底图应具有四面旧边界");
            var provider = new LubanRuntimeConfigProvider(() => new Tables(n => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", n + ".bytes")))));
            if (!provider.TryGetArcadeCatalog(out var catalog, out var error)) throw new InvalidOperationException(error);
            var cameraObject = new GameObject("TemporaryBoundaryCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false;
            camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.10f, .15f, .19f); camera.farClipPlane = 300;
            camera.cullingMask = 1 << 30;
            var lightObject = new GameObject("TemporaryBoundaryLight", typeof(Light));
            var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f;
            light.cullingMask = 1 << 30; lightObject.transform.rotation = Quaternion.Euler(48, -32, 0);
            foreach (var layout in catalog.RoundLayouts)
            {
                view.End();
                var root = new GameObject("BoundaryAuditRuntime"); root.transform.SetParent(map.DynamicObjectRoot, false);
                Set(view, "root", root); Set(view, "islandBaseArenaRoot", map.transform); Set(view, "contentArena", layout.ArenaConfigNumber);
                var stage = catalog.EarlyStages.FirstOrDefault(p => p.ArenaConfigNumber == layout.ArenaConfigNumber);
                Set(view, "earlyStage", stage);
                view.ConfigureRoundBounds(stage?.HalfWidth ?? catalog.Rule.ArenaHalfWidth, stage?.HalfHeight ?? catalog.Rule.ArenaHalfHeight, catalog.Rule.PlayerRadius);
                view.SetIslandRound(layout.ArenaConfigNumber == 1 ? layout.RoundNumber : 0);
                view.SetRoundLayout(layout);
                if (!view.IsRoundLayoutReady) throw new InvalidOperationException(layout.Id + ": " + view.RoundLayoutLoadError);
                int prepared = walls.Count(w => w.enabled);
                var arena = catalog.Arenas.Single(a => a.ArenaNumber == layout.ArenaConfigNumber);
                view.SetOpening(arena.Opening, false);
                int released = walls.Count(w => w.enabled);
                if (layout.ArenaConfigNumber != 1 && released > 0) regressions++;
                if (layout.RoundNumber == 1)
                {
                    // 角色仅作正式比例参照；不启动输入、战斗、存档或网络。
                    var hero = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"), root.transform);
                    hero.transform.localPosition = new Vector3(0, 0, -4); hero.transform.localRotation = Quaternion.Euler(0, 30, 0);
                    foreach (var canvas in hero.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
                    foreach (var collider in hero.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                    foreach (var t in map.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
                    float half = stage == null ? 19.5f : Mathf.Max(stage.HalfWidth, stage.HalfHeight);
                    camera.orthographicSize = half * 1.28f;
                    camera.transform.position = new Vector3(0, half * 2.8f, -half * 1.5f); camera.transform.LookAt(Vector3.zero);
                    if (applied) Capture(camera, "Applied/" + layout.ArenaConfigNumber + "-applied.png");
                    else
                    {
                        Capture(camera, layout.ArenaConfigNumber + "-before.png");
                        var visibleStates = baseRenderers.Select(w => w.enabled).ToArray();
                        foreach (var renderer in baseRenderers) renderer.enabled = false;
                        Capture(camera, layout.ArenaConfigNumber + "-candidate.png");
                        // 候选仅在隔离场景中隐藏旧墙，立即还原后交给正式生命周期释放。
                        for (int i = 0; i < baseRenderers.Length; i++) baseRenderers[i].enabled = visibleStates[i];
                    }
                }
                var states = walls.Select(w => w.enabled).ToArray();
                foreach (var wall in walls) wall.enabled = false;
                int candidate = walls.Count(w => w.enabled);
                for (int i = 0; i < walls.Length; i++) walls[i].enabled = states[i];
                view.End();
                rows.Add(layout.Id + "|" + prepared + "|" + released + "|" + candidate + "|" + walls.Count(w => w.enabled));
            }
            File.WriteAllLines(Path.Combine(Output, applied ? "audit-after.tsv" : "audit-before.tsv"), rows);
            return "layouts=" + catalog.RoundLayouts.Count + "; restoredWalls=" + regressions + "; output=" + Output;
        }
        finally
        {
            view.End(); ResourceLoader.Use(previousLoader);
            SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void Capture(Camera camera, string name)
    {
        const int width = 1000, height = 760;
        var target = RenderTexture.GetTemporary(width, height, 24);
        var previous = RenderTexture.active;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(Output, name), texture.EncodeToPNG());
        }
        finally
        { camera.targetTexture = null; RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); RenderTexture.ReleaseTemporary(target); }
    }
}
