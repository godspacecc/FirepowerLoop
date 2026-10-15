using System;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game;
using Luban;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>临时预览生成地形并导出画面；始终释放导航和预览场景，不改变正式场景或存档。</summary>
public static class ValleySceneryPreview
{
    // 非播放态的独立预览同样要推进 PlayerLoop，否则运行时的分批 Yield 会停在编辑器空闲帧。
    public static void Run(){if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)throw new InvalidOperationException("仅允许空闲编辑模式预览");EditorApplication.update+=Pump;RenderAsync().Forget();}
    private static void Pump()=>EditorApplication.QueuePlayerLoopUpdate();
    private static async UniTaskVoid RenderAsync()
    {
        const string output="Assets/Tests/Results~/SceneryValley";Directory.CreateDirectory(output);
        File.WriteAllText(output+"/preview-status.txt","running");
        File.WriteAllText(output+"/candidate-failures.txt","");
        // 普通临时附加场景与运行时使用相同导航世界；PreviewScene 的导航隔离会改变采集点采样结果。
        var previousScene=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);GeneratedValleyContentHandle generated=null;Material previewSky=null;
        try
        {
            // 临时场景使用固定日间环境，避免打开的正式地图昼夜状态影响植被验收截图。
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.55f,.6f);RenderSettings.ambientIntensity=1;
            var root=new GameObject("TemporaryValleyPreview");SceneManager.MoveGameObjectToScene(root,scene);root.SetActive(false);
            var context=root.AddComponent<MapContext>();var dynamicRoot=new GameObject("RuntimeUnitRoot");dynamicRoot.transform.SetParent(root.transform,false);
            var serialized=new SerializedObject(context);serialized.FindProperty("mapContentRoot").objectReferenceValue=root;serialized.FindProperty("dynamicObjectRoot").objectReferenceValue=dynamicRoot.transform;serialized.ApplyModifiedPropertiesWithoutUndo();
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/"+n+".bytes").bytes)));
            provider.TryGetCivilisationCatalog(out var catalog,out _);provider.TryGetValleyGenerationProfile(MapIds.CivilisationValley,out var profile,out _);
            var builder=new ValleyMapBuilder(new Loader());
            var failures=new System.Collections.Generic.List<string>();
            for(int attempt=0;attempt<8;attempt++)
            {ValleyLayoutGenerator.TryGenerate(37,attempt,profile,catalog,out var layout,out _);var result=await builder.BuildAsync(new Handle(context),layout,profile,CancellationToken.None);if(result.Success){generated=(GeneratedValleyContentHandle)result.Handle;break;}failures.Add(attempt+": "+result.Error);File.WriteAllLines(output+"/candidate-failures.txt",failures);}
            if(generated==null)throw new Exception("预览候选全部失败："+string.Join("; ",failures));root.SetActive(true);
            var light=new GameObject("PreviewSun",typeof(Light));light.transform.SetParent(root.transform,false);light.transform.rotation=Quaternion.Euler(45,-35,0);light.GetComponent<Light>().type=LightType.Directional;light.GetComponent<Light>().intensity=1.2f;light.GetComponent<Light>().color=new Color(1,.93f,.8f);light.GetComponent<Light>().shadows=LightShadows.Soft;
            RenderSettings.sun=light.GetComponent<Light>();
            var cameraObject=new GameObject("PreviewCamera",typeof(Camera));cameraObject.transform.SetParent(root.transform,false);var camera=cameraObject.GetComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.Skybox;var sky=new Material(Shader.Find("Skybox/Procedural"));previewSky=sky;sky.SetFloat("_AtmosphereThickness",1.3f);RenderSettings.skybox=sky;RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=250;RenderSettings.fogEndDistance=1700;RenderSettings.fogColor=new Color(.61f,.71f,.76f);camera.backgroundColor=new Color(.55f,.7f,.8f);camera.nearClipPlane=.1f;camera.farClipPlane=2500;
            void Capture(string name,Vector3 position,Vector3 target,bool overview)
            {
                camera.transform.position=position;camera.transform.LookAt(target);camera.orthographic=overview;camera.orthographicSize=600;camera.fieldOfView=65;
                var render=new RenderTexture(1200,900,24);render.Create();var image=new Texture2D(1200,900,TextureFormat.RGB24,false);var before=RenderTexture.active;
                try
                {
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=render});
                    RenderTexture.active=render;image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();File.WriteAllBytes(output+"/"+name+".png",image.EncodeToPNG());
                }
                finally{RenderTexture.active=before;UnityEngine.Object.DestroyImmediate(image);render.Release();UnityEngine.Object.DestroyImmediate(render);}
            }
            var l=generated.Layout;
            var views=root.GetComponentsInChildren<CivilisationPointView>(true).Where(v=>l.Points.Any(p=>p.Id==v.PointId && p.TemplateId.StartsWith("tree."))).ToArray();
            var cover=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="ValleyGroundCover");
            var coverMeshes=cover.GetComponentsInChildren<MeshFilter>(true);
            var scenery=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="ValleyScenery");
            var sceneryMeshes=scenery.GetComponentsInChildren<MeshFilter>(true).Where(m=>!AssetDatabase.Contains(m.sharedMesh)).ToArray();
            // 正式样片网格采用新的名称；统计全部树木网格，避免名称约定阻断实际截图。
            var shapeCounts=views.SelectMany(v=>v.GetComponentsInChildren<MeshFilter>(true)).Where(m=>m.sharedMesh!=null).Select(m=>m.sharedMesh.name).GroupBy(n=>n).ToDictionary(g=>g.Key,g=>g.Count());
            File.WriteAllText(output+"/statistics.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{
                seed=l.Seed,attempt=l.AttemptIndex,trees=views.Length,shapes=shapeCounts,
                shrubChunks=coverMeshes.Count(m=>m.name.StartsWith("ShrubChunk")),
                grassChunks=coverMeshes.Count(m=>m.name.StartsWith("GrassChunk")),
                coverVertices=coverMeshes.Sum(m=>m.sharedMesh.vertexCount),
                coverTriangles=coverMeshes.Sum(m=>m.sharedMesh.triangles.Length/3),
                colliders=cover.GetComponentsInChildren<Collider>(true).Length,
                sceneryChunks=sceneryMeshes.Length,sceneryTriangles=sceneryMeshes.Sum(m=>m.sharedMesh.triangles.Length/3),
                sceneryColliders=scenery.GetComponentsInChildren<Collider>(true).Length,
                sceneryInteractions=scenery.GetComponentsInChildren<CivilisationPointView>(true).Length
            },Newtonsoft.Json.Formatting.Indented));
            Capture("overview",new Vector3(0,1300,-350),Vector3.zero,true);
            var forward=(l.Route[1].ToVector()-l.Spawn.ToVector()).normalized;
            Capture("departure",l.Spawn.ToVector()-forward*12+Vector3.up*7,l.Spawn.ToVector()+forward*35,false);
            Capture("camp",l.Camp.ToVector()+new Vector3(12,8,-16),l.Camp.ToVector(),false);
            var riverNear=l.River.OrderBy(p=>Vector3.Distance(p.ToVector(),l.Camp.ToVector())).First().ToVector();
            var facing=riverNear-l.Camp.ToVector();facing.y=0;facing.Normalize();
            Capture("camp-landscape",l.Camp.ToVector()-facing*12+Vector3.up*5,l.Camp.ToVector()+facing*65+Vector3.up*5,false);
            Capture("riverbank",riverNear-facing*14+Vector3.up*9,riverNear+facing*8,false);
            var focus=l.Spawn.ToVector()+forward*70;focus.y=l.HeightAt(focus.x,focus.z);
            Capture("forest",focus+new Vector3(16,15,-24),focus+Vector3.up*2,false);
            File.WriteAllText(output+"/preview-status.txt","passed: seed37 attempt"+l.AttemptIndex);
        }
        catch(Exception e){File.WriteAllText(output+"/preview-status.txt","failed: "+e);}
        finally
        {
            try{if(generated!=null)await generated.ReleaseGeneratedAsync();if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);}
            finally{if(previewSky!=null)UnityEngine.Object.DestroyImmediate(previewSky);if(previousScene.IsValid()&&previousScene.isLoaded)SceneManager.SetActiveScene(previousScene);EditorApplication.update-=Pump;}
        }
    }
    private sealed class Handle:IMapContentHandle
    {public Handle(MapContext context){Context=context;}public string MapId=>MapIds.CivilisationValley;public MapContext Context{get;}public bool IsValid=>Context!=null;}
    private sealed class Loader:IResourceLoader
    {
        public T LoadAsset<T>(string p)where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab");
        public ResourceLoadHandle LoadAssetAsync<T>(string p,Action<T> callback)where T:UnityEngine.Object{callback(LoadAsset<T>(p));return new ResourceLoadHandle(new object());}
        public void Release(ResourceLoadHandle handle){}
    }
}
