using QFramework;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>呈现建筑状态并提交触控旋转、确认和取消意图；不直接扣材料或改变建筑记录。</summary>
    public sealed class TemporaryBuildingHudUICtrl : BaseUICtrl
    {
        private TemporaryBuildingHudUI UI => BaseUI as TemporaryBuildingHudUI;
        /// <summary>本次按压的表现状态，仅在 HUD 可见时存在；短按 15 度，超过 0.35 秒后连续旋转。</summary>
        private bool rotationHeld;
        /// <summary>释放所在帧仍归旋转操作；避免 Button 松开回调与共享输入 Update 的顺序造成施工穿透。</summary>
        private int rotationReleasedFrame = -1;
        private bool suppressRotationClick;
        private float rotationHeldSeconds;
        private bool rotationUpdateRegistered;
        private const float RotationHoldDelay = .35f;
        private const float RotationDegreesPerSecond = 90f;

        public override UILayer Layer()
        {
            return UILayer.HUD;
        }

        public override bool BlocksGame => false;

        protected override string Path()
        {
            return "TemporaryBuilding/TemporaryBuildingHud";
        }

        protected override void OnInit()
        {
            UI.BtnRotate.onClick.AddListener(OnRotationClick);
            UI.RotatePress.Pressed += BeginRotationPress;
            UI.RotatePress.Released += EndRotationPress;
            UI.BtnCancel?.onClick.AddListener(() => this.GetSystem<TemporaryBuildingSystem>().CancelCurrentModeAndReturnToSelection());
            UI.BtnConfirm?.onClick.AddListener(() => {
                var system = this.GetSystem<TemporaryBuildingSystem>();
                if (system.Mode == TemporaryBuildingMode.BuildPreview) system.ConfirmBuild(out _);
                else if (system.Mode == TemporaryBuildingMode.DestroyMode) system.ConfirmDestroy(out _);
            });
            this.RegisterEvent<TemporaryBuildingChangedEvent>(_ => Refresh())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => Refresh())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            if (!rotationUpdateRegistered) { LifeCycle.RegisterUpdate(UpdateRotationPress); rotationUpdateRegistered = true; }
            Refresh();
        }

        protected override void OnHide()
        {
            ReleaseRotationPress();
        }

        protected override void OnUnload() { ReleaseRotationPress(); }

        private void BeginRotationPress()
        {
            rotationHeld = true;
            rotationReleasedFrame = -1;
            rotationHeldSeconds = 0f;
            suppressRotationClick = false;
        }

        private void EndRotationPress()
        {
            rotationHeld = false;
            rotationReleasedFrame = Time.frameCount;
        }

        /// <summary>建筑控制区优先消费鼠标；按压捕获后拖出旋转按钮仍归 UI，首帧判定不依赖 EventSystem 更新顺序。</summary>
        internal bool CapturesWorldPointer(Vector2 screenPosition)
        {
            if (!IsShow() || !UI.TouchControlsRoot.activeInHierarchy) return false;
            if (rotationHeld || rotationReleasedFrame == Time.frameCount) return true;
            var canvas = UI.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return ContainsPointer(UI.BtnRotate, screenPosition, camera) ||
                   ContainsPointer(UI.BtnConfirm, screenPosition, camera) ||
                   ContainsPointer(UI.BtnCancel, screenPosition, camera);
        }

        private static bool ContainsPointer(Button button, Vector2 position, Camera camera) =>
            button.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform, position, camera);

        private void OnRotationClick()
        {
            // 长按已连续调整，松开产生的 Button Click 不再额外追加一个步长。
            if (suppressRotationClick) { suppressRotationClick = false; return; }
            this.GetSystem<TemporaryBuildingSystem>().RotatePreview(15f);
        }

        private void UpdateRotationPress() { TickRotationPress(Time.unscaledDeltaTime); }

        /// <summary>共享生命周期驱动长按；只提交预览旋转意图，模式变化立即清理按压状态。</summary>
        private void TickRotationPress(float deltaTime)
        {
            if (!rotationHeld) return;
            var system = this.GetSystem<TemporaryBuildingSystem>();
            if (UI == null || !UI.RotatePress.isActiveAndEnabled || system.Mode != TemporaryBuildingMode.BuildPreview)
            {
                UI?.RotatePress.ResetCapture();
                rotationHeld = false;
                return;
            }
            var previous = rotationHeldSeconds;
            rotationHeldSeconds += Mathf.Max(0f, deltaTime);
            var continuousTime = Mathf.Max(0f, rotationHeldSeconds - RotationHoldDelay) - Mathf.Max(0f, previous - RotationHoldDelay);
            if (continuousTime <= 0f) return;
            suppressRotationClick = true;
            system.RotatePreview(continuousTime * RotationDegreesPerSecond);
        }

        private void ReleaseRotationPress()
        {
            UI?.RotatePress.ResetCapture();
            rotationHeld = false;
            rotationReleasedFrame = -1;
            rotationHeldSeconds = 0f;
            suppressRotationClick = false;
            if (rotationUpdateRegistered) { LifeCycle.UnregisterUpdate(UpdateRotationPress); rotationUpdateRegistered = false; }
        }

        private void Refresh()
        {
            if (UI == null)
            {
                return;
            }

            var snapshot = this.GetSystem<TemporaryBuildingSystem>().GetHudSnapshot();
            var mobile = this.GetSystem<MobileInputSystem>()?.IsMobileInputMode ?? false;
            if (UI.TouchControlsRoot != null)
            {
                UI.TouchControlsRoot.SetActive(snapshot.Mode != TemporaryBuildingMode.Normal && snapshot.Mode != TemporaryBuildingMode.OperatingManualAttack);
                UI.BtnRotate.gameObject.SetActive(snapshot.Mode == TemporaryBuildingMode.BuildPreview);
                UI.BtnConfirm.gameObject.SetActive(snapshot.Mode != TemporaryBuildingMode.BuildProgress);
                UI.BtnConfirm.interactable = snapshot.Mode == TemporaryBuildingMode.DestroyMode
                    ? snapshot.DestroyTargetBuildingId > 0 : snapshot.PlacementValid;
            }
            UI.TitleText.text = Loc.T("ui.temp_building.title");
            UI.ModeText.text = FormatMode(snapshot.Mode);
            UI.HintText.text = FormatHint(snapshot, mobile);
            UI.ResourceText.text = FormatResourceText(snapshot);
            UI.StatusText.text = FormatStatus(snapshot);
            if (UI.ProgressRoot != null)
            {
                UI.ProgressRoot.SetActive(false);
            }

            if (UI.ProgressSlider != null)
            {
                UI.ProgressSlider.value = snapshot.Progress01;
            }
        }

        private static string FormatMode(TemporaryBuildingMode mode)
        {
            switch (mode)
            {
                case TemporaryBuildingMode.BuildPreview: return Loc.T("ui.temp_building.mode.preview");
                case TemporaryBuildingMode.BuildProgress: return Loc.T("ui.temp_building.mode.progress");
                case TemporaryBuildingMode.OperatingManualAttack: return Loc.T("ui.temp_building.mode.operating");
                case TemporaryBuildingMode.DestroyMode: return Loc.T("ui.temp_building.mode.destroy");
                default: return Loc.T("ui.temp_building.mode.normal");
            }
        }

        private static string FormatHint(TemporaryBuildingHudSnapshot snapshot, bool mobile)
        {
            if (mobile && snapshot.Mode == TemporaryBuildingMode.BuildPreview) return Loc.T("ui.camp.hint.place");
            if (mobile && snapshot.Mode == TemporaryBuildingMode.DestroyMode) return Loc.T("ui.camp.hint.demolish");
            switch (snapshot.Mode)
            {
                case TemporaryBuildingMode.BuildPreview: return Loc.T("ui.temp_building.hint.preview");
                case TemporaryBuildingMode.BuildProgress: return Loc.T("ui.temp_building.hint.progress", snapshot.SelectedBuildingName);
                case TemporaryBuildingMode.OperatingManualAttack: return Loc.T("ui.temp_building.hint.operating");
                case TemporaryBuildingMode.DestroyMode: return Loc.T("ui.temp_building.hint.destroy");
                default: return string.Empty;
            }
        }

        private static string FormatStatus(TemporaryBuildingHudSnapshot snapshot)
        {
            if (snapshot.Mode == TemporaryBuildingMode.DestroyMode)
            {
                if (snapshot.DestroyTargetTooFar) return Loc.T("ui.camp.demolish.too_far");
                return snapshot.DestroyTargetBuildingId > 0
                    ? Loc.T("ui.camp.demolish.target", snapshot.SelectedBuildingName)
                    : Loc.T("ui.camp.demolish.no_target");
            }
            if (snapshot.Mode == TemporaryBuildingMode.BuildPreview && !snapshot.PlacementValid)
            {
                return string.IsNullOrEmpty(snapshot.Reason)
                    ? Loc.T("ui.temp_building.status.invalid")
                    : snapshot.Reason;
            }

            if (snapshot.Mode == TemporaryBuildingMode.OperatingManualAttack && snapshot.CooldownRemaining > 0f)
            {
                return Loc.T("ui.temp_building.status.cooldown", Mathf.CeilToInt(snapshot.CooldownRemaining));
            }

            return snapshot.Mode == TemporaryBuildingMode.Normal
                ? string.Empty
                : Loc.T("ui.temp_building.status.ready");
        }

        private static string FormatResourceText(TemporaryBuildingHudSnapshot snapshot)
        {
            if ((snapshot.Mode == TemporaryBuildingMode.BuildPreview ||
                 snapshot.Mode == TemporaryBuildingMode.BuildProgress) &&
                snapshot.BuildMaterials.Count > 0)
            {
                return string.Join("\n", snapshot.BuildMaterials.Select(FormatMaterial));
            }

            return snapshot.Mode == TemporaryBuildingMode.OperatingManualAttack
                ? Loc.T("ui.temp_building.arrows", snapshot.ArrowAmount)
                : string.Empty;
        }

        private static string FormatMaterial(TemporaryBuildingMaterialSnapshot material)
        {
            var color = material.IsEnough ? "#4CAF50" : "#F44336";
            return $"<color={color}>{material.Name}: {material.OwnedAmount}/{material.RequiredAmount}</color>";
        }
    }
}
