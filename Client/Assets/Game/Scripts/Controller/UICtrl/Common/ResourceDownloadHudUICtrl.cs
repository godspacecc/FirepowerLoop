using System;
using UnityEngine;

namespace Game
{
    /// <summary>在右上角显示当前资源下载的只读进度；点击只恢复详情，不启动、取消或判定下载成功。</summary>
    public sealed class ResourceDownloadHudUICtrl : BaseUICtrl
    {
        private ResourceDownloadHudUI UI => BaseUI as ResourceDownloadHudUI;
        /// <summary>表现请求身份；零表示没有有效下载，旧请求结束不能隐藏新进度。</summary>
        private int requestId;
        private OptionalGameplayDownloadProgress progress;
        private Action openDetails;

        protected override string Path() => "Common/ResourceDownloadHud";
        public override UILayer Layer() => UILayer.Popup;
        public override bool BlocksGame => false;
        public override bool IsTransientPopup => false;

        protected override void OnInit() => UI.BtnDetails.onClick.AddListener(() => openDetails?.Invoke());
        protected override void OnShow() => Render();
        protected override void OnHide() { }
        protected override void OnUnload() { }

        /// <summary>每次更新覆盖完整快照；回调由当前请求所有者提供，隐藏详情不影响进度圈。</summary>
        public void SetProgress(int id, OptionalGameplayDownloadProgress value, Action showDetails)
        {
            requestId = id;
            progress = value;
            openDetails = showDetails;
            if (!IsShow()) Show();
            Render();
        }

        /// <summary>仅结束匹配的表现请求；完成或失败后清除点击回调并隐藏。</summary>
        public void End(int id)
        {
            if (requestId != id) return;
            requestId = 0;
            openDetails = null;
            Hide();
        }

        private void Render()
        {
            if (UI == null) return;
            var ratio = progress.TotalBytes <= 0 ? 0f : Mathf.Clamp01((float)progress.CurrentBytes / progress.TotalBytes);
            UI.ProgressRing.fillAmount = ratio;
            UI.PercentText.text = $"{(int)(ratio * 100f)}%";
        }
    }
}
