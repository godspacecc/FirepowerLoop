using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>正式按钮进入真实地图，只用临时档；在既有资源准备窄端口等待一秒以捕获加载画面，结束恢复全部依赖。</summary>
public static class ArcadeLoadingFlowProbe
{
    private const string Output = "Temp/LoadingFlow-20261018";
    private const string OwnerKey = "FirepowerLoop.LoadingProbe.Assembly";
    private static object store, clock, failure;
    private static object originalRun, originalPhase;
    private static bool originalNewButton, originalContinueButton;
    private static string directory, runId;
    private static IGameplayResourcePreparation preparation;
    private static CancellationTokenSource cancellation;
    private static bool running;
    private static bool interruptAfterEntry;
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Start() => Begin(false);
    /// <summary>只在临时局已完整进入后注入观察异常，验证相同Play内失败收尾，不中断正式开局在途事务。</summary>
    public static string StartFailure() => Begin(true);
    private static string Begin(bool interrupt)
    {
        if (!EditorApplication.isPlaying || Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets") throw new Exception("需本项目Play");
        var prior = Owner();
        if (prior != null && (bool)prior.GetField("running", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)) throw new Exception("已有加载观察事务");
        SessionState.SetString(OwnerKey, typeof(ArcadeLoadingFlowProbe).Assembly.FullName);
        interruptAfterEntry = interrupt;
        running = true; cancellation = new CancellationTokenSource(); Run(cancellation.Token).Forget(); return "正式加载流程观察已启动";
    }

    public static string State()
    {
        var owner = Owner();
        if (owner != null && owner != typeof(ArcadeLoadingFlowProbe)) return (string)owner.GetMethod("State").Invoke(null, null);
        return "running=" + running + " run=" + runId + " directory=" + directory;
    }

    private static Type Owner() => AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.FullName == SessionState.GetString(OwnerKey, ""))?.GetType("ArcadeLoadingFlowProbe");

    private static async UniTaskVoid Run(CancellationToken token)
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/flow.log", "正式Boot／新局按钮，隔离临时档；资源准备端口延迟1200ms仅为画面捕获，不代表自然加载耗时。\n");
        try
        {
            double deadline = EditorApplication.timeSinceStartup + 180;
            while (!ResourceLoader.IsConfigured || UIHelper.GetUICtrl<StartUICtrl>()?.IsShow() != true)
            { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("真实主菜单就绪超时"); await UniTask.Delay(100, true, cancellationToken: token); }
            var a = global::Game.Game.Interface;
            var runs = a.GetSystem<ArcadeRunSystem>();
            store = RunField("store").GetValue(runs); clock = RunField("clock").GetValue(runs); failure = RunField("nextDeleteFailureForTests").GetValue(runs);
            var runModel = a.GetModel<ArcadeRunModel>();
            originalRun = typeof(ArcadeRunModel).GetField("current", Hidden).GetValue(runModel);
            originalPhase = typeof(ArcadeRunModel).GetField("<Phase>k__BackingField", Hidden).GetValue(runModel);
            var originalMenu = (StartUI)typeof(BaseUICtrl).GetField("BaseUI", Hidden).GetValue(UIHelper.GetUICtrl<StartUICtrl>());
            originalNewButton = originalMenu.BtnNewGame.gameObject.activeSelf;
            originalContinueButton = originalMenu.BtnContinue.gameObject.activeSelf;
            directory = Path.Combine(Path.GetTempPath(), "FirepowerLoading-" + Guid.NewGuid().ToString("N"));
            runs.ConfigureForTests(directory);
            preparation = a.GetUtility<IGameplayResourcePreparation>();
            a.RegisterUtility<IGameplayResourcePreparation>(new CaptureWait(preparation));
            UIHelper.HideUI<StartUICtrl>(); var menu = UIHelper.ShowUI<StartUICtrl>();
            var view = (StartUI)typeof(BaseUICtrl).GetField("BaseUI", Hidden).GetValue(menu);
            view.BtnNewGame.onClick.Invoke();
            await UniTask.Delay(180, true, cancellationToken: token);
            var loading = UIHelper.GetUICtrl<LoadingUICtrl>();
            if (loading?.IsShow() != true) throw new Exception("开局资源准备阶段未显示加载界面");
            var ui = (LoadingUI)typeof(BaseUICtrl).GetField("BaseUI", Hidden).GetValue(loading);
            if (!ui.MapArtwork.activeInHierarchy) throw new Exception("地图加载背景未启用");
            var backgrounds = ui.MapArtwork.GetComponentsInChildren<Graphic>(true)
                .Select(g => g is RawImage raw ? raw.texture : g is Image image ? image.sprite?.texture : null).Where(t => t != null).ToArray();
            if (backgrounds.Length == 0) throw new Exception("实际加载界面没有背景纹理");
            File.AppendAllText(Output + "/flow.log", "LoadingVisible=True artwork=" + string.Join(";", backgrounds.Select(AssetDatabase.GetAssetPath)) + "\n");
            Canvas.ForceUpdateCanvases(); ScreenCapture.CaptureScreenshot(Output + "/loading-visible.png");
            deadline = EditorApplication.timeSinceStartup + 100;
            while (a.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing || UIHelper.HasUIOpen || a.GetModel<MapSceneModel>().CurrentHandle == null)
            { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("实际开局完成超时"); await UniTask.Delay(100, true, cancellationToken: token); }
            runId = new ArcadeRunFileStore(directory).Inspect().Data?.RunId;
            if (runId == null || runId != a.GetModel<ArcadeRunModel>().Current?.RunId) throw new Exception("临时局身份不符");
            if (loading.IsShow()) throw new Exception("进入战斗后加载界面未隐藏");
            File.AppendAllText(Output + "/flow.log", "EnteredPlaying=True LoadingHidden=True map=" + a.GetModel<MapSceneModel>().CurrentMapId + " run=" + runId + "\n");
            ScreenCapture.CaptureScreenshot(Output + "/entered-game-early.png");
            await UniTask.Delay(2500, true, cancellationToken: token);
            var runtime = typeof(ArcadeCombatModel).GetProperty("Runtime", Hidden).GetValue(a.GetModel<ArcadeCombatModel>());
            if (runtime == null) throw new Exception("开局后现场已提前释放");
            bool transitioning = (bool)runtime.GetType().GetField("IslandTransitionPending").GetValue(runtime);
            File.AppendAllText(Output + "/flow.log", "SettledArena transitioning=" + transitioning + " casual=" + a.GetModel<ArcadeRunModel>().Current?.IsCasualRoute + " waveIndex=" + runtime.GetType().GetField("WaveIndex").GetValue(runtime) + "\n");
            if (transitioning) throw new Exception("短时观察后首轮仍未完成准备");
            ScreenCapture.CaptureScreenshot(Output + "/entered-game.png");
            await UniTask.Delay(200, true, cancellationToken: token);
            if (interruptAfterEntry) throw new InvalidOperationException("预期观察中断：验证已入局后的依赖与菜单恢复");
            await Cleanup();
            VerifyRestored();
            File.AppendAllText(Output + "/flow.log", "PASS 正式新局／真实背景／完成隐藏／放弃返回／恢复依赖\n");
        }
        catch (Exception e)
        {
            File.AppendAllText(Output + "/flow.log", "FAILED " + e + "\n");
            try
            {
                await Cleanup(); VerifyRestored();
                if (interruptAfterEntry && e.Message.StartsWith("预期观察中断")) File.AppendAllText(Output + "/flow.log", "PASS 预期异常后恢复模型、依赖与菜单，可在同一Play继续\n");
            }
            catch (Exception cleanup) { File.AppendAllText(Output + "/flow.log", "CLEANUP FAILED " + cleanup + "\n"); }
        }
        finally { running = false; cancellation?.Dispose(); cancellation = null; }
    }

    private static FieldInfo RunField(string name) => typeof(ArcadeRunSystem).GetField(name, Hidden);

    private static void VerifyRestored()
    {
        var a = global::Game.Game.Interface; var model = a.GetModel<ArcadeRunModel>();
        if (!ReferenceEquals(typeof(ArcadeRunModel).GetField("current", Hidden).GetValue(model), originalRun) || !Equals(model.Phase, originalPhase))
            throw new Exception("观察结束后临时结果仍残留在ArcadeRunModel");
        var menu = UIHelper.GetUICtrl<StartUICtrl>();
        var ui = (StartUI)typeof(BaseUICtrl).GetField("BaseUI", Hidden).GetValue(menu);
        if (!menu.IsShow() || ui.BtnNewGame.gameObject.activeSelf != originalNewButton || ui.BtnContinue.gameObject.activeSelf != originalContinueButton)
            throw new Exception("恢复依赖后菜单仍展示临时存档入口");
        File.AppendAllText(Output + "/flow.log", "RestoredModelAndMenu=True new=" + originalNewButton + " continue=" + originalContinueButton + "\n");
    }

    private static async UniTask Cleanup()
    {
        var a = global::Game.Game.Interface;
        var runs = a.GetSystem<ArcadeRunSystem>();
        try
        {
            if (directory != null)
            {
                string saved = new ArcadeRunFileStore(directory).Inspect().Data?.RunId;
                if (saved != null)
                {
                    if (a.GetModel<ArcadeRunModel>().Current?.RunId != saved) throw new Exception("清理拒绝另一局身份");
                    if (!runs.TryAbandon(out var error)) throw new Exception(error);
                    var result = await a.GetSystem<ArcadeSceneFlowSystem>().ReturnToMainMenuAsync();
                    if (!result.Success) throw new Exception(result.Error);
                }
            }
        }
        finally
        {
            if (store != null)
            {
                RunField("store").SetValue(runs, store); RunField("clock").SetValue(runs, clock); RunField("nextDeleteFailureForTests").SetValue(runs, failure);
                a.RegisterUtility<IGameplayResourcePreparation>(preparation);
                var model = a.GetModel<ArcadeRunModel>();
                typeof(ArcadeRunModel).GetField("current", Hidden).SetValue(model, originalRun);
                typeof(ArcadeRunModel).GetField("<Phase>k__BackingField", Hidden).SetValue(model, originalPhase);
                // 先恢复状态与依赖，再让菜单按原store重读入口，不能残留临时放弃结果。
                if (a.GetModel<MapSceneModel>().CurrentHandle == null && a.GetModel<AppStateModel>().CurrentState.Value == AppState.Paused)
                { UIHelper.HideUI<StartUICtrl>(); UIHelper.ShowUI<StartUICtrl>(); }
                File.AppendAllText(Output + "/flow.log", "RestoredStore=" + ReferenceEquals(RunField("store").GetValue(runs), store) + " mapHandle=" + (a.GetModel<MapSceneModel>().CurrentHandle == null ? "null" : "present") + "\n");
                store = null;
            }
            Time.timeScale = 1f;
        }
    }

    private sealed class CaptureWait : IGameplayResourcePreparation
    {
        private readonly IGameplayResourcePreparation original;
        public CaptureWait(IGameplayResourcePreparation original) { this.original = original; }
        public async UniTask<bool> PrepareAsync(string mapId, Func<bool> isCurrent = null, CancellationToken cancellationToken = default)
        {
            await UniTask.Delay(1200, true, cancellationToken: cancellationToken);
            return (isCurrent == null || isCurrent()) && (original == null || await original.PrepareAsync(mapId, isCurrent, cancellationToken));
        }
    }
}
