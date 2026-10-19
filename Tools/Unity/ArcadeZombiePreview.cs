using System;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>隔离场景拍摄正式僵尸资源的站姿、扑抓和受火效果，不保存场景或触碰玩家数据。</summary>
public static class ArcadeZombiePreview
{
    public static string Run() => Capture("lineup");
    public static string Claw() => Capture("claw");
    public static string Burning() => Capture("burning");

    private static string Capture(string phase)
    {
        if (Application.isPlaying || Application.dataPath.Replace('\\','/') != "E:/GitHub/FirepowerLoop/Client/Assets") throw new InvalidOperationException("仅本项目编辑态预览");
        var prior = SceneManager.GetActiveScene(); var oldTarget = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var ambient = RenderSettings.ambientLight; var mode = RenderSettings.ambientMode;
        RenderTexture target = null; Texture2D image = null;
        try
        {
            SceneManager.SetActiveScene(scene); RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.65f,.67f,.7f);
            var camera = new GameObject("ZombiePreview", typeof(Camera)).GetComponent<Camera>();
            camera.cullingMask = 1 << 30; camera.orthographic = true; camera.orthographicSize = 2.4f;
            var focus = new Vector3(0,1.3f,0); camera.transform.position = focus + new Vector3(0,3.5f,-7f); camera.transform.LookAt(focus);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f,.11f,.15f);
            var light = new GameObject("Key", typeof(Light)).GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.7f; light.transform.eulerAngles = new Vector3(50,-35,0); light.cullingMask = 1 << 30;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = new Vector3(0,-.08f,0); floor.transform.localScale = new Vector3(30,.1f,30);
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/Environment/Content/Slate.mat");
            var enemies = new GameObject[3];
            for (int i=0;i<3;i++)
            {
                string kind = new[] { "Shambler", "Gaunt", "Bloated" }[i];
                var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombie"+kind+".prefab")); enemies[i]=enemy;
                enemy.transform.position = new Vector3((i-1)*2.5f,0,0); enemy.transform.rotation = Quaternion.Euler(0,160,0);
                var view=enemy.GetComponent<EnemyView>(); view.SetNavigationEnabled(false); view.BeginPresentationUse(); view.SetPresentationVariant(i);
                view.Animator.Rebind(); view.Animator.Update(0);
                var clip=view.Animator.runtimeAnimatorController.animationClips.First(c=>c.name.EndsWith(phase=="lineup"?"|Idle":"|Idle_Attack"));
                clip.SampleAnimation(view.Animator.gameObject,phase=="lineup"?.5f:clip.length*.45f);
                view.Animator.enabled=false;
                if(phase=="burning" && i==2)
                {
                    view.SetFlameRemaining(.65f);
                    foreach(var p in enemy.GetComponentsInChildren<ParticleSystem>()){p.Simulate(.22f,false,false,false);p.Pause();}
                }
            }
            foreach(var root in scene.GetRootGameObjects()) foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=30;
            target=new RenderTexture(1440,810,24); image=new Texture2D(1440,810,TextureFormat.RGB24,false); camera.targetTexture=target;
            string folder=Path.GetFullPath("Temp/ZombieRosterPreview");Directory.CreateDirectory(folder);
            Save(camera,target,image,folder+"/"+phase+".png");camera.targetTexture=null;
            return folder;
        }
        finally
        {
            RenderTexture.active=oldTarget;RenderSettings.ambientMode=mode;RenderSettings.ambientLight=ambient;
            if(image!=null)Object.DestroyImmediate(image);if(target!=null){target.Release();Object.DestroyImmediate(target);}
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
        }
    }

    private static void Save(Camera camera, RenderTexture target, Texture2D image, string file)
    {
        camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(file,image.EncodeToPNG());
    }
}
