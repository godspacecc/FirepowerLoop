using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>通过正式表现控制器与真实资源渲染四图；隔离场景、恢复静态架构，不运行对局或写存档。</summary>
public static class ArcadeIslandPassageCapture
{
    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("仅编辑态实拍");
        var testType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Game.Tests.IslandRoundPresentationTests")).First(t => t != null);
        var fixture = Activator.CreateInstance(testType);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previous = SceneManager.GetActiveScene(); var priorTarget = RenderTexture.active;
        var priorPipeline = QualitySettings.renderPipeline;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UniversalRenderPipelineAsset pipeline = null; RenderTexture target = null; Camera camera = null;
        string output = Path.GetFullPath("../Docs/Plans/Artifacts/IslandConfirmed-20261022"); Directory.CreateDirectory(output);
        try
        {
            SceneManager.SetActiveScene(scene); testType.GetMethod("Setup").Invoke(fixture, null);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.68f, .70f, .70f);
            var sourcePipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (sourcePipeline != null)
            { pipeline = Object.Instantiate(sourcePipeline); pipeline.shadowDistance = 110; pipeline.msaaSampleCount = 4; QualitySettings.renderPipeline = pipeline; }
            var light = new GameObject("CaptureSun").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.35f; light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(54, -35, 0);
            camera = new GameObject("CaptureCamera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 30; camera.nearClipPlane = .1f; camera.farClipPlane = 240;
            camera.transform.position = new Vector3(0, 60, -46); camera.transform.LookAt(new Vector3(0, 0, 2));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .27f, .32f);
            target = new RenderTexture(1600, 1000, 24); target.antiAliasing = 4; target.Create(); camera.targetTexture = target;
            var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            actor.transform.position = Vector3.zero;
            var animator = actor.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.enabled = false;
                var clips = animator.runtimeAnimatorController.animationClips;
                var idle = clips.FirstOrDefault(c => c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0) ?? clips.FirstOrDefault();
                if (idle != null) idle.SampleAnimation(animator.gameObject, 0);
            }
            for (int round = 1; round <= 4; round++)
            {
                testType.GetMethod("Prepare", flags).Invoke(fixture, new object[] { round, false });
                var view = (Game.ArcadeCombatViewController)testType.GetField("view", flags).GetValue(fixture);
                if (!view.IsIslandRoundReady) throw new InvalidOperationException(view.IslandRoundLoadError ?? "正式表现尚未就绪");
                camera.Render(); RenderTexture.active = target;
                var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); texture.Apply();
                File.WriteAllBytes(output + "/round-" + round + ".png", texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            }
            return output;
        }
        finally
        {
            testType.GetMethod("Cleanup").Invoke(fixture, null);
            RenderTexture.active = priorTarget; QualitySettings.renderPipeline = priorPipeline;
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pipeline != null) Object.DestroyImmediate(pipeline);
            SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true);
        }
    }
}
