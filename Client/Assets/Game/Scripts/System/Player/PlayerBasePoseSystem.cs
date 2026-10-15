using System;
using QFramework;

namespace Game
{
    /// <summary>监听基地本地玩家移动，将局部姿态交给既有存档系统持久化。</summary>
    public sealed class PlayerBasePoseSystem : AbstractSystem
    {
        protected override void OnInit()
        {
            if (!this.GetSystem<SaveSystem>().RegisterSection(this.GetModel<PlayerBasePoseModel>(), out var error))
                Log.Error("[PlayerBasePoseSystem] 注册基地姿态存档分区失败：" + error);
            this.RegisterEvent<UnitChangedEvent>(OnUnitChanged);
        }

        private void OnUnitChanged(UnitChangedEvent evt)
        {
            if ((evt.ChangeMask & (UnitChangeMask.Position | UnitChangeMask.Rotation)) == 0 ||
                !string.Equals(this.GetModel<MapSceneModel>().CurrentMapId, MapIds.Base, StringComparison.Ordinal) ||
                evt.Current.UnitId != this.GetModel<PlayerModel>().PlayerUnitId || !evt.Current.IsActive) return;

            if (!TryCapturePose(evt.Current.UnitId, evt.Current.MapInstanceId, out var changed, out var error)) return;
            if (error != null)
            {
                Log.Error("[PlayerBasePoseSystem] 捕获基地玩家姿态失败：" + error);
                return;
            }
            if (changed) this.GetSystem<SaveSystem>().MarkDirty("player base pose changed");
        }

        /// <summary>在架构释放前同步读取当前基地玩家的表现姿态；安全跳过不会清除已有姿态。</summary>
        public bool TryCaptureCurrentBasePoseForExit(out bool changed, out string error)
        {
            changed = false;
            error = null;
            if (this.GetModel<SaveModel>().CurrentDomain != OfflineSaveDomain.Base ||
                !string.Equals(this.GetModel<MapSceneModel>().CurrentMapId, MapIds.Base, StringComparison.Ordinal))
            {
                return true;
            }

            var playerId = this.GetModel<PlayerModel>().PlayerUnitId;
            if (!this.GetSystem<UnitSystem>().TryGet(playerId, out var unit) || !unit.IsActive)
            {
                return true;
            }

            if (!TryCapturePose(unit.UnitId, unit.MapInstanceId, out changed, out error))
            {
                error ??= "当前基地玩家姿态不可用";
                return false;
            }

            if (error != null)
            {
                return false;
            }

            if (changed) this.GetSystem<SaveSystem>().MarkDirty("player base pose changed before application quit");
            return true;
        }

        private bool TryCapturePose(int unitId, long mapInstanceId, out bool changed, out string error)
        {
            changed = false;
            error = null;
            var provider = this.GetUtility<IPlayerRuntimePortProvider>();
            if (provider == null)
            {
                error = "当前玩家 运行时端口 不可用";
                return false;
            }

            if (!provider.TryGetCurrentPlayerMapLocalPose(out var pose, out var portError))
            {
                error = string.IsNullOrWhiteSpace(portError) ? "当前玩家地图本地姿态不可用" : portError;
                return false;
            }

            if (!pose.IsActive || pose.UnitId != unitId || pose.MapInstanceId != mapInstanceId)
            {
                error = "当前玩家地图本地姿态标识与活动 Unit 不匹配";
                return false;
            }

            return this.GetModel<PlayerBasePoseModel>().TrySet(pose.LocalPosition, pose.LocalYawDegrees, out changed, out error);
        }
    }
}
