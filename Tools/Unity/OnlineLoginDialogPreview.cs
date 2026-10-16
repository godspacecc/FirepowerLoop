using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>在独立编辑器场景预览正式登录弹窗及主菜单背景；不运行网络、不写存档。</summary>
public static class OnlineLoginDialogPreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("登录弹窗预览仅限编辑模式");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var active = RenderTexture.active;
        var output = Path.GetFullPath("Library/OnlineLoginDialogPreview");
        Directory.CreateDirectory(output);
        var overflow = new List<string>();
        try
        {
            var camera = new GameObject("LoginPreviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(0,0,-10); camera.orthographic=true; camera.orthographicSize=5;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
            var host=new GameObject("LoginPreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var canvas=host.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
            var scaler=host.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=.5f;
            var menu=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab"),host.transform,false);
            menu.GetComponent<StartUI>().BtnContinue.gameObject.SetActive(false);
            var dialog=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Online/OnlineLogin.prefab"),host.transform,false);
            var ui=dialog.GetComponent<OnlineLoginUI>();
            foreach(var fitter in host.GetComponentsInChildren<SafeAreaFitter>(true)) fitter.enabled=false;
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(2400,1080),new Vector2Int(1440,1080)})
            foreach(var language in new[]{"zh-CN","en-US"})
            {
                var texts=JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText("Assets/Game/Config/Localization/"+language+".json"));
                foreach(var loc in host.GetComponentsInChildren<LocalizedText>(true))
                { loc.enabled=false; loc.GetComponent<TextMeshProUGUI>().text=texts[loc.Key]; }
                ui.AnonymousRoot.SetActive(true); ui.AuthenticatedRoot.SetActive(false); ui.ResultText.gameObject.SetActive(false);
                ui.UserNameInput.text=""; ui.PasswordInput.text="";
                var target=new RenderTexture(size.x,size.y,24); var texture=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);
                try
                {
                    camera.targetTexture=target;
                    Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)host.transform); Canvas.ForceUpdateCanvases();
                    foreach(var text in host.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
                    camera.Render(); RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,size.x,size.y),0,0); texture.Apply();
                    File.WriteAllBytes(output+"/login-"+language+"-"+size.x+".png",texture.EncodeToPNG());
                    overflow.AddRange(dialog.GetComponentsInChildren<TextMeshProUGUI>().Where(t=>t.isTextOverflowing).Select(t=>language+" "+size+" "+t.name+": "+t.text));
                }
                finally { camera.targetTexture=null; RenderTexture.active=active; Object.DestroyImmediate(target); Object.DestroyImmediate(texture); }
            }
            File.WriteAllLines(output+"/overflow.txt",overflow);
            return output+" overflow="+overflow.Count;
        }
        finally { RenderTexture.active=active; SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene,true); }
    }
}
