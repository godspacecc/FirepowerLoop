using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>显示对话只读快照并提交带行身份的选择；不决定节点、NPC 功能或镜头状态。</summary>
    public sealed class DialogueUICtrl : BaseUICtrl
    {
        private DialogueUI UI => BaseUI as DialogueUI;
        private DialogueSystem dialogueSystem;
        /// <summary>当前 UI 所属会话；0 表示已清理，卸载旧 View 不会关闭新会话。</summary>
        private long ownerSessionId;
        private DialogueSnapshot rendered;
        protected override string Path() => "Dialogue/Dialogue";
        public override UILayer Layer() => UILayer.Dialog;
        protected override BackgroundConfig GetBackgroundConfig() => new BackgroundConfig
        { ColorType = BackgroundColorType.Transparent, ClickEffect = BackgroundClickEffect.None };

        public static bool TryShow(out string error)
        {
            error = null;
            var system = Game.Interface.GetSystem<DialogueSystem>();
            if (system?.Current == null) { error = "没有活动对话"; return false; }
            try
            {
                var ctrl = UIHelper.ShowUI<DialogueUICtrl>();
                if (ctrl != null && ctrl.IsShow() && ctrl.ownerSessionId == system.Current.SessionId) return true;
                error = "对话界面加载失败";
            }
            catch (System.Exception exception) { error = "对话界面加载失败：" + exception.Message; }
            if (system.Current != null) system.Close(system.Current.SessionId);
            return false;
        }

        protected override void OnInit()
        {
            dialogueSystem = this.GetSystem<DialogueSystem>();
            UI.BtnClose.onClick.AddListener(CloseFromButton);
            UI.BtnNext.onClick.AddListener(Continue);
            UI.ChoiceList.AddItemRefreshListener(RefreshChoice);
            this.RegisterEvent<DialogueChangedEvent>(OnDialogueChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<AppStateChangedEvent>(_ => { if (IsShow()) Render(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => { if (IsShow()) Render(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow()
        {
            ownerSessionId = dialogueSystem?.Current?.SessionId ?? 0;
            if (ownerSessionId == 0) { Hide(); return; }
            LifeCycle.RegisterLateUpdate(CheckForeground);
            Render();
        }
        protected override void OnHide() => ClearOwnedSession();
        protected override void OnUnload()
        {
            ClearOwnedSession();
            dialogueSystem = null;
        }
        private void ClearOwnedSession()
        {
            LifeCycle.UnregisterLateUpdate(CheckForeground);
            var owner = ownerSessionId;
            // 先解除 UI 所有权，再通知领域关闭，避免同步事件重新进入 Hide。
            ownerSessionId = 0; rendered = null;
            if (UI != null)
            {
                var events = UnityEngine.EventSystems.EventSystem.current;
                var selected = events != null ? events.currentSelectedGameObject : null;
                if (selected != null && selected.transform.IsChildOf(UI.transform)) events.SetSelectedGameObject(null);
                UI.ChoiceList.ClearCells();
            }
            if (owner != 0) dialogueSystem?.Close(owner);
        }
        private void CheckForeground()
        {
            // 暂停菜单及其设置、确认框可以覆盖会话；继续后仍显示原节点。
            if (this.GetModel<AppStateModel>().CurrentState.Value == AppState.Paused) return;
            if (IsShow() && !ReferenceEquals(UIHelper.GetTopmostBlockingUI(), this)) Hide();
        }
        protected override void OnBackgroundClick(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (eventData != null && eventData.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left &&
                ReferenceEquals(UIHelper.GetTopmostBlockingUI(), this))
                UIHelper.GetUICtrl<GameplayGuideHudUICtrl>()?.TryOpenPauseAtScreenPosition(eventData.position);
        }
        /// <summary>暂停期间拒绝旧关闭回调，显式卸载与地图清理仍通过 Hide 结束会话。</summary>
        private void CloseFromButton()
        {
            if (this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing &&
                ReferenceEquals(UIHelper.GetTopmostBlockingUI(), this)) Hide();
        }
        private void OnDialogueChanged(DialogueChangedEvent e)
        {
            if (!IsShow() || ownerSessionId == 0) return;
            if (e.Snapshot == null) { Hide(); return; }
            if (e.Snapshot.SessionId == ownerSessionId) Render();
        }
        private void Continue()
        {
            var snapshot = rendered;
            if (snapshot != null) dialogueSystem?.Choose(snapshot.SessionId, snapshot.Revision, null);
        }
        private void Render()
        {
            var snapshot = dialogueSystem?.Current;
            if (snapshot == null || snapshot.SessionId != ownerSessionId) return;
            rendered = snapshot;
            var canInteract = this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing;
            UI.BtnClose.interactable = canInteract;
            UI.BtnNext.interactable = canInteract;
            UI.NameText.text = Loc.T(snapshot.NameKey);
            UI.BodyText.text = Loc.T(snapshot.Node.TextKey);
            UI.NextText.text = Loc.T(string.IsNullOrEmpty(snapshot.Node.NextNodeId) ? "dialogue.leave" : "dialogue.continue");
            UI.BtnNext.gameObject.SetActive(snapshot.Node.Options.Count == 0);
            UI.ChoiceList.totalCount = snapshot.Node.Options.Count;
            UI.ChoiceList.RefreshCells();
            LayoutRebuilder.ForceRebuildLayoutImmediate(UI.ChoiceList.GetComponent<RectTransform>());
            foreach (var scroll in UI.GetComponentsInChildren<ScrollRect>(true)) scroll.verticalNormalizedPosition = 1f;
        }
        private void RefreshChoice(Transform item, int index)
        {
            var snapshot = rendered;
            var box = item.GetComponent<DialogueChoiceBoxUI>();
            if (box == null || snapshot == null || index < 0 || index >= snapshot.Node.Options.Count) return;
            var option = snapshot.Node.Options[index];
            box.ChoiceText.text = Loc.T(option.TextKey);
            box.BtnChoice.interactable = this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing;
            box.BtnChoice.onClick.RemoveAllListeners();
            box.BtnChoice.onClick.AddListener(() => dialogueSystem?.Choose(snapshot.SessionId, snapshot.Revision, option.Id));
        }
    }
}
