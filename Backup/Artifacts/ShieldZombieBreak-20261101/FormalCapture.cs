using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Game;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// 只在隔离临时场景制作拟议敌人外观和攻击节奏，不写正式资源。
public static class ShieldBreakFormalCapture
{
    static string Folder="E:/GitHub/FirepowerLoop/Backup/Artifacts/ShieldZombieBreak-20261101/formal";
    static List<Material> materials=new List<Material>();
    static Material Mat(Color c){var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.color=c;materials.Add(m);return m;}
    static GameObject Prop(PrimitiveType type,Transform owner,Vector3 pos,Vector3 size,Color c){var o=GameObject.CreatePrimitive(type);o.transform.SetParent(owner,false);o.transform.localPosition=pos;o.transform.localScale=size;o.GetComponent<Renderer>().sharedMaterial=Mat(c);return o;}
    static GameObject Player(Vector3 pos){var p=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));p.transform.position=pos;p.transform.rotation=Quaternion.Euler(0,160,0);var w=p.GetComponent<CharacterWeaponPresenter>();if(!w.SetWeaponId("arcade.weapon.basic"))throw new Exception(w.LastError);w.SetManualPoseSampling(true);w.BodyAnimator.Rebind();w.BodyAnimator.Update(0);w.Current.Animations.Idle.Clip.SampleAnimation(w.BodyAnimator.gameObject,.2f);w.ApplyOffhandGrip(w.Current.Animations.Idle);foreach(var a in p.GetComponentsInChildren<Animator>())a.enabled=false;foreach(var c in p.GetComponentsInChildren<Canvas>(true))c.gameObject.SetActive(false);return p;}
    static GameObject Enemy(int kind,Vector3 pos){var names=new[]{"Exploder","Caster","Shield"};var e=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombie"+names[kind]+".prefab"));e.transform.position=pos;e.transform.rotation=Quaternion.Euler(0,160,0);var v=e.GetComponent<EnemyView>();v.SetNavigationEnabled(false);v.BeginPresentationUse();v.Animator.Rebind();v.Animator.Update(0);v.Animator.runtimeAnimatorController.animationClips.First(c=>c.name.EndsWith("|Idle")).SampleAnimation(v.Animator.gameObject,.3f);v.Animator.enabled=false;return e;}
    static void Layers(Scene s){foreach(var r in s.GetRootGameObjects())foreach(var t in r.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;}
    static void Save(Camera cam,RenderTexture rt,Texture2D tex,string path){cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());}
    public static string Run()
    {
        if(Application.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("仅正确工程编辑态");
        var prior=SceneManager.GetActiveScene();var old=RenderTexture.active;var sc=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);RenderTexture rt=null;Texture2D tex=null;
        try{
            SceneManager.SetActiveScene(sc);RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.65f,.67f,.70f);
            var cam=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();cam.orthographic=true;cam.orthographicSize=2.1f;cam.cullingMask=1<<30;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.07f,.10f,.14f);var focus=new Vector3(0,.8f,0);cam.transform.position=focus+new Vector3(0,2,-8);cam.transform.LookAt(focus);
            var light=new GameObject("Key",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.7f;light.transform.eulerAngles=new Vector3(50,-35,0);light.cullingMask=1<<30;
            Prop(PrimitiveType.Cube,null,new Vector3(0,-.08f,0),new Vector3(25,.1f,25),new Color(.23f,.28f,.30f));
            rt=new RenderTexture(960,540,24);tex=new Texture2D(960,540,TextureFormat.RGB24,false);cam.targetTexture=rt;
            var p=Player(new Vector3(-1.6f,0,0));var enemy=Enemy(2,new Vector3(1.6f,0,0));Layers(sc);Save(cam,rt,tex,Folder+"/intact.png");
            enemy.GetComponent<EnemyView>().SetSurvivorShield(false);
            Save(cam,rt,tex,Folder+"/broken.png");
            cam.targetTexture=null;return "intact + broken cloned prefab; product unchanged";
        }finally{RenderTexture.active=old;if(tex!=null)Object.DestroyImmediate(tex);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(sc,true);foreach(var m in materials)if(m!=null)Object.DestroyImmediate(m);materials.Clear();}
    }
}

public class Script{public static object Main(){return ShieldBreakFormalCapture.Run();}}