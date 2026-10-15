using System;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 为已存在的本地玩家提交测试属性修改，复用战斗、生存和存档事务及异常回滚。
    /// 不以暂停、地图、远征阶段或存活状态限制调试意图，不负责创建玩家或撤销远征结算。
    /// </summary>
    public sealed class PlayerAttributeSystem : AbstractSystem
    {
        protected override void OnInit()
        {
            if (!this.GetSystem<SaveSystem>()
                    .RegisterSection(this.GetModel<PlayerAttributeModel>(), out var error))
            {
                Log.Error($"[PlayerAttributeSystem] 玩家属性存档分区注册失败：{error}");
            }
        }

        internal PlayerAttributeFailureKind LastFailureKind { get; private set; }

        public int EffectiveMaxHP => GetPlayerConfig(out var config, out _) && config.MaxHP > 0
            ? this.GetModel<PlayerAttributeModel>().MaxHPOverride ?? config.MaxHP
            : this.GetModel<CombatModel>().PlayerMaxHP;

        public float EffectiveMoveSpeed => GetPlayerConfig(out var config, out _) && config.MoveSpeed > 0f
            ? this.GetModel<PlayerAttributeModel>().MoveSpeedOverride ?? config.MoveSpeed
            : 0f;

        public bool TrySetHP(int value, out PlayerAttributeChange change, out string error)
        {
            change = default;
            LastFailureKind = PlayerAttributeFailureKind.None;
            if (!CanSet(out error))
            {
                LastFailureKind = PlayerAttributeFailureKind.StateRejected;
                return false;
            }

            var snapshot = this.GetSystem<SaveSystem>().CaptureRuntimeSnapshot();
            try
            {
                if (!this.GetSystem<CombatSystem>().TrySetPlayerHP(value, out var before, out var after, out error))
                {
                    LastFailureKind = PlayerAttributeFailureKind.OutOfRange;
                    return false;
                }

                change = new PlayerAttributeChange("hp", before, after, before != after);
                return true;
            }
            catch (Exception e)
            {
                Rollback(snapshot, e, out error);
                return false;
            }
        }

        public bool TrySetPower(float value, out PlayerAttributeChange change, out string error)
        {
            change = default;
            LastFailureKind = PlayerAttributeFailureKind.None;
            if (!CanSet(out error))
            {
                LastFailureKind = PlayerAttributeFailureKind.StateRejected;
                return false;
            }

            var snapshot = this.GetSystem<SaveSystem>().CaptureRuntimeSnapshot();
            try
            {
                if (!this.GetSystem<PlayerConditionSystem>().TrySetPower(value, out var before, out var after, out error))
                {
                    LastFailureKind = PlayerAttributeFailureKind.OutOfRange;
                    return false;
                }

                change = new PlayerAttributeChange("power", before, after, !Mathf.Approximately(before, after));
                return true;
            }
            catch (Exception e)
            {
                Rollback(snapshot, e, out error);
                return false;
            }
        }

        public bool TrySetTili(float value, out PlayerAttributeChange change, out string error)
        {
            change = default;
            LastFailureKind = PlayerAttributeFailureKind.None;
            if (!CanSet(out error))
            {
                LastFailureKind = PlayerAttributeFailureKind.StateRejected;
                return false;
            }

            var snapshot = this.GetSystem<SaveSystem>().CaptureRuntimeSnapshot();
            try
            {
                if (!this.GetSystem<PlayerConditionSystem>().TrySetTili(value, out var before, out var after, out error))
                {
                    LastFailureKind = PlayerAttributeFailureKind.OutOfRange;
                    return false;
                }

                change = new PlayerAttributeChange("tili", before, after, !Mathf.Approximately(before, after));
                return true;
            }
            catch (Exception e)
            {
                Rollback(snapshot, e, out error);
                return false;
            }
        }

        public bool TrySetWater(float value, out PlayerAttributeChange change, out string error)
        {
            return TrySetConditionValue(
                value,
                "water",
                (PlayerConditionSystem system, float requested, out float before, out float after, out string innerError) =>
                    system.TrySetWater(requested, out before, out after, out innerError),
                out change,
                out error);
        }

        public bool TrySetMaxWater(float value, out PlayerAttributeChange change, out string error)
        {
            return TrySetConditionValue(
                value,
                "maxwater",
                (PlayerConditionSystem system, float requested, out float before, out float after, out string innerError) =>
                    system.TrySetMaxWater(requested, out before, out after, out innerError),
                out change,
                out error);
        }

        public bool TrySetTemperature(float value, out PlayerAttributeChange change, out string error)
        {
            return TrySetConditionValue(
                value,
                "temperature",
                (PlayerConditionSystem system, float requested, out float before, out float after, out string innerError) =>
                    system.TrySetTemperature(requested, out before, out after, out innerError),
                out change,
                out error);
        }

        public bool TrySetMaxHP(int value, out PlayerAttributeChange change, out string error)
        {
            change = default;
            LastFailureKind = PlayerAttributeFailureKind.None;
            if (!CanSet(out error))
            {
                LastFailureKind = PlayerAttributeFailureKind.StateRejected;
                return false;
            }

            if (value <= 0)
            {
                error = "最大生命值 必须为正数";
                LastFailureKind = PlayerAttributeFailureKind.OutOfRange;
                return false;
            }

            var snapshot = this.GetSystem<SaveSystem>().CaptureRuntimeSnapshot();
            try
            {
                var before = EffectiveMaxHP;
                if (before == value)
                {
                    change = new PlayerAttributeChange("maxhp", before, before, false);
                    error = null;
                    return true;
                }

                var model = this.GetModel<PlayerAttributeModel>();
                if (!this.GetSystem<CombatSystem>().TryApplyPlayerMaxHP(value, out _, out var after, out error))
                {
                    LastFailureKind = PlayerAttributeFailureKind.OutOfRange;
                    return false;
                }

                model.SetMaxHPOverride(value);
                this.GetSystem<SaveSystem>().MarkDirty("player maxhp override changed");

                change = new PlayerAttributeChange("maxhp", before, after, before != after);
                return true;
            }
            catch (Exception e)
            {
                Rollback(snapshot, e, out error);
                return false;
            }
        }

        public bool TrySetMoveSpeed(float value, out PlayerAttributeChange change, out string error)
        {
            change = default;
            LastFailureKind = PlayerAttributeFailureKind.None;
            if (!CanSet(out error))
            {
                LastFailureKind = PlayerAttributeFailureKind.StateRejected;
                return false;
            }

            if (!IsFinitePositive(value))
            {
                error = "移动速度 必须为正数";
                LastFailureKind = PlayerAttributeFailureKind.OutOfRange;
                return false;
            }

            var snapshot = this.GetSystem<SaveSystem>().CaptureRuntimeSnapshot();
            var moveSystem = this.GetSystem<PlayerMoveSystem>();
            var beforeMoveSpeedCache = moveSystem.MoveSpeedCache;
            try
            {
                var before = EffectiveMoveSpeed;
                if (Mathf.Approximately(before, value))
                {
                    change = new PlayerAttributeChange("movespeed", before, before, false);
                    error = null;
                    return true;
                }

                var model = this.GetModel<PlayerAttributeModel>();
                model.SetMoveSpeedOverride(value);
                this.GetSystem<SaveSystem>().MarkDirty("player movespeed override changed");
                this.SendEvent(new PlayerMoveSpeedChangedEvent(value));

                change = new PlayerAttributeChange("movespeed", before, value, !Mathf.Approximately(before, value));
                error = null;
                return true;
            }
            catch (Exception e)
            {
                Rollback(snapshot, e, moveSystem, beforeMoveSpeedCache, out error);
                return false;
            }
        }

        /// <summary>写入前确认本地玩家记录仍存在；停用或死亡不等同于玩家已移除。</summary>
        private bool CanSet(out string error)
        {
            if (!this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out _))
            {
                error = "本地玩家不存在，无法设置属性";
                return false;
            }
            error = null;
            return true;
        }

        private delegate bool ConditionSetter(
            PlayerConditionSystem system,
            float value,
            out float before,
            out float after,
            out string error);

        private bool TrySetConditionValue(
            float value,
            string attributeId,
            ConditionSetter setter,
            out PlayerAttributeChange change,
            out string error)
        {
            change = default;
            LastFailureKind = PlayerAttributeFailureKind.None;
            if (!CanSet(out error))
            {
                LastFailureKind = PlayerAttributeFailureKind.StateRejected;
                return false;
            }

            var snapshot = this.GetSystem<SaveSystem>().CaptureRuntimeSnapshot();
            try
            {
                if (!setter(this.GetSystem<PlayerConditionSystem>(), value, out var before, out var after, out error))
                {
                    LastFailureKind = PlayerAttributeFailureKind.OutOfRange;
                    return false;
                }

                change = new PlayerAttributeChange(attributeId, before, after, !Mathf.Approximately(before, after));
                return true;
            }
            catch (Exception e)
            {
                Rollback(snapshot, e, out error);
                return false;
            }
        }

        private bool GetPlayerConfig(out PlayerConfigDefinition config, out string error)
        {
            config = default;
            if (!this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) ||
                string.IsNullOrWhiteSpace(player.ConfigId))
            {
                error = "缺少活动玩家配置";
                return false;
            }

            return this.GetUtility<IRuntimeConfigProvider>().TryGetPlayer(player.ConfigId, out config, out error);
        }

        private void Rollback(SaveRuntimeSnapshot snapshot, Exception exception, out string error)
        {
            LastFailureKind = PlayerAttributeFailureKind.ExecutionFailed;
            if (!this.GetSystem<SaveSystem>().RestoreRuntimeSnapshot(snapshot, out var rollbackError))
            {
                error = $"属性设置失败：{exception.Message}；回滚失败：{rollbackError}";
                Log.Error($"运行时操作失败：[PlayerAttributeSystem] {error}");
                return;
            }

            error = $"属性设置失败：{exception.Message}";
            Log.Error($"运行时操作失败：[PlayerAttributeSystem] {error}");
        }

        private void Rollback(
            SaveRuntimeSnapshot snapshot,
            Exception exception,
            PlayerMoveSystem moveSystem,
            float moveSpeedCache,
            out string error)
        {
            LastFailureKind = PlayerAttributeFailureKind.ExecutionFailed;
            var restoredSave = this.GetSystem<SaveSystem>().RestoreRuntimeSnapshot(snapshot, out var rollbackError);
            var restoredMoveCache = true;
            var moveCacheError = string.Empty;

            try
            {
                moveSystem.RestoreMoveSpeedCache(moveSpeedCache);
            }
            catch (Exception e)
            {
                restoredMoveCache = false;
                moveCacheError = e.Message;
            }

            if (!restoredSave || !restoredMoveCache)
            {
                var saveText = restoredSave ? "存档回滚成功" : $"存档回滚失败：{rollbackError}";
                var moveText = restoredMoveCache ? "移动缓存回滚成功" : $"移动缓存回滚失败：{moveCacheError}";
                error = $"属性设置失败：{exception.Message}；{saveText}；{moveText}";
                Log.Error($"运行时操作失败：[PlayerAttributeSystem] {error}");
                return;
            }

            error = $"属性设置失败：{exception.Message}";
            Log.Error($"运行时操作失败：[PlayerAttributeSystem] {error}");
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   value > 0f;
        }
    }
}
