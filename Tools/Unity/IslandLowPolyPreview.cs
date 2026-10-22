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

/// <summary>海岛low poly待确认候选；只修改隔离实例/临时网格和材质，不写正式资源或配置。</summary>
public static class IslandLowPolyPreview
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly List<Object> Owned=new();
    private static Material sand,rock,rockLight,grass,grassLight,bark,leaf,leafLight,wood,sea,shallows,foam;
    private static Mesh palm,boulder;
    public static string Run()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态临时预览");
        string folder=Path.GetFullPath("../Docs/Plans/Artifacts/IslandLowPoly-v1-20261022");Directory.CreateDirectory(folder);
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
            SetupPalette();palm=Palm();boulder=Icosahedron();
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
                if(round==1)Capture("before-overview.png",1600,1000);
                ApplyCandidate(candidate,round);Capture("candidate-round-"+round+".png",1600,1000);
                // 四个正式同机位候选直接渲染到各自视口，不后期改图或生成概念图。
                camera.targetTexture=sheet;camera.rect=new Rect((round-1)%2*.5f,round<=2?.5f:0,.5f,.5f);camera.Render();camera.rect=new Rect(0,0,1,1);camera.targetTexture=null;
                if(round==1)
                {
                    hero.transform.localPosition=new Vector3(-11,0,-7);camera.orthographicSize=8.5f;camera.transform.position=new Vector3(-10,17,-25);camera.transform.LookAt(new Vector3(-14,1,-7));Capture("candidate-detail.png",1400,1000);
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
        return seaFade*Mathf.Clamp01(outside/3.8f)*(3.2f+2.8f*Mathf.PerlinNoise(x*.14f+round*13,z*.14f+29));
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
        var v=original.vertices;var t=original.triangles;var b=new Builder(4);
        for(int i=0;i<v.Length;i++)if(v[i].y>.1f)v[i].y=.9f+.5f*Mathf.PerlinNoise(v[i].x*.23f+12,v[i].z*.23f+29);
        for(int i=0;i<t.Length;i+=3)b.Tri(v[t[i]],v[t[i+1]],v[t[i+2]],Surface(v[t[i]],v[t[i+1]],v[t[i+2]],i));return b.Build("TEMP_LowPolyFoot");
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
