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
public static class EnchantZombiePreview
{
    static string Folder="E:/GitHub/FirepowerLoop/Backup/Artifacts/EnchantZombiePreview-20261102";
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
            var colors=new[]{new Color(.8f,.14f,.22f),new Color(.12f,.65f,.25f),new Color(.15f,.4f,.95f)};
            var p=Player(new Vector3(-2.7f,0,0));var actors=new GameObject[3];
            for(int i=0;i<3;i++){
                actors[i]=Enemy(1,new Vector3(-.9f+i*1.8f,0,0));
                foreach(var r in actors[i].GetComponentsInChildren<Renderer>()){
                    var ms=r.sharedMaterials;for(int k=0;k<ms.Length;k++)if(ms[k]!=null&&(ms[k].name.Contains("Mantle")||ms[k].name.Contains("Orb")||ms[k].name.StartsWith("Shirt")))ms[k]=Mat(colors[i]);r.sharedMaterials=ms;
                }
            }
            Layers(sc);Save(cam,rt,tex,Folder+"/lineup.png");foreach(var e in actors)Object.DestroyImmediate(e);Object.DestroyImmediate(p);
            for(int kind=0;kind<3;kind++){
                var folder=Folder+"/frames-"+kind;Directory.CreateDirectory(folder);p=Player(new Vector3(-2.7f,0,0));var caster=Enemy(1,new Vector3(-.7f,0,0));var target=Enemy(0,new Vector3(2.0f,0,0));
                // 收益目标只用正式普通丧尸；移除自爆预览无关装备。
                Object.DestroyImmediate(target);target=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));target.transform.position=new Vector3(2,0,0);target.transform.rotation=Quaternion.Euler(0,160,0);var tv=target.GetComponent<EnemyView>();tv.SetNavigationEnabled(false);tv.BeginPresentationUse();tv.Animator.Rebind();tv.Animator.Update(0);tv.Animator.runtimeAnimatorController.animationClips.First(c=>c.name.EndsWith("|Idle")).SampleAnimation(tv.Animator.gameObject,.3f);tv.Animator.enabled=false;
                foreach(var r in caster.GetComponentsInChildren<Renderer>()){var ms=r.sharedMaterials;for(int k=0;k<ms.Length;k++)if(ms[k]!=null&&(ms[k].name.Contains("Mantle")||ms[k].name.Contains("Orb")||ms[k].name.StartsWith("Shirt")))ms[k]=Mat(colors[kind]);r.sharedMaterials=ms;}
                var pulse=new GameObject("EnchantPulse",typeof(LineRenderer)).GetComponent<LineRenderer>();pulse.loop=true;pulse.positionCount=64;pulse.widthMultiplier=.05f;pulse.useWorldSpace=true;var pm=new Material(Shader.Find("Universal Render Pipeline/Unlit"));pm.color=colors[kind];materials.Add(pm);pulse.sharedMaterial=pm;
                var received=new GameObject("ReceivedEnchant",typeof(LineRenderer)).GetComponent<LineRenderer>();received.loop=true;received.positionCount=64;received.widthMultiplier=.04f;received.useWorldSpace=true;received.sharedMaterial=pm;
                for(int frame=0;frame<40;frame++){
                    float time=frame/10f;float t=Mathf.Clamp01((time-.5f)/1.3f);float radius=Mathf.Lerp(.1f,3.2f,t);pulse.enabled=time>=.5f&&time<1.9f;received.enabled=time>=1.7f;
                    for(int k=0;k<64;k++){float angle=k*Mathf.PI*2/64;pulse.SetPosition(k,caster.transform.position+new Vector3(Mathf.Cos(angle)*radius,.03f,Mathf.Sin(angle)*radius));received.SetPosition(k,target.transform.position+new Vector3(Mathf.Cos(angle)*.48f,.03f,Mathf.Sin(angle)*.48f));}
                    Layers(sc);Save(cam,rt,tex,folder+"/"+frame.ToString("D3")+".png");
                }
                Object.DestroyImmediate(p);Object.DestroyImmediate(caster);Object.DestroyImmediate(target);Object.DestroyImmediate(pulse.gameObject);Object.DestroyImmediate(received.gameObject);
            }
            cam.targetTexture=null;return "3 enchantment variants and pulse concepts; no formal resource changes";
        }finally{RenderTexture.active=old;if(tex!=null)Object.DestroyImmediate(tex);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(sc,true);foreach(var m in materials)if(m!=null)Object.DestroyImmediate(m);materials.Clear();}
    }
}

public class Script{public static object Main(){return EnchantZombiePreview.Run();}}