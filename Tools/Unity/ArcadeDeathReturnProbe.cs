using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game;
using QFramework;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>隔离临时局重复采样最后一命与返菜单；只调整临时局剩余生命，不改配置／正式存档／构建。</summary>
public static class ArcadeDeathReturnProbe
{
    private const string Output="Temp/DeathReturn-20261018", Key="FirepowerLoop.DeathProbe.Assembly";
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    private static bool running;
    private static bool expectAsync;
    private static bool injectSaveFailure;
    private static bool injectUnloadFailure;
    private static FileStream heldRunFile;
    private static object store,clock,failure,current,phase;
    private static string directory;
    private static IMapContentLoader loader;
    private static object originalView;
    private static bool wasProfiling;
    // Roslyn动态编译无ENABLE_PROFILER符号，通过委托保留采样调用，避免Conditional将其移除。
    private static readonly Action<string> SampleBegin=(Action<string>)Delegate.CreateDelegate(typeof(Action<string>),typeof(Profiler).GetMethod("BeginSample",new[]{typeof(string)}));
    private static readonly Action SampleEnd=(Action)Delegate.CreateDelegate(typeof(Action),typeof(Profiler).GetMethod("EndSample",Type.EmptyTypes));
    private static Type Owner()=>AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.FullName==SessionState.GetString(Key,""))?.GetType("ArcadeDeathReturnProbe");
    private static object Get(object target,string name)=>target.GetType().GetField(name,Hidden).GetValue(target);
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Hidden).SetValue(target,value);
    public static string Main() => Begin(false);
    /// <summary>接线验收要求死亡入口返回时仍处于SavingResult，UI阻断输入且主线程未执行文件写入。</summary>
    public static string VerifyAsync() => Begin(true);
    public static string VerifyFailure() => Begin(true, true);
    public static string VerifyUnloadFailure() => Begin(true, false, true);
    private static string Begin(bool requireAsync, bool failSave = false, bool failUnload = false)
    {
        if(!EditorApplication.isPlaying||Application.dataPath!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("仅本项目Play");
        if(Owner() is Type owner&&(bool)owner.GetField("running",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null))throw new Exception("已有死亡观察事务");
        expectAsync=requireAsync;injectSaveFailure=failSave;injectUnloadFailure=failUnload;SessionState.SetString(Key,typeof(ArcadeDeathReturnProbe).Assembly.FullName);running=true;Run().Forget();return "死亡／返菜单3轮定向采样已启动";
    }
    public static string State()
    {
        if(Owner() is Type owner&&owner!=typeof(ArcadeDeathReturnProbe))return (string)owner.GetMethod("State").Invoke(null,null);
        return "running="+running+"\n"+(File.Exists(Output+"/probe.log")?string.Join("\n",File.ReadAllLines(Output+"/probe.log").Reverse().Take(8).Reverse()):"");
    }
    private static void Log(string text)=>File.AppendAllText(Output+"/probe.log",DateTime.UtcNow.ToString("O")+" "+text+"\n");
    private static async UniTask Wait(Func<bool> ready,string label,double seconds=100)
    {
        double end=EditorApplication.timeSinceStartup+seconds;
        while(!ready()){if(EditorApplication.timeSinceStartup>end)throw new Exception(label+"超时，保持临时隔离");await UniTask.NextFrame();}
    }
    private static async UniTaskVoid Run()
    {
        Directory.CreateDirectory(Output);File.WriteAllText(Output+"/probe.log","正式Boot新局；3轮受控临时局最后一命，Profiler非Deep；不代表设备性能。\n");
        var a=global::Game.Game.Interface;
        try
        {
            await Wait(()=>ResourceLoader.IsConfigured&&UIHelper.GetUICtrl<StartUICtrl>()?.IsShow()==true,"Boot菜单");
            var runs=a.GetSystem<ArcadeRunSystem>();var model=a.GetModel<ArcadeRunModel>();var combat=a.GetSystem<ArcadeCombatSystem>();
            store=Get(runs,"store");clock=Get(runs,"clock");failure=Get(runs,"nextDeleteFailureForTests");current=Get(model,"current");phase=model.Phase;
            originalView=Get(combat,"view");loader=a.GetUtility<IMapContentLoader>();wasProfiling=ProfilerDriver.enabled;ProfilerDriver.enabled=true;
            for(int round=0;round<3;round++)
            {
                directory=Path.Combine(Path.GetTempPath(),"FirepowerDeath-"+Guid.NewGuid().ToString("N"));runs.ConfigureForTests(directory);
                UIHelper.HideUI<StartUICtrl>();var menu=UIHelper.ShowUI<StartUICtrl>();
                ((StartUI)typeof(BaseUICtrl).GetField("BaseUI",Hidden).GetValue(menu)).BtnNewGame.onClick.Invoke();
                var combatModel=a.GetModel<ArcadeCombatModel>();var flow=a.GetSystem<ArcadeSceneFlowSystem>();
                await Wait(()=>combatModel.IsActive&&a.GetModel<AppStateModel>().CurrentState.Value==AppState.Playing&&!UIHelper.HasUIOpen,"正式入局");
                await UniTask.Delay(2500,true);
                var runtime=typeof(ArcadeCombatModel).GetProperty("Runtime",Hidden).GetValue(combatModel);
                await Wait(()=> (float)Get(runtime,"PlayerInvulnerability")<=0 && (float)Get(runtime,"RespawnRemaining")<=0,"可受伤");
                typeof(ArcadeRunModel).GetMethod("SetTransientLives",Hidden).Invoke(model,new object[]{1});
                var view=new TimedView((IArcadeCombatViewPort)originalView);Set(combat,"view",view);
                var timedLoader=new TimedLoader(loader){FailNext=injectUnloadFailure&&round==0};a.RegisterUtility<IMapContentLoader>(timedLoader);
                if(injectSaveFailure && round==0)
                {
                    heldRunFile=new FileStream(new ArcadeRunFileStore(directory).RunPath,FileMode.Open,FileAccess.Read,FileShare.None);
                    combat.TryDamagePlayerForTests(100000,out _,out _);
                    await Wait(()=>!runs.IsSavingResult&&!(bool)Get(combat,"lastDeathReturnPending"),"保存失败回退");
                    heldRunFile.Dispose();heldRunFile=null;
                    if(model.Phase!=ArcadeRunPhase.Arena||(int)Get(runtime,"PlayerHealth")!=1||UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow()==true||a.GetModel<AppStateModel>().CurrentState.Value!=AppState.Playing||new ArcadeRunFileStore(directory).Inspect().Data.HasResult)
                        throw new Exception("保存失败没有恢复原局／一血／输入／遮罩");
                    Log("InjectedSaveFailure=True OriginalNodePreserved=True Health=1 LoadingHidden=True State=Playing");
                    await Wait(()=>(float)Get(runtime,"PlayerInvulnerability")<=0,"失败后重试无敌窗口");
                }
                int first=ProfilerDriver.lastFrameIndex;var wall=System.Diagnostics.Stopwatch.StartNew();var watch=System.Diagnostics.Stopwatch.StartNew();
                SampleBegin("DeathProbe.Fatal");bool ok;
                try{ok=combat.TryDamagePlayerForTests(100000,out _,out var error);if(!ok)throw new Exception(error);}
                finally{SampleEnd();}
                watch.Stop();double entry=watch.Elapsed.TotalMilliseconds;
                if(expectAsync && (model.Phase!=ArcadeRunPhase.SavingResult || !UIHelper.HasUIOpen || a.GetModel<AppStateModel>().CurrentState.Value!=AppState.Loading))
                    throw new Exception("死亡入口仍同步落盘或未阻断在途操作，phase="+model.Phase);
                if(injectUnloadFailure&&round==0)
                {
                    await Wait(()=>!runs.IsSavingResult&&!(bool)Get(combat,"lastDeathReturnPending")&&!(bool)Get(flow,"busy"),"卸载失败收敛");
                    var message=UIHelper.GetUICtrl<MessageUICtrl>();
                    if(model.Phase!=ArcadeRunPhase.Result||a.GetModel<MapSceneModel>().CurrentHandle==null||message?.IsShow()!=true)throw new Exception("卸载失败未保留结果与重试入口");
                    var config=(MessageConfig)Get(message,"currentConfig");
                    typeof(MessageUICtrl).GetMethod("OnMessageButtonClick",Hidden).Invoke(message,new object[]{new object[]{config.Buttons.Single(b=>b.Role==MessageButtonRole.Confirm)}});
                    Log("InjectedUnloadFailure=True SavedResultRetained=True RetryInvoked=True");
                }
                await Wait(()=>!(bool)Get(flow,"busy")&&a.GetModel<MapSceneModel>().CurrentHandle==null&&UIHelper.GetUICtrl<StartUICtrl>()?.IsShow()==true,"返回菜单");
                double total=wall.Elapsed.TotalMilliseconds;var saved=new ArcadeRunFileStore(directory).Inspect();
                if(!saved.IsValid||saved.Data.Outcome!="failed"||saved.Data.RemainingLives!=0)throw new Exception("结果未正确保存");
                Set(combat,"view",originalView);a.RegisterUtility<IMapContentLoader>(loader);
                await UniTask.Delay(250,true);
                Log("Round="+round+" EntryMs="+entry.ToString("F3")+" ViewEnds="+string.Join(",",view.EndTimes.Select(v=>v.ToString("F3")))+" UnloadWallMs="+timedLoader.Elapsed.ToString("F3")+" MenuObservedWallMs="+total.ToString("F3")+" Result=failed");
                CaptureFrames(first,ProfilerDriver.lastFrameIndex,round);
            }
            await Cleanup();Log("PASS 3轮结果／菜单／原模型及端口恢复");
        }
        catch(Exception error){heldRunFile?.Dispose();heldRunFile=null;Log("FAILED "+error);try{await Cleanup();}catch(Exception cleanup){Log("CLEANUP FAILED "+cleanup);}}
        finally{running=store!=null;}
    }
    private static void CaptureFrames(int first,int last,int round)
    {
        var frames=new List<object>();
        for(int frame=Math.Max(first,ProfilerDriver.firstFrameIndex);frame<=last;frame++)
        using(var data=ProfilerDriver.GetRawFrameDataView(frame,0))
        {
            if(!data.valid)continue;
            var selected=new List<object>();
            for(int i=0;i<data.sampleCount;i++)
                if(data.GetSampleName(i).StartsWith("DeathProbe."))
                {
                    int end=Math.Min(data.sampleCount,i+data.GetSampleChildrenCountRecursive(i)+1);
                    var children=Enumerable.Range(i+1,Math.Max(0,end-i-1)).Select(index=>new {Name=data.GetSampleName(index),Ms=data.GetSampleTimeMs(index)}).OrderByDescending(x=>x.Ms).Take(20).ToArray();
                    selected.Add(new{Name=data.GetSampleName(i),Ms=data.GetSampleTimeMs(i),Children=children});
                }
            var top=data.frameTimeMs>12?Enumerable.Range(1,Math.Max(0,data.sampleCount-1)).Select(i=>new{Name=data.GetSampleName(i),Ms=data.GetSampleTimeMs(i)})
                .Where(s=>s.Name!="PlayerLoop"&&s.Name!="EditorLoop").OrderByDescending(s=>s.Ms).Take(18).ToArray():null;
            frames.Add(new{Frame=frame,Ms=data.frameTimeMs,Markers=selected,Top=top});
        }
        File.WriteAllText(Output+"/round-"+round+"-frames.json",Newtonsoft.Json.JsonConvert.SerializeObject(frames,Newtonsoft.Json.Formatting.Indented));
    }
    private static async UniTask Cleanup()
    {
        if(store==null)return;var a=global::Game.Game.Interface;var flow=a.GetSystem<ArcadeSceneFlowSystem>();
        UIHelper.HideUI<StartUICtrl>();await UniTask.NextFrame();
        await Wait(()=>!a.GetSystem<ArcadeRunSystem>().IsSavingResult&&!(bool)Get(a.GetSystem<ArcadeCombatSystem>(),"lastDeathReturnPending")&&!(bool)Get(flow,"busy")&&!(bool)Get(a.GetSystem<MapSceneSystem>(),"busy"),"清理收敛",60);await UniTask.NextFrame();
        if(a.GetModel<MapSceneModel>().CurrentHandle!=null){var result=await flow.ReturnToMainMenuAsync();if(!result.Success)throw new Exception(result.Error);}
        var runs=a.GetSystem<ArcadeRunSystem>();Set(runs,"store",store);Set(runs,"clock",clock);Set(runs,"nextDeleteFailureForTests",failure);
        var model=a.GetModel<ArcadeRunModel>();Set(model,"current",current);Set(model,"<Phase>k__BackingField",phase);
        Set(a.GetSystem<ArcadeCombatSystem>(),"view",originalView);a.RegisterUtility<IMapContentLoader>(loader);
        UIHelper.ShowUI<StartUICtrl>();ProfilerDriver.enabled=wasProfiling;Time.timeScale=1;store=null;
    }
    private sealed class TimedLoader:IMapContentLoader
    {
        private readonly IMapContentLoader original;public double Elapsed;public bool FailNext;
        public TimedLoader(IMapContentLoader original){this.original=original;}
        public UniTask<MapContentLoadResult> LoadAsync(string map,string address,Action<float> progress,CancellationToken token=default)=>original.LoadAsync(map,address,progress,token);
        public async UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle,CancellationToken token=default)
        {var w=System.Diagnostics.Stopwatch.StartNew();try{if(FailNext){FailNext=false;return MapContentUnloadResult.Fail("预期卸载失败：观察重试");}return await original.UnloadAsync(handle,token);}finally{Elapsed+=w.Elapsed.TotalMilliseconds;}}
    }
    private sealed class TimedView:IArcadeCombatViewPort
    {
        private readonly IArcadeCombatViewPort original;public readonly List<double> EndTimes=new();
        public TimedView(IArcadeCombatViewPort original){this.original=original;}
        public bool IsActive=>original.IsActive;
        public void End(){var w=System.Diagnostics.Stopwatch.StartNew();SampleBegin("DeathProbe.ViewEnd");try{original.End();}finally{SampleEnd();EndTimes.Add(w.Elapsed.TotalMilliseconds);}}
        public void Begin(int arena,float width,float height,IReadOnlyList<ArcadeObstacleDefinition> obstacles)=>original.Begin(arena,width,height,obstacles);
        public bool TryScreenToArena(Vector2 screen,out Vector2 point)=>original.TryScreenToArena(screen,out point);
        public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i)=>original.SetPlayer(p,f,v,i);
        public void SetPlayerHealth(int c,int m)=>original.SetPlayerHealth(c,m);
        public void SetEnemy(int id,string c,Vector2 p,int h,int m,bool t=false,int s=0,bool b=false)=>original.SetEnemy(id,c,p,h,m,t,s,b);
        public void RemoveEnemy(int id,bool death=false)=>original.RemoveEnemy(id,death);
        public void SetProjectile(int id,bool h,Vector2 p,Vector2 d)=>original.SetProjectile(id,h,p,d);
        public void RemoveProjectile(int id)=>original.RemoveProjectile(id);
        public void SetDrop(int id,ArcadeDropDefinition d,Vector2 p)=>original.SetDrop(id,d,p);
        public void RemoveDrop(int id)=>original.RemoveDrop(id);
        public void SetRemotePlayer(long id,Vector2 p,Vector2 f,bool v,bool i,int c,int m)=>original.SetRemotePlayer(id,p,f,v,i,c,m);
        public void RemoveRemotePlayer(long id)=>original.RemoveRemotePlayer(id);
        public void TickPlayerFeedback(float delta)=>original.TickPlayerFeedback(delta);
    }
}
