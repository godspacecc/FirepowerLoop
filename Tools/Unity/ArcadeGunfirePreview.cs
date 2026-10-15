using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>用正式资源和街机相机尺度离屏对比枪弹；不是连续试玩或概念插画。</summary>
public static class ArcadeGunfirePreview
{
    public static string Run()
    {
        const string output = "Assets/Tests/Results~/ArcadeGunfire";
        Directory.CreateDirectory(output);
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var priorRt = RenderTexture.active;
        Material groundMaterial = null;
        try
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 3;
            groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            groundMaterial.SetColor("_BaseColor", new Color(.22f, .28f, .19f));
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            var camera = new GameObject("gunfire-preview-camera").AddComponent<Camera>();
            camera.fieldOfView = 45; camera.nearClipPlane = .1f; camera.farClipPlane = 80;
            camera.transform.rotation = Quaternion.Euler(72, 0, 0);
            camera.transform.position = Vector3.up * .75f - camera.transform.forward * 16;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.04f, .06f, .09f);
            camera.allowHDR = false;
            var light = new GameObject("preview-light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(55, -30, 0);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var baselineMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(primitive);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxBullet.prefab");
            var instances = new GameObject[7];
            for (int i = 0; i < instances.Length; i++)
            {
                instances[i] = Object.Instantiate(prefab);
                instances[i].transform.position = new Vector3(-3 + i, .75f, (i % 2) * 1.5f - .75f);
                instances[i].transform.rotation = Quaternion.Euler(0, 35, 0);
            }
            Render(camera, output + "/After.png");
            camera.transform.position = Vector3.up * .75f - camera.transform.forward * 4;
            Render(camera, output + "/Closeup.png");
            camera.transform.position = Vector3.up * .75f - camera.transform.forward * 16;
            var hitTemplate = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content/BulletImpact");
            var hit = Object.Instantiate(hitTemplate.gameObject);
            hit.transform.position = new Vector3(2, .75f, 2.8f); hit.SetActive(true);
            hit.GetComponent<ParticleSystem>().Simulate(.08f, true, true, false);
            var muzzle = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Player/FX/PlayerMuzzleFlash.prefab"));
            muzzle.transform.position = new Vector3(-2, .75f, -2.6f);
            muzzle.GetComponent<ParticleSystem>().Simulate(.015f, true, true, false);
            Render(camera, output + "/Feedback.png");
            Object.DestroyImmediate(hit); Object.DestroyImmediate(muzzle);
            foreach (var instance in instances)
            {
                instance.GetComponent<MeshFilter>().sharedMesh = baselineMesh;
                instance.GetComponent<MeshRenderer>().sharedMaterials = new[] { AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Config/GrayboxMaterial.mat") };
            }
            Render(camera, output + "/Before.png");
            return Path.GetFullPath(output);
        }
        finally
        {
            RenderTexture.active = priorRt;
            if (groundMaterial != null) Object.DestroyImmediate(groundMaterial);
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>固定实际相机的距离、视角和 720p 输出，不加 Bloom 证明弹体自身的辨识度。</summary>
    private static void Render(Camera camera, string path)
    {
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = null;
            Object.DestroyImmediate(texture); Object.DestroyImmediate(rt);
        }
    }
}
