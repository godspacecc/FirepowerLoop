using QFramework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>编辑本机摇杆、按钮和信息栏布局草稿；保存交给设置系统，预览不修改玩家状态或触发玩法输入。</summary>
    public sealed class MobileControlLayoutUICtrl : BaseUICtrl
    {
        private MobileControlLayoutUI UI => (MobileControlLayoutUI)BaseUI;
        /// <summary>仅在编辑页可见期间存在的独立副本；取消与销毁直接丢弃，保存成功后才更新 Model。</summary>
        private MobileControlLayoutData draft;
        private bool moveSelected = true;
        private MobileLayoutButton? buttonSelected;
        /// <summary>当前选中的信息栏身份；与按钮、摇杆互斥，大小滑杆只修改该栏草稿。</summary>
        private MobileLayoutHud? hudSelected;
        private bool arcadePreview;
        private bool trial;
        private bool updateRegistered;
        private Vector2 lastAreaSize;
        private MobileStickLayoutData Selected => moveSelected ? draft.Move : draft.Aim;
        protected override string Path() => "Home/MobileControlLayout";
        public override UILayer Layer() => UILayer.Dialog;

        protected override void OnInit()
        {
            UI.MoveButton.onClick.AddListener(() => Select(true));
            UI.AimButton.onClick.AddListener(() => Select(false));
            UI.MovePreview.Selected = () => { if (!trial) Select(true); };
            UI.AimPreview.Selected = () => { if (!trial) Select(false); };
            UI.MovePreview.Dragged = point => Drag(true, point);
            UI.AimPreview.Dragged = point => Drag(false, point);
            UI.ModeButton.onClick.AddListener(() => { arcadePreview = !arcadePreview; buttonSelected = null; hudSelected = null; SetTrial(false); });
            BindButton(UI.PausePreview, MobileLayoutButton.Pause);
            BindButton(UI.InventoryPreview, MobileLayoutButton.Inventory);
            BindButton(UI.BuildPreview, MobileLayoutButton.Build);
            BindButton(UI.AttackPreview, MobileLayoutButton.Attack);
            BindButton(UI.ReloadPreview, MobileLayoutButton.Reload);
            BindHud(UI.StatusPreview, MobileLayoutHud.Status);
            BindHud(UI.HealthPreview, MobileLayoutHud.Health);
            BindHud(UI.WeaponPreview, MobileLayoutHud.Weapon);
            UI.SizeSlider.onValueChanged.AddListener(value => { if (!trial && !buttonSelected.HasValue) { if (hudSelected.HasValue) draft.Hud(hudSelected.Value).Scale = value; else Selected.Scale = value; Render(); } });
            UI.OpacitySlider.onValueChanged.AddListener(value => { if (!trial && !buttonSelected.HasValue && !hudSelected.HasValue) { Selected.Opacity = value; Render(); } });
            UI.TrialButton.onClick.AddListener(() => SetTrial(!trial));
            UI.ResetButton.onClick.AddListener(() => { draft = new MobileControlLayoutData(); SetTrial(false); });
            UI.CancelButton.onClick.AddListener(Hide);
            UI.SaveButton.onClick.AddListener(Save);
            this.RegisterEvent<LanguageChangedEvent>(_ => { if (IsShow()) Render(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MobileInputModeChangedEvent>(_ =>
            { if (IsShow() && !this.GetSystem<MobileInputSystem>().IsMobileInputMode) Hide(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            if (!this.GetSystem<MobileInputSystem>().IsMobileInputMode) { Hide(); return; }
            draft = this.GetModel<MobileControlLayoutModel>().ToData();
            moveSelected = true;
            buttonSelected = null;
            hudSelected = null;
            arcadePreview = false;
            Canvas.ForceUpdateCanvases();
            SetTrial(false);
            LifeCycle.RegisterUpdate(RefreshOnResize);
            updateRegistered = true;
        }

        /// <summary>取消、外部关闭或销毁只释放草稿；正式设置始终由保存事务更新。</summary>
        protected override void OnHide()
        {
            ReleaseUpdate();
            ResetGestures();
            EventSystem.current?.SetSelectedGameObject(null);
            draft = null;
        }
        // 基类卸载时已清空 BaseUI；控件自身 OnDisable 释放触点，此处只释放 Controller 持有的状态。
        protected override void OnUnload() { ReleaseUpdate(); draft = null; }
        private void ReleaseUpdate()
        { if (updateRegistered) { LifeCycle.UnregisterUpdate(RefreshOnResize); updateRegistered = false; } }
        private void RefreshOnResize()
        { if (UI.LayoutArea.rect.size != lastAreaSize) { ResetGestures(); Render(); } }
        private void Select(bool move) { moveSelected = move; buttonSelected = null; hudSelected = null; Render(); }

        /// <summary>五枚按钮是 Prefab 固定控件；这里只绑定拖动意图，预览永不调用真实游戏按钮。</summary>
        private void BindButton(MobileLayoutStickHandle handle, MobileLayoutButton id)
        {
            handle.Selected = () => { if (!trial) { buttonSelected = id; hudSelected = null; Render(); } };
            handle.Dragged = point => DragButton(id, point);
        }

        private MobileLayoutStickHandle[] ButtonPreviews() => new[]
        { UI.PausePreview, UI.InventoryPreview, UI.BuildPreview, UI.AttackPreview, UI.ReloadPreview };

        private MobileLayoutStickHandle[] HudPreviews() => new[] { UI.StatusPreview, UI.HealthPreview, UI.WeaponPreview };

        /// <summary>绑定三块固定信息预览；拖动只修改草稿中心，取消不影响正式布局。</summary>
        private void BindHud(MobileLayoutStickHandle handle, MobileLayoutHud id)
        {
            handle.Selected = () => { if (!trial && !arcadePreview) { hudSelected = id; buttonSelected = null; Render(); } };
            handle.Dragged = point =>
            {
                if (trial || arcadePreview || draft == null) return;
                var canvas = UI.GetComponentInParent<Canvas>();
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(UI.LayoutArea, point,
                    canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var local)) return;
                var size = UI.LayoutArea.rect.size;
                if (size.x <= 0 || size.y <= 0) return;
                local -= UI.LayoutArea.rect.min;
                var data = draft.Hud(id);
                data.X = Mathf.Clamp01(local.x / size.x); data.Y = Mathf.Clamp01(local.y / size.y);
                hudSelected = id; buttonSelected = null; Render();
            };
        }

        private void ResetGestures()
        {
            UI.MovePreview.ResetGesture(); UI.AimPreview.ResetGesture();
            foreach (var handle in ButtonPreviews()) handle.ResetGesture();
            foreach (var handle in HudPreviews()) handle.ResetGesture();
        }
        /// <summary>切换前清除触点和位移；试用期间禁止布局修改，两个摇杆可分别捕获手指。</summary>
        private void SetTrial(bool enabled)
        {
            trial = enabled;
            ResetGestures();
            UI.MovePreview.Trial = UI.AimPreview.Trial = trial;
            foreach (var handle in ButtonPreviews()) handle.Trial = trial;
            foreach (var handle in HudPreviews()) handle.Trial = trial;
            Render();
        }

        private Rect[] Obstacles()
        {
            if (arcadePreview) return new[]
            { MobileControlLayoutGeometry.Bounds(UI.LayoutArea, UI.DashArea), MobileControlLayoutGeometry.Bounds(UI.LayoutArea, UI.BombArea) };
            var handles = new System.Collections.Generic.List<MobileLayoutStickHandle>(ButtonPreviews());
            handles.AddRange(HudPreviews());
            return handles.ConvertAll(handle => MobileControlLayoutGeometry.Bounds(UI.LayoutArea, (RectTransform)handle.transform)).ToArray();
        }

        /// <summary>编辑草稿仅写比例位置；边界与重叠在共享显示适配中处理，取消不影响正式设置。</summary>
        private void DragButton(MobileLayoutButton id, Vector2 screenPoint)
        {
            if (trial || arcadePreview || draft == null) return;
            var canvas = UI.GetComponentInParent<Canvas>();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(UI.LayoutArea, screenPoint,
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var local)) return;
            var size = UI.LayoutArea.rect.size;
            if (size.x <= 0 || size.y <= 0) return;
            local -= UI.LayoutArea.rect.min;
            var position = draft.Button(id);
            position.X = Mathf.Clamp01(local.x / size.x); position.Y = Mathf.Clamp01(local.y / size.y);
            buttonSelected = id;
            hudSelected = null;
            Render();
        }

        private void Drag(bool move, Vector2 screenPoint)
        {
            if (trial || draft == null) return;
            var canvas = UI.GetComponentInParent<Canvas>();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(UI.LayoutArea, screenPoint,
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var local)) return;
            var data = move ? draft.Move : draft.Aim;
            // 进入编辑后从实际显示中心拖动，后续保存不再需要默认位置适配。
            var size = UI.LayoutArea.rect.size;
            if (size.x <= 0 || size.y <= 0) return;
            local -= UI.LayoutArea.rect.min;
            data.X = local.x / size.x; data.Y = local.y / size.y;
            data.Sanitize(move);
            var fitted = MobileControlLayoutGeometry.Fit(size, data, move, Obstacles());
            data.X = fitted.x / size.x; data.Y = fitted.y / size.y;
            moveSelected = move;
            buttonSelected = null;
            hudSelected = null;
            Render();
        }

        private void Render()
        {
            if (draft == null) return;
            lastAreaSize = UI.LayoutArea.rect.size;
            if (lastAreaSize.x <= 0 || lastAreaSize.y <= 0) return;
            UI.DashArea.gameObject.SetActive(arcadePreview);
            UI.BombArea.gameObject.SetActive(arcadePreview);
            var huds = HudPreviews();
            for (var i = 0; i < huds.Length; i++)
            {
                var handle = huds[i];
                handle.gameObject.SetActive(!arcadePreview);
                if (!arcadePreview) MobileControlLayoutGeometry.RenderHud(UI.LayoutArea, (RectTransform)handle.transform,
                    draft.HudForDisplay((MobileLayoutHud)i, lastAreaSize));
                handle.GetComponent<UnityEngine.UI.Image>().color = hudSelected == (MobileLayoutHud)i
                    ? new Color(.35f, .55f, .7f, .8f) : new Color(.1f, .15f, .2f, .7f);
            }
            var buttons = ButtonPreviews();
            var occupied = new System.Collections.Generic.List<Rect>();
            if (!arcadePreview)
                foreach (var handle in huds) occupied.Add(MobileControlLayoutGeometry.Bounds(UI.LayoutArea, (RectTransform)handle.transform));
            for (var i = 0; i < buttons.Length; i++)
            {
                var handle = buttons[i];
                handle.gameObject.SetActive(!arcadePreview);
                if (arcadePreview) continue;
                var rect = (RectTransform)handle.transform;
                MobileControlLayoutGeometry.RenderButton(UI.LayoutArea, rect, draft.Button((MobileLayoutButton)i), occupied.ToArray());
                occupied.Add(MobileControlLayoutGeometry.Bounds(UI.LayoutArea, rect));
                handle.GetComponent<UnityEngine.UI.Image>().color = buttonSelected == (MobileLayoutButton)i
                    ? new Color(.9f, .4f, .2f, .95f) : new Color(.68f, .22f, .13f, .82f);
            }
            var obstacles = Obstacles();
            MobileControlLayoutGeometry.Render(UI.LayoutArea, (RectTransform)UI.MovePreview.transform,
                UI.MovePreviewOpacity, arcadePreview ? draft.Move : draft.OrdinaryStick(true), true, obstacles);
            MobileControlLayoutGeometry.Render(UI.LayoutArea, (RectTransform)UI.AimPreview.transform,
                UI.AimPreviewOpacity, arcadePreview ? draft.Aim : draft.OrdinaryStick(false), false, obstacles);
            RefreshControls();
            UI.ModeText.text = Loc.T(arcadePreview ? "ui.mobile_layout.mode_arcade" : "ui.mobile_layout.mode_base");
            UI.HintText.text = trial ? Loc.T("ui.mobile_layout.trial_hint") : hudSelected.HasValue
                ? Loc.T("ui.mobile_layout.hud_hint", Loc.T("ui.mobile_layout.hud_" + hudSelected.Value.ToString().ToLowerInvariant())) : buttonSelected.HasValue
                ? Loc.T("ui.mobile_layout.button_hint", Loc.T("ui.mobile_layout.button_" + buttonSelected.Value.ToString().ToLowerInvariant()))
                : Loc.T("ui.mobile_layout.edit_hint");
        }
        private void RefreshControls()
        {
            var scale = hudSelected.HasValue ? draft.Hud(hudSelected.Value).Scale : Selected.Scale;
            UI.SizeSlider.SetValueWithoutNotify(scale);
            UI.OpacitySlider.SetValueWithoutNotify(Selected.Opacity);
            UI.SizeValue.text = $"{Mathf.RoundToInt(scale * 100)}%";
            UI.OpacityValue.text = $"{Mathf.RoundToInt(Selected.Opacity * 100)}%";
            UI.MoveButton.interactable = !trial && (!moveSelected || buttonSelected.HasValue || hudSelected.HasValue);
            UI.AimButton.interactable = !trial && (moveSelected || buttonSelected.HasValue || hudSelected.HasValue);
            UI.ModeButton.interactable = !trial;
            UI.SizeSlider.interactable = !trial && !buttonSelected.HasValue;
            UI.OpacitySlider.interactable = !trial && !buttonSelected.HasValue && !hudSelected.HasValue;
            UI.TrialText.text = Loc.T(trial ? "ui.mobile_layout.edit" : "ui.mobile_layout.trial");
        }
        private void Save()
        {
            if (this.GetSystem<SettingsSystem>().SaveMobileControlLayout(draft)) Hide();
            else UI.HintText.text = Loc.T("ui.mobile_layout.save_failed");
        }
    }
}
