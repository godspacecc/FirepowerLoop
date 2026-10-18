using System;
using System.Collections.Generic;
using System.IO;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>隔离物理场景中检查四种正式骨架的落地、爆炸力度、冻结与回池，保留实际渲染证据。</summary>
public static class EnemyPhysicalReactionPreview
{
    [Serializable] private sealed class Report
    {
        public int rigs, bodies;
        public bool finite, frozen, reusable;
        public float normalMaxLift, explosionMaxLift;
        public float finalHipHeight;
        public string scope = "Editor isolated PhysicsScene; 8 corpses, 120 fixed steps; not a phone or multiplayer session.";
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅在本工程编辑态执行隔离观察");
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Docs/Plans/Artifacts/EnemyRagdoll-20261018"));
        Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene();
        var physics = scene.GetPhysicsScene();
        var views = new List<EnemyView>(); var reactions = new List<EnemyPhysicalReaction>();
        var initialY = new List<float>(); var allBodies = new List<Rigidbody>();
        Material material = null; RenderTexture target = null; Texture2D pixels = null; Camera camera = null;
        var report = new Report { finite = true, frozen = true, reusable = true };
        try
        {
            EnemyPhysicalReaction.ConfigureCollisionLayers();
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "CorpseOnlyGround";
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.layer = LayerMask.NameToLayer(EnemyPhysicalReaction.FloorLayerName);
            ground.transform.position = new Vector3(0, -.05f, 0); ground.transform.localScale = new Vector3(40, .1f, 30);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader); material.color = new Color(.21f, .25f, .27f);
            ground.GetComponent<Renderer>().sharedMaterial = material;
            var cameraObject = new GameObject("PreviewCamera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 6.4f;
            camera.scene = scene;
            camera.transform.position = new Vector3(12, 11, -17); camera.transform.LookAt(new Vector3(0, 1, 1));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.045f, .06f, .08f);
            camera.cullingMask = ~(1 << 5); camera.farClipPlane = 100;
            var lightObject = new GameObject("KeyLight", typeof(Light)); SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(45, -30, 0);
            string[] names = { "ArcadeEnemyBasic", "ArcadeEnemyArcher", "ArcadeEnemyBrute", "ArcadeEnemyBossArcher" };
            for (int row = 0; row < 2; row++) for (int col = 0; col < names.Length; col++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/" + names[col] + ".prefab");
                var actor = Object.Instantiate(prefab); actor.name = names[col] + (row == 0 ? "_Normal" : "_Explosion");
                SceneManager.MoveGameObjectToScene(actor, scene);
                actor.transform.position = new Vector3((col - 1.5f) * 4, 0f, row * 4 - 2);
                var view = actor.GetComponent<EnemyView>(); view.BeginPresentationUse(); view.SetNavigationEnabled(false);
                if (!view.SetCombatWeapon(col == 2 ? "weapon.greatsword" : col == 0 ? "weapon.short-sword" : "weapon.bow"))
                    throw new InvalidOperationException("观察实例未绑定正式武器动作：" + names[col]);
                view.Animator.Rebind(); view.Animator.Update(0f);
                views.Add(view); reactions.Add(actor.GetComponent<EnemyPhysicalReaction>());
                initialY.Add(view.Animator.GetBoneTransform(HumanBodyBones.Hips).position.y);
                allBodies.AddRange(actor.GetComponentsInChildren<Rigidbody>());
            }
            report.rigs = views.Count; report.bodies = allBodies.Count;
            target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
            pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            void Capture(string file)
            {
                camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply(); RenderTexture.active = old;
                File.WriteAllBytes(Path.Combine(folder, file), pixels.EncodeToPNG());
            }
            foreach (var view in views) { view.PlayHitFeedback(); view.TickCombatFeedback(.035f); view.LateTickCombatFeedback(); }
            Capture("01-local-hit-recoil.png");
            for (int i = 0; i < views.Count; i++)
            {
                views[i].TickCombatFeedback(.3f);
                if (!views[i].TryPlayPhysicalDeath(Vector3.forward, i >= 4, out _)) throw new InvalidOperationException("死亡物理未接入");
            }
            for (int step = 0; step < 100; step++)
            {
                foreach (var reaction in reactions) reaction.Tick(.02f);
                foreach (var view in views) if (view.Animator.enabled) view.Animator.Update(.02f);
                foreach (var reaction in reactions) reaction.LateTick();
                physics.Simulate(.02f);
                for (int i = 0; i < views.Count; i++)
                {
                    float lift = views[i].Animator.GetBoneTransform(HumanBodyBones.Hips).position.y - initialY[i];
                    if (i < 4) report.normalMaxLift = Mathf.Max(report.normalMaxLift, lift);
                    else report.explosionMaxLift = Mathf.Max(report.explosionMaxLift, lift);
                }
                foreach (var body in allBodies)
                    report.finite &= !float.IsNaN(body.position.sqrMagnitude) && !float.IsInfinity(body.position.sqrMagnitude) && body.position.sqrMagnitude < 10000;
                if (step == 14) Capture("02-death-and-explosion-030.png");
                if (step == 39) Capture("03-death-and-explosion-080.png");
            }
            Capture("04-frozen-corpses-200.png");
            foreach (var view in views) report.finalHipHeight = Mathf.Max(report.finalHipHeight,
                view.Animator.GetBoneTransform(HumanBodyBones.Hips).position.y);
            var poses = new Vector3[allBodies.Count]; for (int i = 0; i < poses.Length; i++) poses[i] = allBodies[i].position;
            for (int step = 0; step < 20; step++) physics.Simulate(.02f);
            for (int i = 0; i < poses.Length; i++) report.frozen &= allBodies[i].isKinematic && Vector3.Distance(poses[i], allBodies[i].position) < .001f;
            foreach (var view in views) { view.ResetPhysicalFeedback(); view.gameObject.SetActive(false); view.BeginPresentationUse(); view.gameObject.SetActive(true); }
            foreach (var body in allBodies) report.reusable &= body.isKinematic && !body.detectCollisions && !body.GetComponent<Collider>().enabled;
            File.WriteAllText(Path.Combine(folder, "verification.json"), JsonUtility.ToJson(report, true));
            if (!report.finite || !report.frozen || !report.reusable || report.finalHipHeight > .6f ||
                report.explosionMaxLift <= report.normalMaxLift + .3f)
                throw new InvalidOperationException("物理观察检查未通过：" + JsonUtility.ToJson(report));
            return JsonUtility.ToJson(report);
        }
        finally
        {
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            EditorSceneManager.ClosePreviewScene(scene);
            if (material != null) Object.DestroyImmediate(material);
        }
    }
}
