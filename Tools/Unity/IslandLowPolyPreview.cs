using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>海岛 low poly v2 的隔离渲染与已采用几何生成器；Run/RunApplied 不写正式资源，正式接入由独立 Bake 入口执行。</summary>
public static partial class IslandLowPolyPreview
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly List<Object> Owned=new();
    private static Material sand,rock,rockLight,grass,grassLight,bark,leaf,leafLight,wood,sea,shallows,foam,sandLight,sandShade,wetSand,pathSand;
    private static Mesh palm,boulder;
    public static string Run()=>Render(false);
    /// <summary>按相同机位拍摄正式加载结果，不再应用候选替换。</summary>
    public static string RunApplied()=>Render(true);
    private static string Render(bool applied)
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态临时预览");
        string folder=Path.GetFullPath(applied?"../Docs/Plans/Artifacts/IslandLowPolyApplied-20261022":"../Docs/Plans/Artifacts/IslandLowPoly-v2-20261022");Directory.CreateDirectory(folder);
        var prior=SceneManager.GetActiveScene();var priorRT=RenderTexture.active;var priorPipeline=QualitySettings.renderPipeline;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);object fixture=null;Type type=null;
        var sheet=new RenderTexture(2400,1500,24);sheet.Create();
        try
        {
            SceneManager.SetActiveScene(scene);
            type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Game.Tests.IslandRoundPresentationTests")).First(t=>t!=null);fixture=Activator.CreateInstance(type);type.GetMethod("Setup").Invoke(fixture,null);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.68f,.70f,.70f);
            var source=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(source!=null){var pipeline=Object.Instantiate(source);Owned.Add(pipeline);pipeline.shadowDistance=110;pipeline.msaaSampleCount=4;QualitySettings.renderPipeline=pipeline;}
            var light=new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.35f;light.shadows=LightShadows.Soft;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(54,-35,0);
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=240;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.27f,.32f);
            if(!applied){SetupPalette();palm=Palm();boulder=Icosahedron();}
            for(int round=1;round<=4;round++)
            {
                type.GetMethod("Prepare",Hidden).Invoke(fixture,new object[]{round,false});var view=(ArcadeCombatViewController)type.GetField("view",Hidden).GetValue(fixture);
                if(!view.IsIslandRoundReady)throw new InvalidOperationException(view.IslandRoundLoadError??"正式海岛资源未就绪");
                var original=(GameObject)typeof(ArcadeCombatViewController).GetField("island",Hidden).GetValue(view);
                var holder=new GameObject("TEMP_LOW_POLY_CANDIDATE_R"+round);var candidate=Object.Instantiate(original,holder.transform,false);candidate.SetActive(true);candidate.transform.localPosition=Vector3.zero;
                var hero=Hero(holder.transform,Vector3.zero);
                void Layers(){foreach(var t in holder.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;}
                void Capture(string filename,int width,int height)
                {
                    Layers();var rt=RenderTexture.GetTemporary(width,height,24);rt.antiAliasing=4;var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
                    try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(folder,filename),texture.EncodeToPNG());}
                    finally{camera.targetTexture=null;RenderTexture.active=priorRT;Object.DestroyImmediate(texture);RenderTexture.ReleaseTemporary(rt);}
                }
                camera.orthographicSize=30;camera.transform.position=new Vector3(0,60,-46);camera.transform.LookAt(new Vector3(0,0,2));
                if(!applied)
                {
                    if(round==1)Capture("before-overview.png",1600,1000);
                    // 正式接入后直接复用同版资源，避免预览重跑造成棕榈二次缩放或重复装饰。
                    if(candidate.transform.Find("FixedRounds/Round"+round+"/LowPolyDetails")==null)
                    {ApplyCandidate(candidate,round);AddRichness(candidate,round);}
                }
                Capture((applied?"round-":"candidate-round-")+round+".png",1600,1000);
                // 四个正式同机位候选直接渲染到各自视口，不后期改图或生成概念图。
                camera.targetTexture=sheet;camera.rect=new Rect((round-1)%2*.5f,round<=2?.5f:0,.5f,.5f);camera.Render();camera.rect=new Rect(0,0,1,1);camera.targetTexture=null;
                if(round==1)
                {
                    hero.transform.localPosition=new Vector3(-11,0,-7);camera.orthographicSize=8.5f;camera.transform.position=new Vector3(-10,17,-25);camera.transform.LookAt(new Vector3(-14,1,-7));Capture(applied?"detail.png":"candidate-detail.png",1400,1000);
                }
                Object.DestroyImmediate(holder);
            }
            RenderTexture.active=sheet;var sheetImage=new Texture2D(2400,1500,TextureFormat.RGB24,false);
            sheetImage.ReadPixels(new Rect(0,0,2400,1500),0,0);sheetImage.Apply();File.WriteAllBytes(Path.Combine(folder,"four-rounds-overview.png"),sheetImage.EncodeToPNG());Object.DestroyImmediate(sheetImage);RenderTexture.active=priorRT;
            return folder;
        }
        finally
        {
            type?.GetMethod("Cleanup")?.Invoke(fixture,null);RenderTexture.active=priorRT;QualitySettings.renderPipeline=priorPipeline;sheet.Release();Object.DestroyImmediate(sheet);
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);foreach(var asset in Owned)if(asset!=null)Object.DestroyImmediate(asset);Owned.Clear();
        }
    }
    private static Material Mat(string name,Color color)
    {var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="TEMP_LP_"+name,color=color};m.SetFloat("_Smoothness",.05f);Owned.Add(m);return m;}
    private static void SetupPalette()
    {
        sand=Mat("Sand",new Color(.76f,.64f,.40f));rock=Mat("Rock",new Color(.38f,.44f,.43f));rockLight=Mat("RockLight",new Color(.45f,.51f,.48f));
        grass=Mat("MountainGreen",new Color(.31f,.43f,.22f));grassLight=Mat("MountainGreenLight",new Color(.38f,.49f,.25f));
        bark=Mat("PalmBark",new Color(.43f,.28f,.14f));leaf=Mat("PalmGreen",new Color(.17f,.40f,.24f));leafLight=Mat("PalmGreenLight",new Color(.29f,.50f,.26f));
        wood=Mat("Wood",new Color(.44f,.30f,.17f));sea=Mat("DeepSea",new Color(.055f,.37f,.49f));shallows=Mat("ShallowSea",new Color(.10f,.60f,.61f));foam=Mat("Foam",new Color(.70f,.88f,.82f));
        sandLight=Mat("LightSand",new Color(.77f,.65f,.42f));sandShade=Mat("SandPatches",new Color(.72f,.60f,.38f));wetSand=Mat("WetSand",new Color(.57f,.49f,.32f));pathSand=Mat("PathSand",new Color(.73f,.60f,.37f));
        leaf.SetFloat("_Cull",0);leafLight.SetFloat("_Cull",0);
    }
    private static void ApplyCandidate(GameObject root,int round)
    {
        foreach(var f in root.GetComponentsInChildren<MeshFilter>())
        {
            var renderer=f.GetComponent<MeshRenderer>();if(renderer==null||!renderer.enabled)continue;string name=f.name;
            if(name=="FixedPalm"){f.sharedMesh=palm;renderer.sharedMaterials=new[]{bark,leaf,leafLight};}
            else if(name=="ContinuousMountain"){f.sharedMesh=Mountain(round);renderer.sharedMaterials=new[]{rock,rockLight,grass,grassLight};}
            else if(name=="MountainBoulder"||name=="CoastalReef")
            {var old=f.sharedMesh.bounds.size;var scale=f.transform.localScale;f.sharedMesh=boulder;f.transform.localScale=Vector3.Scale(scale,old)*.5f;renderer.sharedMaterials=new[]{rock,rockLight};}
            else if(name=="SolidRockFoot"){f.sharedMesh=Foot(f.sharedMesh);renderer.sharedMaterials=new[]{rock,rockLight,grass,grassLight};}
            else if(name=="Sand"||name=="DryLandOutside"||name=="InlandBackdrop")renderer.sharedMaterial=sand;
            else if(name=="Ocean")renderer.sharedMaterial=sea;
            else if(name=="Water")renderer.sharedMaterial=shallows;
            else if(name=="Shoreline")renderer.sharedMaterial=foam;
            else if(name.Contains("Plank")||name.Contains("DockPost")||name.Contains("Hull")||name.Contains("Cabin"))renderer.sharedMaterial=wood;
        }
    }
    private static bool Corridor(int round,float x,float z)
    {
        for(int i=0;i<ArcadeIslandRegion.PassageCount;i++)
        {int side=ArcadeIslandRegion.PassageSide(round,i);if(side==0&&z>15&&Math.Abs(x)<3.5f||side==2&&z< -15&&Math.Abs(x)<3.5f||side==1&&x>15&&Math.Abs(z)<3.5f||side==3&&x< -15&&Math.Abs(z)<3.5f)return true;}return false;
    }
    private static float Height(int round,float x,float z)
    {
        int side=Math.Abs(x)>Math.Abs(z)?(x>0?1:3):(z>0?0:2);float outside=Mathf.Max(Mathf.Abs(x),Mathf.Abs(z))-17.9f;
        if(outside<=0||ArcadeIslandRegion.SeaSide(round,side)||Corridor(round,x,z))return -.04f;
        // 海侧山体沿原分界缓降，避免粗网格在远岸留下阶梯锯齿。
        float seaFade=1;
        for(int edge=0;edge<4;edge++)if(ArcadeIslandRegion.SeaSide(round,edge))
        {float outward=edge==0?z:edge==1?x:edge==2?-z:-x;float transverse=edge==0||edge==2?Math.Abs(x):Math.Abs(z);seaFade=Mathf.Min(seaFade,Mathf.Clamp01((transverse-outward)/4f));}
        float near=3.2f+2.8f*Mathf.PerlinNoise(x*.14f+round*13,z*.14f+29);
        float ridge=(2+4*Mathf.PerlinNoise(x*.065f+round*5,z*.065f+17))*Mathf.Clamp01((outside-3)/12);
        return seaFade*Mathf.Clamp01(outside/3.8f)*(near+ridge);
    }
    private static Mesh Mountain(int round)
    {
        var b=new Builder(4);const int count=45;const float step=3;
        for(int iz=0;iz<count-1;iz++)for(int ix=0;ix<count-1;ix++)
        {
            float x=(ix-22)*step,z=(iz-22)*step;Vector3 V(float px,float pz)=>new(px,Height(round,px,pz),pz);
            var a=V(x,z);var c=V(x+step,z+step);var d=V(x,z+step);var e=V(x+step,z);
            if(a.y<=0&&c.y<=0&&d.y<=0&&e.y<=0)continue;
            b.Tri(a,d,e,Surface(a,d,e,ix+iz));b.Tri(e,d,c,Surface(e,d,c,ix+iz+1));
        }
        return b.Build("TEMP_LowPolyMountain");
    }
    private static int Surface(Vector3 a,Vector3 b,Vector3 c,int seed)=>Vector3.Cross(b-a,c-a).normalized.y>.65f&&(a.y+b.y+c.y)/3>1.4f?2+(seed&1):(seed&1);
    private static Mesh Foot(Mesh original)
    {
        var bounds=original.bounds;bool wide=bounds.size.x>=bounds.size.z;float length=wide?bounds.extents.x:bounds.extents.z,depth=wide?bounds.extents.z:bounds.extents.x;
        Vector3 P(float along,float y,float across)=>wide?new Vector3(along,y,across):new Vector3(across,y,along);
        float H(float along,bool back)=>.8f+(back?.9f:0)+.8f*Mathf.PerlinNoise(along*.26f+8,back?11:29);
        var b=new Builder(4);int segments=Mathf.Max(3,Mathf.CeilToInt(length*2/2.7f));
        for(int i=0;i<segments;i++)
        {
            float a=Mathf.Lerp(-length,length,i/(float)segments),c=Mathf.Lerp(-length,length,(i+1f)/segments);
            var af=P(a,-.02f,-depth);var ab=P(a,-.02f,depth);var cf=P(c,-.02f,-depth);var cb=P(c,-.02f,depth);
            var at=P(a*.98f,H(a,false),-depth*.84f);var az=P(a*.98f,H(a,true),depth*.84f);var ct=P(c*.98f,H(c,false),-depth*.84f);var cz=P(c*.98f,H(c,true),depth*.84f);
            if(wide){b.Quad(af,at,ct,cf,i%2);b.Quad(ab,cb,cz,az,(i+1)%2);b.Quad(at,az,cz,ct,2+i%2);}
            else{b.Quad(af,cf,ct,at,i%2);b.Quad(ab,az,cz,cb,(i+1)%2);b.Quad(at,ct,cz,az,2+i%2);}
            if(i==0)b.Quad(af,ab,az,at,0);if(i==segments-1)b.Quad(cf,ct,cz,cb,1);
        }
        return b.Build("TEMP_LowPolyFoot");
    }
    /// <summary>第二版只丰富临时视觉：地表色差、通道边缘、山地疏密，不增碰撞/交互/提示。</summary>
    private static void AddRichness(GameObject root,int round)
    {
        var group=new GameObject("TEMP_Richness").transform;group.SetParent(root.transform,false);
        GameObject MeshGo(string name,Mesh mesh,Material[] materials,Vector3 position,Vector3 scale)
        {var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(group,false);go.transform.localPosition=position;go.transform.localScale=scale;go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterials=materials;return go;}
        void Patch(string name,float x,float z,float rx,float rz,Material mat,int seed,float y)
        {
            var b=new Builder(1);const int count=12;Vector3 Edge(int i){float angle=i*Mathf.PI*2/count;float radius=.78f+.22f*Mathf.PerlinNoise(i*.7f+seed,round*2);return new Vector3(x+Mathf.Cos(angle)*rx*radius,y,z+Mathf.Sin(angle)*rz*radius);}
            var center=new Vector3(x,y,z);for(int i=0;i<count;i++)b.Tri(center,Edge(i+1),Edge(i),0);MeshGo(name,b.Build("TEMP_"+name),new[]{mat},Vector3.zero,Vector3.one);
        }
        // 大块浅色沙带与少量深色沙斑保持中央可自由穿行，不做物理起伏或新障碍。
        var patches=new[]{new Vector4(-8,-7,7,3),new Vector4(7,6,7,3.8f),new Vector4(-6,10,7,3),new Vector4(8,-9,5,3),new Vector4(0,0,7.5f,4.5f),new Vector4(-10,2,3,5)};
        for(int i=0;i<patches.Length;i++){var p=patches[(i+round)%patches.Length];Patch("SandTone_"+i,p.x,p.y,p.z,p.w,i%3==0?sandShade:sandLight,round*17+i,.035f+i*.002f);}
        var region=new ArcadeIslandRegion(19.5f,19.5f,round);var wet=new Builder(1);
        for(int i=0;i<region.Sand.Count;i++)
        {
            var a=region.Sand[i];var c=region.Sand[(i+1)%region.Sand.Count];int side=Math.Abs(a.X)>Math.Abs(a.Y)?(a.X>0?1:3):(a.Y>0?0:2);
            if(!ArcadeIslandRegion.SeaSide(round,side)||Corridor(round,a.X,a.Y))continue;
            wet.Quad(new Vector3(a.X*.94f,.050f,a.Y*.94f),new Vector3(c.X*.94f,.050f,c.Y*.94f),new Vector3(c.X,.050f,c.Y),new Vector3(a.X,.050f,a.Y),0);
        }
        MeshGo("WetSandEdge",wet.Build("TEMP_WetSand"),new[]{wetSand},Vector3.zero,Vector3.one);
        for(int passage=0;passage<3;passage++)
        {
            int side=ArcadeIslandRegion.PassageSide(round,passage);if(ArcadeIslandRegion.SeaSide(round,side))continue;
            var direction=side==0?Vector3.forward:side==1?Vector3.right:side==2?Vector3.back:Vector3.left;var normal=new Vector3(direction.z,0,-direction.x);var b=new Builder(1);
            Vector3 At(float t,bool right){float bend=1.1f*Mathf.Sin(t*.13f+round+side);float width=(1.7f+.4f*Mathf.Sin(t*.18f+side))*Mathf.Clamp01((t-6)/5);return direction*t+normal*(bend+(right?width:-width))+Vector3.up*.052f;}
            for(int i=0;i<25;i++){float a=6+i*2,c=a+2;b.Quad(At(a,false),At(c,false),At(c,true),At(a,true),0);}MeshGo("SandyPassage_"+passage,b.Build("TEMP_Path"),new[]{pathSand},Vector3.zero,Vector3.one);
        }
        // 只调整山上装饰的疏密/高矮；所有新大体积岩石仍在旧阻挡范围内。
        int treeIndex=0;
        foreach(var tree in root.GetComponentsInChildren<Transform>().Where(t=>t.name=="FixedPalm").ToArray())
        {
            int index=treeIndex++;var at=tree.localPosition;float cluster=Mathf.PerlinNoise(at.x*.085f+round,at.z*.085f+14);
            if(cluster<.32f&&index%2==0){tree.gameObject.SetActive(false);continue;}
            tree.localPosition=new Vector3(at.x,Height(round,at.x,at.z)-.12f,at.z);tree.localScale*=.65f+cluster*.8f;tree.localRotation*=Quaternion.Euler(0,index*47,0);
            if(index%3!=0)continue;
            float h=Height(round,at.x+1.1f,at.z+1.4f);if(h<=0||Corridor(round,at.x+1.1f,at.z+1.4f))continue;
            MeshGo("MountainShrub",boulder,new[]{grass,grassLight},new Vector3(at.x+1.1f,h+.38f,at.z+1.4f),new Vector3(1.5f,.8f,1.2f));
        }
        foreach(var foot in root.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="SolidRockFoot").ToArray())
        {
            var size=foot.sharedMesh.bounds.size;bool wide=size.x>size.z;int count=wide?4:3;
            for(int i=0;i<count;i++)
            {
                float along=((i+.5f)/count-.5f)*(wide?size.x:size.z)*.83f;float radius=.55f+.70f*Mathf.PerlinNoise(i*2+round,foot.transform.localPosition.x+24);
                var at=foot.transform.localPosition+(wide?new Vector3(along,0,0):new Vector3(0,0,along));float h=Height(round,at.x,at.z);
                var go=MeshGo("LayeredMountainRock",boulder,new[]{rock,rockLight},new Vector3(at.x,Mathf.Max(.55f,h*.35f),at.z),new Vector3(radius,.7f+radius*(i%2==0?1.5f:.7f),radius));go.transform.localRotation=Quaternion.Euler(i*11, i*67+round*31,i%2==0?12:-8);
            }
        }
        // 地面装饰最高仅十余厘米，可辨为沙滩碎石/枯叶，不冒充玩家需要绕行的新障碍。
        for(int i=0;i<14;i++)
        {
            float angle=(i*137+round*31)*Mathf.Deg2Rad;float radius=9+(i%4)*1.5f;float x=Mathf.Cos(angle)*radius,z=Mathf.Sin(angle)*radius;
            MeshGo("BeachPebble",boulder,new[]{rock,rockLight},new Vector3(x,.065f,z),new Vector3(.16f,.065f,.12f));
            if(i%3==0)Patch("DryLeaf_"+i,x+.25f,z-.12f,.5f,.12f,wood,i,.060f);
        }
        // 用户确认中央完全开阔，仅保留上方沙地色差和低矮装饰。
    }
    private static Mesh Palm()
    {
        var b=new Builder(3);Vector3 Center(int level)=>new Vector3(level*.12f,level*1.1f,0);
        for(int h=0;h<5;h++)for(int i=0;i<6;i++)
        {
            Vector3 Ring(int level,int k){float a=k*Mathf.PI/3;return Center(level)+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(.26f-level*.025f);}
            b.Quad(Ring(h,i),Ring(h+1,i),Ring(h+1,i+1),Ring(h,i+1),0);
        }
        var crown=Center(5);
        for(int i=0;i<7;i++)
        {
            float angle=i*Mathf.PI*2/7;var direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));var side=Vector3.Cross(Vector3.up,direction);var basePoint=crown;
            var middle=crown+direction*1.6f+Vector3.up*.38f;var tip=crown+direction*3.2f-Vector3.up*.55f;
            b.Tri(basePoint,middle-side*.60f,middle+Vector3.up*.12f,1+i%2);b.Tri(basePoint,middle+Vector3.up*.12f,middle+side*.60f,2-i%2);
            b.Tri(middle-side*.60f,tip,middle+Vector3.up*.12f,1+i%2);b.Tri(middle+Vector3.up*.12f,tip,middle+side*.60f,2-i%2);
        }
        return b.Build("TEMP_LowPolyPalm");
    }
    private static Mesh Icosahedron()
    {
        float t=(1+Mathf.Sqrt(5))/2;var v=new[]{new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1)}.Select(p=>p.normalized).ToArray();
        int[] f={0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
        var b=new Builder(2);for(int i=0;i<f.Length;i+=3)b.Tri(v[f[i]],v[f[i+1]],v[f[i+2]],i%2);return b.Build("TEMP_LowPolyBoulder");
    }
    private static GameObject Hero(Transform parent,Vector3 at)
    {
        var hero=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),parent);hero.transform.localPosition=at;
        var p=hero.GetComponent<CharacterWeaponPresenter>();p.SetWeaponId("arcade.weapon.basic");p.SetManualPoseSampling(true);p.BodyAnimator.Rebind();p.BodyAnimator.Update(0);p.Current.Animations.Idle.Clip.SampleAnimation(p.BodyAnimator.gameObject,.1f);p.ApplyOffhandGrip(p.Current.Animations.Idle);
        foreach(var c in hero.GetComponentsInChildren<Canvas>(true))c.enabled=false;foreach(var c in hero.GetComponentsInChildren<Collider>(true))c.enabled=false;return hero;
    }
    private sealed class Builder
    {
        private readonly List<Vector3> vertices=new();private readonly List<int>[] triangles;
        public Builder(int materials){triangles=Enumerable.Range(0,materials).Select(_=>new List<int>()).ToArray();}
        public void Tri(Vector3 a,Vector3 b,Vector3 c,int material){int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles[material].AddRange(new[]{i,i+1,i+2});}
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int material){Tri(a,b,c,material);Tri(a,c,d,material);}
        public Mesh Build(string name){var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.subMeshCount=triangles.Length;for(int i=0;i<triangles.Length;i++)mesh.SetTriangles(triangles[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();Owned.Add(mesh);return mesh;}
    }
}
