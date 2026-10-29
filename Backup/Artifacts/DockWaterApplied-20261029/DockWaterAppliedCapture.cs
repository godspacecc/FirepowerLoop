using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>回读正式水面与采用候选，在真实编辑器时钟下同机位逐帧拍摄；不保存场景。</summary>
public static class DockWaterAppliedCapture
{
    private static readonly string Folder=Path.GetFullPath("../Backup/Artifacts/DockWaterApplied-20261029");
    private static Scene prior,scene;
    private static RenderTexture priorRT,target;
    private static Camera camera;
    private static GameObject dock;
    private static Renderer water;
    private static Texture2D pixels,shore;
    private static Material reference;
    private static Shader referenceShader;
    private static Material[] originals,replacements;
    private static int frame;
    private static double nextFrame;
    private static readonly System.Text.StringBuilder Times=new System.Text.StringBuilder("shot,frame,clockSeconds,captureSeconds\n");

    public static string Run()
    {
        if(Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying||EditorApplication.isCompiling)
            throw new InvalidOperationException("仅允许本工程空闲编辑态");
        Directory.CreateDirectory(Path.Combine(Folder,"frames"));
        prior=SceneManager.GetActiveScene();priorRT=RenderTexture.active;
        scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.62f,.64f,.65f);
            dock=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDocksVisual.prefab"));
            dock.name="TEMP_FormalWaterReadback";
            water=dock.GetComponentsInChildren<Renderer>(true).Single(r=>r.sharedMaterials.Any(m=>m.shader.name=="FirepowerLoop/DockWater"));
            shore=(Texture2D)typeof(DockWaterPreview).GetMethod("BuildShore",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{dock,water});
            referenceShader=ShaderUtil.CreateShaderAsset(File.ReadAllText(Path.GetFullPath("../Backup/Artifacts/DockWaterPreview-20261028/CandidateWater.shader")),false);
            if(ShaderUtil.ShaderHasError(referenceShader))throw new InvalidOperationException("采用参考Shader编译失败");
            reference=new Material(referenceShader);reference.SetTexture("_ShoreDistance",shore);
            originals=water.sharedMaterials;replacements=originals.Select(m=>m.shader.name=="FirepowerLoop/DockWater"?reference:m).ToArray();
            foreach(var t in dock.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            var sun=new GameObject("TEMP_ReadbackSun",typeof(Light)).GetComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.3f;sun.cullingMask=1<<30;sun.transform.rotation=Quaternion.Euler(48,-32,0);
            camera=new GameObject("TEMP_ReadbackCamera",typeof(Camera)).GetComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<30;camera.orthographic=true;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.14f,.17f);camera.farClipPlane=180;
            target=new RenderTexture(1280,720,24);pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);camera.targetTexture=target;
            frame=0;nextFrame=EditorApplication.timeSinceStartup;Times.Clear();Times.AppendLine("shot,frame,clockSeconds,captureSeconds");
            File.WriteAllText(Path.Combine(Folder,"capture-status.txt"),"进行中");
            EditorApplication.update+=Step;AssemblyReloadEvents.beforeAssemblyReload+=Cleanup;
            return "已启动正式资源真实时钟回读：两个机位，每机位40帧、目标10fps；结束自动清理，状态见capture-status.txt";
        }
        catch { Cleanup();throw; }
    }

    /// <summary>每帧直接使用正式Shader的Unity时钟，不用替换Shader制造动态。</summary>
    private static void Step()
    {
        if(EditorApplication.timeSinceStartup<nextFrame)return;
        try
        {
            if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("编辑器状态变化，终止回读");
            int shot=frame/40,f=frame%40;
            View(shot==1);water.sharedMaterials=originals;
            // URP编辑态在渲染器内部读取realtimeSinceStartup；GetGlobalVector读不到命令缓冲里的_Time。
            float clock=Time.realtimeSinceStartup;
            Capture("frames/"+shot+"-formal-"+f.ToString("D3")+".png");
            reference.SetFloat("_PreviewTime",clock);water.sharedMaterials=replacements;
            Capture("frames/"+shot+"-reference-"+f.ToString("D3")+".png");
            Times.AppendLine(shot+","+f+","+clock.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+EditorApplication.timeSinceStartup.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            if(f==0)
            {
                string prefix=shot==0?"overview":"close";
                File.Copy(Path.Combine(Folder,"frames/"+shot+"-formal-000.png"),Path.Combine(Folder,prefix+"-formal.png"),true);
                File.Copy(Path.Combine(Folder,"frames/"+shot+"-reference-000.png"),Path.Combine(Folder,prefix+"-reference.png"),true);
                // 同一材质随地图平移；保持机位与灯光相对关系后检查岸线与水纹是否跟随。
                var offset=new Vector3(100,0,-30);dock.transform.position=offset;camera.transform.position+=offset;
                water.sharedMaterials=originals;Capture(prefix+"-translated.png");dock.transform.position=Vector3.zero;View(shot==1);
            }
            frame++;nextFrame=EditorApplication.timeSinceStartup+.1;
            if(frame<80)return;
            File.WriteAllText(Path.Combine(Folder,"times.csv"),Times.ToString());
            File.WriteAllText(Path.Combine(Folder,"capture-status.txt"),"完成：正式资产Shader与材质160张同机位帧；Unity内建_Time实时时钟；未保存场景。" );
            Cleanup();
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(Folder,"capture-status.txt"),"失败："+ex);Cleanup();
        }
    }

    private static void View(bool close)
    {
        camera.orthographicSize=close?5.9f:24f;
        camera.transform.position=close?new Vector3(22,14,-18):new Vector3(0,57,-31);
        camera.transform.LookAt(close?new Vector3(11,0,-4):Vector3.zero);
    }
    private static void Capture(string file)
    {
        camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
        File.WriteAllBytes(Path.Combine(Folder,file),pixels.EncodeToPNG());
    }
    /// <summary>结束、失败或脚本重载均释放临时资源，并返回原场景。</summary>
    private static void Cleanup()
    {
        EditorApplication.update-=Step;AssemblyReloadEvents.beforeAssemblyReload-=Cleanup;
        if(camera!=null)camera.targetTexture=null;RenderTexture.active=priorRT;
        if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);
        if(prior.IsValid())SceneManager.SetActiveScene(prior);if(scene.IsValid())EditorSceneManager.CloseScene(scene,true);
        if(reference!=null)Object.DestroyImmediate(reference);if(referenceShader!=null)Object.DestroyImmediate(referenceShader);if(shore!=null)Object.DestroyImmediate(shore);
    }
}
