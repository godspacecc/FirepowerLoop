using System;
using System.IO;
using Game;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>HUD、结果界面的临时构图预览；仅读取正式字体和按钮，不写入 Assets。</summary>
public static class SurvivorHudResultPreview
{
    /// <summary>在隔离场景输出两张候选图；背景来自已采用草地图的截图，非运行实拍。</summary>
    public static string Run()
    {
        if(EditorApplication.isPlaying || Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets") throw new Exception("工程或状态不符");
        var previous=SceneManager.GetActiveScene(); var active=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        Texture2D background=null;
        var folder=Path.GetFullPath("../Backup/Artifacts/SurvivorDesign-20261029");
        try
        {
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;camera.orthographicSize=5;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;
            var canvas=new GameObject("PreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.gameObject.layer=31;camera.scene=scene;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab").GetComponent<StartUI>();
            var font=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab").GetComponentInChildren<TMP_Text>(true).font;
            Func<string,Transform,float,float,float,float,RectTransform> rect=(name,parent,x,y,w,h)=>{
                var go=new GameObject(name,typeof(RectTransform));go.layer=31;go.transform.SetParent(parent,false);
                var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(w,h);r.anchoredPosition=new Vector2(x,y);return r;};
            Action<Transform,string,float,float,float,float,int> label=(parent,text,x,y,w,h,size)=>{
                var r=rect("PreviewText",parent,x,y,w,h);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSize=size;t.text=text;t.color=Color.white;t.alignment=TextAlignmentOptions.Center;};
            Func<Transform,float,float,float,float,Color,RectTransform> panel=(parent,x,y,w,h,color)=>{var r=rect("PreviewPanel",parent,x,y,w,h);r.gameObject.AddComponent<Image>().color=color;return r;};
            background=new Texture2D(2,2);background.LoadImage(File.ReadAllBytes(Path.GetFullPath("../Backup/Artifacts/FarmGroundApplied-20261023/candidate-overview.png")));
            var bg=rect("已采用草地图截图_非实时战斗",canvas.transform,0,0,1920,1080);bg.gameObject.AddComponent<RawImage>().texture=background;
            var hud=rect("HUD候选",canvas.transform,0,0,1920,1080);
            var dark=new Color(.025f,.04f,.055f,.9f);
            panel(hud,-685,435,470,122,dark);label(hud,"生命  80 / 100",-685,462,440,40,28);
            panel(hud,-685,417,400,16,new Color(.15f,.19f,.21f));panel(hud,-725,417,320,16,new Color(.35f,.78f,.5f));
            panel(hud,0,450,310,92,dark);label(hud,"生存时间  08:42",0,450,300,60,30);
            panel(hud,685,450,470,92,dark);label(hud,"分数  12,480",685,450,440,60,30);
            panel(hud,0,-465,800,92,dark);label(hud,"等级 8    经验  32 / 60",0,-446,760,40,26);
            panel(hud,0,-482,720,12,new Color(.15f,.19f,.21f));panel(hud,-168,-482,384,12,new Color(.32f,.72f,.87f));
            label(hud,"临时布局预览 · 草地图截图背景 · 数值为示例",0,-526,1500,26,18);
            Action<string> capture=name=>{
                var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);
                try{camera.targetTexture=rt;foreach(var tr in canvas.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=31;Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);Canvas.ForceUpdateCanvases();foreach(var t in canvas.GetComponentsInChildren<TMP_Text>())t.ForceMeshUpdate();camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(folder,name),tex.EncodeToPNG());}
                finally{camera.targetTexture=null;RenderTexture.active=active;Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);}};
            capture("hud-v1-1920.png");hud.gameObject.SetActive(false);
            panel(canvas.transform,0,0,1920,1080,new Color(.02f,.03f,.04f,.65f));
            var result=panel(canvas.transform,0,0,900,780,dark);
            label(result,"本局结束",0,270,800,80,48);label(result,"幸存者模式",0,195,800,50,26);
            label(result,"生存时间  08:42",0,70,800,70,36);label(result,"本局分数  12,480",0,-20,800,70,36);
            for(int i=0;i<2;i++){
                var button=Object.Instantiate(source.BtnNewGame,result,false);button.gameObject.SetActive(true);
                foreach(var loc in button.GetComponentsInChildren<LocalizedText>(true))loc.enabled=false;
                var r=(RectTransform)button.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(360,90);r.anchoredPosition=new Vector2(i==0?-200:200,-210);
                button.GetComponentInChildren<TMP_Text>(true).text=i==0?"再来一局":"返回主菜单";
                foreach(var t in button.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            }
            label(result,"临时构图预览 · 尚未接入正式界面",0,-335,800,30,18);
            capture("result-v2-1920.png");return folder;
        }
        finally{RenderTexture.active=active;SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);if(background!=null)Object.DestroyImmediate(background);}
    }
}

