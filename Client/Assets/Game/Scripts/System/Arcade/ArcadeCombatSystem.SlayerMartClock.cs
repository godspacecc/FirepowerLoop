using System;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;
namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>实际单调时钟独立于战斗步长及TimeScale；只属于当前工作态。</summary>
        private Func<double> slayerMartRealtime=()=>Time.realtimeSinceStartupAsDouble;
        private ArcadeCombatRuntime slayerMartClockOwner;
        private string slayerMartClockRegion;
        private int slayerMartClockGeneration;
        /// <summary>双精度活动预算避免逐帧float舍入累积；快照仍投影原字段。</summary>
        private double slayerMartLastRealtime,slayerMartClockRemaining;
        private bool slayerMartClockRunning;
        /// <summary>挑战期限与模拟小步长分离；资源等待和暂停后的首帧不补扣等待时间。</summary>
        private ArcadeCombatRuntime slayerClockOwner;
        private double slayerLastRealtime,slayerFrameSeconds=-1;
        private bool slayerClockRunning;
        private double SlayerElapsed(ArcadeCombatRuntime runtime,double now)
        {
            var app=this.GetModel<AppStateModel>();
            bool running=runtime.SlayerChallenge!=null&&view?.IsRoundLayoutReady==true&&!UIHelper.HasUIOpen&&
                !runtime.CompletionAwaitingRetry&&!runtime.CompletionSubmitted&&(app==null||app.CurrentState.Value==AppState.Playing);
            if(!ReferenceEquals(slayerClockOwner,runtime)){slayerClockOwner=runtime;slayerLastRealtime=now;slayerClockRunning=false;}
            double elapsed=running&&slayerClockRunning?Math.Max(0,now-slayerLastRealtime):0;
            slayerLastRealtime=now;slayerClockRunning=running;return elapsed;
        }
        /// <summary>正式Update帧：期限取实际时钟，移动仍限制为小步长。</summary>
        internal void TickFrame(ArcadeCombatRuntime runtime,float simulationSeconds)
        {
            double now=slayerMartRealtime();
            PrepareSlayerMartClock(runtime,now);
            SyncLiveSlayerMartClock(runtime,now);
            bool wasReady=runtime.SlayerMart?.Ready==true;
            slayerFrameSeconds=SlayerElapsed(runtime,now);
            try{TickCore(runtime,Mathf.Clamp(simulationSeconds,0,.05f),0);}
            finally{slayerFrameSeconds=-1;}
            bool running=CanCountSlayerMartClock(runtime);
            if(!wasReady&&running)
            {
                slayerMartClockRemaining=runtime.SlayerMart.RemainingSeconds;
                slayerMartLastRealtime=slayerMartRealtime();
            }
            slayerMartClockRunning=running;
        }
        /// <summary>同一运行对象的第二家店也建立新时钟，不继承第一店已耗预算。</summary>
        private void PrepareSlayerMartClock(ArcadeCombatRuntime runtime,double now)
        {
            var state=runtime.SlayerMart;string region=state?.RegionInstanceId;int generation=state?.Generation??0;
            if(ReferenceEquals(slayerMartClockOwner,runtime)&&slayerMartClockRegion==region&&slayerMartClockGeneration==generation)return;
            slayerMartClockOwner=runtime;slayerMartClockRegion=region;slayerMartClockGeneration=generation;
            slayerMartLastRealtime=now;slayerMartClockRemaining=state?.RemainingSeconds??0;
            slayerMartClockRunning=CanCountSlayerMartClock(runtime);
        }
        /// <summary>个人菜单继续计时；全局暂停、资源等待和失败重试冻结，零命不停止区域时钟。</summary>
        private bool CanCountSlayerMartClock(ArcadeCombatRuntime runtime)
        {
            var app=this.GetModel<AppStateModel>();
            return runtime.SlayerMartReturnWave>0&&runtime.SlayerMart?.Ready==true&&!runtime.SlayerMart.Closing&&
                runtime.SlayerMartRegionPrepared&&!runtime.CompletionAwaitingRetry&&view?.IsSlayerMartRegionReady==true&&
                string.IsNullOrEmpty(view.SlayerMartRegionLoadError)&&(app==null||app.CurrentState.Value==AppState.Playing);
        }
        /// <summary>购买也先同步截止；已接受回执仍交给领域规则重放，不重新扣款。</summary>
        private void SyncLiveSlayerMartClock(ArcadeCombatRuntime runtime,double now)
        {
            if(!ReferenceEquals(slayerMartClockOwner,runtime)||double.IsNaN(now)||double.IsInfinity(now))return;
            PrepareSlayerMartClock(runtime,now);
            bool running=CanCountSlayerMartClock(runtime);
            double elapsed=Math.Max(0,now-slayerMartLastRealtime);
            slayerMartLastRealtime=Math.Max(slayerMartLastRealtime,now);
            if(slayerMartClockRunning&&running&&elapsed>0)
            {
                slayerMartClockRemaining=Math.Max(0,slayerMartClockRemaining-elapsed);
                double reduction=Math.Max(0,runtime.SlayerMart.RemainingSeconds-slayerMartClockRemaining);
                runtime.SlayerMart=ArcadeSlayerMartRules.AdvanceVisit(runtime.SlayerMart,reduction,true,false);
            }
            slayerMartClockRunning=CanCountSlayerMartClock(runtime);
        }
        private void ClearSlayerMartClock()
        {slayerMartClockOwner=null;slayerMartClockRegion=null;slayerMartClockGeneration=0;slayerMartClockRunning=false;slayerMartLastRealtime=slayerMartClockRemaining=0;
            slayerClockOwner=null;slayerClockRunning=false;slayerLastRealtime=0;slayerFrameSeconds=-1;}
    }
}
