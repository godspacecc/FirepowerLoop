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
public static class HanScenePreview
{
    private static bool cartComparison;
    public static void RunCartComparison(){cartComparison=true;Run();}
    [Serializable] private sealed class CameraPose {public Vector3 position,target;public float fov;}
    // 非播放态的独立预览同样要推进 PlayerLoop，否则运行时的分批 Yield 会停在编辑器空闲帧。
    public static void Run(){if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)throw new InvalidOperationException("仅允许空闲编辑模式预览");EditorApplication.update+=Pump;RenderAsync().Forget();}
    private static void Pump()=>EditorApplication.QueuePlayerLoopUpdate();
    private static async UniTaskVoid RenderAsync()
    {
        var output=cartComparison?"Assets/Tests/Results~/HanCartComparison":"Assets/Tests/Results~/HanScene";Directory.CreateDirectory(output);
        File.WriteAllText(output+"/preview-status.txt","running");
        // 普通临时附加场景与运行时使用相同导航世界；PreviewScene 的导航隔离会改变采集点采样结果。
        var previousScene=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);GeneratedValleyContentHandle generated=null;
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
            var light=new GameObject("PreviewSun",typeof(Light));light.transform.SetParent(root.transform,false);light.transform.rotation=Quaternion.Euler(45,-35,0);light.GetComponent<Light>().type=LightType.Directional;light.GetComponent<Light>().intensity=1.2f;
            light.GetComponent<Light>().shadows=LightShadows.Soft;
            light.GetComponent<Light>().shadowBias=.035f;
            RenderSettings.sun=light.GetComponent<Light>();
            var cameraObject=new GameObject("PreviewCamera",typeof(Camera));cameraObject.transform.SetParent(root.transform,false);var camera=cameraObject.GetComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.55f,.7f,.8f);camera.nearClipPlane=.1f;camera.farClipPlane=2500;
            void Capture(string name,Vector3 position,Vector3 target,bool overview,float fov=65)
            {
                // 对比模式首次记录机位；替换模型后沿用世界坐标，不能靠换角度掩盖差异。
                if(cartComparison&&name.StartsWith("transport-"))
                {
                    var path=output+"/camera-"+name+".json";
                    if(File.Exists(path)){var pose=JsonUtility.FromJson<CameraPose>(File.ReadAllText(path));position=pose.position;target=pose.target;fov=pose.fov;}
                    else File.WriteAllText(path,JsonUtility.ToJson(new CameraPose{position=position,target=target,fov=fov},true));
                }
                camera.transform.position=position;camera.transform.LookAt(target);camera.orthographic=overview;camera.orthographicSize=600;camera.fieldOfView=fov;
                var render=new RenderTexture(1200,900,24);render.Create();var image=new Texture2D(1200,900,TextureFormat.RGB24,false);var before=RenderTexture.active;
                try
                {
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=render});
                    RenderTexture.active=render;image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();File.WriteAllBytes(output+"/"+name+".png",image.EncodeToPNG());
                }
                finally{RenderTexture.active=before;UnityEngine.Object.DestroyImmediate(image);render.Release();UnityEngine.Object.DestroyImmediate(render);}
            }
            var l=generated.Layout;
            foreach(var point in l.Points.Where(p=>p.TemplateId.StartsWith("han.")))
            {
                var target=point.Pose.ToVector()+Vector3.up*.3f;
                Capture(point.TemplateId,target+new Vector3(4,4,-5),target,false);
            }
            foreach(var clue in l.Clues.Where(c=>c.Chain==0))
            {
                var target=clue.Pose.ToVector()+new Vector3(-.5f,.4f,.3f);
                Capture("clue"+clue.Order,target+new Vector3(5,4,-6),target,false);
                if(clue.Order==0)
                {
                    var view=root.GetComponentsInChildren<CivilisationPointView>(true).Single(v=>v.PointId==clue.Id);
                    var story=view.transform.Find("StoryScene");
                    Capture("transport-wide",story.TransformPoint(new Vector3(-6,5.5f,-8)),story.TransformPoint(new Vector3(-.3f,.35f,0)),false,52);
                    var eye=story.TransformPoint(new Vector3(-2,0,-4));eye.y=l.HeightAt(eye.x,eye.z)+1.65f;
                    Capture("transport-player-height",eye,story.TransformPoint(new Vector3(-.7f,.65f,.65f)),false,64);
                }
            }
            Capture("camp-overview",l.Camp.ToVector()+new Vector3(16,12,-20),l.Camp.ToVector(),false);
            File.WriteAllText(output+"/preview-status.txt","passed: seed37 attempt"+l.AttemptIndex);
        }
        catch(Exception e){File.WriteAllText(output+"/preview-status.txt","failed: "+e);}
        finally
        {
            try{if(generated!=null)await generated.ReleaseGeneratedAsync();if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);}
            finally{if(previousScene.IsValid()&&previousScene.isLoaded)SceneManager.SetActiveScene(previousScene);EditorApplication.update-=Pump;}
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
