using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using QFramework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>隔离场景实拍正式商店Prefab和Controller报价；复用隔离存档测试，不读取玩家数据。</summary>
public static class ScoreShopAppliedCapture
{
    public static string Capture(string language="zh-CN",long score=10000)
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Game.Tests.ScoreShopControllerTests")).First(t=>t!=null);
        var fixture=Activator.CreateInstance(type);var scene=EditorSceneManager.NewPreviewScene();RenderTexture rt=null;Texture2D tex=null;
        try
        {
            type.GetMethod("Setup").Invoke(fixture,null);
            var ui=(ArcadeScoreShopUI)type.GetField("ui",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fixture);
            var architecture=(Game.Game)type.GetField("architecture",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fixture);
            architecture.GetModel<LocalizationModel>().SetLanguageCode(language);
            var run=architecture.GetModel<ArcadeRunModel>().Current;run.Score=score;
            typeof(ArcadeRunModel).GetMethod("CommitSafeNode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(architecture.GetModel<ArcadeRunModel>(),new object[]{run});
            var ctrl=type.GetField("ctrl",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fixture);
            typeof(ArcadeScoreShopUICtrl).GetMethod("OnShow",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ctrl,null);
            foreach(var localized in ui.GetComponentsInChildren<LocalizedText>(true))localized.Refresh();
            var root=new GameObject("ScoreShopActualCapture",typeof(RectTransform),typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            root.GetComponent<RectTransform>().sizeDelta=new Vector2(1920,1080);
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
            var cameraObject=new GameObject("ScoreShopCaptureCamera",typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.GetComponent<Camera>();camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);
            camera.orthographic=true;camera.orthographicSize=540;camera.transform.position=new Vector3(0,0,-100);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.06f,.08f);camera.cullingMask=1<<30;
            camera.nearClipPlane=.1f;camera.farClipPlane=1000;rt=new RenderTexture(1920,1080,24);camera.targetTexture=rt;
            canvas.worldCamera=camera;ui.transform.SetParent(root.transform,false);
            foreach(var tr in root.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=30;
            Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(ui.GetComponent<RectTransform>());
            foreach(var text in ui.GetComponentsInChildren<TMP_Text>(true))text.ForceMeshUpdate(true,true);
            Canvas.ForceUpdateCanvases();camera.Render();
            var old=RenderTexture.active;
            try
            {
                RenderTexture.active=rt;tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();
            }
            finally{RenderTexture.active=old;}
            string suffix=language=="zh-CN"?(score==10000?"":"-low-score"):"-en";
            string path=Path.GetFullPath("../Backup/Artifacts/ScoreShop-v2-20261023/unity-applied"+suffix+".png");
            File.WriteAllBytes(path,tex.EncodeToPNG());return path;
        }
        finally
        {
            type.GetMethod("Cleanup").Invoke(fixture,null);if(tex!=null)UnityEngine.Object.DestroyImmediate(tex);
            if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
