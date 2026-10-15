using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;
using UnityEngine.AI;

namespace Game
{
    public sealed class OnlineWorldEnemyCtrl:IController,IDisposable
    {
        private sealed class Entry{public OnlineWorldEnemySnapshot State;public EnemyConfigDefinition Config;public ResourceLoadHandle Handle;public EnemyViewCtrl View;public bool Released;}
        private readonly IArchitecture architecture;private readonly List<Entry> staged=new();private readonly Dictionary<long,Entry> active=new();private readonly Dictionary<long,OnlineWorldEnemyNavigationSample> lastReported=new();private Transform candidateRoot,activeRoot;private long candidateMapInstanceId,candidateWorldId,activeWorldId;private string candidateMapId,activeMapId;private bool committed,disposed;private long lastReportAt,lastHeartbeatAt,observedLeaseRevision,observedExecutorUnitId;
        internal Action<long> BeforeCommitEntryForTests;
        public OnlineWorldEnemyCtrl(IArchitecture architecture){this.architecture=architecture;LifeCycle.RegisterUpdate(Tick);}
        public IArchitecture GetArchitecture()=>architecture;

        /// <summary>联机快照仅装配客户端表现；候选从创建到提交前始终不可见。</summary>
        public async UniTask<MapControllerResult> PrepareMapAsync(MapPreparationContext context, CancellationToken token)
        {
            if (context?.Mode != MapPreparationMode.OnlinePlayersOnly ||
                context.Scene?.DynamicObjectRoot == null || context.OnlineWorld == null)
                return MapControllerResult.Fail("联机敌人准备模式无效");
            RollbackPreparedMap();
            candidateRoot = context.Scene.DynamicObjectRoot;
            candidateMapInstanceId = context.MapInstanceId;
            candidateWorldId = context.OnlineWorld.WorldId;
            candidateMapId = context.OnlineWorld.MapId;
            var unitIds = new HashSet<long>();
            foreach (var enemy in context.OnlineWorld.Enemies)
                if (enemy.UnitId <= 0 || enemy.UnitId > int.MaxValue || !unitIds.Add(enemy.UnitId) ||
                    enemy.WorldId != candidateWorldId || enemy.MapId != candidateMapId)
                    return Fail("联机敌人结构无效或 UnitId 重复");

            try
            {
                foreach (var enemy in context.OnlineWorld.Enemies.Where(x => !x.IsDead))
                {
                    var configProvider = this.GetUtility<IRuntimeConfigProvider>();
                    string configError = null;
                    EnemyConfigDefinition config;
                    if (configProvider == null || !configProvider.TryGetEnemy(enemy.EnemyConfigId,
                            out config, out configError))
                        return Fail(configError ?? "联机敌人配置缺失");
                    var completion = new UniTaskCompletionSource<GameObject>();
                    var handle = ResourceLoader.LoadAssetAsync<GameObject>(config.PrefabAddress,
                        prefab => completion.TrySetResult(prefab));
                    var instances = this.GetUtility<IViewInstanceProvider>();
                    EnemyView view = null;
                    EnemyViewCtrl ctrl = null;
                    var transferred = false;
                    try
                    {
                        GameObject prefab;
                        using (token.Register(() => completion.TrySetCanceled(token)))
                            prefab = await completion.Task;
                        var prefabView = prefab != null ? prefab.GetComponent<EnemyView>() : null;
                        if (prefabView == null || prefabView.GetComponent<NavMeshAgent>() == null)
                            return Fail("联机敌人 Prefab 缺少 EnemyView 或 NavMeshAgent");
                        var world = candidateRoot.TransformPoint(enemy.LocalPosition);
                        if (!NavMesh.SamplePosition(world, out var hit, 2f, NavMesh.AllAreas))
                            return Fail("联机敌人出生点无可用 NavMesh");
                        var yaw = (candidateRoot.rotation * Quaternion.Euler(0f, enemy.YawDegrees, 0f))
                            .eulerAngles.y;
                        view = InstantiateInactive(instances, prefabView, candidateRoot, hit.position,
                            candidateRoot.rotation * Quaternion.Euler(0f, enemy.YawDegrees, 0f));
                        if (view == null) return Fail("联机敌人实例化失败");
                        view.BeginPresentationUse();
                        if (!EnemyCtrl.ValidatePresentationWeaponKind(view, config, out var presentationError))
                            return Fail("联机敌人武器表现不可用：" + presentationError);
                        if (view.GetComponent<CharacterWeaponPresenter>() != null &&
                            !view.SetCombatWeapon(config.WeaponId))
                            return Fail($"联机敌人武器表现不可用：{config.Id}/{config.WeaponId}");
                        ctrl = new EnemyViewCtrl(view, (int)enemy.UnitId, context.MapInstanceId,
                            this.GetUtility<ViewCtrlRegistry>(), instances);
                        ctrl.ConfigureNavigation(config.MoveSpeed, config.AttackRange);
                        ctrl.Snap(world, yaw);
                        ctrl.ResetMovementSampling();
                        staged.Add(new Entry { State = enemy, Config = config, Handle = handle, View = ctrl });
                        transferred = true;
                    }
                    finally
                    {
                        if (!transferred)
                        {
                            if (ctrl != null) ctrl.Dispose();
                            else if (view != null) instances?.Release(view.gameObject);
                            if (handle.IsValid) ResourceLoader.Release(handle);
                        }
                    }
                }
                return MapControllerResult.Ok();
            }
            catch (OperationCanceledException)
            {
                return Fail("联机敌人 Prefab 加载取消或失败");
            }
            catch (Exception exception)
            {
                return Fail("联机敌人准备失败：" + exception.Message);
            }
        }        public MapControllerResult CommitPreparedMap(){if(disposed||candidateRoot==null||candidateMapInstanceId<=0||candidateWorldId<=0||string.IsNullOrWhiteSpace(candidateMapId)||committed)return MapControllerResult.Fail("联机敌人候选上下文无效");if(staged.Any(e=>e==null||e.Released||e.View==null||!e.View.IsValid)||staged.Select(e=>e.State.UnitId).Distinct().Count()!=staged.Count)return Fail("联机敌人候选条目无效或重复");activeRoot=candidateRoot;activeWorldId=candidateWorldId;activeMapId=candidateMapId;try{foreach(Entry e in staged){BeforeCommitEntryForTests?.Invoke(e.State.UnitId);e.View.SetVisible(true);active.Add(e.State.UnitId,e);Apply(e,e.State,true,0);}staged.Clear();candidateRoot=null;candidateMapId=null;candidateMapInstanceId=candidateWorldId=0;committed=true;return MapControllerResult.Ok();}catch(Exception exception){foreach(Entry e in active.Values)Release(e);active.Clear();foreach(Entry e in staged)Release(e);staged.Clear();activeRoot=candidateRoot=null;activeMapId=candidateMapId=null;activeWorldId=candidateMapInstanceId=candidateWorldId=0;committed=false;return MapControllerResult.Fail("联机敌人提交失败："+exception.Message);}}
        public void RollbackPreparedMap(){foreach(Entry e in staged)Release(e);staged.Clear();candidateRoot=null;candidateMapId=null;candidateMapInstanceId=candidateWorldId=0;}
        public void ReleaseCurrentMap(){committed=false;foreach(Entry e in active.Values)Release(e);active.Clear();lastReported.Clear();lastReportAt=lastHeartbeatAt=observedLeaseRevision=observedExecutorUnitId=0;activeRoot=null;activeMapId=null;activeWorldId=0;}
        public void SetCurrentMapActive(bool value){foreach(Entry e in active.Values)e.View.SetVisible(value);}
        public void ApplySnapshot(OnlineWorldSnapshot snapshot){if(!committed||snapshot==null||snapshot.WorldId!=activeWorldId||snapshot.MapId!=activeMapId)return;ObserveNavigationLease(snapshot);var wanted=new HashSet<long>(snapshot.Enemies.Where(x=>!x.IsDead).Select(x=>x.UnitId));foreach(long id in active.Keys.Where(x=>!wanted.Contains(x)).ToArray()){Release(active[id]);active.Remove(id);lastReported.Remove(id);}foreach(OnlineWorldEnemySnapshot state in snapshot.Enemies){if(state.IsDead)continue;if(active.TryGetValue(state.UnitId,out Entry e)&&state.StateRevision>=e.State.StateRevision&&state.LifecycleRevision>=e.State.LifecycleRevision){bool lifecycle=state.LifecycleRevision!=e.State.LifecycleRevision;int damage=Mathf.Max(0,e.State.CurrentHP-state.CurrentHP);e.State=state;Apply(e,state,lifecycle,damage);}}}
        private void Apply(Entry e,OnlineWorldEnemySnapshot s,bool snap,int damage){if(damage>0){e.View.PlayHitFeedback();e.View.ShowDamage(damage);}e.View.SetHealthDisplay(s.CurrentHP,s.MaxHP);e.View.SetHealthBarVisible(!s.IsDead);if(s.IsDead){e.View.MarkOnlineDead();return;}if(snap){e.View.ReviveOnline();Vector3 w=activeRoot.TransformPoint(s.LocalPosition);float yaw=(activeRoot.rotation*Quaternion.Euler(0f,s.YawDegrees,0f)).eulerAngles.y;e.View.Snap(w,yaw);e.View.ResetMovementSampling();}}
        private void Tick(){if(!committed||disposed||activeRoot==null)return;OnlineWorldSnapshot snapshot=this.GetSystem<OnlineWorldSystem>()?.Snapshot;if(snapshot==null||!snapshot.HasWorld)return;bool executor=snapshot.NavigationExecutorUnitId==snapshot.SelfUnitId;foreach(Entry e in active.Values){e.View.SetNavigationEnabled(executor&&!e.State.IsDead);if(e.State.IsDead){e.View.TickCombatFeedback(Time.deltaTime);continue;}if(executor){if(TryTarget(snapshot,e.State.TargetUnitId,out Vector3 destination))e.View.SetDestination(destination);else e.View.Stop();}else{Vector3 target=activeRoot.TransformPoint(e.State.LocalPosition);Vector3 position=Vector3.Lerp(e.View.Position,target,Mathf.Clamp01(Time.deltaTime/0.1f));float yaw=(activeRoot.rotation*Quaternion.Euler(0f,e.State.YawDegrees,0f)).eulerAngles.y;e.View.Snap(position,Mathf.LerpAngle(e.View.Rotation.eulerAngles.y,yaw,Mathf.Clamp01(Time.deltaTime/0.1f)));}e.View.TickCombatFeedback(Time.deltaTime);}if(!executor)return;List<OnlineWorldEnemyNavigationSample> samples=new();foreach(Entry e in active.Values.Where(x=>!x.State.IsDead)){Vector3 local=activeRoot.InverseTransformPoint(e.View.Position);float yaw=(Quaternion.Inverse(activeRoot.rotation)*e.View.Rotation).eulerAngles.y;samples.Add(new OnlineWorldEnemyNavigationSample(e.State.UnitId,e.State.LifecycleRevision,local,yaw));}SampleAndReportNavigation(snapshot,(long)(Time.realtimeSinceStartup*1000f),samples);}
        private void ObserveNavigationLease(OnlineWorldSnapshot snapshot){bool leaseChanged=snapshot.NavigationLeaseRevision!=observedLeaseRevision||snapshot.NavigationExecutorUnitId!=observedExecutorUnitId;bool becameExecutor=leaseChanged&&snapshot.NavigationExecutorUnitId==snapshot.SelfUnitId;observedLeaseRevision=snapshot.NavigationLeaseRevision;observedExecutorUnitId=snapshot.NavigationExecutorUnitId;if(!becameExecutor)return;lastReported.Clear();lastReportAt=lastHeartbeatAt=0;foreach(Entry e in active.Values)if(!e.State.IsDead)Apply(e,e.State,true,0);}
        internal void ObserveNavigationLeaseForTests(OnlineWorldSnapshot snapshot)=>ObserveNavigationLease(snapshot);
        internal bool SampleAndReportNavigation(OnlineWorldSnapshot snapshot,long now,IReadOnlyList<OnlineWorldEnemyNavigationSample> samples){if(snapshot==null||snapshot.NavigationExecutorUnitId!=snapshot.SelfUnitId)return false;bool heartbeat=now-lastHeartbeatAt>=500;if(now-lastReportAt<100&&!heartbeat)return false;List<OnlineWorldEnemyNavigationSample> reports=new();if(now-lastReportAt>=100){foreach(OnlineWorldEnemyNavigationSample sample in samples??Array.Empty<OnlineWorldEnemyNavigationSample>()){if(!lastReported.TryGetValue(sample.UnitId,out OnlineWorldEnemyNavigationSample old)||old.LifecycleRevision!=sample.LifecycleRevision||(old.LocalPosition-sample.LocalPosition).sqrMagnitude>0.000001f||Mathf.Abs(Mathf.DeltaAngle(old.YawDegrees,sample.YawDegrees))>0.001f)reports.Add(sample);}}if(reports.Count==0&&!heartbeat){lastReportAt=now;return false;}if(!this.GetSystem<OnlineWorldSystem>().TryReportEnemyNavigation(snapshot.NavigationLeaseRevision,reports))return false;foreach(OnlineWorldEnemyNavigationSample report in reports)lastReported[report.UnitId]=report;lastReportAt=now;if(heartbeat)lastHeartbeatAt=now;return true;}
        private bool TryTarget(OnlineWorldSnapshot snapshot,long targetId,out Vector3 position){position=default;if(targetId==snapshot.SelfUnitId){IPlayerRuntimePortProvider p=this.GetUtility<IPlayerRuntimePortProvider>();if(p!=null&&p.TryGetCurrentPlayerPose(out PlayerPoseSnapshot pose,out _)&&pose.IsActive){position=pose.Position;return true;}}OnlineWorldPlayerSnapshot target=snapshot.Players.FirstOrDefault(x=>x.UnitId==targetId);if(target.UnitId==0)return false;position=activeRoot.TransformPoint(target.LocalPosition);return true;}
        private MapControllerResult Fail(string error){RollbackPreparedMap();return MapControllerResult.Fail(error);}
        /// <summary>先置于禁用父节点，首次激活前完成本次武器与生命表现装配。</summary>
        private static EnemyView InstantiateInactive(IViewInstanceProvider instances,EnemyView prefab,Transform parent,Vector3 position,Quaternion rotation)
        {
            var staging=new GameObject("OnlineEnemyPresentationStaging");staging.SetActive(false);
            try
            {
                var view=instances?.Instantiate(prefab,staging.transform,position,rotation);
                if(view==null)return null;
                if(view.gameObject.activeInHierarchy){instances.Release(view.gameObject);throw new InvalidOperationException("联机敌人候选实例创建时意外激活");}
                view.gameObject.SetActive(false);view.transform.SetParent(parent,true);return view;
            }
            finally
            {
                if(Application.isPlaying)UnityEngine.Object.Destroy(staging);
                else UnityEngine.Object.DestroyImmediate(staging);
            }
        }
        private void Release(Entry e){if(e==null||e.Released)return;e.Released=true;e.View?.Dispose();if(e.Handle.IsValid)ResourceLoader.Release(e.Handle);}
        public void Dispose(){if(disposed)return;disposed=true;LifeCycle.UnregisterUpdate(Tick);RollbackPreparedMap();ReleaseCurrentMap();}
    }
}
