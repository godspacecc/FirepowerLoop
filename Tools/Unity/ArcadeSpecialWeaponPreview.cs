using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game;
using Object = UnityEngine.Object;

/// <summary>同镜头检查镭射、穿透、火箭和榴弹资源，以及已有权威爆炸端口的隔离表现。</summary>
public static class ArcadeSpecialWeaponPreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets") throw new InvalidOperationException("仅本工程编辑态预览");
        var prior = SceneManager.GetActiveScene(); var oldRt = RenderTexture.active; var loader = ResourceLoader.Current;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        RenderTexture target = null; Texture2D image = null; Material groundMaterial = null;
        ArcadeCombatViewController view = null;
        try
        {
            SceneManager.SetActiveScene(scene); ResourceLoader.Use(new Loader());
            var camera = new GameObject("SpecialWeaponComparison", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 12, -7); camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true; camera.orthographicSize = 5.5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .13f, .17f);
            var light = new GameObject("Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1; light.transform.rotation = Quaternion.Euler(55, -30, 0);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.localScale = Vector3.one * 4;
            groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")); groundMaterial.SetColor("_BaseColor", new Color(.19f, .26f, .29f));
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            target = new RenderTexture(1600, 900, 24); image = new Texture2D(1600, 900, TextureFormat.RGB24, false); camera.targetTexture = target;
            Directory.CreateDirectory("Temp/SpecialWeaponPreview");
            foreach (bool before in new[] { true, false })
            {
                var root = new GameObject("WeaponExamples");
                var names = new[] { "ArcadeRaygunBolt", "ArcadePiercingRay", "ArcadePanzerschreckRocket", "ArcadeGrenade" };
                for (int i = 0; i < names.Length; i++)
                {
                    var bullet = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab"), root.transform);
                    bullet.transform.position = new Vector3((i - 1.5f) * 3.5f, .75f, -2);
                    bullet.GetComponent<ProjectileView>().SetArcadePresentation(before && i == 0 ? null : names[i], false);
                    if (before && i == 1)
                    {
                        var piercing = bullet.transform.Find("ArcadePiercingRay");
                        var beam = piercing.Find("PiercingStreakVisual"); if (beam != null) beam.gameObject.SetActive(false);
                        foreach (var skin in piercing.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.enabled = true;
                    }
                }
                view = new ArcadeCombatViewController(); var owner = new GameObject("BlastExamples");
                Set(view, "root", owner); Set(view, "mapRoot", owner.transform);
                Set(view, "lastExplosionSoundTime", float.MaxValue); Set(view, "lastSciFiExplosionSoundTime", float.MaxValue);
                typeof(ArcadeCombatViewController).GetMethod("LoadContentAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { 15 });
                if (!before)
                {
                    view.PlayProjectileExplosion(new Vector2(-4, 2), 1.35f);
                    view.PlayProjectileExplosion(new Vector2(0, 2), 2.8f);
                    view.PlayProjectileExplosion(new Vector2(4, 2), 3.2f);
                    foreach (var ps in owner.GetComponentsInChildren<ParticleSystem>())
                        if (ps.isPlaying) { ps.Simulate(.14f, false, true, false); ps.Pause(false); }
                }
                camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
                File.WriteAllBytes("Temp/SpecialWeaponPreview/" + (before ? "before" : "after") + ".png", image.EncodeToPNG());
                view.End(); view = null; Object.DestroyImmediate(root);
            }
            camera.targetTexture = null; return Path.GetFullPath("Temp/SpecialWeaponPreview");
        }
        finally
        {
            view?.End(); ResourceLoader.Use(loader); RenderTexture.active = oldRt;
            if (image != null) Object.DestroyImmediate(image); if (target != null) Object.DestroyImmediate(target); if (groundMaterial != null) Object.DestroyImmediate(groundMaterial);
            SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true);
        }
    }
    static void Set(object owner, string field, object value) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    sealed class Loader : IResourceLoader
    {
        public T LoadAsset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + path + ".prefab");
        public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : Object { callback(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
        public void Release(ResourceLoadHandle handle) { }
    }
}
