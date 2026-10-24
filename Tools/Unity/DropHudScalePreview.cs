using System;
using System.IO;
using System.Linq;
using System.Text;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>掉落与无人机尺寸候选：只改隔离实例，与正式主角同机位比较，不保存产品资源。</summary>
public static class DropHudScalePreview
{
    private const string Folder = "../Backup/Artifacts/DropHudScale-v1-20261024";

    public static string Run() => Render(false);
    /// <summary>直接拍摄正式资源，不再施加候选缩放；保留相同机位供采用版逐像素对照。</summary>
    public static string RunApplied() => Render(true);
    private static string Render(bool applied)
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling ||
            Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本工程编辑态隔离预览");
        var prior = SceneManager.GetActiveScene();
        var priorTarget = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Material floorMaterial = null;
        RenderTexture target = null;
        Texture2D pixels = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            string folder = applied ? "../Backup/Artifacts/DropHudScaleApplied-20261024" : Folder;
            Directory.CreateDirectory(folder);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.62f, .64f, .65f);
            var stage = new GameObject("TemporaryModels");
            var hero = Object.Instantiate(Load("Characters/GrayboxPlayer"), stage.transform);
            hero.transform.position = new Vector3(-1.8f, 0, 0);
            hero.transform.rotation = Quaternion.Euler(0, 160, 0);
            var presenter = hero.GetComponent<CharacterWeaponPresenter>();
            if (!presenter.SetWeaponId("arcade.weapon.basic")) throw new InvalidOperationException(presenter.LastError);
            presenter.SetManualPoseSampling(true);
            presenter.BodyAnimator.Rebind();
            presenter.BodyAnimator.Update(0);
            presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject, .1f);
            presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            foreach (var canvas in hero.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
            var coin = Drop(stage.transform, "ArcadeDropCoin", new Vector3(-.2f, .3f, 0), false);
            var red = Drop(stage.transform, "ArcadeDropSkullRed", new Vector3(1.05f, .3f, 0), false);
            var purple = Drop(stage.transform, "ArcadeDropSkullPurple", new Vector3(2.3f, .3f, 0), false);
            var air = Drop(stage.transform, "ArcadeSupportDroneVisual", new Vector3(.05f, .3f, 0), true);
            var equipped = Object.Instantiate(Load("SceneObjects/ArcadeTimedDroneVisual"), stage.transform);
            equipped.transform.position = new Vector3(1.95f, 1.35f, 0);
            // 用相同方向量取模型实体，不把光柱、粒子或空投箱算进尺寸。
            var equippedSize = Longest(BodyBounds(equipped.transform));
            var report = new StringBuilder();
            report.AppendLine("正式主角与基础AK，未改变主角比例；模型均由正式Prefab克隆。");
            report.AppendLine("金币实体最长边=" + Longest(BodyBounds(coin.transform.Find("ArcadeDropVisual"))).ToString("F6"));
            report.AppendLine((applied ? "正式随身无人机实体最长边=" : "原随身无人机实体最长边=") + equippedSize.ToString("F6"));
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.22f, .28f, .28f) };
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0, -.12f, 0);
            floor.transform.localScale = new Vector3(25, .15f, 25);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            foreach (var root in scene.GetRootGameObjects())
                foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
            var light = new GameObject("PreviewSun", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.5f; light.cullingMask = 1 << 30;
            light.transform.rotation = Quaternion.Euler(50, -35, 0);
            var camera = new GameObject("ComparisonCamera", typeof(Camera)).GetComponent<Camera>();
            camera.cullingMask = 1 << 30; camera.orthographic = true; camera.orthographicSize = 2.1f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.075f, .1f, .12f);
            var focus = new Vector3(.15f, .85f, 0);
            camera.transform.position = focus + new Vector3(0, 5.8f, -8);
            camera.transform.LookAt(focus);
            target = new RenderTexture(1600, 900, 24);
            pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            air.SetActive(false); equipped.SetActive(false);
            if (!applied) Capture(camera, target, pixels, "skulls-current", folder);
            float coinSize = Longest(BodyBounds(coin.transform.Find("ArcadeDropVisual")));
            foreach (var skull in new[] { red, purple })
            {
                var visual = skull.transform.Find("ArcadeDropVisual");
                float ratio = coinSize / Longest(BodyBounds(visual));
                if (!applied) visual.localScale *= ratio;
                report.AppendLine(skull.name + (applied ? "正式尺寸与金币之比=" : "候选倍率=") + ratio.ToString("F6") + "，目标最长边=" + Longest(BodyBounds(visual)).ToString("F6"));
            }
            Capture(camera, target, pixels, applied ? "skulls-applied" : "skulls-candidate", folder);
            coin.SetActive(false); red.SetActive(false); purple.SetActive(false);
            air.SetActive(true); equipped.SetActive(true);
            if (!applied) Capture(camera, target, pixels, "drones-current", folder);
            var airBody = air.transform.Find("ArcadeDropVisual");
            float airRatio = equippedSize / Longest(BodyBounds(airBody));
            if (!applied) airBody.localScale *= airRatio;
            // 保留同一空投位置，以可见尺寸说明候选，不改变正式弹出动作。
            if (!applied) equipped.transform.localScale *= .7f;
            report.AppendLine((applied ? "空投无人机正式最长边=" : "空投无人机候选最长边=") + Longest(BodyBounds(airBody)).ToString("F6"));
            report.AppendLine((applied ? "随身无人机正式最长边=" : "随身无人机候选最长边=") + Longest(BodyBounds(equipped.transform)).ToString("F6") + "，原各轴乘0.7");
            Capture(camera, target, pixels, applied ? "drones-applied" : "drones-candidate", folder);
            File.WriteAllText(Path.Combine(folder, "model-measurements.txt"), report.ToString());
            camera.targetTexture = null;
            return Path.GetFullPath(folder) + "\n" + report;
        }
        finally
        {
            RenderTexture.active = priorTarget;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            SceneManager.SetActiveScene(prior);
            EditorSceneManager.CloseScene(scene, true);
            if (floorMaterial != null) Object.DestroyImmediate(floorMaterial);
        }
    }

    /// <summary>沿用正式拾取模板和空投展示入口，保留金币和骷髅原来的展示朝向。</summary>
    private static GameObject Drop(Transform parent, string name, Vector3 position, bool airdrop)
    {
        var go = Object.Instantiate(Load("SceneObjects/TestResourcePickup"), parent);
        go.name = name; go.transform.position = position;
        var pickup = go.GetComponent<ResourcePickupView>();
        pickup.SetArcadePresentation(Load("SceneObjects/" + name), "Prefabs/SceneObjects/" + name);
        if (airdrop) pickup.SetWeaponDelivery(true, 0, 1.2f);
        else go.transform.rotation = Quaternion.Euler(0, 180, 0);
        pickup.SetNameVisible(false);
        return go;
    }

    /// <summary>只量取实体网格，避免光柱及粒子动态包围盒污染展示尺寸。</summary>
    private static Bounds BodyBounds(Transform root)
    {
        bool found = false; var bounds = new Bounds();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer) continue;
            if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
        }
        if (!found) throw new InvalidOperationException("模型没有实体网格：" + root.name);
        return bounds;
    }
    private static float Longest(Bounds bounds) => Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
    private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/" + path + ".prefab") ?? throw new InvalidOperationException(path);
    private static void Capture(Camera camera, RenderTexture target, Texture2D pixels, string name, string folder)
    {
        camera.Render(); RenderTexture.active = target;
        pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
    }
}
