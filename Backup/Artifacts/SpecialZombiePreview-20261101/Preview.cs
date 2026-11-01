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
public static class SpecialZombiePreview
{
    static string Folder="E:/GitHub/FirepowerLoop/Backup/Artifacts/SpecialZombiePreview-20261101";
    static List<Material> materials=new List<Material>();
    static Material Mat(Color c){var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.color=c;materials.Add(m);return m;}
    static GameObject Prop(PrimitiveType type,Transform owner,Vector3 pos,Vector3 size,Color c){var o=GameObject.CreatePrimitive(type);o.transform.SetParent(owner,false);o.transform.localPosition=pos;o.transform.localScale=size;o.GetComponent<Renderer>().sharedMaterial=Mat(c);return o;}
    static GameObject Player(Vector3 pos){var p=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));p.transform.position=pos;p.transform.rotation=Quaternion.Euler(0,160,0);var w=p.GetComponent<CharacterWeaponPresenter>();if(!w.SetWeaponId("arcade.weapon.basic"))throw new Exception(w.LastError);w.SetManualPoseSampling(true);w.BodyAnimator.Rebind();w.BodyAnimator.Update(0);w.Current.Animations.Idle.Clip.SampleAnimation(w.BodyAnimator.gameObject,.2f);w.ApplyOffhandGrip(w.Current.Animations.Idle);foreach(var a in p.GetComponentsInChildren<Animator>())a.enabled=false;foreach(var c in p.GetComponentsInChildren<Canvas>(true))c.gameObject.SetActive(false);return p;}
    static GameObject Enemy(int kind,Vector3 pos){var e=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));e.transform.position=pos;e.transform.rotation=Quaternion.Euler(0,160,0);var v=e.GetComponent<EnemyView>();v.SetNavigationEnabled(false);v.BeginPresentationUse();v.SetPresentationVariant(kind);v.Animator.Rebind();v.Animator.Update(0);v.Animator.runtimeAnimatorController.animationClips.First(c=>c.name.EndsWith("|Idle")).SampleAnimation(v.Animator.gameObject,.3f);v.Animator.enabled=false;
        foreach(var r in e.GetComponentsInChildren<Renderer>()){var ms=r.sharedMaterials;for(int i=0;i<ms.Length;i++)if(ms[i]!=null&&ms[i].name.StartsWith("Shirt"))ms[i]=Mat(kind==0?new Color(.55f,.12f,.09f):kind==1?new Color(.27f,.13f,.42f):new Color(.19f,.27f,.24f));r.sharedMaterials=ms;}
        if(kind==0){for(int i=-1;i<=1;i++)Prop(PrimitiveType.Cylinder,e.transform,new Vector3(i*.15f,.95f,.22f),new Vector3(.11f,.20f,.11f),new Color(.64f,.09f,.04f));Prop(PrimitiveType.Cube,e.transform,new Vector3(0,.91f,.20f),new Vector3(.55f,.09f,.12f),Color.black);}
        if(kind==1){Prop(PrimitiveType.Cube,e.transform,new Vector3(0,1.31f,-.03f),new Vector3(.48f,.08f,.24f),new Color(.31f,.13f,.52f));Prop(PrimitiveType.Sphere,e.transform,new Vector3(.43f,1.20f,.2f),Vector3.one*.17f,new Color(.9f,.27f,.04f));}
        if(kind==2){Prop(PrimitiveType.Cube,e.transform,new Vector3(-.20f,.98f,.60f),new Vector3(.82f,1.13f,.12f),new Color(.27f,.34f,.39f));Prop(PrimitiveType.Cube,e.transform,new Vector3(-.20f,.98f,.68f),new Vector3(.08f,1.10f,.035f),new Color(.65f,.65f,.60f));Prop(PrimitiveType.Cube,e.transform,new Vector3(-.20f,.98f,.68f),new Vector3(.80f,.09f,.035f),new Color(.65f,.65f,.60f));}
        return e;
    }
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
            var p=Player(new Vector3(-2.7f,0,0));var actors=new GameObject[3];for(int i=0;i<3;i++)actors[i]=Enemy(i,new Vector3(-.9f+i*1.8f,0,0));Layers(sc);Save(cam,rt,tex,Folder+"/lineup.png");foreach(var e in actors)Object.DestroyImmediate(e);Object.DestroyImmediate(p);
            for(int kind=0;kind<3;kind++){
                string dir=Folder+"/frames-"+kind;Directory.CreateDirectory(dir);p=Player(new Vector3(-1.65f,0,0));var enemy=Enemy(kind,new Vector3(1.65f,0,0));
                var warning=new GameObject("Warning",typeof(LineRenderer)).GetComponent<LineRenderer>();warning.sharedMaterial=Mat(new Color(1,.15f,.03f));warning.useWorldSpace=true;warning.loop=true;warning.positionCount=64;warning.widthMultiplier=.045f;
                GameObject ball=null;GameObject blast=null;
                if(kind==1){ball=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Arcade/VFX/FirstBatch/Prefabs/Fireball.prefab"));ball.transform.localScale=Vector3.one*.35f;}
                if(kind==0){blast=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDroneExplosion.prefab"));blast.transform.localScale=Vector3.one*.8f;}
                for(int frame=0;frame<40;frame++){
                    float t=frame/10f;float wind=Mathf.Clamp01((t-.9f)/1f);enemy.SetActive(!(kind==0&&t>=2));enemy.transform.position=new Vector3(kind==0?Mathf.Lerp(1.65f,-.1f,Mathf.Clamp01(t)):1.65f,0,0);
                    warning.enabled=kind==0&&t>=1&&t<2;for(int j=0;j<64;j++){float a=j*Mathf.PI*2/64;warning.SetPosition(j,enemy.transform.position+new Vector3(Mathf.Cos(a)*1.5f,.015f,Mathf.Sin(a)*1.5f));}warning.startColor=warning.endColor=new Color(1,.15f,.03f,.5f+.5f*Mathf.Sin(t*30));
                    if(blast!=null){blast.SetActive(t>=2&&t<2.8f);blast.transform.position=enemy.transform.position+Vector3.up*.4f;foreach(var ps in blast.GetComponentsInChildren<ParticleSystem>()){ps.Simulate(Mathf.Max(0,t-2),false,true,true);ps.Pause();}}
                    if(ball!=null){ball.SetActive(t>=1.4f&&t<3);ball.transform.position=Vector3.Lerp(new Vector3(1.45f,1.1f,-.1f),new Vector3(-2f,1.1f,-.1f),Mathf.Clamp01((t-1.4f)/1.4f));foreach(var ps in ball.GetComponentsInChildren<ParticleSystem>()){ps.Simulate(Mathf.Max(0,t-1.4f),false,true,true);ps.Pause();}}
                    if(kind==1&&t>.7f&&t<1.5f){var v=enemy.GetComponent<EnemyView>();var clip=v.Animator.runtimeAnimatorController.animationClips.First(c=>c.name.EndsWith("|Idle_Attack"));clip.SampleAnimation(v.Animator.gameObject,clip.length*Mathf.Clamp01((t-.7f)/.8f));}
                    if(kind==2)enemy.transform.rotation=Quaternion.Euler(0,t<2?160:Mathf.Lerp(160,320,Mathf.Clamp01((t-2)/.7f)),0);
                    Layers(sc);Save(cam,rt,tex,dir+"/"+frame.ToString("D3")+".png");
                }
                Object.DestroyImmediate(p);Object.DestroyImmediate(enemy);Object.DestroyImmediate(warning.gameObject);if(ball!=null)Object.DestroyImmediate(ball);if(blast!=null)Object.DestroyImmediate(blast);
            }
            cam.targetTexture=null;return "lineup + 3 x 40 frames; temporary scene; no product asset changes";
        }finally{RenderTexture.active=old;if(tex!=null)Object.DestroyImmediate(tex);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(sc,true);foreach(var m in materials)if(m!=null)Object.DestroyImmediate(m);materials.Clear();}
    }
}
