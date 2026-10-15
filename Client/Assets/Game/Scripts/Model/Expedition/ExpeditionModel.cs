using QFramework;
using System;

namespace Game
{
    /// <summary>
    /// 表示远征生命周期状态，包括进行中、可返回、返回中和失败待恢复。
    /// </summary>
    public enum ExpeditionStatus
    {
        None,
        Playing,
        ReturnQualified,
        Returning,
        Defeated
    }

    /// <summary>
    /// 保存当前远征地图、计时、任务击杀、临时物资和返回资格等运行时状态。
    /// </summary>
    public sealed class ExpeditionModel : AbstractModel
    {
        public string CurrentMapId { get; private set; }
        public string CurrentExpeditionId { get; private set; }
        public float ElapsedGameMinutes { get; private set; }
        /// <summary>本次出发的一天内分钟；旧存档为零，新局选取后保存，恢复时不再抽样。</summary>
        public float StartMinuteOfDay { get; private set; }
        /// <summary>0 表示旧午夜起点，1 表示显式保存起点；不影响累计耗时。</summary>
        public int ClockSchemaVersion { get; private set; }
        public float ClockGameMinutes => StartMinuteOfDay + ElapsedGameMinutes;
        /// <summary>当局生成清单的权威副本；null 表示旧固定布局，仅在新局提交或读档恢复时安装。</summary>
        public ValleyLayoutSaveData ValleyLayout { get; private set; }
        internal void InstallValleyLayout(ValleyLayoutSaveData layout)
        {
            if (layout != null && (CurrentMapId != layout.MapId || !ValleyLayoutValidator.TryValidateData(layout, out _)))
                throw new InvalidOperationException("远征与河谷生成清单不匹配");
            ValleyLayout = layout?.Clone();
        }
        public float TargetGameMinutes { get; private set; }
        public int TaskTargetKills { get; private set; }
        public int TaskKillCount { get; private set; }
        public ExpeditionStatus Status { get; private set; } = ExpeditionStatus.None;
        public bool IsTaskManaged { get; private set; }
        public bool CanReturn => Status == ExpeditionStatus.ReturnQualified;
        public bool IsActive => Status == ExpeditionStatus.Playing ||
                                Status == ExpeditionStatus.ReturnQualified ||
                                Status == ExpeditionStatus.Returning ||
                                Status == ExpeditionStatus.Defeated;

        protected override void OnInit()
        {
        }

        public void BeginSurvival(string mapId, float targetGameMinutes)
        {
            StartMinuteOfDay = 0; ClockSchemaVersion = 0; ValleyLayout = null;
            IsTaskManaged = false;
            CurrentMapId = mapId;
            TargetGameMinutes = targetGameMinutes;
            TaskTargetKills = 0;
            TaskKillCount = 0;
            ElapsedGameMinutes = 0f;
            Status = ExpeditionStatus.Playing;
        }

        public void BeginTask(string mapId, int targetKills)
        {
            StartMinuteOfDay = 0; ClockSchemaVersion = 0; ValleyLayout = null;
            IsTaskManaged = false;
            CurrentMapId = mapId;
            TargetGameMinutes = 0f;
            TaskTargetKills = targetKills < 0 ? 0 : targetKills;
            TaskKillCount = 0;
            ElapsedGameMinutes = 0f;
            Status = ExpeditionStatus.Playing;
            if (TaskTargetKills == 0)
            {
                QualifyReturn();
            }
        }

        public void SetExpeditionId(string expeditionId) => CurrentExpeditionId = expeditionId;

        /// <summary>由统一入口一次建立配置化远征；兼容目标只用于首份存档和旧档迁移。</summary>
        public void BeginConfiguredExpedition(string mapId, string expeditionId, bool isTaskManaged, float initialSurvivalTargetGameMinutes, int initialTaskTargetKills)
        {
            StartMinuteOfDay = 0; ClockSchemaVersion = 0; ValleyLayout = null;
            CurrentMapId = mapId; CurrentExpeditionId = expeditionId; IsTaskManaged = isTaskManaged;
            TargetGameMinutes = initialSurvivalTargetGameMinutes; TaskTargetKills = initialTaskTargetKills;
            TaskKillCount = 0; ElapsedGameMinutes = 0f; Status = ExpeditionStatus.Playing;
        }

        /// <summary>新远征建立时设置一次白天起点；恢复存档不能调用此入口。</summary>
        internal void InitializeClock(int startMinuteOfDay)
        {
            if (!IsActive || ElapsedGameMinutes != 0 || ClockSchemaVersion != 0 || startMinuteOfDay < 480 || startMinuteOfDay > 720)
                throw new InvalidOperationException("白天起点只能在新远征建立时设置一次");
            StartMinuteOfDay = startMinuteOfDay; ClockSchemaVersion = 1;
        }

        /// <summary>旧公开恢复接缝：在任务分区成功绑定后将当前远征标记为任务托管；重复调用保持 true。</summary>
        public void RestoreTaskManagedState() => IsTaskManaged = true;
        /// <summary>事务恢复专用接缝，精确还原快照中的托管值，不推导或修改其它远征字段。</summary>
        internal void RestoreTaskManagedState(bool value) => IsTaskManaged = value;

        public bool AdvanceGameMinutes(float gameMinutes)
        {
            if (float.IsNaN(gameMinutes) ||
                float.IsInfinity(gameMinutes) ||
                gameMinutes <= 0f ||
                Status != ExpeditionStatus.Playing &&
                Status != ExpeditionStatus.ReturnQualified)
            {
                return false;
            }

            ElapsedGameMinutes += gameMinutes;
            if (!IsTaskManaged && TaskTargetKills == 0 &&
                TargetGameMinutes > 0f &&
                Status == ExpeditionStatus.Playing &&
                ElapsedGameMinutes >= TargetGameMinutes)
            {
                QualifyReturn();
            }

            return true;
        }

        public void AddTaskKill()
        {
            if (IsTaskManaged || Status != ExpeditionStatus.Playing || TaskTargetKills <= 0)
            {
                return;
            }

            TaskKillCount++;
            if (TaskKillCount >= TaskTargetKills)
            {
                QualifyReturn();
            }
        }

        public void QualifyReturn()
        {
            if (Status == ExpeditionStatus.Playing)
            {
                Status = ExpeditionStatus.ReturnQualified;
            }
        }

        public void BeginReturning()
        {
            if (CanReturn)
            {
                Status = ExpeditionStatus.Returning;
            }
        }

        public void RestoreReturnQualified()
        {
            if (Status == ExpeditionStatus.Returning)
            {
                Status = ExpeditionStatus.ReturnQualified;
            }
        }

        public void MarkDefeated()
        {
            if (IsActive)
            {
                Status = ExpeditionStatus.Defeated;
            }
        }

        public void Clear()
        {
            StartMinuteOfDay = 0; ClockSchemaVersion = 0; ValleyLayout = null;
            CurrentMapId = null;
            CurrentExpeditionId = null;
            ElapsedGameMinutes = 0f;
            TargetGameMinutes = 0f;
            TaskTargetKills = 0;
            TaskKillCount = 0;
            Status = ExpeditionStatus.None;
            IsTaskManaged = false;
        }

        public ExpeditionSaveData CaptureSave(string expeditionId, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(expeditionId)) throw new ArgumentException("副本 ID 不能为空", nameof(expeditionId));
            return new ExpeditionSaveData
            {
                Version = ExpeditionSaveData.CurrentVersion,
                TaskSchemaVersion = 1,
                LegacySourceVersion = 0,
                ExpeditionId = expeditionId,
                MapId = CurrentMapId,
                UpdatedAtUtc = utcNow.ToUniversalTime().ToString("O"),
                ElapsedGameMinutes = ElapsedGameMinutes,
                StartMinuteOfDay = StartMinuteOfDay,
                ClockSchemaVersion = ClockSchemaVersion,
                ValleyLayout = ValleyLayout?.Clone(),
                TargetGameMinutes = TargetGameMinutes,
                TaskTargetKills = TaskTargetKills,
                TaskKillCount = TaskKillCount,
                Status = Status,
                Sections = new System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>(StringComparer.Ordinal),
                World = new ExpeditionWorldSaveData()
            };
        }

        public bool TryRestoreSave(ExpeditionSaveData data, out string error)
        {
            error = null;
            if (data == null || data.Version != ExpeditionSaveData.CurrentVersion || string.IsNullOrWhiteSpace(data.ExpeditionId) || string.IsNullOrWhiteSpace(data.MapId))
            { error = "副本存档无效"; return false; }
            if (!ExpeditionClockRules.TryValidate(data.ClockSchemaVersion, data.StartMinuteOfDay, out error)) return false;
            if (data.ValleyLayout != null && (data.MapId != data.ValleyLayout.MapId || !ValleyLayoutValidator.TryValidateData(data.ValleyLayout, out error)))
            { error ??= "副本与河谷布局身份不匹配"; return false; }
            if (float.IsNaN(data.ElapsedGameMinutes) || float.IsInfinity(data.ElapsedGameMinutes) || data.ElapsedGameMinutes < 0f ||
                float.IsNaN(data.TargetGameMinutes) || float.IsInfinity(data.TargetGameMinutes) || data.TargetGameMinutes < 0f ||
                data.TaskTargetKills < 0 || data.TaskKillCount < 0 || data.TaskKillCount > data.TaskTargetKills)
            { error = "副本玩法状态无效"; return false; }
            CurrentMapId = data.MapId;
            StartMinuteOfDay = data.StartMinuteOfDay;
            ClockSchemaVersion = data.ClockSchemaVersion;
            ValleyLayout = data.ValleyLayout?.Clone();
            ElapsedGameMinutes = data.ElapsedGameMinutes;
            CurrentExpeditionId = data.ExpeditionId;
            TargetGameMinutes = data.TargetGameMinutes;
            TaskTargetKills = data.TaskTargetKills;
            TaskKillCount = data.TaskKillCount;
            // 任务分区完成交叉绑定前保持未托管，避免读取半恢复状态。
            IsTaskManaged = false;
            Status = data.Status == ExpeditionStatus.Returning || data.Status == ExpeditionStatus.Defeated || data.Status == ExpeditionStatus.ReturnQualified || data.Status == ExpeditionStatus.Playing
                ? data.Status : ExpeditionStatus.Playing;
            return true;
        }
    }
}
