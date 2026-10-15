using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Game
{
    public sealed class OnlineWorldCorpseCtrl : IController, IDisposable
    {
        private sealed class Entry
        {
            public OnlineWorldCorpseSnapshot State;
            public ResourceLoadHandle Handle;
            public GameObject Instance;
            public OnlineCorpseView View;
            public bool Released;
        }

        private readonly IArchitecture architecture;
        private readonly List<Entry> staged = new();
        private readonly Dictionary<long, Entry> active = new();
        private readonly Dictionary<long, int> loading = new();
        private Transform candidateRoot, activeRoot;
        private long candidateMapInstanceId, candidateWorldId, activeMapInstanceId, activeWorldId;
        private string candidateMapId, activeMapId;
        private int generation;
        private bool committed, disposed;

        public OnlineWorldCorpseCtrl(IArchitecture architecture) => this.architecture = architecture;
        public IArchitecture GetArchitecture() => architecture;

        public async UniTask<MapControllerResult> PrepareMapAsync(MapPreparationContext context, CancellationToken token)
        {
            if (context?.Mode != MapPreparationMode.OnlinePlayersOnly || context.Scene?.DynamicObjectRoot == null || context.OnlineWorld == null)
                return MapControllerResult.Fail("联机尸体准备模式无效");
            RollbackPreparedMap();
            candidateRoot=context.Scene.DynamicObjectRoot;candidateMapInstanceId=context.MapInstanceId;candidateWorldId=context.OnlineWorld.WorldId;candidateMapId=context.OnlineWorld.MapId;
            int owner=++generation;
            if(context.OnlineWorld.Corpses.Any(x=>x.CorpseId<=0||x.WorldId!=candidateWorldId||x.MapId!=candidateMapId)||context.OnlineWorld.Corpses.Select(x=>x.CorpseId).Distinct().Count()!=context.OnlineWorld.Corpses.Count)
            {RollbackPreparedMap();return MapControllerResult.Fail("联机尸体结构无效或 CorpseId 重复");}
            foreach (var corpse in context.OnlineWorld.Corpses)
            {
                var entry=await Load(corpse,candidateRoot,candidateMapInstanceId,token,owner);
                if(entry==null){RollbackPreparedMap();return MapControllerResult.Fail("联机尸体 Prefab 加载失败");}
                staged.Add(entry);
            }
            return MapControllerResult.Ok();
        }

        public MapControllerResult CommitPreparedMap()
        {
            if(disposed||candidateRoot==null||candidateMapInstanceId<=0||candidateWorldId<=0||committed||staged.Select(x=>x.State.CorpseId).Distinct().Count()!=staged.Count)
                return MapControllerResult.Fail("联机尸体候选上下文无效");
            activeRoot=candidateRoot;activeMapInstanceId=candidateMapInstanceId;activeWorldId=candidateWorldId;activeMapId=candidateMapId;
            foreach(var entry in staged){entry.Instance.SetActive(true);active.Add(entry.State.CorpseId,entry);}staged.Clear();
            candidateRoot=null;candidateMapId=null;candidateMapInstanceId=candidateWorldId=0;committed=true;return MapControllerResult.Ok();
        }

        public void RollbackPreparedMap(){++generation;foreach(var entry in staged)Release(entry);staged.Clear();candidateRoot=null;candidateMapId=null;candidateMapInstanceId=candidateWorldId=0;}
        public void ReleaseCurrentMap(){++generation;committed=false;loading.Clear();foreach(var entry in active.Values)Release(entry);active.Clear();activeRoot=null;activeMapId=null;activeMapInstanceId=activeWorldId=0;}
        public void SetCurrentMapActive(bool value){foreach(var entry in active.Values)entry.Instance.SetActive(value);}

        public void ApplySnapshot(OnlineWorldSnapshot snapshot)
        {
            if(!committed||snapshot==null||snapshot.WorldId!=activeWorldId||snapshot.MapId!=activeMapId)return;
            var wanted=new HashSet<long>(snapshot.Corpses.Select(x=>x.CorpseId));
            foreach(long id in active.Keys.Where(x=>!wanted.Contains(x)).ToArray()){Release(active[id]);active.Remove(id);}
            foreach(var corpse in snapshot.Corpses)
            {
                if(active.TryGetValue(corpse.CorpseId,out var entry))
                {
                    if(corpse.StateRevision>=entry.State.StateRevision){entry.State=corpse;ApplyPose(entry);}
                }
                else if(!loading.ContainsKey(corpse.CorpseId)){int owner=generation;loading[corpse.CorpseId]=owner;LoadAndActivate(corpse,owner).Forget();}
            }
        }

        private async UniTaskVoid LoadAndActivate(OnlineWorldCorpseSnapshot corpse,int owner)
        {
            try
            {
                var entry=await Load(corpse,activeRoot,activeMapInstanceId,CancellationToken.None,owner);
                if(entry==null)return;
                OnlineWorldSnapshot snapshot=architecture.GetSystem<OnlineWorldSystem>()?.Snapshot;
                var latest=snapshot?.Corpses.FirstOrDefault(x=>x.CorpseId==corpse.CorpseId)??default;
                if(!committed||generation!=owner||latest.CorpseId==0||active.ContainsKey(corpse.CorpseId)){Release(entry);return;}
                entry.State=latest;ApplyPose(entry);entry.Instance.SetActive(true);active.Add(latest.CorpseId,entry);
            }
            finally{if(loading.TryGetValue(corpse.CorpseId,out int current)&&current==owner)loading.Remove(corpse.CorpseId);}
        }

        private async UniTask<Entry> Load(OnlineWorldCorpseSnapshot corpse,Transform parent,long mapInstanceId,CancellationToken token,int owner)
        {
            IRuntimeConfigProvider configs=architecture.GetUtility<IRuntimeConfigProvider>();string address=null;
            if(corpse.SourceKind==1&&configs.TryGetEnemy(corpse.SourceConfigId,out EnemyConfigDefinition enemy,out _))address=enemy.PrefabAddress;
            else if(corpse.SourceKind==2&&configs.TryGetPlayer(corpse.SourceConfigId,out PlayerConfigDefinition player,out _))address=player.PrefabAddress;
            else if(corpse.SourceKind==3&&configs.TryGetLoot(corpse.SourceConfigId,out LootConfigDefinition loot,out _))address=loot.PrefabAddress;
            if(string.IsNullOrWhiteSpace(address))return null;
            var completion=new UniTaskCompletionSource<GameObject>();ResourceLoadHandle handle=ResourceLoader.LoadAssetAsync<GameObject>(address,x=>completion.TrySetResult(x));GameObject prefab;
            try{using(token.Register(()=>completion.TrySetCanceled(token)))prefab=await completion.Task;}catch{if(handle.IsValid)ResourceLoader.Release(handle);return null;}
            if(prefab==null||disposed||token.IsCancellationRequested||parent==null||generation!=owner){if(handle.IsValid)ResourceLoader.Release(handle);return null;}
            GameObject instance=Object.Instantiate(prefab,parent);if(instance==null){if(handle.IsValid)ResourceLoader.Release(handle);return null;}instance.SetActive(false);
            foreach(var collider in instance.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            foreach(var nav in instance.GetComponentsInChildren<NavMeshAgent>(true))nav.enabled=false;
            var playerView=instance.GetComponent<PlayerView>();if(playerView!=null){playerView.enabled=false;}
            var enemyView=instance.GetComponent<EnemyView>();if(enemyView!=null){enemyView.MarkOnlineDead();enemyView.SetHealthBarVisible(false);enemyView.enabled=false;}
            var view=instance.AddComponent<OnlineCorpseView>();view.Configure(corpse.CorpseId,mapInstanceId);
            var entry=new Entry{State=corpse,Handle=handle,Instance=instance,View=view};ApplyPose(entry);return entry;
        }

        private void ApplyPose(Entry entry){if(entry.Instance==null||entry.Instance.transform.parent==null)return;entry.Instance.transform.localPosition=entry.State.LocalPosition;entry.Instance.transform.localRotation=Quaternion.Euler(0,entry.State.YawDegrees,0);}
        private void Release(Entry entry){if(entry==null||entry.Released)return;entry.Released=true;entry.View?.ClearOwnership();if(entry.Instance!=null)architecture.GetUtility<IViewInstanceProvider>()?.Release(entry.Instance);if(entry.Handle.IsValid)ResourceLoader.Release(entry.Handle);}
        public void Dispose(){if(disposed)return;disposed=true;RollbackPreparedMap();ReleaseCurrentMap();}
    }
}
