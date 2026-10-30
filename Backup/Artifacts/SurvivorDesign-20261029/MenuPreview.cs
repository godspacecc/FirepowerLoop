using System;
using System.IO;
using System.Linq;
using Game;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>只在临时场景复制正式按钮制作候选，不保存正式资源。</summary>
public static class SurvivorMenuPreview
{
    public static string Run()
    {
        if(EditorApplication.isPlaying || Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets") throw new Exception("工程或状态不符");
        var previous=SceneManager.GetActiveScene(); var active=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var folder=Path.GetFullPath("../Backup/Artifacts/SurvivorDesign-20261029");
        try
        {
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(0,0,-10); camera.orthographic=true; camera.orthographicSize=5;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black; camera.cullingMask=1<<31;
            var canvas=new GameObject("PreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
            var scaler=canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=.5f;
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab"),canvas.transform,false);
            root.SetActive(true);
            foreach(var fitter in root.GetComponentsInChildren<SafeAreaFitter>(true)) fitter.enabled=false;
            foreach(var loc in root.GetComponentsInChildren<LocalizedText>(true)) loc.enabled=false;
            var ui=root.GetComponent<StartUI>(); ui.BtnContinue.gameObject.SetActive(false); ui.BtnNewGame.gameObject.SetActive(true);
            ui.VersionText.text=""; ui.UpdateStatusText.text="";
            var button=Object.Instantiate(ui.BtnNewGame,ui.BtnNewGame.transform.parent,false);
            button.name="SurvivorPreviewOnly"; button.transform.SetSiblingIndex(ui.BtnNewGame.transform.GetSiblingIndex()+1);
            ui.BtnNewGame.GetComponentInChildren<TMP_Text>(true).text="闯关模式";
            button.GetComponentInChildren<TMP_Text>(true).text="幸存者模式";
            foreach(var t in canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=31;
            foreach(int width in new[]{1920,1440,2340})
            {
                var rt=new RenderTexture(width,1080,24); var tex=new Texture2D(width,1080,TextureFormat.RGB24,false);
                try
                {
                    camera.targetTexture=rt;
                    Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(ui.ButtonGroup); Canvas.ForceUpdateCanvases();
                    foreach(var text in root.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
                    camera.Render(); RenderTexture.active=rt; tex.ReadPixels(new Rect(0,0,width,1080),0,0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(folder,"menu-v2-"+width+".png"),tex.EncodeToPNG());
                }
                finally{ camera.targetTexture=null; RenderTexture.active=active; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
            }
            return "已生成3种比例；仅临时克隆，未保存正式资源";
        }
        finally{RenderTexture.active=active; SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene,true);}
    }
}
