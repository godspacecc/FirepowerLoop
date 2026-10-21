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

/// <summary>在临时编辑器场景渲染正式主菜单和本局弹窗；不启动战斗、不写存档、不保存场景。</summary>
public static class UnifiedMainMenuPreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var active = RenderTexture.active;
        string folder = Path.GetFullPath("Library/UnifiedMainMenuPreview");
        Directory.CreateDirectory(folder);
        try
        {
            var camera = new GameObject("MenuPreviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
            camera.transform.position=new Vector3(0,0,-10); camera.orthographic=true; camera.orthographicSize=5;
            var canvasRoot=new GameObject("MenuPreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var canvas=canvasRoot.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
            var scaler=canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=.5f;
            var menu=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab"),canvasRoot.transform,false);
            var dialog=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeResume.prefab"),canvasRoot.transform,false);
            dialog.GetComponent<SafeAreaFitter>().enabled=false;
            var start=menu.GetComponent<StartUI>(); var run=dialog.GetComponent<ArcadeResumeUI>();
            var report=new List<string>();
            foreach(var width in new[]{1920,2400})
            foreach(var state in new[]{"new","progress","result","coop","coop-result"})
            foreach(var language in new[]{"zh-CN","en-US"})
            {
                var strings=JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText("Assets/Game/Config/Localization/"+language+".json"));
                foreach(var loc in canvasRoot.GetComponentsInChildren<LocalizedText>(true))
                { loc.enabled=false; loc.GetComponent<TextMeshProUGUI>().text=strings[loc.Key]; }
                bool saved=state!="new", result=state=="result" || state=="coop-result", coop=state.StartsWith("coop");
                start.BtnNewGame.gameObject.SetActive(!saved); start.BtnContinue.gameObject.SetActive(saved);
                start.BtnContinue.GetComponentInChildren<TextMeshProUGUI>(true).text=strings[result?"ui.start.run_result":"ui.start.run_continue"];
                start.VersionText.gameObject.SetActive(true); start.VersionText.text="v0.0.1";
                dialog.SetActive(saved); run.ResumeDetails.SetActive(true); run.BtnOnline.gameObject.SetActive(false);
                run.DialogTitle.text=strings[result?"ui.arcade.dialog.result":"ui.arcade.dialog.progress"];
                run.TextSummary.text=strings[result ? coop ? "ui.arcade.result.victory" : "ui.arcade.result.failed"
                    : coop ? "ui.arcade.details.coop" : "ui.arcade.details.continuable"];
                run.TextRewards.text=coop?"21,090":"12,750";
                run.SoloStats.SetActive(!coop); run.OnlinePlayers.SetActive(coop);
                run.TextArenaCount.text="04"; run.TextLives.text=result?"00":"02"; run.TextGold.text="280";
                run.TextPlayerNames.text=language=="zh-CN"?"玩家一\n玩家二":"PLAYER ONE\nPLAYER TWO";
                run.TextPlayerScores.text="12,750\n8,340";
                run.TextPlayerStates.text=strings[result?"ui.arcade.details.confirmed":"ui.arcade.online.ready"]+"\n"+
                    strings[result?"ui.arcade.details.pending_confirmation":"ui.arcade.online.not_ready"];
                run.TextMessage.text=result?"":strings[coop?"ui.arcade.details.coop_hint":"ui.arcade.details.resume_hint"];
                run.BtnContinue.gameObject.SetActive(!result); run.BtnAbandon.gameObject.SetActive(!result); run.BtnSettle.gameObject.SetActive(result);
                run.BtnContinue.GetComponentInChildren<TextMeshProUGUI>(true).text=strings[coop?"ui.arcade.online.cancel_ready":"ui.arcade.resume"];
                var target=new RenderTexture(width,1080,24); var texture=new Texture2D(width,1080,TextureFormat.RGBA32,false);
                try
                {
                    camera.targetTexture=target;
                    Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvasRoot.transform); Canvas.ForceUpdateCanvases();
                    foreach(var text in canvasRoot.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
                    camera.Render(); RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,width,1080),0,0); texture.Apply();
                    File.WriteAllBytes(folder+"/"+state+"-"+language+"-"+width+".png",texture.EncodeToPNG());
                    report.AddRange(canvasRoot.GetComponentsInChildren<TextMeshProUGUI>().Where(t=>t.isTextOverflowing).Select(t=>state+" "+language+" "+width+" overflow: "+t.name+" "+t.text));
                }
                finally { camera.targetTexture=null; RenderTexture.active=active; Object.DestroyImmediate(target); Object.DestroyImmediate(texture); }
            }
            File.WriteAllLines(folder+"/overflow.txt",report);
            return folder+"; overflow="+report.Count;
        }
        finally { RenderTexture.active=active; SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene,true); }
    }
}
