using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>正式Controller与正式资源的隔离回读；手工权威输入、无AI或伤害，不冒充自然通关。</summary>
public static class TenStageCreatureAppliedCapture
{
    private const int Layer=30,Width=1280,Height=720;
    private static string Folder=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/TenStageCreaturesApplied-20261026"));
    private static readonly System.Reflection.BindingFlags Hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    public static string Run()
    {
        if(Application.dataPath.Replace('\\','/')!="F:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("错误项目或编辑器忙");
        int kind=int.Parse(File.ReadAllText(Path.Combine(Folder,"capture-kind.txt")));
        if(!TenStageCreatureBake.Kinds.Contains(kind))throw new InvalidOperationException("错误角色");
        var before=SceneManager.GetActiveScene();var previousTarget=RenderTexture.active;var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var lights=Object.FindObjectsByType<Light>(FindObjectsSortMode.None).ToDictionary(l=>l,l=>l.enabled);foreach(var light in lights.Keys)light.enabled=false;
        var owned=new List<Object>();Camera camera=null;RenderTexture target=null;Texture2D pixels=null;ArcadeCombatViewController controller=null;
        try
        {
            SceneManager.SetActiveScene(scene);var world=new GameObject("FormalCreatureReadback");Ground(world.transform,owned);

            Key("Key",1.3f,new Vector3(48,-32,0));Key("Fill",.4f,new Vector3(38,145,0));Key("Side",.18f,new Vector3(35,65,0));
            camera=new GameObject("ReadbackCamera",typeof(Camera)).GetComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.cullingMask=1<<Layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.16f,.18f);camera.farClipPlane=160;camera.nearClipPlane=.02f;
            target=new RenderTexture(Width,Height,24);pixels=new Texture2D(Width,Height,TextureFormat.RGB24,false);camera.targetTexture=target;
            var hero=CreateHero(owned);hero.Root.transform.position=new Vector3(-2.1f,0,1.5f);hero.Root.transform.rotation=Quaternion.Euler(0,180,0);SampleHero(hero);
            controller=new ArcadeCombatViewController();var type=typeof(ArcadeCombatViewController);type.GetField("root",Hidden).SetValue(controller,world);type.GetField("mapRoot",Hidden).SetValue(controller,world.transform);
            type.GetField("contentTemplates",Hidden).SetValue(controller,AssetDatabase.LoadAssetAtPath<GameObject>(TenStageCreatureBake.Aggregate).transform.Find("Content"));
            var tables=new Tables(n=>new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            var layout=tables.TbArcadeRoundLayout.DataList.Select(ArcadeRoundLayoutParameters.FromConfig).First(p=>kind==2?p.BossKind==2&&p.ArenaConfigNumber==10:p.SpecialKind==kind);
            controller.ConfigureRoundBounds(19.5f,19.5f,.55f);controller.SetRoundLayout(layout);if(!controller.IsRoundLayoutReady)throw new InvalidOperationException(controller.RoundLayoutLoadError);
            var environment=(GameObject)type.GetField("roundEnvironmentRoot",Hidden).GetValue(controller);environment.SetActive(false);
            controller.RecordRoundEnemyPosition(7,new Vector2(0,-2));controller.SetEnemyContent(7,kind==2?0:kind,kind==2?2:0,false);
            var actor=world.GetComponentsInChildren<Transform>(true).First(t=>t.name=="AdoptedCreatureActor");SetLayer(actor.parent.gameObject);
            foreach(Transform child in actor.parent)if(child!=actor)child.gameObject.SetActive(false);
            Bounds envelope=BoundsOf(actor.gameObject);var moved=envelope;moved.center+=new Vector3(0,0,2);envelope.Encapsulate(moved);envelope.Encapsulate(BoundsOf(hero.Root));envelope.Expand(.7f);Fit(camera,envelope,new Vector3(8.5f,5.5f,8));
            string output=Path.Combine(Folder,TenStageCreatureBake.Name(kind));Directory.CreateDirectory(output);string frames=Path.Combine(output,"frames");Directory.CreateDirectory(frames);
            float maxShoulderGap=0;
            var timeline=new List<string>{"frame,time,authorityAge,rootZ,windupRemaining,recovery,phase"};
            for(int frame=0;frame<160;frame++)
            {
                float t=frame/20f,clock=Mathf.Min(t,7);float age=clock;
                if(kind==2){int cycle=Mathf.Min(2,(int)(clock/2.4f));age=cycle*layout.SkillPeriod+Mathf.Min(2.35f,clock-cycle*2.4f);}
                float z=kind==2?Mathf.Lerp(-2,0,Mathf.Clamp01((Mathf.Min(clock,2.35f)-layout.SkillWarning)/1.1f)):Mathf.Lerp(-2,0,Mathf.Clamp01(clock/2));
                if(kind!=2&&clock>4&&clock<5)z=(clock-4)*.3f;else if(kind!=2&&clock>=5)z=.3f;
                float remaining=(kind==6||kind==10)&&clock>=2&&clock<2.5f?2.5f-clock:0;
                float recovery=(kind==6||kind==10)&&clock>=2.5f&&clock<2.9f?2.9f-clock:0;
                controller.RecordRoundEnemyPosition(7,new Vector2(0,z));controller.SetAdoptedCreatureState(7,age,Vector2.up,.5f,remaining,recovery);
                if(kind==17||kind==2)
                {
                    var core=actor.Find("Body/Core");
                    foreach(int side in new[]{-1,1})
                    {
                        var arm=actor.Find("Body/Arm"+(side<0?"L":"R")+"/Upper");
                        var start=arm.localPosition-arm.localRotation*Vector3.up*arm.localScale.y*.5f;
                        var anchor=kind==17?new Vector3(side*.47f,.5f,0):new Vector3(side*.59f,.34f,.73f);
                        maxShoulderGap=Mathf.Max(maxShoulderGap,Vector3.Distance(start,core.localPosition+core.localRotation*anchor));
                    }
                    if(maxShoulderGap>.0003f)throw new InvalidOperationException("组合采样肩关节断开："+maxShoulderGap);
                }
                SampleHero(hero);Save(Path.Combine(frames,frame.ToString("D4")+".png"));if(frame==35)Save(Path.Combine(output,"poster.png"));
                timeline.Add(FormattableString.Invariant($"{frame},{t:F3},{age:F3},{z:F3},{remaining:F3},{recovery:F3},{(t>=7?"paused":kind==2?"rule cycle "+(int)(age/layout.SkillPeriod):remaining>0?"melee windup":recovery>0?"melee recovery":"authority skill and travel")}"));
            }
            File.WriteAllText(Path.Combine(output,"composition-audit.txt"),"Maximum composed shoulder gap="+maxShoulderGap+"m; 160 frames, both shoulders; actor root remains authoritative.");
            File.WriteAllLines(Path.Combine(output,"timeline.csv"),timeline);return TenStageCreatureBake.Name(kind)+": 160 formal-controller frames; last second paused; isolated authority input, no combat simulation.";
            void Key(string name,float intensity,Vector3 angles){var light=new GameObject(name,typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=intensity;light.cullingMask=1<<Layer;light.transform.rotation=Quaternion.Euler(angles);light.shadows=LightShadows.None;}
            void Save(string path){camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,Width,Height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
        }
        finally
        {
            if(controller!=null)typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(controller,null);
            if(camera!=null)camera.targetTexture=null;RenderTexture.active=previousTarget;if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);
            foreach(var pair in lights)if(pair.Key!=null)pair.Key.enabled=pair.Value;
            SceneManager.SetActiveScene(before);EditorSceneManager.CloseScene(scene,true);foreach(var obj in owned)if(obj!=null&&!EditorUtility.IsPersistent(obj))Object.DestroyImmediate(obj);
        }
    }
    private static Bounds BoundsOf(GameObject go)
    {
        var filters=go.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null).ToArray();
        if(filters.Length==0)throw new InvalidOperationException("没有可读网格："+go.name);
        bool first=true;Bounds b=default;
        foreach(var filter in filters)foreach(var v in filter.sharedMesh.vertices)
        {var p=filter.transform.TransformPoint(v);if(first){b=new Bounds(p,Vector3.zero);first=false;}else b.Encapsulate(p);}
        return b;
    }
    private static string Triple(Vector3 v)=>FormattableString.Invariant($"{v.x:F4},{v.y:F4},{v.z:F4}");

    /// <summary>按整段实际网格包围范围投影求镜头，保证头尾手脚和正式主角完整同框。</summary>
    private static void Fit(Camera camera,Bounds bounds,Vector3 direction)
    {
        var center=bounds.center;camera.transform.position=center+direction.normalized*24;camera.transform.LookAt(center);
        float width=0,height=0;
        foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
        {
            var offset=Vector3.Scale(bounds.extents,new Vector3(x,y,z));
            width=Mathf.Max(width,Mathf.Abs(Vector3.Dot(offset,camera.transform.right)));height=Mathf.Max(height,Mathf.Abs(Vector3.Dot(offset,camera.transform.up)));
        }
        camera.orthographicSize=Mathf.Max(1.8f,Mathf.Max(height,width/((float)Width/Height))+.48f);
    }

    private static Material Material(uint rgba,List<Object> owned)
    {
        var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");if(shader==null)throw new InvalidOperationException("缺少Lit Shader");
        var c=new Color((rgba>>24&255)/255f,(rgba>>16&255)/255f,(rgba>>8&255)/255f,1);var m=new Material(shader){name="TEMP_CaptureMaterial",color=c};
        if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",c);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.08f);if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",.08f);owned.Add(m);return m;
    }
    private static void Ground(Transform t,List<Object> owned)
    {
        var m=Material(0xA2ADA3FFu,owned);Plane("NeutralFloor",new Vector3(0,-.012f,0),new Vector2(32,32),m);
        var line=Material(0x929E97FFu,owned);
        for(int i=-12;i<=12;i++){Plane("MeterGrid",new Vector3(i,-.010f,0),new Vector2(.014f,26),line);Plane("MeterGrid",new Vector3(0,-.010f,i),new Vector2(26,.014f),line);}
        void Plane(string name,Vector3 p,Vector2 size,Material mat)
        {
            var mesh=new Mesh{name="TEMP_"+name};float x=size.x*.5f,z=size.y*.5f;
            mesh.vertices=new[]{new Vector3(-x,0,-z),new Vector3(-x,0,z),new Vector3(x,0,z),new Vector3(x,0,-z)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();owned.Add(mesh);
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(t,false);go.transform.localPosition=p;go.layer=Layer;
            go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=mat;
        }
    }

    /// <summary>正式Prefab原尺寸持枪待机，显式禁用自动推进；仅克隆蒙皮输出，不改正式源。</summary>
    private static Actor CreateHero(List<Object> owned)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");if(prefab==null)throw new InvalidOperationException("缺少正式主角");
        var root=Object.Instantiate(prefab);root.SetActive(true);var presenter=root.GetComponent<CharacterWeaponPresenter>();
        if(presenter==null||!presenter.SetWeaponId("arcade.weapon.basic"))throw new InvalidOperationException("正式主角持枪失败");
        presenter.SetManualPoseSampling(true);var animator=presenter.BodyAnimator;animator.enabled=true;animator.Rebind();animator.Update(0);
        var actor=new Actor{Root=root,Animator=animator,Presenter=presenter,Idle=presenter.Current.Animations.Idle.Clip};
        foreach(var a in root.GetComponentsInChildren<Animator>(true))a.enabled=false;
        foreach(var a in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))a.enabled=false;
        foreach(var c in root.GetComponentsInChildren<Collider>(true))c.enabled=false;
        foreach(var c in root.GetComponentsInChildren<Canvas>(true))c.gameObject.SetActive(false);
        foreach(var body in root.GetComponentsInChildren<Rigidbody>(true))body.isKinematic=true;
        foreach(var p in root.GetComponentsInChildren<ParticleSystem>(true))p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach(var source in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if(!source.enabled||!source.gameObject.activeInHierarchy||source.sharedMesh==null||source.bones.Length==0)continue;
            var original=source.sharedMesh;var output=Object.Instantiate(original);output.name="TEMP_FormalSampledSkin";owned.Add(output);
            var proxy=new GameObject("TEMP_FormalSampledSkin",typeof(MeshFilter),typeof(MeshRenderer));proxy.transform.SetParent(source.transform,false);
            proxy.GetComponent<MeshFilter>().sharedMesh=output;proxy.GetComponent<MeshRenderer>().sharedMaterials=source.sharedMaterials;
            var skin=new Skin{Source=source,Output=output,Vertices=original.vertices,Normals=original.normals,Weights=original.boneWeights,Bindposes=original.bindposes,Bones=source.bones};
            if(skin.Weights.Length!=skin.Vertices.Length||skin.Normals.Length!=skin.Vertices.Length||skin.Bindposes.Length!=skin.Bones.Length)throw new InvalidOperationException("正式蒙皮数据不完整");
            skin.Positions=new Vector3[skin.Vertices.Length];skin.Directions=new Vector3[skin.Vertices.Length];skin.Matrices=new Matrix4x4[skin.Bones.Length];actor.Skins.Add(skin);source.enabled=false;
        }
        if(actor.Skins.Count==0)throw new InvalidOperationException("正式主角没有采样蒙皮");SetLayer(root);return actor;
    }
    private static void SampleHero(Actor actor)
    {
        actor.Idle.SampleAnimation(actor.Animator.gameObject,.1f);actor.Presenter.ApplyOffhandGrip(actor.Presenter.Current.Animations.Idle);
        foreach(var skin in actor.Skins)
        {
            for(int i=0;i<skin.Bones.Length;i++)skin.Matrices[i]=skin.Source.transform.worldToLocalMatrix*skin.Bones[i].localToWorldMatrix*skin.Bindposes[i];
            for(int i=0;i<skin.Vertices.Length;i++)
            {
                var w=skin.Weights[i];var v=skin.Vertices[i];var n=skin.Normals[i];var m=skin.Matrices;
                skin.Positions[i]=m[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+m[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+m[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+m[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                skin.Directions[i]=(m[w.boneIndex0].MultiplyVector(n)*w.weight0+m[w.boneIndex1].MultiplyVector(n)*w.weight1+m[w.boneIndex2].MultiplyVector(n)*w.weight2+m[w.boneIndex3].MultiplyVector(n)*w.weight3).normalized;
            }
            skin.Output.vertices=skin.Positions;skin.Output.normals=skin.Directions;skin.Output.RecalculateBounds();
        }
    }
    private static void SetLayer(GameObject root){foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;}
    private sealed class Actor
    { public GameObject Root;public Animator Animator;public CharacterWeaponPresenter Presenter;public AnimationClip Idle;public readonly List<Skin> Skins=new List<Skin>(); }
    private sealed class Skin
    { public SkinnedMeshRenderer Source;public Mesh Output;public Vector3[] Vertices,Normals,Positions,Directions;public BoneWeight[] Weights;public Matrix4x4[] Bindposes,Matrices;public Transform[] Bones; }
}
