using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>管理浮动提示队列、身份去重、真实时间寿命与回收；排列由 Prefab 负责，不判定战斗结果或改变暂停状态。</summary>
    public sealed class FloatUICtrl : BaseUICtrl
    {
        /// <summary>提示实际显示后的默认寿命，单位为真实秒。</summary>
        public const float DefaultTipLifetimeSeconds = 2f;
        private const string PoolName = "NotifyFloatTip";
        private const int InitialPoolCapacity = 5;
        private const int MaxVisibleTips = 5;
        private const float ShowIntervalSeconds = 0.3f;

        private readonly Queue<PendingTip> pendingTips = new Queue<PendingTip>();
        private readonly List<VisibleTip> visibleTips = new List<VisibleTip>();
        private int processQueueTimerId;
        private bool poolInitialized;

        private FloatUI UI => BaseUI as FloatUI;

        public override bool BlocksGame => false;

        public override UILayer Layer()
        {
            return UILayer.Tips;
        }

        protected override string Path()
        {
            return "Notify/Float";
        }

        protected override void OnInit()
        {
            if (UI?.TipTemplate == null)
            {
                return;
            }

            UI.TipTemplate.gameObject.SetActive(false);
            GameObjectPoolHelper.InitPool(UI.TipTemplate.gameObject, PoolName, InitialPoolCapacity);
            var destroyWatcher = UI.GetComponent<FloatViewDestroyWatcher>() ?? UI.gameObject.AddComponent<FloatViewDestroyWatcher>();
            destroyWatcher.Bind(this, UI);
            poolInitialized = true;
        }

        protected override void OnShow()
        {
            ProcessQueue(true);
        }

        protected override void OnHide()
        {
            ClearForHide();
        }

        protected override void OnUnload()
        {
            ClearForUnload();
        }

        private void ClearForUnload()
        {
            CancelProcessTimer();
            foreach (var tip in visibleTips)
            {
                TimerMgr.Instance.Delete(tip.TimerId);
            }

            pendingTips.Clear();
            visibleTips.Clear();
            GameObjectPoolHelper.RemovePool(PoolName);
            poolInitialized = false;
        }

        internal void OnBoundViewDestroyed(FloatUI destroyedUi)
        {
            ClearForUnload();
        }

        /// <summary>请求时检查队列和可见项；重复身份直接忽略，不刷新寿命，回收后自然允许再次请求。</summary>
        public void SetTips(string tips, float lifetimeSeconds = DefaultTipLifetimeSeconds, string uniqueKey = null)
        {
            if (string.IsNullOrWhiteSpace(tips))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(uniqueKey))
            {
                foreach (var pending in pendingTips) if (pending.UniqueKey == uniqueKey) return;
                foreach (var visible in visibleTips) if (visible.Box != null && visible.UniqueKey == uniqueKey) return;
            }
            pendingTips.Enqueue(new PendingTip(tips, NormalizeLifetime(lifetimeSeconds), uniqueKey));
            ProcessQueue(visibleTips.Count == 0);
        }

        private void ProcessQueue(bool displayImmediately)
        {
            RemoveDestroyedTips();
            if (!CanDisplayTips() || pendingTips.Count == 0)
            {
                if (pendingTips.Count == 0)
                {
                    CancelProcessTimer();
                }

                return;
            }

            if (visibleTips.Count >= MaxVisibleTips)
            {
                CancelProcessTimer();
                return;
            }

            if (displayImmediately)
            {
                ShowNextTip();
            }

            if (pendingTips.Count == 0 || visibleTips.Count >= MaxVisibleTips)
            {
                CancelProcessTimer();
                return;
            }

            if (processQueueTimerId == 0)
            {
                processQueueTimerId = TimerMgr.Instance.Add(() =>
                {
                    processQueueTimerId = 0;
                    ProcessQueue(true);
                    return false;
                }, delay: ShowIntervalSeconds, useUnscaledTime: true);
            }
        }

        private bool CanDisplayTips()
        {
            return poolInitialized && UI != null && UI.TipsRoot != null && UI.TipTemplate != null;
        }

        private void ShowNextTip()
        {
            if (!CanDisplayTips() || pendingTips.Count == 0 || visibleTips.Count >= MaxVisibleTips)
            {
                return;
            }

            var boxObject = GameObjectPoolHelper.GetObjectFromPool(UI.TipTemplate.gameObject, PoolName, false);
            var box = boxObject != null ? boxObject.GetComponent<FloatTextBoxUI>() : null;
            if (box == null || box.TextTips == null)
            {
                if (boxObject != null)
                {
                    GameObjectPoolHelper.ReturnObjectToPool(boxObject);
                }

                return;
            }

            var pending = pendingTips.Dequeue();
            var rect = box.GetComponent<RectTransform>();
            box.transform.SetParent(UI.TipsRoot, false);
            box.TextTips.text = pending.Text;
            box.gameObject.SetActive(true);
            if (rect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }

            var visible = new VisibleTip
            {
                Box = box,
                UniqueKey = pending.UniqueKey
            };
            visible.TimerId = TimerMgr.Instance.Add(() =>
            {
                RemoveTip(box);
                ProcessQueue(true);
                return false;
            }, delay: pending.LifetimeSeconds, useUnscaledTime: true);
            visibleTips.Add(visible);
            RepositionVisibleTips();
        }

        private void RemoveTip(FloatTextBoxUI box)
        {
            var index = visibleTips.FindIndex(tip => tip.Box == box);
            if (index < 0)
            {
                return;
            }

            var tip = visibleTips[index];
            TimerMgr.Instance.Delete(tip.TimerId);
            visibleTips.RemoveAt(index);
            ReturnTip(tip.Box);
            RepositionVisibleTips();
        }

        private void ClearForHide()
        {
            CancelProcessTimer();
            pendingTips.Clear();
            foreach (var tip in visibleTips)
            {
                TimerMgr.Instance.Delete(tip.TimerId);
                ReturnTip(tip.Box);
            }

            visibleTips.Clear();
        }

        private static void ReturnTip(FloatTextBoxUI box)
        {
            if (box == null)
            {
                return;
            }

            if (box.TextTips != null)
            {
                box.TextTips.text = string.Empty;
            }

            box.transform.localPosition = Vector3.zero;
            box.gameObject.SetActive(false);
            GameObjectPoolHelper.ReturnObjectToPool(box.gameObject);
        }

        private void RemoveDestroyedTips()
        {
            for (var index = visibleTips.Count - 1; index >= 0; index--)
            {
                if (visibleTips[index].Box != null)
                {
                    continue;
                }

                TimerMgr.Instance.Delete(visibleTips[index].TimerId);
                visibleTips.RemoveAt(index);
            }

            RepositionVisibleTips();
        }

        private void RepositionVisibleTips()
        {
            // 只刷新 Prefab 的布局，避免代码位置与布局下一帧计算的结果互相覆盖。
            if (UI != null && UI.TipsRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(UI.TipsRoot);
            }
        }

        private void CancelProcessTimer()
        {
            TimerMgr.Instance.Delete(processQueueTimerId);
            processQueueTimerId = 0;
        }

        private static float NormalizeLifetime(float lifetimeSeconds)
        {
            return lifetimeSeconds > 0f ? lifetimeSeconds : DefaultTipLifetimeSeconds;
        }

        private readonly struct PendingTip
        {
            public PendingTip(string text, float lifetimeSeconds, string uniqueKey)
            {
                Text = text;
                LifetimeSeconds = lifetimeSeconds;
                UniqueKey = uniqueKey;
            }

            public string Text { get; }
            public float LifetimeSeconds { get; }
            /// <summary>可选稳定身份；空值允许重复，身份占用从入队持续到可见项回收。</summary>
            public string UniqueKey { get; }
        }

        private sealed class VisibleTip
        {
            public FloatTextBoxUI Box;
            public int TimerId;
            /// <summary>从等待项转移的去重身份；移除、隐藏或卸载时随本项释放。</summary>
            public string UniqueKey;
        }

    }

    public sealed class FloatViewDestroyWatcher : MonoBehaviour
    {
        private FloatUICtrl owner;
        private FloatUI view;

        public void Bind(FloatUICtrl controller, FloatUI floatUi)
        {
            owner = controller;
            view = floatUi;
        }

        private void OnDestroy()
        {
            owner?.OnBoundViewDestroyed(view);
        }
    }
}
