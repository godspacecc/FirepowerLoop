using System;
using System.IO;
using System.Collections.Generic;
using Game;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>在隔离编辑器场景渲染正式加载页，使用示例进度，不运行地图或写入存档。</summary>
public static class ArcadeCoastLoadingPreview
{
    public static string Run()
    {
        if (Application.isPlaying || Application.dataPath.Replace('\\','/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅允许本项目编辑态预览");
        var priorScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var priorTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D texture = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("CoastLoadingPreviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(0,0,-10);
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var root = new GameObject("CoastLoadingPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080);
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/Loading.prefab"),root.transform,false);
            var ui = instance.GetComponent<LoadingUI>();
            string artwork = AssetDatabase.GetAssetPath(ui.MapArtwork.GetComponent<Image>().sprite);
            if (artwork != "Assets/Game/Art/UI/Start/ArcadeCoastLoadingBackground.png") throw new InvalidOperationException("加载背景引用不符");
            var strings = JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            foreach(var localized in instance.GetComponentsInChildren<LocalizedText>(true))
            {
                localized.enabled = false;
                localized.GetComponent<TextMeshProUGUI>().text = strings[localized.Key];
            }
            ui.BaseArtwork.SetActive(false); ui.MapArtwork.SetActive(true);
            ui.BaseTitle.SetActive(false); ui.MapTitle.SetActive(true);
            ui.MapRoot.SetActive(true); ui.DownloadRoot.SetActive(false); ui.ErrorRoot.SetActive(false);
            ui.BtnRetry.gameObject.SetActive(false); ui.BtnCopyLog.gameObject.SetActive(false);
            ui.CopyStatusText.text = ""; ui.BtnBack.interactable = false;
            ui.ProgressSlider.value = .55f; ui.PercentText.text = "55%";
            ui.MessageText.text = strings["ui.arcade.island.loading"];
            target = new RenderTexture(1920,1080,24); texture = new Texture2D(1920,1080,TextureFormat.RGB24,false);
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform); Canvas.ForceUpdateCanvases();
            foreach(var text in instance.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
            camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0,0,1920,1080),0,0); texture.Apply();
            string path = Path.GetFullPath("Temp/CoastChecks/loading-preview.png");
            File.WriteAllBytes(path,texture.EncodeToPNG());
            camera.targetTexture = null;
            return path + "; 正式 Prefab，示例进度 55%，背景=" + artwork;
        }
        finally
        {
            RenderTexture.active = priorTarget;
            if(texture != null)Object.DestroyImmediate(texture);
            if(target != null)Object.DestroyImmediate(target);
            SceneManager.SetActiveScene(priorScene); EditorSceneManager.CloseScene(scene,true);
        }
    }
}
