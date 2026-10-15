using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    public sealed class RuntimeLogUICtrl : BaseUICtrl
    {
        private const float BottomFollowTolerance = 0.01f;
        private const float DetailVerticalPadding = 20f;
        private const string CopyEmptyText = "当前筛选没有日志，已复制空日志报告。";
        private const string CopySuccessText = "已复制当前筛选日志。";
        private const string ExportFailedText = "导出失败，请查看内部日志。";

        internal enum RuntimeLogFilter
        {
            All,
            Normal,
            Warning,
            Error
        }

        private RuntimeLogEntry[] visibleEntries = Array.Empty<RuntimeLogEntry>();
        private bool listDirty = true;
        private bool logListFilled;
        private RuntimeLogFilter currentFilter = RuntimeLogFilter.All;
        private Func<IEnumerable<RuntimeLogEntry>, AppState, string, DateTime?, string, string> exportLogs =
            RuntimeLogExportUtility.Export;
        private RuntimeLogUI UI => BaseUI as RuntimeLogUI;

        public override UILayer Layer()
        {
            return UILayer.Dialog;
        }

        protected override string Path()
        {
            return "Diagnostics/RuntimeLog";
        }

        protected override void OnInit()
        {
            listDirty = true;
            logListFilled = false;
            currentFilter = RuntimeLogFilter.All;
            UI.BtnClose.onClick.AddListener(Hide);
            UI.BtnClear.onClick.AddListener(OnClearClick);
            UI.BtnCopy.onClick.AddListener(OnCopyClick);
            UI.BtnExport.onClick.AddListener(OnExportClick);
            UI.BtnAll.onClick.AddListener(() => OnFilterClick(RuntimeLogFilter.All));
            UI.BtnNormal.onClick.AddListener(() => OnFilterClick(RuntimeLogFilter.Normal));
            UI.BtnWarning.onClick.AddListener(() => OnFilterClick(RuntimeLogFilter.Warning));
            UI.BtnError.onClick.AddListener(() => OnFilterClick(RuntimeLogFilter.Error));
            UI.LogList.AddItemRefreshListener(OnLogItemRefresh);
            RefreshFilterOutlines();
            ShowAuthoritativeDetail();

            this.RegisterEvent<RuntimeLogChangedEvent>(OnRuntimeLogChanged)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            Refresh();
        }

        protected override void OnHide()
        {
        }

        private void Refresh(bool forceLatest = false)
        {
            if (UnityObjectState.IsMissing(UI) || UnityObjectState.IsMissing(UI.LogList))
            {
                return;
            }

            var previousCount = visibleEntries.Length;
            var wasAtBottom = !logListFilled ||
                UI.LogList.verticalNormalizedPosition <= BottomFollowTolerance;
            visibleEntries = Filter(this.GetModel<RuntimeLogModel>().Snapshot());
            UI.LogList.totalCount = visibleEntries.Length;
            if (!logListFilled || forceLatest || wasAtBottom || (previousCount == 0 && visibleEntries.Length > 0))
            {
                UI.LogList.RefillCellsFromEnd(0);
                logListFilled = true;
            }
            else
            {
                UI.LogList.RefreshCells();
            }

            ShowAuthoritativeDetail();
            listDirty = false;
        }

        private void OnLogItemRefresh(Transform transform, int index)
        {
            var item = transform.GetComponent<RuntimeLogItemBoxUI>();
            if (item == null || index < 0 || index >= visibleEntries.Length)
            {
                return;
            }

            var entry = visibleEntries[index];
            item.TextTime.text = entry.Timestamp.ToString("HH:mm:ss.fff");
            item.TextType.text = entry.Type.ToString();
            item.TextMessage.text = entry.Message;
        }

        private void OnClearClick()
        {
            this.GetSystem<RuntimeLogSystem>().Clear();
        }

        private void OnCopyClick()
        {
            var snapshot = Filter(this.GetModel<RuntimeLogModel>().Snapshot());
            GUIUtility.systemCopyBuffer = RuntimeLogTextFormatter.Format(
                snapshot,
                this.GetModel<AppStateModel>().CurrentState.Value,
                this.GetSystem<MapSceneSystem>().CurrentMapId);
            ShowFeedback(snapshot.Length == 0 ? CopyEmptyText : CopySuccessText);
        }

        private void OnExportClick()
        {
            var snapshot = Filter(this.GetModel<RuntimeLogModel>().Snapshot());
            try
            {
                var path = exportLogs(
                    snapshot,
                    this.GetModel<AppStateModel>().CurrentState.Value,
                    null,
                    null,
                    this.GetSystem<MapSceneSystem>().CurrentMapId);
                GUIUtility.systemCopyBuffer = path;
                ShowFeedback(ExportSuccessText(path));
            }
            catch (Exception exception)
            {
                Log.Error(exception);
                ShowFeedback(ExportFailedText);
            }
        }

        private void OnRuntimeLogChanged(RuntimeLogChangedEvent evt)
        {
            listDirty = true;
            if (IsShow())
            {
                Refresh();
            }
        }

        private RuntimeLogEntry[] Filter(IReadOnlyList<RuntimeLogEntry> entries)
        {
            var result = new List<RuntimeLogEntry>();
            foreach (var entry in entries)
            {
                if (Accepts(currentFilter, entry.Type))
                {
                    result.Add(entry);
                }
            }

            return result.ToArray();
        }

        private static bool Accepts(RuntimeLogFilter filter, LogType type)
        {
            return filter switch
            {
                RuntimeLogFilter.All => true,
                RuntimeLogFilter.Normal => type == LogType.Log,
                RuntimeLogFilter.Warning => type == LogType.Warning,
                RuntimeLogFilter.Error => type == LogType.Error || type == LogType.Assert || type == LogType.Exception,
                _ => true
            };
        }

        private void OnFilterClick(RuntimeLogFilter filter)
        {
            currentFilter = filter;
            RefreshFilterOutlines();
            Refresh(true);
        }

        private void RefreshFilterOutlines()
        {
            SetOutline(UI.BtnAll, currentFilter == RuntimeLogFilter.All);
            SetOutline(UI.BtnNormal, currentFilter == RuntimeLogFilter.Normal);
            SetOutline(UI.BtnWarning, currentFilter == RuntimeLogFilter.Warning);
            SetOutline(UI.BtnError, currentFilter == RuntimeLogFilter.Error);
        }

        private static void SetOutline(Button button, bool selected)
        {
            var outline = button != null ? button.GetComponent<Outline>() : null;
            if (outline != null)
            {
                outline.enabled = selected;
            }
        }

        private static string ExportSuccessText(string path)
        {
            return $"已导出：{path}";
        }

        private void ShowAuthoritativeDetail()
        {
            var detail = visibleEntries.Length == 0
                ? string.Empty
                : BuildDetail(visibleEntries[visibleEntries.Length - 1]);
            SetDetail(detail);
        }

        private void ShowFeedback(string feedback)
        {
            var detail = visibleEntries.Length == 0
                ? string.Empty
                : BuildDetail(visibleEntries[visibleEntries.Length - 1]);
            SetDetail(string.IsNullOrEmpty(detail) ? feedback : $"{feedback}\n\n{detail}");
        }

        private static string BuildDetail(RuntimeLogEntry entry)
        {
            return string.IsNullOrWhiteSpace(entry.StackTrace)
                ? entry.Message
                : $"{entry.Message}\n{entry.StackTrace}";
        }

        private void SetDetail(string text)
        {
            if (UnityObjectState.IsMissing(UI) ||
                UnityObjectState.IsMissing(UI.DetailText) ||
                UnityObjectState.IsMissing(UI.DetailScrollRect) ||
                UnityObjectState.IsMissing(UI.DetailScrollRect.content))
            {
                return;
            }

            UI.DetailText.text = text ?? string.Empty;
            UI.DetailText.ForceMeshUpdate();

            var viewport = UI.DetailScrollRect.viewport;
            var content = UI.DetailScrollRect.content;
            var detailRect = (RectTransform)UI.DetailText.transform;
            var viewportHeight = viewport != null ? viewport.rect.height : 0f;
            var contentHeight = Mathf.Max(viewportHeight, UI.DetailText.preferredHeight + DetailVerticalPadding);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
            detailRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0f, contentHeight - DetailVerticalPadding));
            UI.DetailScrollRect.verticalNormalizedPosition = 1f;
        }
    }
}
