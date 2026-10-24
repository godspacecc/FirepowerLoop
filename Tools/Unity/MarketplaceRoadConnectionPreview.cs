using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>只在独立临时场景展示集市八巷口与井字路的对应，不保存或修改正式资源。</summary>
public static class MarketplaceRoadConnectionPreview
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
    const int Layer = 29;
    static readonly Type Source = typeof(NextThreeStagesArtPreview);
    static T Get<T>(string name) => (T)Source.GetField(name, Flags).GetValue(null);
    static void Set(string name, object value) => Source.GetField(name, Flags).SetValue(null, value);
    static object Call(string name, params object[] values)
    {
        var method = Source.GetMethod(name, Flags); var args = method.GetParameters();
        var full = new object[args.Length];
        for (var i = 0; i < full.Length; i++) full[i] = i < values.Length ? values[i] : args[i].DefaultValue;
        try { return method.Invoke(null, full); }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
    }
    static void Box(string name, float x, float y, float z, float w, float h, float d, Material material)
        => Call("Box", name, x, y, z, w, h, d, material);

    /// <summary>铺装采用同一网格取并集，交叉处不叠砖；宽度与现有六米巷口一致。</summary>
    public static void ApplyConfirmedRoads(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>().Where(t =>
            t.name == "MarketCrosswalk" || t.name == "MarketWalkPaver" || t.name == "WornPaver" ||
            t.name == "PreviewConnectedRoadBase" || t.name == "PreviewConnectedRoadPaver").ToArray())
            Object.DestroyImmediate(t.gameObject);
        var baseMaterial = Get<Material>("tintA");
        foreach (var side in new[] { -1, 1 })
        {
            Box("PreviewConnectedRoadBase", side * 11, .035f, 0, 6, .035f, 64, baseMaterial);
            Box("PreviewConnectedRoadBase", 0, .035f, side * 9, 68, .035f, 6, baseMaterial);
        }
        var cells = new HashSet<Vector2Int>();
        foreach (var side in new[] { -1, 1 })
            for (var j = -1; j <= 1; j++)
            {
                for (var z = -31; z <= 31; z += 2) cells.Add(new Vector2Int(side * 11 + j * 2, z));
                for (var x = -33; x <= 33; x += 2) cells.Add(new Vector2Int(x, side * 9 + j * 2));
            }
        foreach (var cell in cells.OrderBy(c => c.x).ThenBy(c => c.y))
            Box("PreviewConnectedRoadPaver", cell.x, .064f, cell.y, 1.94f, .022f, 1.94f,
                ((cell.x + cell.y) / 2) % 3 == 0 ? Get<Material>("lightStone") : Get<Material>("stone"));
        var gates = root.GetComponentsInChildren<Transform>().Where(t => t.name == "VisiblePassage").ToArray();
        if (gates.Length != 8 || gates.Any(t => !(Mathf.Abs(Mathf.Abs(t.localPosition.x) - 11) < .01f ||
            Mathf.Abs(Mathf.Abs(t.localPosition.z) - 9) < .01f))) throw new InvalidOperationException("八巷口位置与候选井字道路不匹配");
    }

    public static string Capture() => CaptureCore(false);
    /// <summary>同机位重载已保存的正式资源，供接入后与采用图比较。</summary>
    public static string CaptureApplied() => CaptureCore(true);
    static string CaptureCore(bool applied)
    {
        if (Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets" ||
            EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("仅本工程编辑态预览");
        var previous = SceneManager.GetActiveScene(); var activeTexture = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var folder = Path.GetFullPath("../Docs/Plans/Artifacts/" + (applied ? "MarketplaceRoadApplied-20261024" : "MarketplaceRoad-v2-20261024")); Directory.CreateDirectory(folder);
        try
        {
            SceneManager.SetActiveScene(scene); Call("Palette", 2);
            var root = new GameObject("TEMP_UNAPPROVED_MarketplaceRoad_v2").transform; Set("root", root);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.52f, .55f, .53f); RenderSettings.fog = false;
            var sun = new GameObject("PreviewSun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.05f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48, -28, 0); sun.cullingMask = 1 << Layer;
            var camera = new GameObject("PreviewCamera", typeof(Camera)).GetComponent<Camera>(); Set("camera", camera);
            camera.orthographic = true; camera.orthographicSize = 36; camera.farClipPlane = 220; camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.15f, .20f, .21f);
            camera.transform.position = new Vector3(0, 74, -57); camera.transform.LookAt(Vector3.zero);
            if (applied)
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeMarketplaceVisual.prefab"), root);
            else
            {
                Box("BackdropGround", 0, -.4f, 0, 108, .5f, 104, Get<Material>("ground"));
                Call("MarketArt");
            }
            Call("Hero", 0f, -6f); Call("SetLayers");
            if (!applied)
            {
                Call("Save", folder + "/before.png", 1500, 1100);
                ApplyConfirmedRoads(root); Call("SetLayers");
            }
            Call("Save", folder + "/after.png", 1500, 1100);
            camera.orthographicSize = 13; camera.transform.position = new Vector3(-28, 30, -39);
            camera.transform.LookAt(new Vector3(-11, 0, -19)); Call("Save", folder + "/connection.png", 1280, 900);
            return folder + (applied ? "；正式Prefab重载实拍，独立场景，非自然对局" : "；8巷口位置核对通过；独立临时同源场景，正式资源未改");
        }
        finally
        {
            RenderTexture.active = activeTexture; SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true);
            var owned = Get<List<Object>>("owned"); foreach (var o in owned) if (o != null) Object.DestroyImmediate(o); owned.Clear();
            ((IList)Source.GetField("actors", Flags).GetValue(null)).Clear();
        }
    }
}
