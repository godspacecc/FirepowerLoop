using System;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>在独立预览场景采样正式重型五动作和俯视比例；不运行战斗，不保存正式场景。</summary>
public static class ArcadeHeavyVisualPreview
{
    public static string Run()
    {
        if (Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅本项目编辑态预览");
        var scene = EditorSceneManager.NewPreviewScene();
        var prior = RenderTexture.active;
        RenderTexture rt = null; Texture2D texture = null; Camera camera = null;
        string directory = Path.GetFullPath("Temp/HeavyVisual-20261018"); Directory.CreateDirectory(directory);
        try
        {
            var cameraObject = new GameObject("PreviewCamera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>(); camera.scene = scene; camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f, .18f, .22f); camera.cullingMask = 1 << 31;
            var lightObject = new GameObject("PreviewLight", typeof(Light)); SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.7f; light.cullingMask = 1 << 31;
            light.transform.rotation = Quaternion.Euler(45, -30, 0);
            rt = new RenderTexture(1100, 900, 24); texture = new Texture2D(1100, 900, TextureFormat.RGB24, false); camera.targetTexture = rt;
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeEnemyBrute.prefab"));
            SceneManager.MoveGameObjectToScene(instance, scene); instance.transform.position = Vector3.zero; Layer(instance);
            var view = instance.GetComponent<EnemyView>(); view.SetCombatWeapon("weapon.greatsword");
            var animator = view.Animator;
            foreach (string state in new[] { "Idle", "Run", "Attack", "Hit", "Death" })
            {
                animator.Rebind(); animator.Update(0); animator.Play(state, 0, .35f); animator.Update(.02f);
                instance.GetComponent<CharacterWeaponPresenter>().ApplyOffhandGrip();
                var center = instance.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.enabled).bounds.center;
                camera.transform.position = center + new Vector3(3.3f, 1.5f, 4f); camera.transform.LookAt(center); camera.orthographicSize = 1.8f;
                Save(camera, texture, rt, directory + "/" + state + ".png");
            }
            animator.Rebind(); animator.Update(0); animator.Play("Idle", 0, .2f); animator.Update(.02f);
            var soldier = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeEnemyBasic.prefab"));
            SceneManager.MoveGameObjectToScene(soldier, scene); soldier.transform.position = new Vector3(-2.2f, 0, 0); Layer(soldier);
            soldier.GetComponent<EnemyView>().SetCombatWeapon("weapon.short-sword");
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            SceneManager.MoveGameObjectToScene(player, scene); player.transform.position = new Vector3(2.2f, 0, 0); Layer(player);
            camera.transform.position = new Vector3(0, 8, 5); camera.transform.LookAt(new Vector3(0, .5f, 0)); camera.orthographicSize = 3.4f;
            Save(camera, texture, rt, directory + "/top-down-comparison.png");
            return directory;
        }
        finally
        {
            RenderTexture.active = prior;
            if (camera != null) camera.targetTexture = null;
            if (texture != null) Object.DestroyImmediate(texture);
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Layer(GameObject root)
    { foreach (var transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 31; }

    private static void Save(Camera camera, Texture2D texture, RenderTexture target, string path)
    {
        camera.Render(); RenderTexture.active = target;
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG());
    }
}
