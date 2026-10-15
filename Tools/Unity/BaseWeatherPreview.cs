using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Game;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>隔离正式基地副本，同机位渲染天气；不保存源场景或改玩家存档。</summary>
public static class BaseWeatherPreview
{
    private const string Output="Assets/Tests/Results~/BaseWeather";
    public static string Run()
    {
        Directory.CreateDirectory(Output);var previous=SceneManager.GetActiveScene();
        var source=SceneManager.GetSceneByPath("Assets/Game/Scenes/BaseMap.unity");var opened=!source.IsValid()||!source.isLoaded;
        var preview=default(Scene);var suspended=new List<Light>();GameObject lightObject=null;BaseWeatherView weather=null;MapProceduralSkyView sky=null;
        try
        {
            if(opened)source=EditorSceneManager.OpenScene("Assets/Game/Scenes/BaseMap.unity",OpenSceneMode.Additive);
            var context=source.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MapContext>(true)).First();
            preview=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var root=UnityEngine.Object.Instantiate(context.MapContentRoot);root.SetActive(true);SceneManager.MoveGameObjectToScene(root,preview);
            foreach(var light in UnityEngine.Object.FindObjectsOfType<Light>())if(light.type==LightType.Directional&&light.enabled){suspended.Add(light);light.enabled=false;}
            lightObject=new GameObject("WeatherPreviewSun",typeof(Light));SceneManager.MoveGameObjectToScene(lightObject,preview);
            var sun=lightObject.GetComponent<Light>();sun.type=LightType.Directional;sun.shadows=LightShadows.Soft;
            var lighting=lightObject.AddComponent<DayNightView>();Call(lighting,"Awake");
            sky=root.GetComponent<MapProceduralSkyView>();Call(sky,"Initialize",lighting);
            weather=root.GetComponent<BaseWeatherView>();Call(weather,"Initialize");
            var cameraObject=new GameObject("WeatherPreviewCamera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,preview);
            var camera=cameraObject.GetComponent<Camera>();camera.scene=preview;camera.clearFlags=CameraClearFlags.Skybox;camera.nearClipPlane=.1f;camera.farClipPlane=2500;camera.fieldOfView=49;
            camera.transform.position=new Vector3(14,9,-24);camera.transform.LookAt(new Vector3(-3,3.7f,12));
            var names=new[]{"dry","rain","clear-wet","clear-dry"};
            var states=new[]{new BaseWeatherSnapshot(0,0,0,"晴"),new BaseWeatherSnapshot(1,1,1,"雨"),new BaseWeatherSnapshot(0,0,1,"雨后"),new BaseWeatherSnapshot(0,0,0,"干燥")};
            for(var i=0;i<states.Length;i++)
            {
                Call(lighting,"Apply",new DayNightVisualSnapshot(.5f,false));Call(sky,"Apply",new DayNightVisualSnapshot(.5f,false));
                Call(lighting,"ApplyWeather",states[i]);Call(sky,"ApplyWeather",states[i]);Call(weather,"Apply",states[i]);
                foreach(var particles in root.GetComponentsInChildren<ParticleSystem>())particles.Clear();
                if(i==1)for(var step=0;step<30;step++)Call(weather,"StepVisual",.05f,Vector3.zero,false);
                Capture(camera,names[i]);
                if(i==1||i==2)
                {
                    camera.transform.position=new Vector3(4,1.65f,-8);camera.transform.LookAt(new Vector3(-2,1,6));Capture(camera,names[i]+"-eye");
                    camera.transform.position=new Vector3(14,9,-24);camera.transform.LookAt(new Vector3(-3,3.7f,12));
                }
            }
            return "6张正式基地资源的编辑器预览："+Output;
        }
        finally
        {
            if(weather)Call(weather,"Release");if(sky)Call(sky,"Release");if(lightObject)UnityEngine.Object.DestroyImmediate(lightObject);
            foreach(var light in suspended)if(light)light.enabled=true;
            if(preview.IsValid()&&preview.isLoaded)EditorSceneManager.CloseScene(preview,true);
            if(opened&&source.IsValid()&&source.isLoaded)EditorSceneManager.CloseScene(source,true);
            if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
        }
    }
    private static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
    private static void Capture(Camera camera,string name)
    {
        var render=new RenderTexture(1200,900,24);render.Create();var pixels=new Texture2D(1200,900,TextureFormat.RGB24,false);var old=RenderTexture.active;
        try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=render});RenderTexture.active=render;pixels.ReadPixels(new Rect(0,0,1200,900),0,0);pixels.Apply();File.WriteAllBytes(Output+"/"+name+".png",pixels.EncodeToPNG());}
        finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(pixels);render.Release();UnityEngine.Object.DestroyImmediate(render);}
    }
}
