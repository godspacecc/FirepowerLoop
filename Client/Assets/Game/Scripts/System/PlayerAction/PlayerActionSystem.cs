using QFramework;
using UnityEngine;

namespace Game
{
    public sealed class PlayerActionSystem : AbstractSystem
    {
        private long nextActionId;
        private bool hasAction;
        private PlayerActionSnapshot snapshot;

        protected override void OnInit()
        {
            LifeCycle.RegisterUpdate(OnUpdate);
            this.RegisterEvent<AppStateChangedEvent>(evt =>
            {
                if (evt.Current != AppState.Playing) CancelActive(PlayerActionCancelReason.PlayerUnavailable);
            });
            this.RegisterEvent<CombatantDiedEvent>(evt =>
            {
                if (evt.UnitId == PlayerModel.LocalPlayerUnitId) CancelActive(PlayerActionCancelReason.PlayerUnavailable);
            });
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => CancelActive(PlayerActionCancelReason.PlayerUnavailable));
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate);
            CancelActive(PlayerActionCancelReason.PlayerUnavailable);
        }

        public bool TryBegin(PlayerActionRequest request, out long actionId, out string error)
        {
            if(this.GetSystem<PlayerMoveSystem>()?.IsSwimming==true && ValleySwimmingRules.BlocksAction(request.Kind))
            {actionId=0;error="游泳时不能执行此动作";return false;}
            CancelActive(PlayerActionCancelReason.Replaced);
            actionId = 0;
            if (request.Kind == PlayerActionKind.None || float.IsNaN(request.DurationSeconds) || float.IsInfinity(request.DurationSeconds) || request.DurationSeconds <= 0f)
            {
                error = "玩家动作 请求 无效";
                return false;
            }

            actionId = ++nextActionId;
            snapshot = new PlayerActionSnapshot(actionId, request.Kind, 0f, request.DurationSeconds, 0f, request.IconAddress);
            hasAction = true;
            error = null;
            this.SendEvent(new PlayerActionChangedEvent(true, snapshot));
            return true;
        }

        public bool CancelActive(PlayerActionCancelReason reason)
        {
            if (!hasAction) return true;
            var ended = snapshot;
            hasAction = false;
            snapshot = default;
            this.SendEvent(new PlayerActionChangedEvent(false, default));
            this.SendEvent(new PlayerActionEndedEvent(ended.ActionId, ended.Kind, false, reason));
            return true;
        }

        public bool TryGetSnapshot(out PlayerActionSnapshot current)
        {
            current = snapshot;
            return hasAction;
        }

        public void TickForTests(float deltaSeconds)
        {
            Tick(Mathf.Max(0f, deltaSeconds));
        }

        private void OnUpdate()
        {
            Tick(Time.deltaTime);
        }

        private void Tick(float deltaSeconds)
        {
            if (!hasAction) return;
            if(this.GetSystem<PlayerMoveSystem>()?.IsSwimming==true && ValleySwimmingRules.BlocksAction(snapshot.Kind))
            {CancelActive(PlayerActionCancelReason.ContextInvalid);return;}
            var updated = new PlayerActionSnapshot(snapshot.ActionId, snapshot.Kind, Mathf.Min(snapshot.ElapsedSeconds + deltaSeconds, snapshot.DurationSeconds), snapshot.DurationSeconds, Mathf.Clamp01((snapshot.ElapsedSeconds + deltaSeconds) / snapshot.DurationSeconds), snapshot.IconAddress);
            snapshot = updated;
            if (updated.ElapsedSeconds + 0.0001f < updated.DurationSeconds)
            {
                this.SendEvent(new PlayerActionChangedEvent(true, updated));
                return;
            }

            hasAction = false;
            snapshot = default;
            this.SendEvent(new PlayerActionChangedEvent(false, default));
            this.SendEvent(new PlayerActionEndedEvent(updated.ActionId, updated.Kind, true, PlayerActionCancelReason.ExternalInterrupted));
        }
    }
}
