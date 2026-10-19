using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Game;
using Luban;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>使用实际奖励模板、配置和拾取 Prefab 做隔离画面检查；不运行规则、写档或打包。</summary>
public static class ArcadeTreasureRewardPreview
{
    private const string Folder = "../Docs/Plans/Artifacts/TreasureRewardsRealistic-20261019";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("奖励预览只允许编辑模式");
        var prior = SceneManager.GetActiveScene(); var priorTarget = RenderTexture.active;
        var singleton = typeof(Architecture<Game.Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
        var languages = typeof(LocalizationLanguages).GetField("cachedConfig", BindingFlags.Static | BindingFlags.NonPublic);
        var oldArchitecture = singleton.GetValue(null); var oldLanguages = languages.GetValue(null); var oldLoader = ResourceLoader.Current;
        var oldAmbient = RenderSettings.ambientMode; var oldAmbientLight = RenderSettings.ambientLight;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var view = new ArcadeCombatViewController(); var drops = new PreviewDrops();
        try
        {
            SceneManager.SetActiveScene(scene); Directory.CreateDirectory(Folder);
            ResourceLoader.Use(new LocalizationLoader()); LocalizationLanguages.ResetForTests();
            var architecture = new Game.Game(); singleton.SetValue(null, architecture);
            typeof(Architecture<Game.Game>).GetField("mInited", Hidden).SetValue(architecture, true);
            architecture.RegisterModel(new LocalizationModel()); architecture.RegisterUtility(new LocalizationUtility(false));
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes"))));
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            if (!provider.TryGetArcadeCatalog(out var catalog, out var error)) throw new InvalidOperationException(error);
            var kit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content");
            var root = new GameObject("TreasureRewardPreview"); drops.Root = root.transform;
            Set(view, "root", root); Set(view, "mapRoot", root.transform); Set(view, "contentTemplates", kit); Set(view, "lootPresentation", drops);
            var camera = new GameObject("RewardCamera", typeof(Camera)).GetComponent<Camera>();
            camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .055f, .07f);
            camera.cullingMask = 1 << 30;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.4f, .43f, .49f);
            Light("Key", new Color(1, .92f, .8f), 1.8f, new Vector3(45, -32, 0));
            Light("Fill", new Color(.6f, .82f, 1), .9f, new Vector3(35, 150, 0));
            // 近景只放大镜头，不改变奖励自身大小。
            var close = new GameObject("RewardCloseup");
            foreach (var spec in new[] { ("GoldBar", -1.05f), ("Gem", 1.05f) })
            {
                var item = Object.Instantiate(kit.Find(spec.Item1).gameObject, close.transform, false);
                item.transform.localPosition = new Vector3(spec.Item2, .3f, 0); item.transform.localRotation = Quaternion.Euler(0, 28, 0); item.SetActive(true);
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(close.transform, false);
            floor.transform.localPosition = new Vector3(0, -.12f, 0); floor.transform.localScale = new Vector3(8, .12f, 5);
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/Environment/Content/Slate.mat");
            camera.transform.position = new Vector3(0, 4.8f, -6.5f); camera.transform.LookAt(new Vector3(0, .35f, 0)); camera.orthographicSize = 1.65f;
            Layer(close.transform); Capture(camera, "models.png"); Object.DestroyImmediate(close);
            foreach (string id in new[] { "arcade.treasure.first", "arcade.armory.second" })
            {
                var room = catalog.TreasureRooms.First(p => p.Id == id);
                view.SetTreasureRoom(room, true, true, true, room.Rewards.Select(r => r.Id).ToArray()); view.TickPlayerFeedback(1.25f);
                camera.transform.position = new Vector3(0, 21, -14); camera.transform.LookAt(Vector3.zero); camera.orthographicSize = 9.4f;
                Layer(root.transform); foreach (var text in root.GetComponentsInChildren<TextMeshPro>(true)) text.ForceMeshUpdate(true, true);
                Capture(camera, id == "arcade.treasure.first" ? "treasure-room.png" : "armory.png");
                if (id == "arcade.treasure.first")
                {
                    typeof(ArcadeCombatViewController).GetMethod("EmitTreasurePickupFlash", Hidden).Invoke(view, new object[] { new Vector2(-1, -2) });
                    var flash = root.GetComponentsInChildren<ParticleSystem>().First(p => p.name == "TreasurePickupFlash");
                    flash.Simulate(.06f, false, false, false); Layer(root.transform); Capture(camera, "pickup-flash.png");
                }
            }
            File.WriteAllText(Folder + "/verification.txt", "隔离资源预览，非实际双端游玩。\n" + string.Join("\n", drops.Bounds));
            return Path.GetFullPath(Folder);
        }
        finally
        {
            view.End(); RenderTexture.active = priorTarget;
            RenderSettings.ambientMode = oldAmbient; RenderSettings.ambientLight = oldAmbientLight;
            singleton.SetValue(null, oldArchitecture); languages.SetValue(null, oldLanguages); ResourceLoader.Use(oldLoader);
            SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true);
        }
    }
    private static void Set(object view, string name, object value) => typeof(ArcadeCombatViewController).GetField(name, Hidden).SetValue(view, value);
    private static void Layer(Transform root) { root.gameObject.layer = 30; foreach (Transform child in root) Layer(child); }
    private static void Light(string name, Color color, float intensity, Vector3 euler)
    { var light = new GameObject(name, typeof(Light)).GetComponent<Light>(); light.type = LightType.Directional; light.color = color; light.intensity = intensity; light.transform.eulerAngles = euler; light.cullingMask = 1 << 30; }
    private static void Capture(Camera camera, string name)
    {
        var target = new RenderTexture(1920, 1080, 24); var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        var old = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0,0,1920,1080),0,0); texture.Apply();
            if(name=="models.png")
            {
                // 实际 GPU 画面同时区分两类材质，防止 SRP 参数缓存串色仍被当作有效预览。
                int warm=0,blue=0;
                for(int y=350;y<750;y++)for(int x=350;x<1550;x++)
                {
                    var color=texture.GetPixel(x,y);
                    if(x<900 && color.r>color.b*1.35f && color.r>color.g*1.08f)warm++;
                    if(x>1000 && color.b>color.r*1.4f && color.b>color.g*1.05f)blue++;
                }
                File.WriteAllText(Folder+"/material-check.txt","GPU 色彩检查：金色像素="+warm+"，蓝色像素="+blue);
                if(warm<1000||blue<1000)throw new InvalidOperationException("奖励实际渲染颜色不符，拒绝交付串色预览");
            }
            File.WriteAllBytes(Folder + "/" + name, texture.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = old; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); }
    }
    private sealed class LocalizationLoader : IResourceLoader
    {
        public T LoadAsset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
        public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : Object { callback(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
        public void Release(ResourceLoadHandle handle) { }
    }
    /// <summary>仅预览用适配器：以正式拾取 View 装配配置模型，记录网格高度；不产生领域物品。</summary>
    private sealed class PreviewDrops : IArcadeLootPresentationPort
    {
        public Transform Root;
        public readonly List<string> Bounds = new();
        private readonly Dictionary<int, ResourcePickupView> items = new();
        public bool TrySetDrop(int id, Vector3 position, string name, int amount, out string error) => TrySetDrop(id, position, name, amount, null, out error);
        public bool TrySetDrop(int id, Vector3 position, string name, int amount, string address, out string error)
        {
            error = null;
            var pickup = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab"), Root, false).GetComponent<ResourcePickupView>();
            pickup.transform.position = position; pickup.transform.rotation = Quaternion.Euler(0, 28, 0); pickup.GetComponent<Collider>().enabled = false;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + address + ".prefab");
            if (prefab == null) throw new InvalidOperationException("奖励模型缺失：" + address);
            pickup.SetArcadePresentation(prefab, address); pickup.RefreshDisplay(name, amount); pickup.SetNameVisible(false); items.Add(id, pickup);
            return true;
        }
        public void FitDropVisual(int id, float maxSize)
        {
            var pickup = items[id]; pickup.FitArcadeVisual(maxSize);
            var renderer = pickup.transform.Find("ArcadeDropVisual").GetComponentsInChildren<Renderer>(); var bounds = renderer[0].bounds;
            foreach (var r in renderer) bounds.Encapsulate(r.bounds);
            Bounds.Add(pickup.ActiveArcadeVisualPrefabAddress + " size=" + bounds.size + " bottom=" + bounds.min.y.ToString("F3"));
        }
        public void SetDropOpacity(int id, float opacity) { if (items.TryGetValue(id, out var item)) item.SetArcadeOpacity(opacity); }
        public void RemoveDrop(int id) { if (!items.TryGetValue(id, out var item)) return; items.Remove(id); if (item != null) Object.DestroyImmediate(item.gameObject); }
        public void ClearArcadeDrops() { foreach (var id in items.Keys.ToArray()) RemoveDrop(id); }
    }
}
