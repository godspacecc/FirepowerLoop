using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Game;
using Newtonsoft.Json;
using QFramework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>在隔离新局中点击正式HUD/暂停/设置按钮检查手机布局取消链；不保存设备布局或玩家存档。</summary>
public static class ArcadeMobileLayoutFlowProbe
{
    private const string Output = "Temp/InfectedIslandPlay/Actual";
    public static string Start() { Run().Forget(); return "正式暂停/手机布局/取消/继续链已启动"; }
    private static T UI<T>() where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Exclude,
        FindObjectsSortMode.None).Single();
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async UniTaskVoid Run()
    {
        MobileInputSystem mobile = null; bool prior = false;
        try
        {
            Check(EditorApplication.isPlaying && Application.dataPath.Replace('\\', '/') == "E:/GitHub/FirepowerLoop/Client/Assets", "需本工程Play");
            Directory.CreateDirectory(Output);
            mobile = Game.Game.Interface.GetSystem<MobileInputSystem>(); prior = mobile.IsMobileInputMode;
            string before = JsonConvert.SerializeObject(Game.Game.Interface.GetModel<MobileControlLayoutModel>().ToData());
            mobile.TrySetEditorSimulation(true);
            UI<ArcadeHudUI>().BtnPause.onClick.Invoke(); await UniTask.Delay(150, ignoreTimeScale: true);
            Check(Game.Game.Interface.GetModel<AppStateModel>().CurrentState.Value == AppState.Paused, "暂停未生效");
            UI<PauseUI>().BtnSetting.onClick.Invoke(); await UniTask.Delay(150, ignoreTimeScale: true);
            UI<SettingUI>().ControlsTabButton.onClick.Invoke(); await UniTask.Delay(100, ignoreTimeScale: true);
            UI<SettingMobilePageUI>().OpenLayoutButton.onClick.Invoke(); await UniTask.Delay(150, ignoreTimeScale: true);
            Check(UIHelper.GetUICtrl<MobileControlLayoutUICtrl>().IsShow(), "正式布局页未打开");
            var layout = UI<MobileControlLayoutUI>(); layout.ModeButton.onClick.Invoke();
            layout.SizeSlider.value = 1.2f;
            await UniTask.Delay(150, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot(Output + "/mobile-layout-first-person.png");
            await UniTask.Delay(100, ignoreTimeScale: true);
            layout.CancelButton.onClick.Invoke(); await UniTask.Delay(100, ignoreTimeScale: true);
            Check(!UIHelper.GetUICtrl<MobileControlLayoutUICtrl>().IsShow(), "取消未关闭布局页");
            Check(before == JsonConvert.SerializeObject(Game.Game.Interface.GetModel<MobileControlLayoutModel>().ToData()), "取消改写了设备布局");
            UI<SettingUI>().BtnExit.onClick.Invoke(); await UniTask.Delay(100, ignoreTimeScale: true);
            UI<PauseUI>().BtnContinue.onClick.Invoke(); await UniTask.Delay(150, ignoreTimeScale: true);
            Check(Game.Game.Interface.GetModel<AppStateModel>().CurrentState.Value != AppState.Paused && Time.timeScale == 1f,
                "继续未恢复战斗/时间倍率");
            Check(!UIHelper.HasUIOpen, "返回战斗仍有阻断窗口");
            ScreenCapture.CaptureScreenshot(Output + "/mobile-resumed.png");
            File.AppendAllText(Output + "/flow.log", "PASS 正式HUD暂停→设置→操作页→布局→第一人称预览→修改草稿→取消→关闭设置→继续；设备布局未改，TimeScale=1，无遮挡窗口。编辑器模拟触屏，非实体手机。\n");
        }
        catch (Exception error) { Directory.CreateDirectory(Output); File.AppendAllText(Output + "/flow.log", "FAIL " + error + "\n"); }
        finally { mobile?.TrySetEditorSimulation(prior); }
    }
}
