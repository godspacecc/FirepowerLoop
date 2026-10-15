using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class WeaponEquipmentSystem
    {
        /// <summary>仅节流存档脏通知，单位秒；不影响燃烧推进，退出仍捕获当前余量。</summary>
        private float torchSaveElapsed;
        private const float TorchSaveIntervalSeconds = 5f;
        /// <summary>点燃只改变当前合法手持实例；首次使用初始化配置余量，耗尽实例绝不补满。</summary>
        public bool TryToggleTorch(out string error)
        {
            error = null;
            if (!CanModifyEquipment(out error) || !ValidateCurrentSource()) return false;
            var model = this.GetModel<WeaponEquipmentModel>();
            if (!model.IsHoldingTorch || !model.HeldSource.HasValue ||
                !TryGetSource(model.HeldSource.Value, out var stack)) { error="当前未手持火把"; return false; }
            if (model.TorchLit) { model.TorchLit=false; MarkChanged("torch extinguished"); return true; }
            var config = this.GetUtility<IRuntimeConfigProvider>();
            if (!config.TryGetItem(stack.ItemId, out var item, out error)) return false;
            var remaining = stack.TorchRemainingSeconds ?? item.BurnSeconds;
            if (remaining <= 0f) { error="火把已燃尽"; return false; }
            stack.TorchRemainingSeconds=remaining;
            model.TorchLit=true;
            MarkChanged("torch ignited");
            return true;
        }

        private void UpdateTorch() => TickTorch(Time.deltaTime);

        /// <summary>共享生命周期消耗燃料；暂停、加载、联机与街机不推进，长帧最多消耗至零。</summary>
        internal void TickTorch(float deltaSeconds)
        {
            var model = this.GetModel<WeaponEquipmentModel>();
            if (!model.TorchLit || !model.IsHoldingTorch || !model.HeldSource.HasValue ||
                deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)) return;
            var map = this.GetModel<MapSceneModel>();
            if (map.Status.Value != MapLoadStatus.Loaded || map.CurrentMode != MapPreparationMode.OfflineFull ||
                this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing) return;
            if (!ValidateCurrentSource() || !TryGetSource(model.HeldSource.Value, out var stack))
            { model.TorchLit=false; return; }
            if (!stack.TorchRemainingSeconds.HasValue)
            {
                if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(stack.ItemId, out var item, out _)) return;
                stack.TorchRemainingSeconds=item.BurnSeconds;
            }
            stack.TorchRemainingSeconds=Mathf.Max(0f,stack.TorchRemainingSeconds.Value-deltaSeconds);
            // SaveSystem 的脏通知会重置防抖时间；逐帧标脏会让持续燃烧永远等不到自动保存。
            torchSaveElapsed += deltaSeconds;
            if (torchSaveElapsed >= TorchSaveIntervalSeconds)
            {
                torchSaveElapsed %= TorchSaveIntervalSeconds;
                this.GetSystem<SaveSystem>().MarkDirty("torch fuel burned");
            }
            if (stack.TorchRemainingSeconds.Value <= 0f)
            { model.TorchLit=false; MarkChanged("torch exhausted"); }
        }
    }
}
