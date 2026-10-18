using System;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Game;
using QFramework;
using UnityEngine;

/// <summary>与两份观察工具源码一起动态编译；受控busy窗口验证清理不会提前恢复正式端口，不创建正式局。</summary>
public static class ArcadeProbeCleanupBoundary
{
    private const BindingFlags Fields = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Output = "Temp/ProbeCleanupBoundary.log";
    public static string Main()
    {
        if(!UnityEditor.EditorApplication.isPlaying || Application.dataPath!="E:/GitHub/FirepowerLoop/Client/Assets") throw new Exception("仅本项目Play可执行观察边界诊断");
        File.WriteAllText(Output, "受控地图busy窗口；正式菜单、临时空存档，不触发Player Build。\n"); Run().Forget(); return "started";
    }
    private static void Set(Type type, string name, object value) => type.GetField(name, Fields).SetValue(null, value);
    private static async UniTaskVoid Run()
    {
        try
        {
            double readyDeadline=UnityEditor.EditorApplication.timeSinceStartup+120;
            while(!ResourceLoader.IsConfigured || UIHelper.GetUICtrl<StartUICtrl>()?.IsShow()!=true)
            {
                if(UnityEditor.EditorApplication.timeSinceStartup>readyDeadline) throw new Exception("Boot资源和正式菜单就绪超时，未接管端口");
                await UniTask.Delay(100,true);
            }
            await Check(typeof(ArcadeLoadingFlowProbe), false);
            await Check(typeof(ArcadeHeavyCombatProbe), true);
            await CheckActual(typeof(ArcadeLoadingFlowProbe), false);
            await CheckActual(typeof(ArcadeHeavyCombatProbe), true);
        }
        catch (Exception error) { File.AppendAllText(Output, "ERROR " + error + "\n"); }
    }
    private static async UniTask Check(Type probe, bool heavy)
    {
        var a = global::Game.Game.Interface; var runs = a.GetSystem<ArcadeRunSystem>(); var model = a.GetModel<ArcadeRunModel>();
        var field = typeof(ArcadeRunSystem).GetField("store", Hidden); object original = field.GetValue(runs);
        object oldClock = typeof(ArcadeRunSystem).GetField("clock", Hidden).GetValue(runs);
        object oldFailure = typeof(ArcadeRunSystem).GetField("nextDeleteFailureForTests", Hidden).GetValue(runs);
        object current = typeof(ArcadeRunModel).GetField("current", Hidden).GetValue(model), phase = model.Phase;
        string directory = Path.Combine(Path.GetTempPath(), "FirepowerBoundary-" + Guid.NewGuid().ToString("N"));
        runs.ConfigureForTests(directory); object temporary = field.GetValue(runs);
        Set(probe, heavy ? "oldStore" : "store", original); Set(probe, heavy ? "oldClock" : "clock", oldClock);
        Set(probe, heavy ? "oldFailure" : "failure", oldFailure); Set(probe, heavy ? "oldRun" : "originalRun", current);
        Set(probe, heavy ? "oldPhase" : "originalPhase", phase); Set(probe, "directory", directory); Set(probe, "runId", "not-entered");
        Directory.CreateDirectory(heavy ? "Temp/HeavyCombat-20261018" : "Temp/LoadingFlow-20261018");
        if (!heavy) Set(probe, "preparation", a.GetUtility<IGameplayResourcePreparation>());
        var flow = a.GetSystem<ArcadeSceneFlowSystem>(); var busy = typeof(ArcadeSceneFlowSystem).GetField("busy", Hidden);
        busy.SetValue(flow, true); bool retained = false;
        Release().Forget();
        try { await (UniTask)probe.GetMethod("Cleanup", Fields).Invoke(null, null); }
        finally
        {
            await UniTask.Delay(500, true);
            busy.SetValue(flow, false);
            // 红测也必须恢复原依赖，避免诊断工具本身留下污染。
            typeof(ArcadeRunSystem).GetField("store", Hidden).SetValue(runs, original);
            typeof(ArcadeRunSystem).GetField("clock", Hidden).SetValue(runs, oldClock);
            typeof(ArcadeRunSystem).GetField("nextDeleteFailureForTests", Hidden).SetValue(runs, oldFailure);
            typeof(ArcadeRunModel).GetField("current", Hidden).SetValue(model, current);
            typeof(ArcadeRunModel).GetField("<Phase>k__BackingField", Hidden).SetValue(model, phase);
            Time.timeScale=1;
        }
        File.AppendAllText(Output, probe.Name + " IsolationRetainedWhileBusy=" + retained + " " + (retained ? "PASS" : "FAIL") + "\n");
        async UniTaskVoid Release()
        {
            await UniTask.Delay(250, true); retained = ReferenceEquals(field.GetValue(runs), temporary);
            busy.SetValue(flow, false);
        }
    }
    /// <summary>真实地图LoadAsync在途时开始清理；不提前取消已提交地图，等待入局续体和卸载完成。</summary>
    private static async UniTask CheckActual(Type probe, bool heavy)
    {
        var a=global::Game.Game.Interface; var runs=a.GetSystem<ArcadeRunSystem>(); var model=a.GetModel<ArcadeRunModel>();
        var storeField=typeof(ArcadeRunSystem).GetField("store",Hidden);
        object original=storeField.GetValue(runs), current=typeof(ArcadeRunModel).GetField("current",Hidden).GetValue(model), phase=model.Phase;
        Set(probe,heavy?"oldStore":"store",original);
        Set(probe,heavy?"oldClock":"clock",typeof(ArcadeRunSystem).GetField("clock",Hidden).GetValue(runs));
        Set(probe,heavy?"oldFailure":"failure",typeof(ArcadeRunSystem).GetField("nextDeleteFailureForTests",Hidden).GetValue(runs));
        Set(probe,heavy?"oldRun":"originalRun",current); Set(probe,heavy?"oldPhase":"originalPhase",phase);
        var preparation=a.GetUtility<IGameplayResourcePreparation>(); if(!heavy) Set(probe,"preparation",preparation);
        string directory=Path.Combine(Path.GetTempPath(),"FirepowerBoundaryMap-"+Guid.NewGuid().ToString("N"));
        Set(probe,"directory",directory); Set(probe,"runId",null); runs.ConfigureForTests(directory);
        object temporary=storeField.GetValue(runs); var originalLoader=a.GetUtility<IMapContentLoader>();
        var gate=new DelayedLoader(originalLoader,()=>ReferenceEquals(storeField.GetValue(runs),temporary));
        a.RegisterUtility<IMapContentLoader>(gate);
        UIHelper.HideUI<StartUICtrl>(); var menu=UIHelper.ShowUI<StartUICtrl>();
        ((StartUI)typeof(BaseUICtrl).GetField("BaseUI",Hidden).GetValue(menu)).BtnNewGame.onClick.Invoke();
        double deadline=UnityEditor.EditorApplication.timeSinceStartup+30;
        while(!gate.Started) { if(UnityEditor.EditorApplication.timeSinceStartup>deadline) throw new Exception("真实地图注入未开始，保留隔离"); await UniTask.NextFrame(); }
        await (UniTask)probe.GetMethod("Cleanup",Fields).Invoke(null,null);
        a.RegisterUtility<IMapContentLoader>(originalLoader);
        bool restored=ReferenceEquals(storeField.GetValue(runs),original)&&ReferenceEquals(typeof(ArcadeRunModel).GetField("current",Hidden).GetValue(model),current)&&Equals(model.Phase,phase)&&a.GetModel<MapSceneModel>().CurrentHandle==null;
        File.AppendAllText(Output,probe.Name+" ActualMapInFlight=True IsolationRetained="+gate.Retained+" ModelAndStoreRestored="+restored+" "+(gate.Retained&&restored?"PASS":"FAIL")+"\n");
    }
    private sealed class DelayedLoader:IMapContentLoader
    {
        private readonly IMapContentLoader original; private readonly Func<bool> isolated;
        public bool Started, Retained;
        public DelayedLoader(IMapContentLoader original,Func<bool> isolated) {this.original=original;this.isolated=isolated;}
        public async UniTask<MapContentLoadResult> LoadAsync(string map,string address,Action<float> progress,System.Threading.CancellationToken token=default)
        {
            Started=true; await UniTask.Delay(600,true,cancellationToken:token); Retained=isolated();
            if(!Retained) return MapContentLoadResult.Fail("诊断拒绝在已恢复正式端口后继续地图加载");
            return await original.LoadAsync(map,address,progress,token);
        }
        public UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle,System.Threading.CancellationToken token=default)=>original.UnloadAsync(handle,token);
    }
}
