using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
/// <summary>隔离预览及已采用模板烘焙；候选与正式模板回放分别保存，角色仅使用克隆实例。</summary>
public static class WeaponEvolutionPreview
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static object Field(object o,string name)=>o.GetType().GetField(name,Fields).GetValue(o);
    private static void Set(object o,string name,object value)=>o.GetType().GetField(name,Fields).SetValue(o,value);
    private static object Call(object o,string name,params object[] args)
    {try{return o.GetType().GetMethod(name,Fields).Invoke(o,args);}catch(TargetInvocationException error){throw new Exception(name+": "+error.InnerException,error);}}
    public static string Run(int kind)=>Capture(kind,false);
    public static string RunV2(int kind)=>EvolutionPreviewFx.Record(kind);
    public static string RunGrenadeV3()=>EvolutionPreviewFx.Record(5,true);
    public static string RecordApplied(int kind)=>EvolutionPreviewFx.Record(kind,kind==5,true);
    public static string BakeAdopted()=>EvolutionPreviewFx.BakeAdopted();
    private static string Capture(int kind,bool refined)
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("仅本工程编辑态");
        string[] weapons={"basic","rapid","spread","flamethrower","piercing","grenade","rocket"};
        string weapon=weapons[kind];
        Type fixtureType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Game.Tests.ArcadeWeaponEvolutionTests")).First(t=>t!=null);
        object fixture=Activator.CreateInstance(fixtureType);Call(fixture,"Setup");Call(fixture,"Ready",weapon,true);
        object runtime=Field(fixture,"runtime");object system=Field(fixture,"system");
        var enemies=(IDictionary)Field(runtime,"Enemies");var projectiles=(IDictionary)Field(runtime,"Projectiles");
        if(kind==0){Call(fixture,"Enemy",501,0f,2.8f,1);Call(fixture,"Enemy",502,0f,5.2f,1);Call(fixture,"Enemy",503,0f,7.6f,1);}
        else if(kind==2){Call(fixture,"Enemy",501,0f,2.8f,1000);Call(fixture,"Enemy",502,.9f,3.1f,1000);Call(fixture,"Enemy",503,-.9f,3.1f,1000);}
        else if(kind==3)Call(fixture,"Enemy",501,0f,5f,1000);
        else if(kind==4){Call(fixture,"Enemy",501,1.6f,2.7f,1000);Call(fixture,"Enemy",502,1.6f,4.8f,1000);Call(fixture,"Enemy",503,1.6f,6.9f,1000);}
        else if(kind==6){Call(fixture,"Pick","support.drone");Call(fixture,"Enemy",501,-4f,5f,1000);Call(fixture,"Enemy",502,4f,5f,1000);}
        else if(kind==1){Call(fixture,"Enemy",501,-1.2f,7f,1000);Call(fixture,"Enemy",502,0f,7f,1000);Call(fixture,"Enemy",503,1.2f,7f,1000);}
        var scene=EditorSceneManager.NewPreviewScene();var owned=new List<Object>();var visuals=new Dictionary<string,GameObject>();
        var previous=RenderTexture.active;RenderTexture rt=null;Texture2D pixels=null;
        Material Mat(Color color){var mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));mat.color=color;owned.Add(mat);return mat;}
        GameObject Primitive(string key,PrimitiveType shape,Material mat)
        {if(visuals.TryGetValue(key,out var old))return old;var go=GameObject.CreatePrimitive(shape);Object.DestroyImmediate(go.GetComponent<Collider>());SceneManager.MoveGameObjectToScene(go,scene);go.GetComponent<Renderer>().sharedMaterial=mat;visuals[key]=go;return go;}
        GameObject Clone(string path){var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));SceneManager.MoveGameObjectToScene(go,scene);return go;}
        try
        {
            var ground=Mat(new Color(.10f,.14f,.16f));var purple=Mat(new Color(.75f,.16f,1));var orange=Mat(new Color(1,.25f,.025f));var hot=Mat(new Color(1,.7f,.08f));var burnFloor=Mat(new Color(.25f,.09f,.025f));var cyan=Mat(new Color(.2f,.9f,1));
            var floor=Primitive("floor",PrimitiveType.Cube,ground);floor.transform.position=new Vector3(0,-.13f,4);floor.transform.localScale=new Vector3(20,.2f,20);
            var hero=Clone("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");hero.transform.position=Vector3.zero;hero.transform.rotation=Quaternion.identity;
            foreach(var canvas in hero.GetComponentsInChildren<Canvas>(true))canvas.gameObject.SetActive(false);
            hero.GetComponent<PlayerView>().SetSharedWeaponAnimationActive(true);var presenter=hero.GetComponent<CharacterWeaponPresenter>();
            if(!presenter.SetWeaponId("arcade.weapon."+(kind==6?"basic":weapon)))throw new Exception(presenter.LastError);
            presenter.BodyAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(.05f);presenter.ApplyOffhandGrip();
            foreach(DictionaryEntry pair in enemies)
            {
                var go=Clone("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab");visuals["enemy"+pair.Key]=go;
                foreach(var canvas in go.GetComponentsInChildren<Canvas>(true))canvas.gameObject.SetActive(false);
                foreach(var animator in go.GetComponentsInChildren<Animator>(true)){animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Update(.05f);}
            }
            if(kind==6)for(int i=0;i<2;i++)visuals["drone"+i]=Clone("Assets/Game/Prefabs/SceneObjects/ArcadeTimedDroneVisual.prefab");
            var cameraGo=new GameObject("EvolutionPreviewCamera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraGo,scene);var camera=cameraGo.GetComponent<Camera>();camera.scene=scene;
            camera.orthographic=true;camera.orthographicSize=5.9f;camera.aspect=1.5f;camera.transform.position=new Vector3(7,15,-10);camera.transform.LookAt(new Vector3(0,0,3.7f));
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.055f,.08f);camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.cullingMask=1<<30;
            var lightGo=new GameObject("PreviewSun",typeof(Light));SceneManager.MoveGameObjectToScene(lightGo,scene);var light=lightGo.GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(50,-30,0);
            int frameWidth=refined?960:720,frameHeight=refined?640:480;
            rt=new RenderTexture(frameWidth,frameHeight,24);pixels=new Texture2D(frameWidth,frameHeight,TextureFormat.RGB24,false);camera.targetTexture=rt;
            var fx=refined?new EvolutionPreviewFx(scene,owned,camera):null;
            string folder=Path.GetFullPath("Temp/WeaponEvolutionPreview-"+(refined?"v2":"v1")+"/"+kind);Directory.CreateDirectory(folder);
            int fired=0;float sim=0;const float dt=.025f;int lastCount=0;var events=new List<string>();
            for(int frame=0;frame<(refined?160:80);frame++)
            {
                sim+=dt;
                if(kind==6)Call(system,"TickDrone",runtime,dt);
                else
                {
                    int desired=kind==1?(sim<.9f?Math.Min(9,(int)(sim/.10f)):9):kind==4?Math.Min(4,(int)(sim/(refined?.38f:.10f))):kind==3?Math.Min(3,(int)(sim/.10f)):sim>=.1f?1:0;
                    if(fired<desired){Call(fixture,"Fire");fired++;presenter.PlayUpperBodyGunFire();presenter.PlayWeaponMuzzleEffect();}
                }
                if(kind==3&&sim>.7f&&enemies.Contains(501)){Set(enemies[501],"Position",new Vector2(0,1.8f));}
                Call(system,"TickProjectiles",runtime,dt);
                fx?.Begin(dt,sim,kind);
                presenter.BodyAnimator.Update(dt);presenter.ApplyOffhandGrip();
                var activeKeys=new HashSet<string>{"floor"};
                foreach(DictionaryEntry pair in enemies)
                {
                    string key="enemy"+pair.Key;activeKeys.Add(key);var pos=(Vector2)Field(pair.Value,"Position");var go=visuals[key];go.SetActive(true);go.transform.position=new Vector3(pos.x,0,pos.y);go.transform.rotation=Quaternion.Euler(0,180,0);
                    int health=(int)Field(pair.Value,"Health");if(health<1000&&kind!=0)events.Add(frame+": enemy "+pair.Key+" hp="+health);
                }
                foreach(DictionaryEntry pair in projectiles)
                {
                    object shot=pair.Value;string key="shot"+pair.Key;activeKeys.Add(key);var pos=(Vector2)Field(shot,"Position");var velocity=(Vector2)Field(shot,"Velocity");float width=(float)Field(shot,"EvolutionHitRadius");bool child=(bool)Field(shot,"EvolutionClusterChild");
                    bool grenade=kind==5;var go=Primitive(key,grenade?PrimitiveType.Sphere:PrimitiveType.Cube,kind==3?orange:width>0?cyan:purple);
                    if(refined){go.SetActive(false);fx.Shot(pair.Key.ToString(),pos,velocity,width,child,kind);continue;}
                    go.SetActive(true);go.transform.position=new Vector3(pos.x,kind==6?2:1.15f,pos.y);go.transform.rotation=Quaternion.LookRotation(new Vector3(velocity.x,0,velocity.y));
                    go.transform.localScale=grenade?Vector3.one*(child ? .22f : .4f):new Vector3(width>0?width*2:.12f,kind==3?.2f:.1f,width>0?2f:kind==3?.7f:.5f);
                }
                var areas=(IList)Field(runtime,"WeaponBurnAreas");
                foreach(object area in areas)
                {
                    string key="fire"+Field(area,"Id");activeKeys.Add(key);var go=Primitive(key,PrimitiveType.Cylinder,burnFloor);go.SetActive(true);go.transform.position=new Vector3((float)Field(area,"X"),.035f,(float)Field(area,"Z"));go.transform.localScale=new Vector3(1.3f,.02f,1.3f);
                    if(refined){go.SetActive(false);fx.Ground(key,new Vector2((float)Field(area,"X"),(float)Field(area,"Z")),(float)Field(area,"RemainingSeconds"));continue;}
                    for(int i=0;i<4;i++){string flameKey=key+"flame"+i;activeKeys.Add(flameKey);var flame=Primitive(flameKey,PrimitiveType.Capsule,i%2==0?hot:orange);flame.SetActive(true);float angle=i*Mathf.PI/2;flame.transform.position=go.transform.position+new Vector3(Mathf.Cos(angle)*.3f,.13f,Mathf.Sin(angle)*.3f);flame.transform.localScale=new Vector3(.12f,.20f+.08f*Mathf.Sin(frame*.8f+i),.12f);}
                }
                if(kind==6)
                {
                    var powers=(IDictionary)Field(runtime,"Powerups");object support=powers.Values.Cast<object>().First(o=>Field(o,"ProductId") as string=="arcade.support.drone");
                    object[] states={Field(support,"Drone"),Field(support,"ExtraDrone")};
                    for(int i=0;i<2;i++)if(states[i]!=null){string key="drone"+i;activeKeys.Add(key);var go=visuals[key];go.SetActive(true);go.transform.position=new Vector3((float)Field(states[i],"X"),2,(float)Field(states[i],"Z"));float dx=(float)Field(states[i],"DirectionX"),dz=(float)Field(states[i],"DirectionZ");if(dx*dx+dz*dz>.001f)go.transform.rotation=Quaternion.LookRotation(new Vector3(dx,0,dz));}
                }
                foreach(var pair in visuals)if(!activeKeys.Contains(pair.Key))pair.Value.SetActive(false);
                fx?.End();
                foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
                camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,frameWidth,frameHeight),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,frame.ToString("D3")+".png"),pixels.EncodeToPNG());
                if(projectiles.Count!=lastCount){events.Add(frame+": projectileCount="+projectiles.Count);lastCount=projectiles.Count;}
            }
            File.WriteAllLines(Path.Combine(folder,"facts.txt"),events);camera.targetTexture=null;return folder;
        }
        finally{RenderTexture.active=previous;if(pixels!=null)Object.DestroyImmediate(pixels);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}EditorSceneManager.ClosePreviewScene(scene);foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item);}
    }
}

/// <summary>独立预览的粒子、光晕和拖尾候选；只创建内存材质与预览对象，不写正式资源。</summary>
internal sealed class EvolutionPreviewFx
{
    /// <summary>仅在具体预览采用后调用；按同一粒子参数保存正式模板，重复生成保留GUID。</summary>
    public static string BakeAdopted()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("仅本工程编辑态");
        const string art="Assets/Game/Art/Arcade/VFX/WeaponEvolution";
        Directory.CreateDirectory(art);AssetDatabase.Refresh();
        var preview=EditorSceneManager.NewPreviewScene();var resources=new List<Object>();var saved=new List<string>();
        T Save<T>(T value,string name)where T:Object
        {
            string path=art+"/"+name;var existing=AssetDatabase.LoadAssetAtPath<T>(path);
            if(existing!=null){EditorUtility.CopySerialized(value,existing);EditorUtility.SetDirty(existing);return existing;}
            AssetDatabase.CreateAsset(value,path);return value;
        }
        try
        {
            var materials=new EvolutionPreviewFx(preview,resources,null);
            var soft=Save((Texture2D)materials.glow.GetTexture("_BaseMap"),"SoftGlow.asset");materials.glow.SetTexture("_BaseMap",soft);
            var fireMat=Save(materials.fire,"Fire.mat");var smokeMat=Save(materials.smoke,"Smoke.mat");
            var glowMat=Save(materials.glow,"SoftGlow.mat");var metalMat=Save(materials.metal,"GrenadeMetal.mat");
            string[] names={"ArcadeWeaponBurnGround","ArcadeWeaponPiercingEvolved","ArcadeWeaponGrenadeEvolved","ArcadeWeaponGrenadePrimaryBlast","ArcadeWeaponGrenadeChildBlast"};
            for(int mode=1;mode<=5;mode++)
            {
                var fx=new EvolutionPreviewFx(preview,resources,null);fx.fire=fireMat;fx.smoke=smokeMat;fx.glow=glowMat;fx.metal=metalMat;fx.largeGrenade=true;
                fx.Begin(.025f,0,mode==2?4:5);
                if(mode==1)fx.Ground("ground",Vector2.zero,2);
                if(mode==2)fx.Shot("beam",Vector2.zero,Vector2.up,.65f,false,4);
                if(mode==3)fx.Shot("parent",Vector2.zero,Vector2.up,0,false,5);
                if(mode>=4){fx.Blast(Vector3.zero,mode==5);fx.End();}
                Vector3 origin=new Vector3(0,mode==1?.06f:mode==2?1.15f:mode==3?.45f:.55f,0);
                var root=new GameObject(names[mode-1]);SceneManager.MoveGameObjectToScene(root,preview);root.transform.position=origin;
                foreach(var particle in fx.loops.Values.Concat(fx.bursts))
                {
                    particle.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                    if(mode>=4)
                    {
                        var main=particle.main;main.loop=false;main.duration=1.6f;
                        int count=particle.name=="blast-fire"?(mode==5?22:38):particle.name=="blast-sparks"?(mode==5?20:40):20;
                        var emission=particle.emission;emission.enabled=true;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,(short)count)});
                    }
                    particle.transform.SetParent(root.transform,true);
                }
                foreach(var line in fx.lines.Values)
                {
                    var positions=new Vector3[line.positionCount];line.GetPositions(positions);
                    line.transform.SetParent(root.transform,false);line.useWorldSpace=false;
                    for(int i=0;i<positions.Length;i++)positions[i]-=origin;line.SetPositions(positions);
                }
                foreach(var body in fx.grenadeBodies.Values)body.transform.SetParent(root.transform,true);
                root.transform.position=Vector3.zero;
                var visual=root.AddComponent<ArcadeWeaponEvolutionVisual>();var serialized=new SerializedObject(visual);serialized.FindProperty("mode").intValue=mode;serialized.ApplyModifiedPropertiesWithoutUndo();
                string path="Assets/Game/Prefabs/SceneObjects/"+names[mode-1]+".prefab";
                PrefabUtility.SaveAsPrefabAsset(root,path);saved.Add(path);Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();return string.Join("\n",saved);
        }
        finally{EditorSceneManager.ClosePreviewScene(preview);foreach(var resource in resources)if(resource!=null&&!AssetDatabase.Contains(resource))Object.DestroyImmediate(resource);}
    }
    /// <summary>单独演示待采用视觉，时序按已确认玩法编排，不依赖正在变更的关卡目录，不冒充实战。</summary>
    public static string Record(int kind,bool grenadeV3=false,bool adopted=false)
    {
        if(kind<3||kind>5||EditorApplication.isPlaying||EditorApplication.isCompiling)throw new Exception("仅编辑态录制第二组视觉候选");
        var preview=EditorSceneManager.NewPreviewScene();var resources=new List<Object>();var old=RenderTexture.active;RenderTexture rt=null;Texture2D pixels=null;
        GameObject Clone(string path){var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));SceneManager.MoveGameObjectToScene(go,preview);foreach(var c in go.GetComponentsInChildren<Canvas>(true))c.gameObject.SetActive(false);return go;}
        try
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);Object.DestroyImmediate(floor.GetComponent<Collider>());SceneManager.MoveGameObjectToScene(floor,preview);floor.transform.position=new Vector3(0,-.12f,4);floor.transform.localScale=new Vector3(20,.2f,20);
            var ground=new Material(Shader.Find("Universal Render Pipeline/Unlit"));ground.color=new Color(.1f,.14f,.16f);resources.Add(ground);floor.GetComponent<Renderer>().sharedMaterial=ground;
            var hero=Clone("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");var weapon=hero.GetComponent<CharacterWeaponPresenter>();hero.GetComponent<PlayerView>().SetSharedWeaponAnimationActive(true);
            string[] ids={"flamethrower","piercing","grenade"};if(!weapon.SetWeaponId("arcade.weapon."+ids[kind-3]))throw new Exception(weapon.LastError);
            weapon.BodyAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;weapon.BodyAnimator.Rebind();weapon.BodyAnimator.Update(.05f);weapon.ApplyOffhandGrip();
            var foes=new List<GameObject>();if(kind!=5)for(int i=0;i<(kind==4?3:1);i++){var foe=Clone("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab");foe.transform.position=kind==4?new Vector3(1.6f,0,2.7f+2*i):new Vector3(0,0,4.6f);foe.transform.rotation=Quaternion.Euler(0,180,0);foreach(var animator in foe.GetComponentsInChildren<Animator>(true)){animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Update(.05f);}foes.Add(foe);}
            var camGo=new GameObject("v2-candidate-camera",typeof(Camera));SceneManager.MoveGameObjectToScene(camGo,preview);var camera=camGo.GetComponent<Camera>();camera.scene=preview;camera.orthographic=true;camera.orthographicSize=5.5f;camera.aspect=1.5f;camera.transform.position=new Vector3(7,15,-10);camera.transform.LookAt(new Vector3(0,0,3.7f));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.055f,.08f);camera.cullingMask=1<<30;
            var sunGo=new GameObject("sun",typeof(Light));SceneManager.MoveGameObjectToScene(sunGo,preview);var sun=sunGo.GetComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.6f;sun.cullingMask=1<<30;sunGo.transform.rotation=Quaternion.Euler(50,-30,0);
            rt=new RenderTexture(960,640,24);pixels=new Texture2D(960,640,TextureFormat.RGB24,false);camera.targetTexture=rt;
            var fx=new EvolutionPreviewFx(preview,resources,camera);fx.largeGrenade=grenadeV3;fx.adopted=adopted;string folder=Path.GetFullPath("Temp/WeaponEvolutionPreview-"+(adopted?"applied":grenadeV3?"v3":"v2")+"/"+kind);Directory.CreateDirectory(folder);int lastFire=0;
            for(int frame=0;frame<160;frame++)
            {
                float t=(frame+1)*.025f;fx.Begin(.025f,t,kind);int shot=kind==4?Math.Min(4,(int)(t/.38f)):kind==3?Math.Min(3,(int)(t/.10f)):t>.1f?1:0;
                if(shot>lastFire){weapon.PlayUpperBodyGunFire();weapon.PlayWeaponMuzzleEffect();lastFire=shot;}
                if(kind==3)
                {
                    for(int wave=0;wave<3;wave++)
                    {
                        float age=t-.1f*(wave+1);
                        if(age>=0&&age<.45f)for(int lane=-1;lane<=1;lane++){float a=lane*9*Mathf.Deg2Rad;fx.Shot("flame"+wave+":"+lane,new Vector2(Mathf.Sin(a)*age*8,1+Mathf.Cos(a)*age*8),new Vector2(Mathf.Sin(a),Mathf.Cos(a))*8,0,false,3);}
                    }
                    for(int row=0;row<5;row++)for(int lane=-1;lane<=1;lane++)
                    {float z=1.2f+row*.6f,start=.12f+row*.075f;float remaining=2.45f+row*.04f-t;if(t>start&&remaining>0)fx.Ground("ground"+row+":"+lane,new Vector2(lane*z*.15f,z),remaining);}
                    if(t>.7f)foes[0].transform.position=new Vector3(0,0,2.9f);
                }
                else if(kind==4)
                {
                    for(int i=1;i<=4;i++){float age=t-i*.38f;if(age>=0&&age<.35f)fx.Shot("pierce"+i,new Vector2(0,1+age*24),new Vector2(0,24),i==4?.65f:0,false,4);}
                }
                else
                {
                    float age=t-.1f;
                    if(age>=0&&age<.85f)fx.Shot("parent",new Vector2(0,.8f+age*6),new Vector2(0,6),0,false,5);
                    float split=t-.95f;
                    if(split>=0&&split<.35f)for(int i=0;i<3;i++){float a=i*2*Mathf.PI/3;var direction=new Vector2(Mathf.Sin(a),Mathf.Cos(a));fx.Shot("child"+i,new Vector2(0,5.9f)+direction*split*3,direction*3,0,true,5);}
                }
                fx.End();weapon.BodyAnimator.Update(.025f);weapon.ApplyOffhandGrip();
                foreach(var root in preview.GetRootGameObjects())foreach(var tr in root.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=30;
                camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,960,640),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,frame.ToString("D3")+".png"),pixels.EncodeToPNG());
            }
            camera.targetTexture=null;return folder;
        }
        finally{RenderTexture.active=old;if(pixels!=null)Object.DestroyImmediate(pixels);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}EditorSceneManager.ClosePreviewScene(preview);foreach(var r in resources)if(r!=null)Object.DestroyImmediate(r);}
    }
    private readonly Scene scene;
    private readonly List<Object> owned;
    private readonly Dictionary<string,ParticleSystem> loops=new Dictionary<string,ParticleSystem>();
    private readonly Dictionary<string,LineRenderer> lines=new Dictionary<string,LineRenderer>();
    private readonly Dictionary<string,Vector3> lastShots=new Dictionary<string,Vector3>();
    private readonly Dictionary<string,bool> lastChildren=new Dictionary<string,bool>();
    private readonly Dictionary<string,float> shotBirth=new Dictionary<string,float>();
    private readonly Dictionary<string,GameObject> grenadeBodies=new Dictionary<string,GameObject>();
    private readonly HashSet<string> seen=new HashSet<string>();
    private readonly List<ParticleSystem> bursts=new List<ParticleSystem>();
    private readonly List<Vector4> rings=new List<Vector4>();
    private Material fire,smoke,glow;
    private Material metal;
    private bool largeGrenade,adopted;
    private readonly Dictionary<string,ArcadeWeaponEvolutionVisual> adoptedVisuals=new Dictionary<string,ArcadeWeaponEvolutionVisual>();
    private readonly Dictionary<string,float> adoptedExpiry=new Dictionary<string,float>();
    private void Adopted(string key,string prefab,Vector3 position,Vector3 direction,float radius,float remaining,bool child,float phase)
    {
        seen.Add(key);
        if(!adoptedVisuals.TryGetValue(key,out var v))
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/"+prefab+".prefab"));SceneManager.MoveGameObjectToScene(go,scene);v=go.GetComponent<ArcadeWeaponEvolutionVisual>();adoptedVisuals[key]=v;
        }
        v.gameObject.SetActive(true);v.transform.position=position;v.transform.rotation=direction.sqrMagnitude>.001f?Quaternion.LookRotation(direction):Quaternion.identity;
        v.Apply(radius,remaining,child,phase);
    }
    private float dt,time;
    private int kind;
    private GameObject Node(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
    public EvolutionPreviewFx(Scene value,List<Object> resources,Camera camera)
    {
        scene=value;owned=resources;
        fire=Copy("Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/Fire.mat");
        smoke=Copy("Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/BlastSmoke.mat");
        glow=Copy("Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/Fire.mat");
        metal=new Material(Shader.Find("Universal Render Pipeline/Unlit"));metal.color=new Color(.28f,.36f,.15f);owned.Add(metal);
        var texture=new Texture2D(128,128,TextureFormat.RGBA32,false);owned.Add(texture);
        for(int y=0;y<128;y++)for(int x=0;x<128;x++)
        {float u=(x-63.5f)/63.5f,v=(y-63.5f)/63.5f;float a=Mathf.Pow(Mathf.Clamp01(1-u*u-v*v),3);texture.SetPixel(x,y,new Color(1,1,1,a));}
        texture.Apply();texture.wrapMode=TextureWrapMode.Clamp;glow.SetTexture("_BaseMap",texture);
    }
    private Material Copy(string path){var m=new Material(AssetDatabase.LoadAssetAtPath<Material>(path));m.SetColor("_BaseColor",Color.white);owned.Add(m);return m;}
    public void Begin(float seconds,float clock,int weapon)
    {
        dt=seconds;time=clock;kind=weapon;seen.Clear();
        foreach(var line in lines.Values)line.enabled=false;
        if(kind==4&&time>1.14f&&time<1.52f)
        {
            float charge=(time-1.14f)/.38f;
            Ring("charge",new Vector3(0,1.15f,.9f),.42f*(1-charge)+.08f,.045f,new Color(.4f,.85f,1,.8f));
            var ps=Loop("charge-motes",glow,new Vector3(0,1.15f,.9f),.10f,.3f,.6f,new Color(.2f,.9f,1,.8f),false);
            var e=ps.emission;e.rateOverTime=40;
        }
    }
    private ParticleSystem Make(string name,Material material,Vector3 position,float size,float life,float speed,Color color,bool atlas)
    {
        var go=Node(name);go.transform.position=position;var p=go.AddComponent<ParticleSystem>();p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var m=p.main;m.playOnAwake=false;m.loop=true;m.duration=8;m.startLifetime=new ParticleSystem.MinMaxCurve(life*.65f,life);m.startSize=new ParticleSystem.MinMaxCurve(size*.65f,size);m.startSpeed=new ParticleSystem.MinMaxCurve(speed*.5f,speed);m.startColor=color;m.simulationSpace=ParticleSystemSimulationSpace.World;m.maxParticles=100;
        p.useAutoRandomSeed=false;p.randomSeed=(uint)(name.Length*17+31);
        var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=20;shape.radius=.3f;go.transform.rotation=Quaternion.Euler(-90,0,0);
        var emission=p.emission;emission.rateOverTime=12;
        var col=p.colorOverLifetime;col.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(.7f,.65f),new GradientAlphaKey(0,1)});col.color=gradient;
        var sizeLife=p.sizeOverLifetime;sizeLife.enabled=true;sizeLife.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,.55f,1,1.1f));
        var render=go.GetComponent<ParticleSystemRenderer>();render.sharedMaterial=material;render.renderMode=ParticleSystemRenderMode.Billboard;render.sortingOrder=3;
        if(atlas){var sheet=p.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=16;sheet.numTilesY=5;sheet.frameOverTime=new ParticleSystem.MinMaxCurve(0,1);sheet.startFrame=new ParticleSystem.MinMaxCurve(0,1);}
        p.Play(false);return p;
    }
    private ParticleSystem Loop(string key,Material mat,Vector3 pos,float size,float life,float speed,Color color,bool atlas)
    {
        seen.Add(key);
        if(!loops.TryGetValue(key,out var p)){p=Make(key,mat,pos,size,life,speed,color,atlas);loops[key]=p;}
        p.transform.position=pos;var e=p.emission;e.rateOverTime=12;return p;
    }
    public void Ground(string key,Vector2 pos,float remaining)
    {
        if(adopted){Adopted(key,"ArcadeWeaponBurnGround",new Vector3(pos.x,.06f,pos.y),Vector3.forward,.65f,remaining,false,0);return;}
        var at=new Vector3(pos.x,.06f,pos.y);float fade=Mathf.Clamp01(remaining/.65f);
        var p=Loop(key,fire,at,.62f,.6f,.65f,new Color(1,.83f,.55f,.8f),true);var e=p.emission;e.rateOverTime=14*fade;
        var core=Loop(key+"core",glow,at+Vector3.up*.04f,.5f,.3f,.07f,new Color(1,.28f,.015f,.35f),false);var ce=core.emission;ce.rateOverTime=8*fade;
        var embers=Loop(key+"embers",glow,at,.065f,.7f,1.1f,new Color(1,.6f,.15f,.9f),false);var ee=embers.emission;ee.rateOverTime=7*fade;
        var soot=Loop(key+"smoke",smoke,at+Vector3.up*.2f,.65f,1.1f,.5f,new Color(.18f,.2f,.23f,.25f),false);var se=soot.emission;se.rateOverTime=3*fade;
    }
    public void Shot(string key,Vector2 pos,Vector2 velocity,float radius,bool child,int weapon)
    {
        string id="shot-"+key;seen.Add(id);var at=new Vector3(pos.x,1.15f,pos.y);var direction=new Vector3(velocity.x,0,velocity.y).normalized;
        if(adopted&&(weapon==5||weapon==4&&radius>0))
        {
            if(!shotBirth.ContainsKey(id))shotBirth[id]=time;
            float phase=Mathf.Clamp01((time-shotBirth[id])/(child?.35f:.85f));
            if(weapon==5)at.y=.45f+Mathf.Sin(phase*Mathf.PI)*(child?.85f:2.1f);
            Adopted(id,weapon==5?"ArcadeWeaponGrenadeEvolved":"ArcadeWeaponPiercingEvolved",at,direction,radius,0,child,phase);
            lastShots[id]=at;lastChildren[id]=child;return;
        }
        if(weapon==5&&largeGrenade)
        {
            if(!shotBirth.ContainsKey(id))shotBirth[id]=time;
            float age=time-shotBirth[id];at.y=.45f+Mathf.Sin(Mathf.Clamp01(age/(child?.35f:.85f))*Mathf.PI)*(child?.85f:2.1f);
            if(!grenadeBodies.TryGetValue(id,out var body)){body=GameObject.CreatePrimitive(PrimitiveType.Capsule);Object.DestroyImmediate(body.GetComponent<Collider>());SceneManager.MoveGameObjectToScene(body,scene);body.GetComponent<Renderer>().sharedMaterial=metal;grenadeBodies[id]=body;}
            body.SetActive(true);body.transform.position=at;body.transform.localScale=child?new Vector3(.3f,.23f,.3f):new Vector3(.42f,.3f,.42f);body.transform.rotation=Quaternion.Euler(age*540,0,35);
        }
        lastChildren[id]=child;lastShots[id]=at;
        if(weapon==3)
        {
            var flame=Loop(id+"fire",fire,at,.64f,.25f,.7f,new Color(1,.8f,.5f,.9f),true);var emission=flame.emission;emission.rateOverTime=35;
            return;
        }
        if(weapon==4)
        {
            if(radius<=0){Beam(id,at-direction*.55f,at,.09f,new Color(.75f,.25f,1,.8f));return;}
            var tail=at-direction*2.5f;
            Beam(id+"halo",tail,at,radius*2.2f,new Color(.05f,.65f,1,.3f));
            Beam(id+"body",tail,at,radius*1.4f,new Color(.12f,.8f,1,.75f));
            Beam(id+"core",tail,at,.24f,new Color(.8f,1,1,1));
            var side=Vector3.Cross(direction,Vector3.up);
            for(int i=0;i<2;i++)
            {
                var l=Line(id+"flow"+i,.055f,new Color(.55f,.5f,1,.8f));l.positionCount=18;
                for(int j=0;j<18;j++){float f=j/17f;float wave=Mathf.Sin(f*16-time*35+i*Mathf.PI);l.SetPosition(j,Vector3.Lerp(tail,at,f)+side*wave*radius*.65f);}
            }
            var sparks=Loop(id+"spark",glow,at,.075f,.35f,1,new Color(.55f,.9f,1,.9f),false);var e=sparks.emission;e.rateOverTime=35;
            return;
        }
        if(weapon==5)
        {
            // 金属核仍是临时机体；能量拖尾与分裂爆炸由实际弹丸的出现/移除驱动。
            var trail=Loop(id+"trail",glow,at,child?.18f:.3f,.3f,.12f,new Color(.85f,.2f,1,.75f),false);var emission=trail.emission;emission.rateOverTime=65;
            if(!largeGrenade)Beam(id+"core",at-direction*.13f,at+direction*.13f,child?.16f:.28f,new Color(.9f,.75f,1,1));
            var dust=Loop(id+"smoke",smoke,at,.2f,.5f,.2f,new Color(.3f,.18f,.4f,.25f),false);var de=dust.emission;de.rateOverTime=18;
        }
    }
    private LineRenderer Line(string key,float width,Color color)
    {
        if(!lines.TryGetValue(key,out var line)){line=Node(key).AddComponent<LineRenderer>();line.sharedMaterial=glow;line.useWorldSpace=true;line.numCapVertices=8;line.numCornerVertices=4;lines[key]=line;}
        line.enabled=true;line.startWidth=line.endWidth=width;line.startColor=line.endColor=color;return line;
    }
    private void Beam(string key,Vector3 a,Vector3 b,float width,Color color)
    {var l=Line(key,width,color);l.positionCount=2;l.SetPosition(0,a);l.SetPosition(1,b);}
    private void Ring(string key,Vector3 at,float radius,float width,Color color)
    {var l=Line(key,width,color);l.positionCount=65;for(int i=0;i<65;i++){float a=i*Mathf.PI/32;l.SetPosition(i,at+new Vector3(Mathf.Cos(a)*radius,.015f,Mathf.Sin(a)*radius));}}
    private void Blast(Vector3 at,bool child)
    {
        if(adopted)
        {
            string key="blast"+adoptedExpiry.Count;at.y=.55f;
            Adopted(key,child?"ArcadeWeaponGrenadeChildBlast":"ArcadeWeaponGrenadePrimaryBlast",at,Vector3.forward,0,0,child,0);adoptedExpiry[key]=time+1.65f;return;
        }
        float scale=largeGrenade?(child?1.2f:1.65f):(child?.65f:1);
        if(largeGrenade)at.y=.55f;
        var flame=Make("blast-fire",fire,at,(largeGrenade?1.25f:.9f)*scale,largeGrenade?.48f:child?.28f:.2f,largeGrenade?2.2f:1.3f,new Color(1,.8f,.55f,.9f),true);var e=flame.emission;e.enabled=false;flame.Emit(largeGrenade?(child?22:38):(child?12:16));bursts.Add(flame);
        var spark=Make("blast-sparks",glow,at,.11f,.6f,4*scale,new Color(1,.55f,.9f,.9f),false);var shape=spark.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.1f;var se=spark.emission;se.enabled=false;spark.Emit(child?20:40);bursts.Add(spark);
        var dust=Make("blast-smoke",smoke,at,(largeGrenade?1.25f:.85f)*scale,largeGrenade?1.5f:1.1f,1.2f,new Color(.22f,.18f,.27f,.45f),false);var de=dust.emission;de.enabled=false;dust.Emit(largeGrenade?20:12);bursts.Add(dust);
        rings.Add(new Vector4(at.x,at.z,time,scale));
    }
    public void End()
    {
        foreach(var id in lastShots.Keys.ToArray())
            if(!seen.Contains(id)){if(kind==5)Blast(lastShots[id],lastChildren[id]);if(grenadeBodies.TryGetValue(id,out var body))body.SetActive(false);lastShots.Remove(id);lastChildren.Remove(id);}
        foreach(var pair in adoptedVisuals)
        {
            if(!seen.Contains(pair.Key)&&!adoptedExpiry.ContainsKey(pair.Key))pair.Value.gameObject.SetActive(false);
            if(adoptedExpiry.TryGetValue(pair.Key,out var until)&&time>until)pair.Value.gameObject.SetActive(false);
            if(pair.Value.gameObject.activeSelf)pair.Value.Tick(dt);
        }
        foreach(var pair in loops)
        {if(!seen.Contains(pair.Key)){var e=pair.Value.emission;e.rateOverTime=0;}pair.Value.Simulate(dt,false,false,true);}
        foreach(var p in bursts)p.Simulate(dt,false,false,true);
        for(int i=0;i<rings.Count;i++)
        {
            var r=rings[i];float age=time-r.z;
            if(age<.45f)Ring("blast-ring"+i,new Vector3(r.x,.12f,r.y),(.2f+age*6)*r.w,.09f*(1-age/.45f),new Color(.8f,.35f,1,(1-age/.45f)*.6f));
        }
    }
}
