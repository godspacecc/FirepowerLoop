using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>仅微调手枪子弹尺寸和独立材质，前后以相同镜头渲染正式三枪弹体。</summary>
public static class ArcadePistolBulletAuthoring
{
    const string Prefab = "Assets/Game/Prefabs/Combat/GrayboxBullet.prefab";
    const string Folder = "Assets/Game/Art/Arcade/VFX/Gunfire/";
    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本工程编辑态制作");
        Render("before");
        var root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var child = root.transform.Find("ArcadeBulletBasic");
            child.localScale = new Vector3(.8f, .8f, .66f);
            var source = root.GetComponent<MeshRenderer>().sharedMaterials;
            var materials = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                string path = Folder + "Pistol" + source[i].name + ".mat";
                if (!File.Exists(path) && !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source[i]), path))
                    throw new InvalidOperationException("无法制作手枪独立材质");
                materials[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
                materials[i].SetColor("_BaseColor", new Color(2.6f, 2.4f, 1.5f, 1));
                EditorUtility.SetDirty(materials[i]);
            }
            child.GetComponent<MeshRenderer>().sharedMaterials = materials;
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            AssetDatabase.SaveAssets();
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Render("after");
        return Path.GetFullPath("Temp/PistolBulletPreview");
    }

    /// <summary>只克隆正式弹体，碰撞载体及其他武器保持原样，固定相机便于逐像素对照。</summary>
    static void Render(string stage)
    {
        var prior = SceneManager.GetActiveScene(); var oldRt = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        RenderTexture target = null; Texture2D image = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("BulletComparisonCamera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 2, -.5f); camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            camera.orthographic = true; camera.orthographicSize = .8f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .12f, .15f);
            var names = new[] { "ArcadeBulletBasic", "ArcadeBulletRapid", "ArcadeBulletSpread" };
            for (int i = 0; i < names.Length; i++)
            {
                var bullet = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
                bullet.transform.position = new Vector3((i - 1) * 1.1f, 0, 0);
                bullet.GetComponent<Game.ProjectileView>().SetArcadePresentation(names[i], false);
                if (stage == "before" && i == 0)
                {
                    var basic = bullet.transform.Find(names[i]);
                    basic.localScale = new Vector3(.9f, .9f, .8f);
                    basic.GetComponent<MeshRenderer>().sharedMaterials = bullet.GetComponent<MeshRenderer>().sharedMaterials;
                }
            }
            target = new RenderTexture(1600, 720, 24); image = new Texture2D(1600, 720, TextureFormat.RGB24, false);
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1600, 720), 0, 0); image.Apply();
            Directory.CreateDirectory("Temp/PistolBulletPreview");
            File.WriteAllBytes("Temp/PistolBulletPreview/" + stage + ".png", image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = oldRt;
            if (image != null) Object.DestroyImmediate(image); if (target != null) Object.DestroyImmediate(target);
            SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true);
        }
    }
}
