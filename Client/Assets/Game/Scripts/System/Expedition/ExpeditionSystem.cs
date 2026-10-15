using System.Collections.Generic;
using QFramework;

namespace Game
{
    /// <summary>
    /// 推进远征计时并消费敌人死亡事件，维护任务击杀和返回资格变化。
    /// </summary>
    public sealed class ExpeditionSystem : AbstractSystem
    {
        private readonly HashSet<int> countedEnemyIds = new HashSet<int>();
        private int lastDisplayedMinute = -1;

        protected override void OnInit()
        {
            this.RegisterEvent<CombatantDiedEvent>(OnCombatantDied);
        }

        protected override void OnDeinit()
        {
            countedEnemyIds.Clear();
        }

        public void BeginSurvival(string mapId, float targetGameMinutes)
        {
            countedEnemyIds.Clear();
            lastDisplayedMinute = -1;
            this.GetModel<ExpeditionModel>().BeginSurvival(mapId, targetGameMinutes);
            this.SendEvent(new ExpeditionChangedEvent());
        }

        public void BeginTask(string mapId, int targetKills)
        {
            countedEnemyIds.Clear();
            lastDisplayedMinute = -1;
            this.GetModel<ExpeditionModel>().BeginTask(mapId, targetKills);
            this.SendEvent(new ExpeditionChangedEvent());
        }

        public void RecordEnemyKilled()
        {
            var expedition = this.GetModel<ExpeditionModel>();
            if (expedition.IsTaskManaged)
            {
                if (!this.GetSystem<ExpeditionTaskSystem>().RecordEnemyKill()) return;
                this.GetSystem<SaveSystem>().MarkDirty("expedition task progress");
            }
            else expedition.AddTaskKill();
            this.SendEvent(new ExpeditionChangedEvent());
        }

        /// <summary>
        /// 正式进图入口：先验证零项或一项定义，再建立同身份的顶层远征和任务状态，最终只发布一次变化事件。
        /// 任务初始化失败会同时恢复远征、任务、敌人去重集合和显示分钟，不发布事件也不遗留部分新状态。
        /// </summary>
        public bool TryBeginConfiguredExpedition(string mapId, string expeditionId, IReadOnlyList<ExpeditionTaskDefinition> definitions, out string error)
        {
            error = string.Empty; definitions ??= System.Array.Empty<ExpeditionTaskDefinition>();
            if (string.IsNullOrWhiteSpace(mapId) || string.IsNullOrWhiteSpace(expeditionId) || definitions.Count > 1)
            { error = "配置化远征参数无效"; return false; }
            var survivalTarget = 0f; var killTarget = 0;
            if (definitions.Count == 1)
            {
                var d = definitions[0];
                if (d == null || string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.NameKey) ||
                    d.MapId != mapId || d.TargetAmount <= 0f || float.IsNaN(d.TargetAmount) || float.IsInfinity(d.TargetAmount) || d.Order <= 0 ||
                    (d.ObjectiveType != global::TaskObjectiveType.Survive && d.ObjectiveType != global::TaskObjectiveType.Kill) ||
                    (d.ObjectiveType == global::TaskObjectiveType.Kill && d.TargetAmount != UnityEngine.Mathf.Floor(d.TargetAmount)))
                { error = "配置化撤离任务无效"; return false; }
                if (d.ObjectiveType == global::TaskObjectiveType.Survive) survivalTarget = d.TargetAmount; else killTarget = (int)d.TargetAmount;
            }
            var model = this.GetModel<ExpeditionModel>();
            var taskModel = this.GetModel<ExpeditionTaskModel>();
            var previousExpedition = model.IsActive
                ? model.CaptureSave(model.CurrentExpeditionId, System.DateTime.UtcNow)
                : null;
            var previousTask = taskModel.Capture()?.DeepClone();
            var previousTaskManaged = model.IsTaskManaged;
            var previousCountedEnemyIds = new HashSet<int>(countedEnemyIds);
            var previousDisplayedMinute = lastDisplayedMinute;
            countedEnemyIds.Clear(); lastDisplayedMinute = -1;
            model.BeginConfiguredExpedition(mapId, expeditionId, definitions.Count == 1, survivalTarget, killTarget);
            // 只在新局抽取，局部随机源不消耗地图生成的序列；存档恢复直接使用原起点。
            model.InitializeClock(ExpeditionClockRules.DrawStartMinuteOfDay(new System.Random(System.Guid.NewGuid().GetHashCode())));
            if (!this.GetSystem<ExpeditionTaskSystem>().TryBegin(mapId, expeditionId, definitions, out error))
            {
                taskModel.Restore(previousTask);
                if (previousExpedition == null) model.Clear();
                else
                {
                    model.TryRestoreSave(previousExpedition, out _);
                    model.RestoreTaskManagedState(previousTaskManaged);
                }
                countedEnemyIds.Clear();
                countedEnemyIds.UnionWith(previousCountedEnemyIds);
                lastDisplayedMinute = previousDisplayedMinute;
                return false;
            }
            this.SendEvent(new ExpeditionChangedEvent()); return true;
        }

        public void AdvanceGameMinutes(float gameMinutes)
        {
            var model = this.GetModel<ExpeditionModel>();
            var wasReturnQualified = model.CanReturn;
            var previousDisplayedMinute = UnityEngine.Mathf.FloorToInt(model.ElapsedGameMinutes);
            if (!model.AdvanceGameMinutes(gameMinutes))
            {
                return;
            }

            var taskChanged = model.IsTaskManaged && this.GetSystem<ExpeditionTaskSystem>().AdvanceSurvival(gameMinutes);
            var displayedMinute = UnityEngine.Mathf.FloorToInt(model.ElapsedGameMinutes);
            // Configured survival progress is persisted at the same integer-minute cadence shown by the HUD.
            // Legacy expeditions retain their previous first-update notification behavior.
            var displayedProgressChanged = model.IsTaskManaged
                ? displayedMinute != previousDisplayedMinute
                : displayedMinute != lastDisplayedMinute;
            if (displayedProgressChanged || wasReturnQualified != model.CanReturn)
            {
                lastDisplayedMinute = displayedMinute;
                if (taskChanged) this.GetSystem<SaveSystem>().MarkDirty("expedition task progress");
                this.SendEvent(new ExpeditionChangedEvent());
            }
        }

        public void Clear()
        {
            countedEnemyIds.Clear();
            this.GetModel<ExpeditionModel>().Clear();
            this.GetSystem<ExpeditionTaskSystem>()?.Clear();
            this.SendEvent(new ExpeditionChangedEvent());
        }

        public void FailCurrentExpedition()
        {
            countedEnemyIds.Clear();
            this.GetModel<ExpeditionModel>().Clear();
            this.GetSystem<ExpeditionTaskSystem>()?.Clear();
            this.SendEvent(new ExpeditionChangedEvent());
        }

        private void OnCombatantDied(CombatantDiedEvent evt)
        {
            if (this.GetModel<CombatModel>().TryGetCombatant(evt.UnitId, out var combatant) &&
                combatant.Faction == CombatFaction.Enemy &&
                countedEnemyIds.Add(evt.UnitId))
            {
                RecordEnemyKilled();
            }
        }
    }
}
