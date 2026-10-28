using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Game;
using Object = UnityEngine.Object;

/// <summary>读取正式生成配置决定是否加载头顶模型，隔离拍摄采用效果，不保存产品资源。</summary>
public static class SkullOverheadAppliedCapture
{
    public static string Run()
    {
        if (Application.dataPath.Replace('\\','/') != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅允许本项目编辑态预览");
        var previous = SceneManager.GetActiveScene();
        var previousRT = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        Material floorMaterial = null;
        RenderTexture target = null;
        Texture2D pixels = null;
        string output = Path.GetFullPath("../Backup/Artifacts/SkullOverheadApplied-20261028");
        Directory.CreateDirectory(output);
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.62f,.64f,.65f);
            var skulls = new GameObject[2]; var tables = new global::TbArcadePowerup(new Luban.ByteBuf(File.ReadAllBytes("Assets/Game/Config/tbarcadepowerup.bytes")));
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -1.1f : 1.1f;
                var hero = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
                hero.transform.position = new Vector3(x,0,0);
                hero.transform.rotation = Quaternion.Euler(0,20,0);
                var presenter = hero.GetComponent<CharacterWeaponPresenter>();
                if (!presenter.SetWeaponId("arcade.weapon.basic")) throw new InvalidOperationException(presenter.LastError);
                presenter.SetManualPoseSampling(true);
                presenter.BodyAnimator.Rebind(); presenter.BodyAnimator.Update(0);
                presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.1f);
                presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
                foreach(var canvas in hero.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
                if(!string.IsNullOrEmpty(tables.Get(i==0?"arcade.powerup.skull.small":"arcade.powerup.skull.large").ActiveVisualPrefabName)) skulls[i] = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDropSkull"+(i==0?"Red":"Purple")+".prefab"));
                // 与正式强化投影一致：直接使用Prefab原比例、原旋转，锚点高度1.8米。
                if(skulls[i] != null) skulls[i].transform.position = new Vector3(x,1.8f,0);
            }
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.color = new Color(.22f,.28f,.28f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0,-.12f,0); floor.transform.localScale = new Vector3(25,.15f,25);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            foreach(var root in scene.GetRootGameObjects())
                foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
            var sun = new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.5f; sun.cullingMask = 1<<30;
            sun.transform.rotation = Quaternion.Euler(50,-35,0);
            var camera = new GameObject("ComparisonCamera",typeof(Camera)).GetComponent<Camera>();
            camera.cullingMask = 1<<30; camera.orthographic = true; camera.orthographicSize = 1.7f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.075f,.1f,.12f);
            var focus = new Vector3(0,1,0);
            camera.transform.position = focus + new Vector3(0,5.8f,8); camera.transform.LookAt(focus);
            target = new RenderTexture(1200,800,24); pixels = new Texture2D(1200,800,TextureFormat.RGB24,false);
            camera.targetTexture = target;
            foreach(bool proposed in new[]{true})
            {
                foreach(var skull in skulls) if(skull != null) skull.SetActive(true);
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,1200,800),0,0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(output,"applied.png"),pixels.EncodeToPNG());
            }
            camera.targetTexture = null;
            return "正式配置隔离回读截图已保存："+output;
        }
        finally
        {
            RenderTexture.active = previousRT;
            if(pixels != null) Object.DestroyImmediate(pixels);
            if(target != null) { target.Release(); Object.DestroyImmediate(target); }
            SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene,true);
            if(floorMaterial != null) Object.DestroyImmediate(floorMaterial);
        }
    }
}
