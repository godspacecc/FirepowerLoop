using System;
using System.IO;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>在独立物理预览场景实拍尸体冻结、保留和淡出，关闭后不留下场景对象或资源。</summary>
public static class EnemyCorpseRetentionPreview
{
    public static string Run()
    {
        if (Application.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("只允许本工程编辑态观察");
        string folder = Path.GetFullPath("../Docs/Plans/Artifacts/EnemyCorpseRetention-20261020");
        Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene();
        var physics = scene.GetPhysicsScene();
        RenderTexture target = null; Texture2D pixels = null; Material floorMaterial = null;
        EnemyView view = null;
        var oldTarget = RenderTexture.active;
        try
        {
            EnemyPhysicalReaction.ConfigureCollisionLayers();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = new Vector3(0, -.05f, 0); floor.transform.localScale = new Vector3(20, .1f, 20);
            floor.layer = LayerMask.NameToLayer(EnemyPhysicalReaction.FloorLayerName);
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.color = new Color(.16f, .23f, .18f); floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var camera = new GameObject("CorpseCamera", typeof(Camera)).GetComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene); camera.scene = scene;
            camera.transform.position = new Vector3(3, 5, -5); camera.transform.LookAt(new Vector3(0, .2f, 0));
            camera.orthographic = true; camera.orthographicSize = 2.25f; camera.cullingMask = ~(1 << 5);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .08f, .1f);
            var light = new GameObject("Key", typeof(Light)).GetComponent<Light>();
            SceneManager.MoveGameObjectToScene(light.gameObject, scene);
            light.type = LightType.Directional; light.intensity = 1.8f; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));
            SceneManager.MoveGameObjectToScene(actor, scene);
            actor.transform.position = Vector3.zero; actor.transform.rotation = Quaternion.identity;
            view = actor.GetComponent<EnemyView>(); view.BeginPresentationUse(); view.SetNavigationEnabled(false);
            view.Animator.Rebind(); view.Animator.Update(0f);
            view.TryPlayPhysicalDeath(Vector3.forward, false, out float lifetime);
            target = new RenderTexture(960, 640, 24); pixels = new Texture2D(960, 640, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            for (int i = 0; i < 110; i++)
            {
                view.TickCombatFeedback(.02f);
                if (view.Animator.enabled) view.Animator.Update(.02f);
                view.LateTickCombatFeedback(); physics.Simulate(.02f);
            }
            if (actor.GetComponent<EnemyPhysicalReaction>().IsSimulating) throw new Exception("物理未按短时预算冻结");
            Color32[] Capture(string name)
            {
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
                return pixels.GetPixels32();
            }
            view.TickCombatFeedback(2.8f); view.SetCorpseFade(3f); var full = Capture("01-frozen-at-5-seconds");
            view.TickCombatFeedback(2.4f); view.SetCorpseFade(.6f); var half = Capture("02-half-faded-at-7.4-seconds");
            view.TickCombatFeedback(.6f); view.SetCorpseFade(0f); var gone = Capture("03-fully-faded-at-8-seconds");
            actor.SetActive(false); var baseline = Capture("04-empty-baseline");
            double Distance(Color32[] a, Color32[] b)
            {
                double sum = 0;
                for (int i = 0; i < a.Length; i++)
                    sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
                return sum / a.Length;
            }
            double fullDifference = Distance(full, baseline), halfDifference = Distance(half, baseline), goneDifference = Distance(gone, baseline);
            if (!(fullDifference > halfDifference && halfDifference > goneDifference && goneDifference < .2))
                throw new Exception($"实际渲染淡出不连续：{fullDifference}, {halfDifference}, {goneDifference}");
            view.BeginPresentationUse(); actor.SetActive(true); view.Animator.Rebind(); view.Animator.Update(0);
            view.TryPlayPhysicalDeath(Vector3.forward, true, out _);
            actor.GetComponent<EnemyPhysicalReaction>().StopDeathSimulation();
            Capture("05-budget-grounded-fallback");
            var report = $"LifetimeSeconds={lifetime}; FrozenAt2.2Seconds=True; FullDifference={fullDifference:F4}; HalfDifference={halfDifference:F4}; GoneDifference={goneDifference:F4}; Scope=Editor isolated preview; not device FPS or live multiplayer.";
            File.WriteAllText(Path.Combine(folder, "verification.txt"), report);
            return report + " Folder=" + folder;
        }
        finally
        {
            RenderTexture.active = oldTarget;
            if (view != null) view.ReleasePresentationResources();
            EditorSceneManager.ClosePreviewScene(scene);
            if (target != null) Object.DestroyImmediate(target);
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (floorMaterial != null) Object.DestroyImmediate(floorMaterial);
        }
    }
}
