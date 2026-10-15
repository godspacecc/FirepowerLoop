using QFramework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>管理可拖动的测试入口及位置设置；保持最高层可点击，不裁决指令业务或阻断玩法输入。</summary>
    public sealed class TestCmdButtonUICtrl : BaseUICtrl
    {
        private TestCmdButtonUI UI => BaseUI as TestCmdButtonUI;
        private TestCmdButtonPointerRelay Relay => BaseUI.GetComponent<TestCmdButtonPointerRelay>();
        private RectTransform RectTransform => BaseUI.transform as RectTransform;

        public override bool BlocksGame => false;

        public override UILayer Layer()
        {
            return UILayer.Top;
        }

        protected override string Path()
        {
            return "Diagnostics/TestCmdButton";
        }

        protected override void OnInit()
        {
            Relay.ClickRequested += OnClickRequested;
            Relay.BeginDragRequested += OnBeginDragRequested;
            Relay.DragRequested += OnDragRequested;
            Relay.EndDragRequested += OnEndDragRequested;
            Relay.ParentRectChanged += OnParentRectChanged;

            this.GetModel<TestToolSettingsModel>()
                .TestCmdButtonNormalizedPosition
                .RegisterWithInitValue(ApplyNormalizedPosition)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);

        }

        protected override void OnShow()
        {
            ApplyNormalizedPosition(this.GetModel<TestToolSettingsModel>().TestCmdButtonNormalizedPosition.Value);
        }

        protected override void OnHide()
        {
        }

        private void OnClickRequested()
        {
            UIHelper.ShowUI<TestCmdUICtrl>();
        }

        private void OnBeginDragRequested(PointerEventData eventData)
        {
            ApplyDragPosition(eventData);
        }

        private void OnDragRequested(PointerEventData eventData)
        {
            ApplyDragPosition(eventData);
        }

        private void OnEndDragRequested(PointerEventData eventData)
        {
            ApplyDragPosition(eventData);
            var normalized = LocalToNormalized(GetClampedLocalPosition(RectTransform.anchoredPosition));
            this.GetModel<TestToolSettingsModel>().SetTestCmdButtonPosition(normalized);
            this.GetSystem<SettingsSystem>().MarkDirty();
        }

        private void OnParentRectChanged()
        {
            ApplyNormalizedPosition(this.GetModel<TestToolSettingsModel>().TestCmdButtonNormalizedPosition.Value);
        }

        private void ApplyDragPosition(PointerEventData eventData)
        {
            if (eventData == null || !TryGetParentRect(out var parentRect))
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect,
                    eventData.position,
                    eventData.pressEventCamera,
                    out var localPoint))
            {
                return;
            }

            RectTransform.anchoredPosition = GetClampedLocalPosition(localPoint);
        }

        private void ApplyNormalizedPosition(Vector2 normalized)
        {
            if (!TryGetParentRect(out _))
            {
                return;
            }

            RectTransform.anchoredPosition = NormalizedToLocal(normalized);
        }

        private Vector2 NormalizedToLocal(Vector2 normalized)
        {
            var rect = GetParentRect().rect;
            var local = new Vector2(
                Mathf.Lerp(rect.xMin, rect.xMax, normalized.x),
                Mathf.Lerp(rect.yMin, rect.yMax, normalized.y));
            return GetClampedLocalPosition(local);
        }

        private Vector2 LocalToNormalized(Vector2 localPosition)
        {
            var rect = GetParentRect().rect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return new Vector2(
                    TestToolSettingsData.DefaultTestCmdButtonPositionX,
                    TestToolSettingsData.DefaultTestCmdButtonPositionY);
            }

            return new Vector2(
                Mathf.InverseLerp(rect.xMin, rect.xMax, localPosition.x),
                Mathf.InverseLerp(rect.yMin, rect.yMax, localPosition.y));
        }

        private Vector2 GetClampedLocalPosition(Vector2 localPosition)
        {
            var rect = GetParentRect().rect;
            var size = RectTransform.rect.size;
            var halfWidth = size.x * 0.5f;
            var halfHeight = size.y * 0.5f;
            return new Vector2(
                Mathf.Clamp(localPosition.x, rect.xMin + halfWidth, rect.xMax - halfWidth),
                Mathf.Clamp(localPosition.y, rect.yMin + halfHeight, rect.yMax - halfHeight));
        }

        private bool TryGetParentRect(out RectTransform rectTransform)
        {
            rectTransform = GetParentRect();
            return rectTransform != null;
        }

        private RectTransform GetParentRect()
        {
            return BaseUI.transform.parent as RectTransform;
        }
    }
}
