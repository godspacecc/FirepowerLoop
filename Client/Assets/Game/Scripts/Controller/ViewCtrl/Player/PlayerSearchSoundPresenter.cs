using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>表现本机搜索翻包及事务成功收取；不判定奖励、背包或存档，远端搜索须另有权威快照。</summary>
    public sealed class PlayerSearchSoundPresenter : IDisposable
    {
        private readonly Transform actor;
        private readonly IArchitecture architecture;
        private readonly List<IUnRegister> subscriptions = new List<IUnRegister>();
        private AudioPlaybackHandle rummage, collection;
        /// <summary>当前或等待事务提交的搜索动作身份；0 表示不能认领任何完成事件。</summary>
        private long actionId;
        private bool searching, disposed;
        public PlayerSearchSoundPresenter(Transform actor, IArchitecture architecture)
        {
            this.actor = actor; this.architecture = architecture;
            subscriptions.Add(architecture.RegisterEvent<PlayerActionChangedEvent>(OnChanged));
            subscriptions.Add(architecture.RegisterEvent<PlayerActionEndedEvent>(e =>
            {
                // 搜索事务同步发布结果后才返回结束事件；完成或取消都不保留过期身份。
                if (e.ActionId == actionId) Reset();
            }));
            subscriptions.Add(architecture.RegisterEvent<CivilisationSearchFinishedEvent>(OnFinished));
            subscriptions.Add(architecture.RegisterEvent<SearchItemCollectedEvent>(_ => PlayCollection()));
            subscriptions.Add(architecture.RegisterEvent<LootCollectedEvent>(e =>
            {
                if (e.CollectorUnitId == PlayerModel.LocalPlayerUnitId && e.AcceptedAmount > 0) PlayCollection();
            }));
            subscriptions.Add(architecture.RegisterEvent<AppStateChangedEvent>(e => { if (e.Current != AppState.Playing) Reset(); }));
            subscriptions.Add(architecture.RegisterEvent<MapCurrentReleasedEvent>(_ => Reset()));
        }
        private AudioSystem Audio => architecture.GetSystem<AudioSystem>();
        /// <summary>进度重复通知不重复加载；动作结束先停翻包，保留身份到本次同步事务结果。</summary>
        private void OnChanged(PlayerActionChangedEvent e)
        {
            if (disposed) return;
            if (!e.HasAction) { Audio?.Stop(rummage); rummage = AudioPlaybackHandle.Empty; searching = false; return; }
            if (e.Snapshot.Kind != PlayerActionKind.Search) { Reset(); return; }
            if (e.Snapshot.ActionId == actionId) return;
            Reset();
            if (actor == null || !actor.gameObject.activeInHierarchy ||
                architecture.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing) return;
            actionId = e.Snapshot.ActionId; searching = true;
            rummage = Audio.PlayLoopAt("FoleySearchBag", actor.position, 32f, -5, new SoundPlaybackOptions(0.32f, 16f, 0.25f));
        }
        /// <summary>只接受本动作的提交成功，取消、失败、重复及迟到结果不播放收取声。</summary>
        private void OnFinished(CivilisationSearchFinishedEvent e)
        {
            if (disposed || searching || actionId == 0 || e.ActionId != actionId) return;
            actionId = 0;
            if (e.Error == null) PlayCollection();
        }
        /// <summary>成功收取可在没有持续动作时发生；同一角色最多持有一个收取声，避免批量拾取叠加。</summary>
        private void PlayCollection()
        {
            if (disposed || actor == null || !actor.gameObject.activeInHierarchy ||
                architecture.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing) return;
            Audio.Stop(collection);
            collection = Audio.PlaySoundAt("FoleyCollectItem", actor.position, 32f, -5, new SoundPlaybackOptions(0.45f, 16f, 0.25f));
        }
        private void Reset()
        {
            Audio?.Stop(rummage); Audio?.Stop(collection);
            rummage = collection = AudioPlaybackHandle.Empty; actionId = 0; searching = false;
        }
        public void Dispose()
        {
            if (disposed) return;
            Reset(); disposed = true;
            foreach (var subscription in subscriptions) subscription.UnRegister();
            subscriptions.Clear();
        }
    }
}
