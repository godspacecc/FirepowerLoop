using System;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>
    /// 监听玩家死亡并编排失败远征回基地流程，失败时保留可重试状态。
    /// </summary>
    public sealed class CombatFlowSystem : AbstractSystem
    {
        private bool returningAfterDeath;
        private string lastFailureError;

        public string LastFailureError => lastFailureError;

        protected override void OnInit()
        {
            this.RegisterEvent<CombatantDiedEvent>(OnCombatantDied);
        }

        private void OnCombatantDied(CombatantDiedEvent evt)
        {
            if (this.GetModel<CombatModel>().TryGetCombatant(evt.UnitId, out var combatant) &&
                combatant.Faction == CombatFaction.Player)
            {
                HandlePlayerDiedAsync().Forget();
            }
        }

        private async UniTask HandlePlayerDiedAsync()
        {
            if (returningAfterDeath)
            {
                return;
            }

            var expedition = this.GetModel<ExpeditionModel>();
            if (!expedition.IsActive)
            {
                return;
            }

            returningAfterDeath = true;
            try
            {
                expedition.MarkDefeated();
                try
                {
                    this.SendEvent(new ExpeditionChangedEvent());
                }
                catch (Exception exception)
                {
                    Log.Error($"[CombatFlowSystem] 玩家死亡后 ExpeditionChangedEvent 处理失败：{exception}");
                }

                var result = await this.GetSystem<ExpeditionFlowSystem>()
                    .FailExpeditionAndReturnToBaseAsync();
                if (result.Success)
                {
                    lastFailureError = string.Empty;
                }
                else
                {
                    lastFailureError = result.Error;
                    Log.Error($"[CombatFlowSystem] 失败回基地未完成：{result.Error}");
                }
            }
            catch (Exception exception)
            {
                lastFailureError = $"{exception.GetType().Name}: {exception.Message}";
                Log.Error($"[CombatFlowSystem] 玩家死亡返回意外失败：{exception}");
            }
            finally
            {
                returningAfterDeath = false;
            }
        }
    }
}
