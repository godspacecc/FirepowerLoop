using System;
using System.IO;
using System.Linq;
using Game;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>正式Prefab的样例状态渲染；不加载业务架构、不写玩家存档，临时对象均归预览场景。</summary>
public static class BasePreparationUiPreview
{
    public static string Render()
    {
        var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Docs/Plans/Visuals"));Directory.CreateDirectory(output);
        RenderOne("Assets/Game/Prefabs/UI/Inventory/Page/StatusManagement.prefab",Path.Combine(output,"基地准备-状态页-Unity预览.png"),false);
        RenderOne("Assets/Game/Prefabs/UI/Expedition/MapSelection.prefab",Path.Combine(output,"基地准备-章节页-Unity预览.png"),true);
        return output;
    }
    static void RenderOne(string path,string output,bool map)
    {
        var scene=EditorSceneManager.NewPreviewScene();RenderTexture target=null;Texture2D pixels=null;Camera camera=null;
        try
        {
            var canvasGo=new GameObject("Preparation Preview",typeof(RectTransform),typeof(Canvas));canvasGo.SetActive(false);SceneManager.MoveGameObjectToScene(canvasGo,scene);
            var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;
            var cameraGo=new GameObject("Preview Camera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraGo,scene);camera=cameraGo.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.055f,.065f);camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;camera.orthographicSize=325;camera.nearClipPlane=.1f;camera.farClipPlane=100;canvas.worldCamera=camera;canvas.planeDistance=10;
            var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),canvasGo.transform);
            var table=JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath,"Game/Config/Localization/zh-CN.json")));
            foreach(var localized in root.GetComponentsInChildren<LocalizedText>(true))
            {
                var label=localized.GetComponent<TMP_Text>();if(label!=null&&table[localized.Key]!=null)label.text=table[localized.Key].Value<string>();
                UnityEngine.Object.DestroyImmediate(localized);
            }
            var rect=(RectTransform)root.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;rect.sizeDelta=new Vector2(map?1000:1868,map?650:878);
            if(map)SampleMap(root.GetComponent<MapSelectionUI>());else SampleStatus(root.GetComponent<StatusManagementPageUI>());
            int width=map?1100:1920,height=map?710:1080;
            target=new RenderTexture(width,height,24);camera.targetTexture=target;camera.scene=scene;
            root.SetActive(true);canvasGo.SetActive(true);Canvas.ForceUpdateCanvases();foreach(var text in root.GetComponentsInChildren<TMP_Text>())text.ForceMeshUpdate();
            camera.Render();var previous=RenderTexture.active;
            try{RenderTexture.active=target;pixels=new Texture2D(width,height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();File.WriteAllBytes(output,pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;}
        }
        finally{if(camera!=null)camera.targetTexture=null;if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void SampleMap(MapSelectionUI ui)
    {
        ui.SelectedName.text="文明河谷";ui.EraText.text="传统技术章节";ui.StoryText.text="只可携带传统技术与地图资料进入本章节";ui.ResourceText.text="章节资源已就绪";ui.ActionText.text="开始探索";ui.BtnAction.interactable=false;
        ui.PreparationRoot.SetActive(true);ui.CarryWeightText.text="携带重量：21.5 / 20.0 千克";ui.CarryStateText.text="携带超重，且含本章节禁带物品";ui.ForbiddenScroll.gameObject.SetActive(true);
        ui.ForbiddenList.AddItemRefreshListener((t,i)=>t.GetComponent<PreparationForbiddenItemBoxUI>().NameText.text=new[]{"手枪 × 1","手枪弹药 × 20"}[i]);ui.ForbiddenList.totalCount=2;ui.ForbiddenList.RefreshCells();
        ui.TechnologyText.text="章节科技：2 / 8\n剧情尚未通关";ui.TechRoot.SetActive(true);
        ui.MapList.AddItemRefreshListener((t,i)=> {foreach(var text in t.GetComponentsInChildren<TMP_Text>(true))text.text=i==0?"文明河谷":"下一章节";});ui.MapList.totalCount=2;ui.MapList.RefreshCells();
    }
    static void SampleStatus(StatusManagementPageUI ui)
    {
        ui.PreparationRoot.SetActive(true);ui.BaseDateText.text="基地第 2 天 08:00";ui.BaseGoldText.text="金币：160";ui.RestInfoText.text="免费恢复状态\n每日准备资金：80 金币";ui.BtnRest.gameObject.SetActive(true);
        ui.HealthValueText.text="100 / 100";ui.HealthFill.fillAmount=1;ui.DetailNameText.text="生命";ui.DetailValueText.text="100 / 100";ui.DetailStateText.text="良好";ui.DetailDescriptionText.text="休息将恢复生命、体力、能量与水分。";
        ui.StatusList.AddItemRefreshListener((t,i)=>{var row=t.GetComponent<StatusItemBoxUI>();row.NameText.text=new[]{"体温","体力","水分","能量"}[i];row.StateText.text="良好";row.ValueText.gameObject.SetActive(false);row.MeterRoot.SetActive(true);row.MeterFill.fillAmount=1;row.Selected.SetActive(false);row.Icon.sprite=i==0?row.TemperatureSprite:i==1?row.StaminaSprite:i==2?row.WaterSprite:row.PowerSprite;});ui.StatusList.totalCount=4;ui.StatusList.RefreshCells();
        ui.AfflictionList.AddItemRefreshListener((t,i)=>{var row=t.GetComponent<StatusAfflictionBoxUI>();row.NameText.text=i==0?"伤势":"疾病";row.StateText.text="尚未开放";row.Selected.SetActive(false);});ui.AfflictionList.totalCount=2;ui.AfflictionList.RefreshCells();
    }
}
