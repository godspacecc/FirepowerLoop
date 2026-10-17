using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>只投影双人示例权威帧的隔离 HUD 预览；不启动游戏、连接网络或读写玩家存档。</summary>
public static class SlayerChallengeHudPreview
{
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本项目编辑模式预览");
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var global=typeof(Architecture<Game.Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic);
        var prior=global.GetValue(null);var architecture=new Game.Game();global.SetValue(null,architecture);
        var initialized=typeof(Architecture<Game.Game>).GetField("mInited",flags);initialized.SetValue(architecture,true);
        architecture.RegisterModel(new LocalizationModel());architecture.RegisterModel(new ArcadeRunModel());architecture.RegisterModel(new HotKeyModel());
        architecture.RegisterModel(new OnlineAccountModel());architecture.RegisterModel(new AppStateModel());
        architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;
        var localization=new LocalizationUtility(false);
        foreach(string language in new[]{"zh-CN","en-US"})localization.LoadFromJson(language,File.ReadAllText("Assets/Game/Config/Localization/"+language+".json"));
        typeof(LocalizationUtility).GetField("loadedConfiguredTables",flags).SetValue(localization,true);architecture.RegisterUtility(localization);
        architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(()=>new Tables(n=>new Luban.ByteBuf(File.ReadAllBytes("Assets/Game/Config/"+n+".bytes")))));
        // 不初始化在线系统，避免注册网络与生命周期；仅供正式 HUD 读取示例快照。
        initialized.SetValue(architecture,false);var online=new OnlineArcadeRunSystem();architecture.RegisterSystem(online);initialized.SetValue(architecture,true);
        var previous=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        var oldTexture=RenderTexture.active;string folder=Path.GetFullPath("Temp/SlayerChallengeHudPreview");Directory.CreateDirectory(folder);
        try
        {
            var camera=new GameObject("SlayerPreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.05f,.07f);camera.transform.position=new Vector3(0,0,-10);
            var canvasRoot=new GameObject("SlayerPreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));var canvas=canvasRoot.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvasRoot.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var hud=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"),canvasRoot.transform,false);
            foreach(var fitter in hud.GetComponentsInChildren<SafeAreaFitter>(true))fitter.enabled=false;
            var controller=new ArcadeHudUICtrl();typeof(BaseUICtrl).GetField("BaseUI",flags).SetValue(controller,hud.GetComponent<ArcadeHudUI>());
            typeof(ArcadeHudUICtrl).GetMethod("OnInit",flags).Invoke(controller,null);typeof(BaseUICtrl).GetField("_isActive",flags).SetValue(controller,true);typeof(ArcadeHudUICtrl).GetField("onlineMode",flags).SetValue(controller,true);
            typeof(ArcadeHudUICtrl).GetMethod("ApplyInputLayout",flags).Invoke(controller,new object[]{false,true});
            var players=new[]{new OnlineArcadePlayerSnapshot(1,"Operator A",3,150,1,2,"arcade.weapon.basic",false),new OnlineArcadePlayerSnapshot(2,"Operator B",3,150,1,2,"arcade.weapon.basic",false)};
            var bodies=new[]{new OnlineArcadeBattlePlayerSnapshot(1,0,0,0,1,100,100,true,false,0),new OnlineArcadeBattlePlayerSnapshot(2,1,0,0,1,100,100,true,false,0)};
            var battle=new OnlineArcadeBattleSnapshot(2,1,4,10,170,2,1,1000,bodies,Array.Empty<OnlineArcadeEnemySnapshot>(),arenaConfigNumber:3,roundGeneration:1);
            var snapshot=new OnlineArcadeRunClientSnapshot(true,1,2,1,OnlineArcadeRunPhase.Arena,1,2,"preview",players,OnlineArcadeOperationState.Idle,OnlineArcadeResultCode.Success,battle);
            typeof(OnlineArcadeRunSystem).GetField("lastSnapshot",flags).SetValue(online,snapshot);
            foreach(var spec in new[]{("zh-CN",1920,1080,1L),("zh-CN",2340,1080,2L),("en-US",1280,720,3L),("en-US",1920,1080,5L)})
            {
                architecture.GetModel<LocalizationModel>().CurrentLanguageCode.Value=spec.Item1;
                foreach(var localized in hud.GetComponentsInChildren<LocalizedText>(true))localized.Refresh();
                battle=new OnlineArcadeBattleSnapshot(4,1,1,10,30,2,1,1000,bodies,Array.Empty<OnlineArcadeEnemySnapshot>(),isChallenge:true,challengeId:"arcade.challenge.slayer-guardian",challengeSeconds:spec.Item4==1?1200:spec.Item4==2?65:0,arenaConfigNumber:4,roundGeneration:1,slayerChallenge:new OnlineSlayerChallengeSnapshot((int)spec.Item4,2,4,1600,0,new long[]{2},new long[]{1},Array.Empty<long>()));
                snapshot=new OnlineArcadeRunClientSnapshot(true,1,2,1,OnlineArcadeRunPhase.Challenge,3,4,"preview",players,OnlineArcadeOperationState.Idle,OnlineArcadeResultCode.Success,battle);
                typeof(OnlineArcadeRunSystem).GetField("lastSnapshot",flags).SetValue(online,snapshot);
                architecture.GetModel<OnlineAccountModel>().Publish(new OnlineAccountSnapshot(OnlineConnectionState.Authenticated,OnlineOperationState.Idle,OnlineAccountResultCode.Success,"preview",1L,1L,1,false,"preview"));
                typeof(ArcadeHudUICtrl).GetMethod("RenderOnline",flags).Invoke(controller,new object[]{snapshot});
                var target=new RenderTexture(spec.Item2,spec.Item3,24);var image=new Texture2D(spec.Item2,spec.Item3,TextureFormat.RGB24,false);
                try
                {
                    camera.targetTexture=target;Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)hud.transform);Canvas.ForceUpdateCanvases();
                    foreach(var text in hud.GetComponentsInChildren<TextMeshProUGUI>())text.ForceMeshUpdate();
                    camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,spec.Item2,spec.Item3),0,0);image.Apply();
                    string file=Path.Combine(folder,spec.Item1+"-"+spec.Item2+"-phase"+spec.Item4);File.WriteAllBytes(file+".png",image.EncodeToPNG());
                    File.WriteAllLines(file+".txt",hud.GetComponentsInChildren<TextMeshProUGUI>().Where(t=>t.name=="TextState"||t.name=="TextArena"||t.name=="TextWave"||t.transform.IsChildOf(hud.GetComponent<ArcadeHudUI>().BtnSlayerChallenge.transform)).Select(t=>t.name+" overflow="+t.isTextOverflowing+" text="+t.text));
                }
                finally{camera.targetTexture=null;RenderTexture.active=oldTexture;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
            }
            return folder;
        }
        finally{RenderTexture.active=oldTexture;SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);architecture.Deinit();global.SetValue(null,prior);}
    }
}
