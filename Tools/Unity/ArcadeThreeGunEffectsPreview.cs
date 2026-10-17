using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using Luban;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>正式三枪资源的同镜头改前后对比，隔离预览不代表自然游戏流程。</summary>
public static class ArcadeThreeGunEffectsPreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("需编辑态");
        var prior = SceneManager.GetActiveScene(); var oldRt = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        Material groundMaterial = null; RenderTexture rt = null; Texture2D tex = null;
        const string output = "Temp/WeaponEffects/Preview";
        try
        {
            Directory.CreateDirectory(output);
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes"))));
            if (!provider.TryGetArcadeCatalog(out var catalog, out var error)) throw new Exception(error);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 3f;
            groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            groundMaterial.SetColor("_BaseColor", new Color(.14f, .19f, .14f));
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            var camera = new GameObject("ThreeGunCamera").AddComponent<Camera>();
            camera.fieldOfView = 45f; camera.nearClipPlane = .1f; camera.farClipPlane = 80;
            camera.transform.rotation = Quaternion.Euler(72, 0, 0);
            camera.transform.position = new Vector3(0, .75f, 1) - camera.transform.forward * 16f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .06f, .09f);
            var light = new GameObject("ThreeGunLight").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f; light.transform.rotation = Quaternion.Euler(55, -30, 0);
            rt = new RenderTexture(1280, 720, 24); tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            camera.targetTexture = rt;
            foreach (bool baseline in new[] { true, false })
            {
                var root = new GameObject("ThreeGunComparison");
                string[] ids = { "arcade.weapon.basic", "arcade.weapon.rapid", "arcade.weapon.spread" };
                for (int i = 0; i < ids.Length; i++)
                {
                    var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"), root.transform);
                    go.transform.position = new Vector3((i - 1) * 4, 0, -2);
                    var view = go.GetComponent<PlayerView>(); var weapon = catalog.Weapons.Single(w => w.Id == ids[i]);
                    typeof(PlayerViewCtrl).GetMethod("ConfigureSharedArcadeWeaponPresentation", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { view, weapon });
                    view.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; view.Animator.Update(.02f);
                    view.PlayProjectileFireFeedback();
                    var presenter = go.GetComponent<CharacterWeaponPresenter>();
                    if (baseline) presenter.ClearUpperBodyGunFire();
                    presenter.ApplyOffhandGrip();
                    var muzzle = (ParticleSystem)typeof(PlayerView).GetField("muzzleFlash", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
                    muzzle.transform.position = view.GetProjectileOrigin(Vector3.forward);
                    if (baseline)
                        foreach (var ps in muzzle.GetComponentsInChildren<ParticleSystem>(true))
                        { var main = ps.main; main.startSize = ps == muzzle ? .42f : .25f; main.startLifetime = .045f; }
                    muzzle.Simulate(.015f, true, true, false); muzzle.Pause(true);
                    int count = i == 2 ? 5 : i == 1 ? 4 : 2;
                    for (int j = 0; j < count; j++)
                    {
                        var bullet = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxBullet.prefab"), root.transform);
                        float angle = i == 2 ? -14f + 7f * j : 0;
                        var direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                        bullet.GetComponent<ProjectileView>().SetArcadePresentation(baseline ? null : weapon.ProjectileVisualResourceName, false);
                        bullet.transform.position = go.transform.position + Vector3.up * .75f + direction * (i == 2 ? 4f : 1.8f + j * 1.2f);
                        bullet.transform.rotation = Quaternion.LookRotation(direction);
                    }
                }
                camera.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                File.WriteAllBytes(output + (baseline ? "/before.png" : "/after.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(root);
            }
            return Path.GetFullPath(output);
        }
        finally
        {
            RenderTexture.active = oldRt;
            if (rt != null) Object.DestroyImmediate(rt); if (tex != null) Object.DestroyImmediate(tex);
            if (groundMaterial != null) Object.DestroyImmediate(groundMaterial);
            SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true);
        }
    }
}
