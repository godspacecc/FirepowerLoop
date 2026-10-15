using System;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>显示资源更新公告并提交重启意图；不下载资源、不决定版本，也不关闭进程。</summary>
    public sealed class ResourceUpdateMessageUICtrl : BaseUICtrl
    {
        private ResourceUpdateMessageUI UI => BaseUI as ResourceUpdateMessageUI;
        private Action restart;
        private bool restartRequested;

        protected override string Path()
        {
            return "Common/ResourceUpdateMessage";
        }

        protected override void OnInit()
        {
            UI.BtnLater.onClick.AddListener(Hide);
            UI.BtnRestart.onClick.AddListener(() =>
            {
                if (restartRequested) return;
                restartRequested = true;
                UI.BtnRestart.interactable = false;
                restart?.Invoke();
            });
        }

        protected override void OnShow()
        {
        }

        protected override void OnHide()
        {
            restart = null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        public override UILayer Layer()
        {
            return UILayer.Top;
        }

        /// <summary>每次显示替换完整公告和回调，不继承上一次请求状态。</summary>
        public void SetContent(string title, string content, Action onRestart)
        {
            restart = onRestart;
            restartRequested = false;
            UI.TitleText.text = title ?? string.Empty;
            UI.ContentText.text = content ?? string.Empty;
            UI.BtnRestart.interactable = true;
            UI.ContentText.GetComponentInParent<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition = 1f;
        }

        /// <summary>失败时保留弹窗与手动操作指引，允许重试或稍后关闭。</summary>
        public void ShowRestartFailure()
        {
            restartRequested = false;
            UI.BtnRestart.interactable = true;
            UI.TitleText.text = Texts.StartUpdateRestartFailed;
            UI.ContentText.text = Loc.T("ui.start.update.restart_manual");
        }
    }
}
