using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>同步复制正式基地与天空配置到临时场景，检查四个时刻；不保存场景或玩家存档。</summary>
public static class BaseDayNightPreview
{
    private const string Output = "Assets/Tests/Results~/BaseDayNight";

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("天空预览只允许空闲编辑模式");
        Directory.CreateDirectory(Output);
        var previous = SceneManager.GetActiveScene();
        const string path = "Assets/Game/Scenes/BaseMap.unity";
        var source = SceneManager.GetSceneByPath(path);
        var openedSource = !source.IsValid() || !source.isLoaded;
        var preview = default(Scene);
        MapProceduralSkyView sky = null;
        GameObject lightObject = null;
        var suspendedLights = new List<Light>();
        try
        {
            if (openedSource) source = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            MapContext sourceContext = null;
            foreach (var root in source.GetRootGameObjects())
                if (sourceContext == null) sourceContext = root.GetComponentInChildren<MapContext>(true);
            if (sourceContext == null) throw new Exception("正式基地缺少地图上下文");
            preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var content = UnityEngine.Object.Instantiate(sourceContext.MapContentRoot);
            content.name = "TemporaryBaseDayNightPreview";
            SceneManager.MoveGameObjectToScene(content, preview);
            content.SetActive(true);
            sky = content.GetComponent<MapProceduralSkyView>();
            // Camera.scene 不隔离其他加载场景的灯；同步预览期间暂停原方向光，finally 原样恢复。
            foreach (var existing in UnityEngine.Object.FindObjectsOfType<Light>())
                if (existing.type == LightType.Directional && existing.enabled)
                {
                    suspendedLights.Add(existing);
                    existing.enabled = false;
                }
            lightObject = new GameObject("SkyPreviewLight", typeof(Light));
            SceneManager.MoveGameObjectToScene(lightObject, preview);
            lightObject.GetComponent<Light>().type = LightType.Directional;
            lightObject.GetComponent<Light>().shadows = LightShadows.Soft;
            var lighting = lightObject.AddComponent<DayNightView>();
            Call(lighting, "Awake");
            if (!(bool)Call(sky, "Initialize", lighting)) throw new Exception("天空初始化失败");
            var cameraObject = new GameObject("SkyPreviewCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 2500f;
            camera.fieldOfView = 49f;
            foreach (var hour in new[] { 6f, 12f, 18f, 0f })
            {
                var snapshot = new DayNightVisualSnapshot(hour / 24f, false);
                Call(sky, "Apply", snapshot);
                Call(lighting, "Apply", snapshot);
                var sunDirection = -(Vector3)RenderSettings.skybox.GetVector("_SunDirection");
                var celestial = hour == 0f ? -sunDirection : sunDirection;
                camera.transform.position = new Vector3(14, 9, -24);
                camera.cullingMask = -1;
                camera.transform.LookAt(new Vector3(-3, 3.7f, 12));
                Capture(camera, "camp-" + hour.ToString("00"));
                camera.cullingMask = 0;
                camera.transform.rotation = Quaternion.LookRotation(celestial);
                Capture(camera, "sky-" + hour.ToString("00"));
            }
            File.WriteAllText(Output + "/status.txt", "passed: formal scene copy; 8 synchronous URP renders");
            return "8 URP renders saved to " + Output;
        }
        catch (Exception exception)
        {
            File.WriteAllText(Output + "/status.txt", "failed: " + exception);
            throw;
        }
        finally
        {
            if (sky != null) Call(sky, "Release");
            if (lightObject != null) UnityEngine.Object.DestroyImmediate(lightObject);
            foreach (var existing in suspendedLights)
                if (existing != null) existing.enabled = true;
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.CloseScene(preview, true);
            if (openedSource && source.IsValid() && source.isLoaded) EditorSceneManager.CloseScene(source, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static object Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    private static void Capture(Camera camera, string name)
    {
        var render = new RenderTexture(1200, 900, 24);
        render.Create();
        var pixels = new Texture2D(1200, 900, TextureFormat.RGB24, false);
        var before = RenderTexture.active;
        try
        {
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = render });
            RenderTexture.active = render;
            pixels.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = before;
            UnityEngine.Object.DestroyImmediate(pixels);
            render.Release();
            UnityEngine.Object.DestroyImmediate(render);
        }
    }
}


