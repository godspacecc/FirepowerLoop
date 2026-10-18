using System.Linq;
using QFramework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>全屏实尺编辑本机触屏草稿；使用正式HUD的动作和共享几何，不发送玩法输入。</summary>
    public sealed class MobileControlLayoutUICtrl : BaseUICtrl
    {
        private MobileControlLayoutUI UI => (MobileControlLayoutUI)BaseUI;
        /// <summary>可见期间的独立草稿；保存成功前不写入权威设备设置。</summary>
        private MobileControlLayoutData draft;
        private bool moveSelected = true;
        private MobileLayoutButton? buttonSelected;
        private bool firstPersonPreview;
        private bool trial;
        private bool updateRegistered;
        private Vector2 lastAreaSize;
        /// <summary>保存两个图标按钮的Prefab底色，结束选中后恢复与实战一致的显示。</summary>
        private Color pauseButtonColor, bombButtonColor;
        private MobileStickLayoutData Selected => moveSelected ? draft.Move : draft.Aim;
        protected override string Path() => "Home/MobileControlLayout";
        public override UILayer Layer() => UILayer.Dialog;

        protected override void OnInit()
        {
            UI.MoveButton.onClick.AddListener(() => Select(true));
            UI.AimButton.onClick.AddListener(() => Select(false));
            UI.MovePreview.Selected = () => { if (!trial) Select(true); };
            UI.AimPreview.Selected = () => { if (!trial) Select(false); };
            UI.MovePreview.Dragged = point => DragStick(true, point);
            UI.AimPreview.Dragged = point => DragStick(false, point);
            UI.ModeButton.onClick.AddListener(() =>
            { firstPersonPreview = !firstPersonPreview; buttonSelected = null; moveSelected = true; SetTrial(false); });
            var buttons = ButtonPreviews();
            pauseButtonColor = UI.PausePreview.GetComponent<UnityEngine.UI.Image>().color;
            bombButtonColor = UI.BombPreview.GetComponent<UnityEngine.UI.Image>().color;
            for (var i = 0; i < buttons.Length; i++) BindButton(buttons[i], (MobileLayoutButton)i);
            UI.SizeSlider.onValueChanged.AddListener(value =>
            { if (!trial && !buttonSelected.HasValue) { Selected.Scale = value; Render(); } });
            UI.OpacitySlider.onValueChanged.AddListener(value =>
            { if (!trial && !buttonSelected.HasValue) { Selected.Opacity = value; Render(); } });
            UI.TrialButton.onClick.AddListener(() => SetTrial(!trial));
            UI.ResetButton.onClick.AddListener(() => { draft = new MobileControlLayoutData(); SetTrial(false); });
            UI.CancelButton.onClick.AddListener(Hide);
            UI.SaveButton.onClick.AddListener(Save);
            UI.CollapseToolbarButton.onClick.AddListener(() => SetToolbarExpanded(false));
            UI.ExpandToolbarButton.onClick.AddListener(() => SetToolbarExpanded(true));
            this.RegisterEvent<LanguageChangedEvent>(_ => { if (IsShow()) Render(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MobileInputModeChangedEvent>(_ =>
            { if (IsShow() && !this.GetSystem<MobileInputSystem>().IsMobileInputMode) Hide(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            if (!this.GetSystem<MobileInputSystem>().IsMobileInputMode) { Hide(); return; }
            draft = this.GetModel<MobileControlLayoutModel>().ToData();
            moveSelected = true; buttonSelected = null; firstPersonPreview = false;
            SetToolbarExpanded(true);
            Canvas.ForceUpdateCanvases(); SetTrial(false);
            LifeCycle.RegisterUpdate(RefreshOnResize); updateRegistered = true;
        }
        protected override void OnHide()
        { ReleaseUpdate(); ResetGestures(); EventSystem.current?.SetSelectedGameObject(null); draft = null; }
        protected override void OnUnload() { ReleaseUpdate(); draft = null; }
        private void ReleaseUpdate()
        { if (updateRegistered) { LifeCycle.UnregisterUpdate(RefreshOnResize); updateRegistered = false; } }
        private void RefreshOnResize()
        { if (UI.ControlSafeArea.rect.size != lastAreaSize) { ResetGestures(); Render(); } }
        private void Select(bool move) { moveSelected = move; buttonSelected = null; Render(); }

        /// <summary>预览只捕获编辑意图；不复制正式按钮或持续开火组件的回调。</summary>
        private void BindButton(MobileLayoutStickHandle handle, MobileLayoutButton id)
        {
            handle.Selected = () => { if (!trial) { buttonSelected = id; Render(); } };
            handle.Dragged = point =>
            {
                if (!TryPosition(point, out var local)) return;
                var data = draft.Button(id); data.X = local.x; data.Y = local.y;
                buttonSelected = id; Render();
            };
        }
        private MobileLayoutStickHandle[] ButtonPreviews() => new[]
        { UI.PausePreview, UI.BombPreview, UI.DashPreview, UI.FieldUpgradePreview, UI.DonateLifePreview, UI.ExitFirstPersonPreview, UI.FirstPersonFirePreview };
        private void ResetGestures()
        {
            UI.MovePreview.ResetGesture(); UI.AimPreview.ResetGesture();
            foreach (var handle in ButtonPreviews()) handle.ResetGesture();
        }
        private void SetTrial(bool enabled)
        {
            trial = enabled; ResetGestures(); UI.MovePreview.Trial = UI.AimPreview.Trial = trial;
            foreach (var handle in ButtonPreviews()) handle.Trial = trial;
            Render();
        }

        /// <summary>工具栏显隐只影响编辑工具；不更改草稿、试用状态或全屏操控几何。</summary>
        private void SetToolbarExpanded(bool expanded)
        {
            ResetGestures();
            EventSystem.current?.SetSelectedGameObject(null);
            UI.ToolbarRoot.SetActive(expanded);
            UI.ExpandToolbarButton.gameObject.SetActive(!expanded);
        }

        /// <summary>把全屏编辑触点转换为安全区比例，直接复用实战的坐标与尺寸。</summary>
        private bool TryPosition(Vector2 screenPoint, out Vector2 position)
        {
            position = default;
            if (trial || draft == null) return false;
            var canvas = UI.GetComponentInParent<Canvas>();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(UI.LayoutArea, screenPoint,
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var local)) return false;
            var size = UI.LayoutArea.rect.size;
            if (size.x <= 0 || size.y <= 0) return false;
            local -= UI.LayoutArea.rect.min;
            position = new Vector2(Mathf.Clamp01(local.x / size.x), Mathf.Clamp01(local.y / size.y));
            return true;
        }
        private void DragStick(bool move, Vector2 screenPoint)
        {
            if (!TryPosition(screenPoint, out var position)) return;
            var data = move ? draft.Move : draft.Aim;
            data.X = position.x; data.Y = position.y; data.Sanitize(move);
            moveSelected = move; buttonSelected = null; Render();
        }

        private void Render()
        {
            if (draft == null) return;
            lastAreaSize = UI.ControlSafeArea.rect.size;
            if (lastAreaSize.x <= 0 || lastAreaSize.y <= 0) return;
            var buttons = ButtonPreviews();
            UI.ExitFirstPersonPreview.gameObject.SetActive(firstPersonPreview);
            UI.FirstPersonFirePreview.gameObject.SetActive(firstPersonPreview);
            UI.AimPreview.gameObject.SetActive(!firstPersonPreview);
            var obstacles = MobileControlLayoutGeometry.RenderArcadeButtons(UI.LayoutArea, draft,
                buttons.Select(handle => (RectTransform)handle.transform).ToArray(), firstPersonPreview);
            for (var i = 0; i < buttons.Length; i++)
                buttons[i].GetComponent<UnityEngine.UI.Image>().color = buttonSelected == (MobileLayoutButton)i
                    ? new Color(.9f, .4f, .2f, .95f) : i == (int)MobileLayoutButton.Pause ? pauseButtonColor
                    : i == (int)MobileLayoutButton.Bomb ? bombButtonColor : new Color(.2f, .4f, .5f, .85f);
            MobileControlLayoutGeometry.Render(UI.LayoutArea, (RectTransform)UI.MovePreview.transform, UI.MovePreviewOpacity, draft.Move, true, obstacles);
            MobileControlLayoutGeometry.Render(UI.LayoutArea, (RectTransform)UI.AimPreview.transform, UI.AimPreviewOpacity, draft.Aim, false, obstacles);
            UI.SizeSlider.SetValueWithoutNotify(Selected.Scale); UI.OpacitySlider.SetValueWithoutNotify(Selected.Opacity);
            UI.SizeValue.text = $"{Mathf.RoundToInt(Selected.Scale * 100)}%";
            UI.OpacityValue.text = $"{Mathf.RoundToInt(Selected.Opacity * 100)}%";
            UI.MoveButton.interactable = !trial && (!moveSelected || buttonSelected.HasValue);
            UI.AimButton.interactable = !trial && !firstPersonPreview && (moveSelected || buttonSelected.HasValue);
            UI.ModeButton.interactable = !trial;
            UI.SizeSlider.interactable = UI.OpacitySlider.interactable = !trial && !buttonSelected.HasValue;
            UI.TrialText.text = Loc.T(trial ? "ui.mobile_layout.edit" : "ui.mobile_layout.trial");
            UI.ModeText.text = Loc.T(firstPersonPreview ? "ui.mobile_layout.mode_first_person" : "ui.mobile_layout.mode_arcade");
            UI.HintText.text = Loc.T(trial ? "ui.mobile_layout.trial_hint" : "ui.mobile_layout.edit_hint");
        }
        private void Save()
        {
            if (this.GetSystem<SettingsSystem>().SaveMobileControlLayout(draft)) Hide();
            else UI.HintText.text = Loc.T("ui.mobile_layout.save_failed");
        }
    }
}
