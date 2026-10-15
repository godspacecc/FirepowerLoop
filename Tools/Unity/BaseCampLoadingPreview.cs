using System;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>从正式基地和默认摆件取景，不写玩家存档；光照取正式 Main 的正午快照。</summary>
public static class BaseCampLoadingPreview
{
    public static string Render() => Capture("camp-current", false);
    public static string Before() => Capture("camp-before", false);
    public static string Overview() => Capture("camp-overview", true);
    /// <summary>正式场景的入口与室内人眼高度取景；仅为画面检查，不写入运行时相机配置。</summary>
    public static string CabinEntrance() => Capture("cabin-entrance", false, new Vector3(-11.2f,2.1f,10.8f), new Vector3(-9,2.4f,19));
    public static string CabinInterior() => Capture("cabin-interior", false, new Vector3(-9,2.5f,18.7f), new Vector3(-9,2.7f,25));
    /// <summary>室内向前墙取景，检查两侧窗框、窗台及透过玻璃的实际室外景观。</summary>
    public static string CabinWindows() => Capture("cabin-windows", false, new Vector3(-9,2.6f,24.2f), new Vector3(-9,2.8f,17.6f));
    /// <summary>只发布本工具刚从正式场景渲染的原始像素，保留 Sprite GUID 与加载界面接线。</summary>
    public static string Publish()
    {
        var captured=Render();
        const string destination="Assets/Game/Art/UI/Loading/BaseLoadingBackground.png";
        File.Copy(captured,destination,true);
        AssetDatabase.ImportAsset(destination,ImportAssetOptions.ForceUpdate);
        return Path.GetFullPath(destination);
    }
    /// <summary>渲染正式加载 Prefab 的基地分支，只设置预览进度，不保存 UI 或触发地图加载。</summary>
    public static string LoadingUI()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("预览仅用于编辑模式");
        var oldScene=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var oldTarget=RenderTexture.active;
        var target=new RenderTexture(1680,945,24);var texture=new Texture2D(1680,945,TextureFormat.RGB24,false);
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera=new GameObject("UI preview",typeof(Camera)).GetComponent<Camera>();camera.scene=scene;
            camera.transform.position=new Vector3(0,0,-10);camera.targetTexture=target;
            var canvas=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var ui=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/Loading.prefab"),canvas.transform).GetComponent<Game.LoadingUI>();
            ui.MapRoot.SetActive(true);ui.DownloadRoot.SetActive(false);ui.BaseArtwork.SetActive(true);ui.MapArtwork.SetActive(false);
            ui.BaseTitle.SetActive(true);ui.MapTitle.SetActive(false);ui.ErrorRoot.SetActive(false);
            ui.MessageText.text="正在准备基地";ui.PercentText.text="65%";ui.ProgressSlider.value=.65f;
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            texture.ReadPixels(new Rect(0,0,1680,945),0,0);texture.Apply();
            string file="Assets/Tests/Results~/BaseLoadingArt/loading-ui.png";File.WriteAllBytes(file,texture.EncodeToPNG());
            return Path.GetFullPath(file);
        }
        finally
        {
            RenderTexture.active=oldTarget;Object.DestroyImmediate(texture);Object.DestroyImmediate(target);
            EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(oldScene);
        }
    }
    private static string Capture(string name, bool overhead, Vector3? position=null, Vector3? lookAt=null)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出 Play Mode");
        const string path = "Assets/Game/Scenes/BaseMap.unity";
        var scene = SceneManager.GetSceneByPath(path);
        if (scene.isLoaded && scene.isDirty) throw new InvalidOperationException("基地场景尚未保存");
        var oldActive = SceneManager.GetActiveScene();
        if (scene.isLoaded) EditorSceneManager.CloseScene(scene,true);
        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        var oldTarget = RenderTexture.active;
        var target = new RenderTexture(1680, 945, 24);
        var texture = new Texture2D(1680, 945, TextureFormat.RGB24, false);
        GameObject preview = null;
        GameObject content = null;
        bool wasActive = false;
        var oldAmbientMode = RenderSettings.ambientMode;
        var oldAmbient = RenderSettings.ambientLight;
        var oldSun = RenderSettings.sun;
        var oldFog = RenderSettings.fog;
        var otherSuns = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.type==LightType.Directional && l.enabled).ToArray();
        try
        {
            SceneManager.SetActiveScene(scene);
            foreach(var l in otherSuns)l.enabled=false;
            var context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapContext>(true)).Single();
            content = context.MapContentRoot; wasActive = content.activeSelf; content.SetActive(true);
            preview = new GameObject("LoadingArtPreview_Temporary");
            var config = new LubanRuntimeConfigProvider(() => new Tables(n => new Luban.ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + n + ".bytes").bytes)));
            if (!config.TryGetBaseCampLayout(out var layout, out var error)) throw new InvalidOperationException(error);
            foreach (var row in layout)
            {
                if (!config.TryGetTempBuilding(row.BuildingId, out var definition, out error)) throw new InvalidOperationException(error);
                var prop = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + definition.PrefabAddress + ".prefab"), preview.transform);
                prop.transform.SetPositionAndRotation(row.Position, Quaternion.Euler(0,row.Yaw,0));
                prop.transform.localScale *= row.Scale;
                if (!prop.GetComponent<TemporaryBuildingView>().SetCommitted(definition.BlocksNavigation,out error)) throw new InvalidOperationException(error);
            }
            foreach (var particles in preview.GetComponentsInChildren<ParticleSystem>(true)) particles.Simulate(2, true, true, true);
            var cameraGo = new GameObject("Camera", typeof(Camera)); cameraGo.transform.SetParent(preview.transform);
            var camera = cameraGo.GetComponent<Camera>(); camera.scene = scene;
            camera.transform.position = position ?? (overhead ? new Vector3(0,30,-26) : new Vector3(14,9,-24));
            camera.transform.LookAt(lookAt ?? (overhead ? new Vector3(0,0,3) : new Vector3(-3,3.7f,12)));
            camera.orthographic = overhead; camera.orthographicSize = 22;
            camera.fieldOfView = position.HasValue ? 70 : 49; camera.farClipPlane = 800; camera.nearClipPlane=.05f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.55f,.67f,.75f);
            camera.targetTexture = target;
            var sunGo = new GameObject("正午预览光",typeof(Light)); sunGo.transform.SetParent(preview.transform);
            var sun = sunGo.GetComponent<Light>(); sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(72,-30,0); sun.intensity = 1.05f; sun.color = new Color(1,.956f,.84f);
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun; RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.58f,.62f,.68f); RenderSettings.fog = false;
            camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0); texture.Apply();
            var file = "Assets/Tests/Results~/BaseLoadingArt/" + name + ".png";
            Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllBytes(file,texture.EncodeToPNG());
            return Path.GetFullPath(file);
        }
        finally
        {
            RenderTexture.active = oldTarget; Object.DestroyImmediate(texture); Object.DestroyImmediate(target);
            if (preview != null) Object.DestroyImmediate(preview);
            if (content != null) content.SetActive(wasActive);
            RenderSettings.ambientMode = oldAmbientMode; RenderSettings.ambientLight = oldAmbient;
            RenderSettings.sun = oldSun; RenderSettings.fog = oldFog;
            foreach(var l in otherSuns)if(l!=null)l.enabled=true;
            EditorSceneManager.CloseScene(scene,true);
            if (oldActive.IsValid() && oldActive.isLoaded) SceneManager.SetActiveScene(oldActive);
        }
    }
}

