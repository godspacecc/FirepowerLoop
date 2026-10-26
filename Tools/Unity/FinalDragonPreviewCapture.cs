using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>小龙隔离预览拍摄。手动采样时间，无正式场景保存、无游戏规则执行。</summary>
public static class FinalDragonPreviewCapture
{
    private const int Layer=30, Width=1280, Height=720, Frames=240;
    private static string Folder=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/FinalDragonModel-v1-20261026"));
    private static Scene prior, preview, staging;
    private static Camera camera;
    private static RenderTexture target, priorTarget;
    private static Texture2D pixels;
    private static Actor hero;
    private static FinalDragonCandidate.Rig dragon;
    private static readonly List<Object> owned=new List<Object>();
    private static readonly List<string> timeline=new List<string>();
    private static int next;
    private static bool busy;

    /// <summary>启动一次独立预览；动态逐帧由编辑器回调推进，避免工具超时和重复捕获。</summary>
    public static string Run(bool video=false)
    {
        if(Application.dataPath.Replace('\\','/')!="F:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.isUpdating)
            throw new InvalidOperationException("只允许FirepowerLoop空闲编辑态拍摄");
        if(SessionState.GetBool("FinalDragonPreviewBusy",false))throw new InvalidOperationException("小龙预览仍在运行，拒绝重复启动");
        prior=SceneManager.GetActiveScene();priorTarget=RenderTexture.active;
        try
        {
            SessionState.SetBool("FinalDragonPreviewBusy",true);busy=true;
            Directory.CreateDirectory(Folder);Directory.CreateDirectory(Path.Combine(Folder,"frames"));
            // 用暂存场景创建实例，避免任何临时对象创建在Boot；真正渲染只使用PreviewScene。
            staging=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(staging);
            preview=EditorSceneManager.NewPreviewScene();
            // 空场景没有天空盒烘焙，显式提供环境探针，防止翼膜背光处成为纯黑。
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.56f,.59f,.62f);
            var ambient=new UnityEngine.Rendering.SphericalHarmonicsL2();ambient.AddAmbientLight(new Color(.5f,.52f,.55f));RenderSettings.ambientProbe=ambient;
            var world=new GameObject("TEMP_FinalDragonWorld");Ground(world.transform,owned);
            Key("Key",1.0f,new Vector3(45,-35,0));Key("Fill",.32f,new Vector3(30,130,0));
            camera=new GameObject("TEMP_DragonCamera",typeof(Camera)).GetComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.cullingMask=1<<Layer;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.16f,.18f);camera.nearClipPlane=.03f;camera.farClipPlane=100;
            target=new RenderTexture(Width,Height,24);pixels=new Texture2D(Width,Height,TextureFormat.RGB24,false);camera.targetTexture=target;
            dragon=FinalDragonCandidate.Create(world.transform);SetLayer(dragon.Root);
            hero=CreateHero(owned);hero.Root.transform.SetPositionAndRotation(new Vector3(-3.05f,.025f,2.7f),Quaternion.Euler(0,20,0));SampleHero(hero);
            foreach(var go in staging.GetRootGameObjects())SceneManager.MoveGameObjectToScene(go,preview);
            camera.scene=preview;
            var envelope=BoundsOf(hero.Root);float minY=float.MaxValue;
            for(int i=0;i<Frames;i++) {FinalDragonCandidate.Sample(dragon,i/20f);var b=BoundsOf(dragon.Root);envelope.Encapsulate(b);minY=Mathf.Min(minY,b.min.y);}
            FinalDragonCandidate.Sample(dragon,0);
            var still=BoundsOf(dragon.Root);still.Encapsulate(BoundsOf(hero.Root));Fit(camera,still,new Vector3(6,4.5f,10));Save("three-quarter.png");
            Fit(camera,still,new Vector3(12,3,0));Save("side.png");
            Fit(camera,still,new Vector3(0,4,12));Save("front.png");
            var head=BoundsOf(dragon.Head.gameObject);Fit(camera,head,new Vector3(9,4,10));camera.orthographicSize=.95f;Save("head-detail.png");
            Fit(camera,envelope,new Vector3(9,5.5f,10));
            int triangles=dragon.Root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3);
            if(minY<-.035f)throw new InvalidOperationException("模型穿过地面："+minY);
            if(dragon.Root.GetComponentsInChildren<Collider>(true).Length!=0)throw new InvalidOperationException("临时模型不应创建碰撞器");
            File.WriteAllText(Path.Combine(Folder,"model-audit.txt"),"source=temporary procedural Unity mesh; not formal resource\nhero=Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab\nheroScale="+Triple(hero.Root.transform.localScale)+"\nheroBounds="+Triple(BoundsOf(hero.Root).size)+"\ndragonIdleBounds="+Triple(BoundsOf(dragon.Root).size)+"\nfullMotionBounds="+Triple(envelope.size)+"\nminVertexY="+minY+"\ntriangles="+triangles+"\nmeshObjects="+dragon.Root.GetComponentsInChildren<MeshFilter>().Length+"\nnoAI=true\nnoDamage=true\nnoColliders=true\nisolatedPreviewScene="+EditorSceneManager.IsPreviewScene(preview)+"\npreviewCameraSceneMatches="+(camera.scene==preview)+"\n");
            if(!video){Cleanup();return "静态同框已拍摄，临时场景已清理："+Folder;}
            timeline.Clear();timeline.Add("frame,time,phase,rootZ,jawDegrees,leftFootY,rightFootY");next=0;
            File.WriteAllText(Path.Combine(Folder,"capture-state.txt"),"running:0/240");
            EditorApplication.update+=Tick;return "开始240帧/12秒候选动作拍摄，状态文件="+Folder+"/capture-state.txt";
        }
        catch(Exception e){try{TryWriteFailure(e);}finally{Cleanup();}throw;}
    }
    private static void Tick()
    {
        try
        {
            if(!busy||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("编辑器状态变化，停止临时拍摄");
            for(int j=0;j<3&&next<Frames;j++,next++)
            {
                float t=next/20f;FinalDragonCandidate.Sample(dragon,t);SampleHero(hero);Save("frames/"+next.ToString("D3")+".png");
                timeline.Add(FormattableString.Invariant($"{next},{t:F2},{Phase(t)},{dragon.Root.transform.position.z:F4},{dragon.Jaw.localEulerAngles.x:F3},{dragon.Legs[0].Foot.position.y:F4},{dragon.Legs[2].Foot.position.y:F4}"));
            }
            File.WriteAllText(Path.Combine(Folder,"capture-state.txt"),"running:"+next+"/240");
            if(next>=Frames){File.WriteAllLines(Path.Combine(Folder,"motion-timeline.csv"),timeline);Cleanup();File.WriteAllText(Path.Combine(Folder,"capture-state.txt"),"complete:240/240; temporary scene cleaned");}
        }
        catch(Exception e){try{TryWriteFailure(e);}finally{Cleanup();}}
    }
    private static string Phase(float t)=>t<1.5f?"idle":t<4.5f?"walk":t<5.4f?"windup":t<6.1f?"lunge":t<7.4f?"settle":t<9.5f?"jaw pose":"wing settle";
    private static void Key(string name,float intensity,Vector3 rotation)
    {var light=new GameObject("TEMP_Dragon"+name,typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=intensity;light.cullingMask=1<<Layer;light.transform.rotation=Quaternion.Euler(rotation);light.shadows=name=="Key"?LightShadows.Soft:LightShadows.None;}
    private static void Save(string name)
    {camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,Width,Height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(Folder,name),pixels.EncodeToPNG());}
    private static void Cleanup()
    {
        EditorApplication.update-=Tick;busy=false;SessionState.SetBool("FinalDragonPreviewBusy",false);
        try
        {
            if(camera!=null)camera.targetTexture=null;RenderTexture.active=priorTarget;
            if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);
        }
        finally
        {
            try
            {
                if(prior.IsValid()&&prior.isLoaded)SceneManager.SetActiveScene(prior);
                if(preview.IsValid()&&preview.isLoaded)EditorSceneManager.ClosePreviewScene(preview);
            }
            finally
            {
                try{if(staging.IsValid()&&staging.isLoaded)EditorSceneManager.CloseScene(staging,true);}
                finally
                {
                    foreach(var item in owned)if(item!=null&&!EditorUtility.IsPersistent(item))Object.DestroyImmediate(item);owned.Clear();
                    if(dragon!=null)foreach(var item in dragon.Owned)if(item!=null&&!EditorUtility.IsPersistent(item))Object.DestroyImmediate(item);
                    dragon=null;hero=null;camera=null;target=null;pixels=null;
                }
            }
        }
    }
    private static void TryWriteFailure(Exception e)
    {try{File.WriteAllText(Path.Combine(Folder,"capture-state.txt"),"failed:"+e);}catch(IOException){}catch(UnauthorizedAccessException){}}
    // 下方正式主角蒙皮采样、镜头拟合与地面辅助沿用已验证的既有预览工具。
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
