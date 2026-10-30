using System;
using System.IO;
using System.Collections.Generic;
using Game;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;
/// <summary>临时克隆已采用HUD与原移动摇杆，不写入正式资源。</summary>
public static class SurvivorMobilePreview
{
    /// <summary>按原Prefab祖先RectTransform保持摇杆位置与尺寸，只渲染移动控件。</summary>
    public static string Run()
    {
        if(EditorApplication.isPlaying || Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("工程或状态不符");
        var previous=SceneManager.GetActiveScene();var active=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        Texture2D background=null,image=null;RenderTexture rt=null;
        try{
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.scene=scene;camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;camera.orthographicSize=5;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;
            var canvas=new GameObject("PreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)).GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            background=new Texture2D(2,2);background.LoadImage(File.ReadAllBytes(Path.GetFullPath("../Backup/Artifacts/FarmGroundApplied-20261023/candidate-overview.png")));
            var bg=new GameObject("草地图截图背景",typeof(RectTransform),typeof(RawImage));bg.transform.SetParent(canvas.transform,false);var br=(RectTransform)bg.transform;br.anchorMin=Vector2.zero;br.anchorMax=Vector2.one;br.offsetMin=br.offsetMax=Vector2.zero;bg.GetComponent<RawImage>().texture=background;
            var hud=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Survivor/SurvivorHud.prefab"),canvas.transform,false);hud.SetActive(true);
            foreach(var loc in hud.GetComponentsInChildren<LocalizedText>(true))loc.enabled=false;
            foreach(var safe in hud.GetComponentsInChildren<SafeAreaFitter>(true))safe.enabled=false;
            var ui=hud.GetComponent<SurvivorHudUI>();ui.HealthText.text="生命  80 / 100";ui.TimeText.text="生存时间  08:42";ui.ScoreText.text="分数  12,480";ui.ExperienceText.text="等级 8    经验  32 / 60";ui.HealthBar.normalizedValue=.8f;ui.ExperienceBar.normalizedValue=32f/60f;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab").GetComponent<ArcadeHudUI>();
            var parents=new Stack<RectTransform>();for(var p=source.MoveJoystickRoot.transform.parent;p!=null;p=p.parent)parents.Push((RectTransform)p);
            Transform target=canvas.transform;
            while(parents.Count>0){var src=parents.Pop();var go=new GameObject(src.name+"_仅布局",typeof(RectTransform));go.transform.SetParent(target,false);var dst=(RectTransform)go.transform;dst.anchorMin=src.anchorMin;dst.anchorMax=src.anchorMax;dst.pivot=src.pivot;dst.sizeDelta=src.sizeDelta;dst.anchoredPosition3D=src.anchoredPosition3D;dst.localScale=src.localScale;dst.localRotation=src.localRotation;target=dst;}
            var joystick=Object.Instantiate(source.MoveJoystickRoot,target,false);joystick.SetActive(true);
            var note=new GameObject("预览说明",typeof(RectTransform),typeof(TextMeshProUGUI));note.transform.SetParent(canvas.transform,false);var nr=(RectTransform)note.transform;nr.anchorMin=nr.anchorMax=new Vector2(.5f,0);nr.anchoredPosition=new Vector2(0,22);nr.sizeDelta=new Vector2(1600,36);var text=note.GetComponent<TextMeshProUGUI>();text.font=ui.TimeText.font;text.fontSize=20;text.alignment=TextAlignmentOptions.Center;text.text="移动端临时预览 · 左下为原移动控件（位置与尺寸沿用原Prefab）· 草地图截图背景";
            foreach(var t in canvas.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            rt=new RenderTexture(1920,1080,24);image=new Texture2D(1920,1080,TextureFormat.RGB24,false);camera.targetTexture=rt;Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);Canvas.ForceUpdateCanvases();foreach(var t in canvas.GetComponentsInChildren<TMP_Text>())t.ForceMeshUpdate();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();var path=Path.GetFullPath("../Backup/Artifacts/SurvivorDesign-20261029/mobile-hud-v1-1920.png");File.WriteAllBytes(path,image.EncodeToPNG());camera.targetTexture=null;return path;
        }
        finally{RenderTexture.active=active;SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);if(rt!=null)Object.DestroyImmediate(rt);if(image!=null)Object.DestroyImmediate(image);if(background!=null)Object.DestroyImmediate(background);}
    }
}
