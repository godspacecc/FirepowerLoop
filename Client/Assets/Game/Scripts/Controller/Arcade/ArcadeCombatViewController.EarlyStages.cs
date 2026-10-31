using System;
using System.Linq;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>Begin从已验证目录接收配置，轮级重试保留配置而更换独占加载状态。</summary>
        private ArcadeEarlyStageParameters earlyStage;
        private EarlyStageLoad earlyStageLoad;
        private GameObject earlyHerdTemplate;
        /// <summary>仅当前幸存者现场生效，End清除，仍使用现有候选及句柄所有权。</summary>
        private string survivorEnvironmentAddress;
        public void SetSurvivorEnvironment(string address)
        {
            if(root==null||roundContentRoot!=null||earlyStageLoad!=null)throw new InvalidOperationException("幸存者场地必须在布局加载前指定");
            if(string.IsNullOrWhiteSpace(address))throw new ArgumentException("幸存者场地地址缺失");
            survivorEnvironmentAddress=address;
        }
        /// <summary>仅暂时接管旧静态底图的外壁，不触碰本轮动态碰撞或永久修改场景。</summary>
        private readonly Dictionary<Collider,bool> earlyBaseWalls=new();
        private void HideEarlyStageBaseWalls()
        {
            if(earlyStage==null||islandBaseArenaRoot==null)return;
            foreach(var collider in islandBaseArenaRoot.GetComponentsInChildren<Collider>(true))
            {
                if(root!=null&&collider.transform.IsChildOf(root.transform))continue;
                if(collider.name!="NorthBoundary"&&collider.name!="SouthBoundary"&&collider.name!="EastBoundary"&&collider.name!="WestBoundary")continue;
                if(!earlyBaseWalls.ContainsKey(collider))earlyBaseWalls.Add(collider,collider.enabled);collider.enabled=false;
            }
        }
        private void RestoreEarlyStageBaseWalls()
        {foreach(var pair in earlyBaseWalls)if(pair.Key!=null)pair.Key.enabled=pair.Value;earlyBaseWalls.Clear();}
        private sealed class EarlyStageLoad
        {
            public GameObject Owner,Round;
            public ResourceLoadHandle EnvironmentHandle,HerdHandle;
            /// <summary>三关正式演员与特效模板和环境同代拥有，释放后晚回调只能结束等待。</summary>
            public ResourceLoadHandle StageBodyHandle,StageCrowHandle,StageTravelHandle;
            public GameObject StageBody,StageCrow,StageTravel;
            public AnimationClip StageIdle,StageRun,StageAttack;
            public bool Released;
        }
        /// <summary>正式环境与牛模板全部验证后才发布就绪；晚回调没有重挂旧地图的权限。</summary>
        private async UniTaskVoid LoadConfirmedEarlyStage(ArcadeRoundLayoutParameters layout)
        {
            var state=new EarlyStageLoad{Owner=root,Round=roundContentRoot};earlyStageLoad=state;var stage=earlyStage;
            bool hasHerd=stage.HerdCount>0&&string.IsNullOrEmpty(survivorEnvironmentAddress);
            string environmentAddress=survivorEnvironmentAddress??stage.EnvironmentAddress;
            var environment=new UniTaskCompletionSource<GameObject>();var herd=new UniTaskCompletionSource<GameObject>();
            try
            {
                state.EnvironmentHandle=ResourceLoader.LoadAssetAsync<GameObject>(environmentAddress,p=>environment.TrySetResult(p));
                if(hasHerd)state.HerdHandle=ResourceLoader.LoadAssetAsync<GameObject>(stage.HerdAddress,p=>herd.TrySetResult(p));
                var stageTemplates=LoadNextStageTemplates(state,stage.ArenaConfigNumber);
                var prefab=await environment.Task;
                var cow=hasHerd?await herd.Task:null;
                await stageTemplates;
                if(!EarlyStageLoadIsCurrent(state))return;
                if(prefab==null||prefab.GetComponentsInChildren<Renderer>(true).Length==0||prefab.GetComponentsInChildren<Collider>(true).Length>0||prefab.GetComponentsInChildren<MonoBehaviour>(true).Length>0||prefab.GetComponentsInChildren<Animator>(true).Length>0||
                    prefab.GetComponentsInChildren<Renderer>(true).Any(r=>r.sharedMaterials.Any(m=>m==null||m.shader==null)))throw new InvalidOperationException("已确认场地环境资源不完整");
                if(hasHerd&&(cow==null||cow.GetComponent<ArcadeHerdVisual>()==null||!cow.GetComponent<ArcadeHerdVisual>().Validate(out _)||cow.GetComponentsInChildren<Collider>(true).Length>0))throw new InvalidOperationException("已确认牛模板资源不完整");
                ValidateFourRoundEnvironment(prefab,stage,layout);
                var instance=UnityEngine.Object.Instantiate(prefab,roundEnvironmentRoot.transform,false);instance.name="ConfirmedEarlyEnvironment";instance.SetActive(false);
                if(UsesFourRoundEnvironment(stage.ArenaConfigNumber))
                {
                    // 只显示本轮采用布局，入口沿用原有尺寸、颜色和坐标。
                    for(int round=1;round<=4;round++)instance.transform.Find("Round_"+round).gameObject.SetActive(round==layout.RoundNumber);
                    BuildRoundEntryMarkers(layout);
                }
                instance.SetActive(true);earlyHerdTemplate=cow;
                roundLayoutReady=true;roundLayoutLoadError=null;SyncRoundContentEnvironment();ProjectHerds();ProjectNextStageEnemies();
            }
            catch(Exception e)
            {
                if(!EarlyStageLoadIsCurrent(state))return;
                ReleaseRoundContent();roundLayout=layout;roundLayoutLoadError=e.Message;
                Log.Error("[ArcadeCombatViewController] 已确认场地准备失败："+e.Message);
            }
        }
        /// <summary>采用场地四轮必须完整，先检查模板再实例化；缺失时沿原事务失败并允许原轮重试。</summary>
        private static void ValidateFourRoundEnvironment(GameObject prefab,ArcadeEarlyStageParameters stage,ArcadeRoundLayoutParameters layout)
        {
            if(!UsesFourRoundEnvironment(stage.ArenaConfigNumber))return;
            if(layout.RoundNumber<1||layout.RoundNumber>4||prefab.transform.childCount!=4)throw new InvalidOperationException("已采用场地四轮布局不完整");
            for(int round=1;round<=4;round++)
            {
                var variant=prefab.transform.Find("Round_"+round);
                if(variant==null||variant.GetComponentsInChildren<Renderer>(true).Length==0)throw new InvalidOperationException("已采用场地布局缺失："+round);
            }
        }
        /// <summary>原巨石与三场采用环境共用四轮选择，其余单体环境沿用原规则。</summary>
        private static bool UsesFourRoundEnvironment(int arena)=>arena==5||arena==7||arena==10||arena==20;
        private bool EarlyStageLoadIsCurrent(EarlyStageLoad state)=>!state.Released&&ReferenceEquals(earlyStageLoad,state)&&root!=null&&root==state.Owner&&roundContentRoot!=null&&roundContentRoot==state.Round;
        /// <summary>实例随轮根释放，模板句柄只由本状态归还一次，旧回调不影响新状态。</summary>
        private void ReleaseEarlyStageLoad()
        {
            ClearHerdViews();
            ClearNextStageEnemies();
            var state=earlyStageLoad;earlyStageLoad=null;earlyHerdTemplate=null;
            if(state==null||state.Released)return;state.Released=true;
            if(state.EnvironmentHandle.IsValid)ResourceLoader.Release(state.EnvironmentHandle);
            if(state.HerdHandle.IsValid)ResourceLoader.Release(state.HerdHandle);
            if(state.StageBodyHandle.IsValid)ResourceLoader.Release(state.StageBodyHandle);
            if(state.StageCrowHandle.IsValid)ResourceLoader.Release(state.StageCrowHandle);
            if(state.StageTravelHandle.IsValid)ResourceLoader.Release(state.StageTravelHandle);
            state.EnvironmentHandle=state.HerdHandle=ResourceLoadHandle.Empty;
            state.StageBodyHandle=state.StageCrowHandle=state.StageTravelHandle=ResourceLoadHandle.Empty;
            state.StageBody=state.StageCrow=state.StageTravel=null;state.StageIdle=state.StageRun=state.StageAttack=null;
        }
    }
}
