using System;
using System.Collections.Generic;
using QFramework;

namespace Game
{
    /// <summary>建立、推进及绑定撤离任务；只在聚合完成时授予现有远征返回资格。</summary>
    public sealed class ExpeditionTaskSystem : AbstractSystem
    {
        protected override void OnInit()
        {
            if (!this.GetSystem<SaveSystem>().RegisterSection(this.GetModel<ExpeditionTaskModel>(), out var error)) throw new InvalidOperationException(error);
        }

        /// <summary>在远征模型已建立同一身份后原子绑定零项或一项任务；失败时不修改任务或资格。</summary>
        public bool TryBegin(string mapId, string expeditionId, IReadOnlyList<ExpeditionTaskDefinition> definitions, out string error)
        {
            error = string.Empty; definitions ??= Array.Empty<ExpeditionTaskDefinition>();
            var expedition = this.GetModel<ExpeditionModel>();
            if (string.IsNullOrWhiteSpace(mapId) || string.IsNullOrWhiteSpace(expeditionId) || definitions.Count > 1 || expedition.Status != ExpeditionStatus.Playing || expedition.CurrentMapId != mapId || expedition.CurrentExpeditionId != expeditionId || expedition.IsTaskManaged != (definitions.Count == 1))
            { error = "远征与撤离任务初始化身份不一致"; return false; }
            var model = this.GetModel<ExpeditionTaskModel>();
            if (definitions.Count == 0) { model.Replace(mapId, expeditionId, Array.Empty<ExpeditionTaskSnapshot>()); expedition.QualifyReturn(); return true; }
            var definition = definitions[0];
            if (definition == null || definition.MapId != mapId || string.IsNullOrWhiteSpace(definition.Id) || definition.TargetAmount <= 0f || float.IsNaN(definition.TargetAmount) || float.IsInfinity(definition.TargetAmount) || definition.Order <= 0)
            { error = "撤离任务定义无效"; return false; }
            model.Replace(mapId, expeditionId, new[] { new ExpeditionTaskSnapshot(expeditionId + ":" + definition.Id, definition.Id, mapId, expeditionId, definition.ObjectiveType, definition.TargetAmount, 0f, definition.Order, ExpeditionTaskStatus.InProgress) });
            return true;
        }

        /// <summary>只推进匹配的进行中生存任务；重复完成、无效增量或身份不一致均返回 false。</summary>
        public bool AdvanceSurvival(float gameMinutes) => Advance(global::TaskObjectiveType.Survive, gameMinutes);
        /// <summary>只推进匹配的进行中击杀任务一次；敌方与去重判定由 ExpeditionSystem 完成。</summary>
        public bool RecordEnemyKill() => Advance(global::TaskObjectiveType.Kill, 1f);

        private bool Advance(global::TaskObjectiveType type, float amount)
        {
            if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return false;
            var expedition = this.GetModel<ExpeditionModel>(); var model = this.GetModel<ExpeditionTaskModel>(); var task = model.StateTask;
            if (task == null || task.Status != ExpeditionTaskStatus.InProgress || task.ObjectiveType != type || expedition.Status != ExpeditionStatus.Playing || task.MapId != expedition.CurrentMapId || task.ExpeditionId != expedition.CurrentExpeditionId) return false;
            var current = UnityEngine.Mathf.Min(task.TargetAmount, task.CurrentAmount + amount); var status = current >= task.TargetAmount ? ExpeditionTaskStatus.Completed : ExpeditionTaskStatus.InProgress;
            model.Replace(model.MapId, model.ExpeditionId, new[] { new ExpeditionTaskSnapshot(task.InstanceId, task.TaskId, task.MapId, task.ExpeditionId, task.ObjectiveType, task.TargetAmount, current, task.Order, status) });
            if (status == ExpeditionTaskStatus.Completed) expedition.QualifyReturn();
            return true;
        }

        /// <summary>在分区和顶层远征恢复后交叉绑定；任何配置、身份或状态矛盾都保持输入状态并返回失败。</summary>
        public bool TryBindRestoredExpedition(ExpeditionSaveData data, out string error)
        {
            error = string.Empty; var expedition = this.GetModel<ExpeditionModel>(); var model = this.GetModel<ExpeditionTaskModel>();
            if (data == null || expedition.CurrentMapId != data.MapId || expedition.CurrentExpeditionId != data.ExpeditionId) { error = "恢复远征身份不一致"; return false; }
            if (data.TaskSchemaVersion == 0 && data.LegacySourceVersion is 1 or 2 && !data.Sections.ContainsKey(ExpeditionTaskModel.SaveSectionId))
                return MigrateLegacy(data, out error);
            if (data.TaskSchemaVersion != 1 || data.LegacySourceVersion != 0 || model.MapId != data.MapId || model.ExpeditionId != data.ExpeditionId || model.Tasks.Count > 1) { error = "撤离任务分区身份无效"; return false; }
            if (model.Tasks.Count == 1)
            {
                var task = model.Tasks[0];
                if (!this.GetUtility<IRuntimeConfigProvider>().TryGetExpeditionTasks(data.MapId, out var definitions, out error)) return false;
                if (definitions.Count != 1 || definitions[0].Id != task.TaskId || definitions[0].MapId != task.MapId || definitions[0].ObjectiveType != task.ObjectiveType)
                { error = "已保存撤离任务与当前地图配置不匹配"; return false; }
                var completed = task.Status == ExpeditionTaskStatus.Completed;
                if (data.Status == ExpeditionStatus.None || (data.Status == ExpeditionStatus.Playing && completed) || ((data.Status == ExpeditionStatus.ReturnQualified || data.Status == ExpeditionStatus.Returning) && !completed)) { error = "撤离任务结果与远征状态矛盾"; return false; }
            }
            else if (data.Status == ExpeditionStatus.Playing || data.Status == ExpeditionStatus.None) { error = "空任务远征状态无效"; return false; }
            expedition.RestoreTaskManagedState(model.Tasks.Count == 1);
            return true;
        }

        private bool MigrateLegacy(ExpeditionSaveData data, out string error)
        {
            error = string.Empty; var defs = Array.Empty<ExpeditionTaskDefinition>() as IReadOnlyList<ExpeditionTaskDefinition>;
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetExpeditionTasks(data.MapId, out defs, out error)) return false;
            if (defs.Count > 1) { error = "旧副本对应撤离任务配置数量无效"; return false; }
            ExpeditionTaskSnapshot task = null;
            if (defs.Count == 1)
            {
                var d = defs[0]; var target = d.ObjectiveType == global::TaskObjectiveType.Survive ? data.TargetGameMinutes : data.TaskTargetKills; var current = d.ObjectiveType == global::TaskObjectiveType.Survive ? data.ElapsedGameMinutes : data.TaskKillCount;
                if (target > 0f) task = new ExpeditionTaskSnapshot(data.ExpeditionId + ":" + d.Id, d.Id, data.MapId, data.ExpeditionId, d.ObjectiveType, target, UnityEngine.Mathf.Min(current, target), d.Order, current >= target ? ExpeditionTaskStatus.Completed : ExpeditionTaskStatus.InProgress);
            }
            this.GetModel<ExpeditionTaskModel>().Replace(data.MapId, data.ExpeditionId, task == null ? Array.Empty<ExpeditionTaskSnapshot>() : new[] { task });
            this.GetModel<ExpeditionModel>().RestoreTaskManagedState(task != null);
            if (data.Status == ExpeditionStatus.Playing && (task == null || task.Status == ExpeditionTaskStatus.Completed)) this.GetModel<ExpeditionModel>().QualifyReturn();
            return true;
        }

        /// <summary>由远征结束或事务清理调用，幂等清空任务分区；不发送事件且不单独修改顶层远征。</summary>
        public void Clear() => this.GetModel<ExpeditionTaskModel>().Reset();
    }
}
