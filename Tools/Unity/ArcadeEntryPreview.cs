using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>在独立预览场景渲染真实入口 Prefab；不运行游戏、不修改用户存档及已打开场景。</summary>
public static class ArcadeEntryPreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("入口预览仅用于编辑模式");
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var priorActive = RenderTexture.active;
        try
        {
            var cameraRoot = new GameObject("EntryPreviewCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.GetComponent<Camera>(); camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; camera.transform.position=new Vector3(0,0,-10);
            camera.orthographic=true; camera.orthographicSize=5; camera.nearClipPlane=.1f; camera.farClipPlane=100;
            var canvasRoot=new GameObject("EntryPreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasRoot,scene);
            var canvas=canvasRoot.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
            var scaler=canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=.5f;
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeResume.prefab"),canvasRoot.transform,false);
            // 预览输出尺寸与编辑器 GameView 无关，安全区在此显式模拟，避免取用桌面窗口的比例。
            root.GetComponent<SafeAreaFitter>().enabled=false;
            var view=root.GetComponent<ArcadeResumeUI>();
            var ctrl=new ArcadeResumeUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(ctrl,view);
            foreach(var spec in new[]{new Spec("entry-zh",1672,941,false,"zh-CN"),new Spec("resume-zh",1672,941,true,"zh-CN"),new Spec("entry-en",1672,941,false,"en-US"),new Spec("entry-4x3",1440,1080,false,"zh-CN"),new Spec("entry-wide-safe",2340,1080,false,"zh-CN")})
            {
                var target=new RenderTexture(spec.Width,spec.Height,24); var texture=new Texture2D(spec.Width,spec.Height,TextureFormat.RGBA32,false);
                try
                {
                    camera.targetTexture=target;
                    typeof(ArcadeResumeUICtrl).GetField("selectionMode",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(ctrl,!spec.Resume);
                    var run=spec.Resume?new ArcadeRunSaveData{CompletedArenaCount=4,RemainingLives=2,RunGold=280}:null;
                    typeof(ArcadeResumeUICtrl).GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ctrl,new object[]{run});
                    var strings=JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText("Assets/Game/Config/Localization/"+spec.Language+".json"));
                    foreach(var loc in root.GetComponentsInChildren<LocalizedText>(true)) loc.GetComponent<TextMeshProUGUI>().text=strings[loc.Key];
                    view.BtnContinue.GetComponentInChildren<TextMeshProUGUI>().text=strings[spec.Resume?"ui.arcade.resume":"ui.arcade.entry.start"];
                    // 编辑器预览不加载运行时资源目录，使用同一正式表格式化示例数据。
                    if(spec.Resume) { view.TextSummary.text=string.Format(strings["ui.arcade.resume.summary"],4,2,280); view.TextRewards.text=strings["ui.arcade.none"]; }
                    var safe=(RectTransform)root.transform.Find("SafeContent");
                    safe.anchorMin=spec.Width>2000?new Vector2(.045f,.025f):Vector2.zero;
                    safe.anchorMax=spec.Width>2000?new Vector2(.955f,.975f):Vector2.one;
                    Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform); Canvas.ForceUpdateCanvases();
                    foreach(var text in root.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
                    camera.Render(); RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,spec.Width,spec.Height),0,0); texture.Apply();
                    var folder="Assets/Tests/Results~/ArcadeEntry"; Directory.CreateDirectory(folder);
                    File.WriteAllBytes(folder+"/"+spec.Name+".png",texture.EncodeToPNG());
                    File.WriteAllLines(folder+"/"+spec.Name+".txt",root.GetComponentsInChildren<TextMeshProUGUI>().Select(t=>t.name+" overflow="+t.isTextOverflowing+" size="+t.fontSize+" text="+t.text));
                }
                finally { camera.targetTexture=null; RenderTexture.active=priorActive; Object.DestroyImmediate(target); Object.DestroyImmediate(texture); }
            }
            return Path.GetFullPath("Assets/Tests/Results~/ArcadeEntry");
        }
        finally { RenderTexture.active=priorActive; SceneManager.SetActiveScene(previousScene); EditorSceneManager.CloseScene(scene, true); }
    }
    private sealed class Spec
    {
        public string Name,Language; public int Width,Height; public bool Resume;
        public Spec(string name,int width,int height,bool resume,string language){Name=name;Width=width;Height=height;Resume=resume;Language=language;}
    }
}
