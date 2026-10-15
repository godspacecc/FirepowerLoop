using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    public enum ExpeditionTaskStatus { InProgress = 0, Completed = 1 }

    /// <summary>对外只读的单轮撤离任务状态；实例身份在创建后保持稳定。</summary>
    public sealed class ExpeditionTaskSnapshot
    {
        public ExpeditionTaskSnapshot(string instanceId, string taskId, string mapId, string expeditionId, global::TaskObjectiveType objectiveType, float targetAmount, float currentAmount, int order, ExpeditionTaskStatus status)
        { InstanceId = instanceId; TaskId = taskId; MapId = mapId; ExpeditionId = expeditionId; ObjectiveType = objectiveType; TargetAmount = targetAmount; CurrentAmount = currentAmount; Order = order; Status = status; }
        public string InstanceId { get; }
        public string TaskId { get; }
        public string MapId { get; }
        public string ExpeditionId { get; }
        public global::TaskObjectiveType ObjectiveType { get; }
        public float TargetAmount { get; }
        public float CurrentAmount { get; }
        public int Order { get; }
        public ExpeditionTaskStatus Status { get; }
    }

    /// <summary>持有零项或一项撤离任务并负责分区序列化；事件推进与返回事务由系统层负责。</summary>
    public sealed class ExpeditionTaskModel : AbstractModel, IScopedSaveSectionProvider
    {
        public const string SaveSectionId = "expedition.tasks";
        private string mapId;
        private string expeditionId;
        private ExpeditionTaskSnapshot[] tasks = Array.Empty<ExpeditionTaskSnapshot>();

        /// <summary>固定的副本专属存档分区标识。</summary>
        public string SectionId => SaveSectionId;
        public SaveSectionScope Scope => SaveSectionScope.ExpeditionOnly;
        public string MapId => mapId;
        public string ExpeditionId => expeditionId;
        public IReadOnlyList<ExpeditionTaskSnapshot> Tasks => Array.AsReadOnly(tasks.Select(Clone).ToArray());
        protected override void OnInit() { }

        /// <summary>仅供任务系统在完整校验后一次替换绑定身份与任务快照。</summary>
        internal void Replace(string newMapId, string newExpeditionId, IEnumerable<ExpeditionTaskSnapshot> newTasks)
        { mapId = newMapId; expeditionId = newExpeditionId; tasks = (newTasks ?? Enumerable.Empty<ExpeditionTaskSnapshot>()).Select(Clone).ToArray(); }

        internal ExpeditionTaskSnapshot StateTask => tasks.Length == 1 ? tasks[0] : null;

        /// <summary>只读验证待恢复分区；null 专用于旧档迁移前置检查，不改变当前绑定。</summary>
        public bool Validate(JToken data, out string error)
        {
            error = string.Empty;
            if (data == null) return true;
            return TryParse(data, out _, out _, out _, out error);
        }

        /// <summary>捕获当前稳定绑定的深层新对象；未绑定时返回 null，让调用方保持旧标记并重试。</summary>
        public JToken Capture()
        {
            if (string.IsNullOrWhiteSpace(mapId) || string.IsNullOrWhiteSpace(expeditionId)) return null;
            return new JObject
            {
                ["schemaVersion"] = 1, ["mapId"] = mapId, ["expeditionId"] = expeditionId,
                ["tasks"] = new JArray(tasks.Select(task => new JObject
                {
                    ["instanceId"] = task.InstanceId, ["taskId"] = task.TaskId, ["mapId"] = task.MapId,
                    ["expeditionId"] = task.ExpeditionId, ["objectiveType"] = task.ObjectiveType.ToString(),
                    ["targetAmount"] = task.TargetAmount, ["currentAmount"] = task.CurrentAmount,
                    ["order"] = task.Order, ["status"] = task.Status.ToString()
                }))
            };
        }

        /// <summary>在存档提交或事务回滚时完整替换任务状态；null 幂等清空，非法非空数据抛错且不部分恢复。</summary>
        public void Restore(JToken data)
        {
            if (data == null) { Reset(); return; }
            if (!TryParse(data, out var parsedMapId, out var parsedExpeditionId, out var parsedTasks, out var error)) throw new InvalidOperationException(error);
            Replace(parsedMapId, parsedExpeditionId, parsedTasks);
        }

        /// <summary>结束远征或恢复空快照时幂等解除身份并清空任务，不修改顶层远征模型。</summary>
        public void Reset() { mapId = null; expeditionId = null; tasks = Array.Empty<ExpeditionTaskSnapshot>(); }

        private static bool TryParse(JToken data, out string parsedMapId, out string parsedExpeditionId, out ExpeditionTaskSnapshot[] parsedTasks, out string error)
        {
            parsedMapId = data?["mapId"]?.Value<string>(); parsedExpeditionId = data?["expeditionId"]?.Value<string>(); parsedTasks = Array.Empty<ExpeditionTaskSnapshot>(); error = string.Empty;
            if (data?.Type != JTokenType.Object || data["schemaVersion"]?.Type != JTokenType.Integer || data["schemaVersion"]?.Value<int>() != 1 || string.IsNullOrWhiteSpace(parsedMapId) || string.IsNullOrWhiteSpace(parsedExpeditionId) || !(data["tasks"] is JArray array) || array.Count > 1)
            { error = "撤离任务分区结构无效"; return false; }
            var result = new List<ExpeditionTaskSnapshot>();
            foreach (var token in array)
            {
                var instanceId = token?["instanceId"]?.Value<string>(); var taskId = token?["taskId"]?.Value<string>();
                var itemMapId = token?["mapId"]?.Value<string>(); var itemExpeditionId = token?["expeditionId"]?.Value<string>();
                var objectiveText = token?["objectiveType"]?.Value<string>();
                var statusText = token?["status"]?.Value<string>();
                if (token?.Type != JTokenType.Object || token["objectiveType"]?.Type != JTokenType.String || token["status"]?.Type != JTokenType.String ||
                    !Enum.GetNames(typeof(global::TaskObjectiveType)).Contains(objectiveText, StringComparer.Ordinal) ||
                    !Enum.GetNames(typeof(ExpeditionTaskStatus)).Contains(statusText, StringComparer.Ordinal) ||
                    !Enum.TryParse(token["objectiveType"].Value<string>(), false, out global::TaskObjectiveType objective) ||
                    !Enum.TryParse(token["status"].Value<string>(), false, out ExpeditionTaskStatus status)) { error = "撤离任务枚举无效"; return false; }
                if (!IsNumber(token["targetAmount"]) || !IsNumber(token["currentAmount"]) || token["order"]?.Type != JTokenType.Integer)
                { error = "撤离任务数值字段无效"; return false; }
                var target = token?["targetAmount"]?.Value<float>() ?? float.NaN; var current = token?["currentAmount"]?.Value<float>() ?? float.NaN; var order = token?["order"]?.Value<int>() ?? 0;
                if (string.IsNullOrWhiteSpace(taskId) || instanceId != parsedExpeditionId + ":" + taskId || itemMapId != parsedMapId || itemExpeditionId != parsedExpeditionId ||
                    (objective != global::TaskObjectiveType.Survive && objective != global::TaskObjectiveType.Kill) || !IsFinite(target) || target <= 0f || (objective == global::TaskObjectiveType.Kill && target != UnityEngine.Mathf.Floor(target)) || !IsFinite(current) || current < 0f || current > target || order <= 0 ||
                    status == ExpeditionTaskStatus.Completed != (current >= target)) { error = "撤离任务状态无效"; return false; }
                result.Add(new ExpeditionTaskSnapshot(instanceId, taskId, itemMapId, itemExpeditionId, objective, target, current, order, status));
            }
            parsedTasks = result.ToArray(); return true;
        }

        private static ExpeditionTaskSnapshot Clone(ExpeditionTaskSnapshot task) => new ExpeditionTaskSnapshot(task.InstanceId, task.TaskId, task.MapId, task.ExpeditionId, task.ObjectiveType, task.TargetAmount, task.CurrentAmount, task.Order, task.Status);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsNumber(JToken token) => token?.Type == JTokenType.Integer || token?.Type == JTokenType.Float;
    }
}
