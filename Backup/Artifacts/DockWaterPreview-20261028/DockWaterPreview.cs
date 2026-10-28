using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>当前正式码头的隔离水体候选；不保存场景、资源或业务状态。</summary>
public static class DockWaterPreview
{
    private const int Layer=30, Width=1280, Height=720, Grid=512;
    private static string Folder=>Path.GetFullPath("../Backup/Artifacts/DockWaterPreview-20261028");

    /// <summary>由父代理在Unity空闲编辑态串行执行，输出同机位对照与确定时间轴帧。</summary>
    public static string Run()
    {
        if(Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying||EditorApplication.isCompiling)
            throw new InvalidOperationException("仅允许本工程空闲编辑态");
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(Path.Combine(Folder,"frames"));
        var prior=SceneManager.GetActiveScene(); var priorRT=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        Camera camera=null;RenderTexture target=null;Texture2D pixels=null,shore=null;Shader shader=null;Material candidate=null;
        try
        {
            SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.62f,.64f,.65f);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDocksVisual.prefab");
            if(prefab==null)throw new InvalidOperationException("正式码头Prefab缺失");
            var dock=Object.Instantiate(prefab);dock.name="TEMP_FormalDockClone";
            var water=dock.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.sharedMaterials.Any(m=>m!=null&&m.shader.name=="FirepowerLoop/DockWater")).ToArray();
            if(water.Length!=1)throw new InvalidOperationException("水面识别异常: "+water.Length);
            shore=BuildShore(dock,water[0]);
            shader=ShaderUtil.CreateShaderAsset(File.ReadAllText(Path.Combine(Folder,"CandidateWater.shader")),false);
            if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("候选Shader编译失败");
            candidate=new Material(shader){name="TEMP_WaterPreview"};candidate.SetTexture("_ShoreDistance",shore);
            var originals=water[0].sharedMaterials;
            var replacements=originals.Select(m=>m.shader.name=="FirepowerLoop/DockWater"?candidate:m).ToArray();
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;
            var sun=new GameObject("TEMP_PreviewSun",typeof(Light)).GetComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.3f;sun.cullingMask=1<<Layer;sun.transform.rotation=Quaternion.Euler(48,-32,0);
            camera=new GameObject("TEMP_PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<Layer;camera.orthographic=true;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.14f,.17f);camera.farClipPlane=180;
            target=new RenderTexture(Width,Height,24);pixels=new Texture2D(Width,Height,TextureFormat.RGB24,false);camera.targetTexture=target;
            View(false);
            water[0].sharedMaterials=originals;Capture("overview-current.png");
            water[0].sharedMaterials=replacements;Capture("overview-candidate.png");
            View(true);
            water[0].sharedMaterials=originals;Capture("close-current.png");
            water[0].sharedMaterials=replacements;Capture("close-candidate.png");
            // 两个同机位各4秒：左当前、右候选；编码阶段组合，水面时间始终明确可复现。
            for(int shot=0;shot<2;shot++)
            {
                View(shot==1);
                for(int f=0;f<80;f++)
                {
                    candidate.SetFloat("_PreviewTime",f/20f);
                    water[0].sharedMaterials=originals;Capture("frames/"+shot+"-current-"+f.ToString("D3")+".png");
                    water[0].sharedMaterials=replacements;Capture("frames/"+shot+"-candidate-"+f.ToString("D3")+".png");
                }
            }
            File.WriteAllText(Path.Combine(Folder,"render-report.txt"),"隔离预览；正式Prefab与材质未修改。正式水面渲染器="+water[0].name+"；纹理="+Grid+"；源场景="+prior.path+"；帧=320，20fps，两机位各4秒。岸距来源：正式网格水平平台三角形，不采用虚构岸线。");
            return "候选图4张、原始帧320张；待encode.py输出8秒对照视频。路径="+Folder;

            void View(bool close)
            {
                camera.orthographicSize=close?5.9f:24f;
                camera.transform.position=close?new Vector3(22,14,-18):new Vector3(0,57,-31);
                camera.transform.LookAt(close?new Vector3(11,.0f,-4):Vector3.zero);
            }
            void Capture(string name)
            {
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,Width,Height),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(Folder,name),pixels.EncodeToPNG());
            }
        }
        finally
        {
            if(camera!=null)camera.targetTexture=null;RenderTexture.active=priorRT;
            if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
            if(candidate!=null)Object.DestroyImmediate(candidate);if(shader!=null)Object.DestroyImmediate(shader);if(shore!=null)Object.DestroyImmediate(shore);
        }
    }

    /// <summary>从真实平台的水平三角形栅格化岸线，并计算水面离平台的近似米数。</summary>
    private static Texture2D BuildShore(GameObject dock,Renderer water)
    {
        var mask=new bool[Grid*Grid];int surfaces=0;
        foreach(var filter in dock.GetComponentsInChildren<MeshFilter>(true))
        {
            if(filter.GetComponent<Renderer>()==water)continue;
            var mesh=filter.sharedMesh;var vertices=mesh.vertices;var indices=mesh.triangles;
            for(int t=0;t<indices.Length;t+=3)
            {
                var a=filter.transform.TransformPoint(vertices[indices[t]]);var b=filter.transform.TransformPoint(vertices[indices[t+1]]);var c=filter.transform.TransformPoint(vertices[indices[t+2]]);
                if(Mathf.Abs(a.y)>.11f||Mathf.Abs(b.y)>.11f||Mathf.Abs(c.y)>.11f)continue;
                if(Vector3.Cross(b-a,c-a).normalized.y<.9f)continue;
                var av=Uv(a);var bv=Uv(b);var cv=Uv(c);surfaces++;
                int x0=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(av.x,Mathf.Min(bv.x,cv.x))),0,Grid-1),x1=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(av.x,Mathf.Max(bv.x,cv.x))),0,Grid-1);
                int y0=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(av.y,Mathf.Min(bv.y,cv.y))),0,Grid-1),y1=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(av.y,Mathf.Max(bv.y,cv.y))),0,Grid-1);
                for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                {
                    var p=new Vector2(x+.5f,y+.5f);float e0=Cross(bv-av,p-av),e1=Cross(cv-bv,p-bv),e2=Cross(av-cv,p-cv);
                    if((e0>=0&&e1>=0&&e2>=0)||(e0<=0&&e1<=0&&e2<=0))mask[y*Grid+x]=true;
                }
            }
        }
        if(surfaces<20||mask.Count(v=>v)<1000)throw new InvalidOperationException("真实平台岸线提取失败: "+surfaces);
        float sx=70f/Grid,sz=62f/Grid,diagonal=Mathf.Sqrt(sx*sx+sz*sz);var d=mask.Select(v=>v?0f:1000f).ToArray();
        for(int y=0;y<Grid;y++)for(int x=0;x<Grid;x++)
        {
            int i=y*Grid+x;if(x>0)d[i]=Mathf.Min(d[i],d[i-1]+sx);if(y>0)d[i]=Mathf.Min(d[i],d[i-Grid]+sz);
            if(x>0&&y>0)d[i]=Mathf.Min(d[i],d[i-Grid-1]+diagonal);if(x<Grid-1&&y>0)d[i]=Mathf.Min(d[i],d[i-Grid+1]+diagonal);
        }
        for(int y=Grid-1;y>=0;y--)for(int x=Grid-1;x>=0;x--)
        {
            int i=y*Grid+x;if(x<Grid-1)d[i]=Mathf.Min(d[i],d[i+1]+sx);if(y<Grid-1)d[i]=Mathf.Min(d[i],d[i+Grid]+sz);
            if(x<Grid-1&&y<Grid-1)d[i]=Mathf.Min(d[i],d[i+Grid+1]+diagonal);if(x>0&&y<Grid-1)d[i]=Mathf.Min(d[i],d[i+Grid-1]+diagonal);
        }
        var tex=new Texture2D(Grid,Grid,TextureFormat.RFloat,false,true){name="TEMP_ActualDockShoreDistance",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        tex.SetPixelData(d,0);tex.Apply();return tex;
    }
    private static Vector2 Uv(Vector3 p)=>new Vector2((p.x+35)/70*Grid,(p.z+31)/62*Grid);
    private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
}
