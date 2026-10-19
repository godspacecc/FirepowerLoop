using System;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>在隔离场景采样正式敌人受火和死亡资源，保留当前场景，不运行战斗或保存数据。</summary>
public static class ArcadeEnemyFlamePreview
{
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态预览");
        var prior=SceneManager.GetActiveScene();var oldTarget=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;
        RenderTexture target=null;Texture2D image=null;
        try
        {
            SceneManager.SetActiveScene(scene);RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.52f,.55f);
            for(int i=0;i<2;i++)
            {
                var enemy=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeEnemyBasic.prefab"));
                enemy.transform.position=new Vector3(i==0?-1.8f:1.8f,0,0);enemy.transform.rotation=Quaternion.Euler(0,200,0);
                var view=enemy.GetComponent<EnemyView>();view.SetNavigationEnabled(false);
                var animator=view.Animator;animator.Rebind();animator.Update(0);
                var clip=animator.runtimeAnimatorController.animationClips.First(c=>c.name.Contains(i==0?"CombatDamage":"Death"));
                clip.SampleAnimation(animator.gameObject,i==0?.15f:clip.length*.9f);
                if(i==0)view.SetFlameRemaining(.65f);else view.PlayFlameDeath(true);
                foreach(var ps in enemy.GetComponentsInChildren<ParticleSystem>())
                {
                    if(ps.name=="BodyFlame"||ps.name=="DeathSmoke"){if(ps.isPlaying)ps.Simulate(i==0?.25f:.35f,false,false,false);}
                    else ps.Simulate(.12f,false,false,false);
                    ps.Pause();
                }
            }
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(30,.1f,30);
            floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/Environment/Content/Slate.mat");
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            var light=new GameObject("PreviewKey",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.7f;light.transform.eulerAngles=new Vector3(50,-35,0);light.cullingMask=1<<30;
            var camera=new GameObject("EnemyFlamePreviewCamera",typeof(Camera)).GetComponent<Camera>();
            camera.cullingMask=1<<30;camera.orthographic=true;camera.orthographicSize=1.8f;
            var focus=new Vector3(0,.5f,0);camera.transform.position=focus+new Vector3(0,7,-7);camera.transform.LookAt(focus);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.12f,.16f);
            target=new RenderTexture(1440,900,24);image=new Texture2D(1440,900,TextureFormat.RGB24,false);
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1440,900),0,0);image.Apply();camera.targetTexture=null;
            string folder=Path.GetFullPath("Temp/FlamethrowerPreview");Directory.CreateDirectory(folder);
            string file=Path.Combine(folder,"enemy-hit-and-kill.png");File.WriteAllBytes(file,image.EncodeToPNG());return file;
        }
        finally
        {
            RenderTexture.active=oldTarget;RenderSettings.ambientMode=mode;RenderSettings.ambientLight=ambient;
            if(image!=null)Object.DestroyImmediate(image);if(target!=null)Object.DestroyImmediate(target);
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
        }
    }
}
