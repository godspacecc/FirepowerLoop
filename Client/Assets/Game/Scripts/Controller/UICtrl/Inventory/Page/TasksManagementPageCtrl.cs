using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>管理宿主的任务分页；读取当前远征任务快照，不推进目标、授予撤离资格或保存任务历史。</summary>
    public sealed class TasksManagementPageCtrl : BasePageCtrl
    {
        private TasksManagementPageUI UI => (TasksManagementPageUI)BaseUI;
        private readonly List<IUnRegister> subscriptions = new List<IUnRegister>();
        private readonly List<ExpeditionTaskSnapshot> visibleTasks = new List<ExpeditionTaskSnapshot>();
        /// <summary>仅为本次显示的筛选与选择身份；隐藏时清除，不作为任务状态写回。</summary>
        private bool completed;
        private string selectedInstance;

        protected override void OnInit()
        {
            UI.ActiveButton.onClick.AddListener(() => SelectCategory(false));
            UI.CompletedButton.onClick.AddListener(() => SelectCategory(true));
            UI.TaskList.AddItemRefreshListener(RenderTask);
        }

        protected override void OnShow()
        {
            completed = false;
            subscriptions.Add(this.RegisterEvent<ExpeditionChangedEvent>(_ => Refresh()));
            subscriptions.Add(this.RegisterEvent<MapLoadingCompletedEvent>(_ => Refresh()));
            subscriptions.Add(this.RegisterEvent<MapCurrentReleasedEvent>(_ => Refresh()));
            subscriptions.Add(this.RegisterEvent<LanguageChangedEvent>(_ => Refresh()));
            Refresh();
        }

        /// <summary>显示订阅和列表选择随分页释放；任务权威快照继续由原系统管理。</summary>
        protected override void OnHide()
        {
            foreach (var item in subscriptions) item.UnRegister();
            subscriptions.Clear(); visibleTasks.Clear(); selectedInstance = null;
            UI.TaskList.ClearCells();
        }

        private void SelectCategory(bool value)
        {
            completed = value; selectedInstance = null;
            UI.DetailScroll.verticalNormalizedPosition = 1;
            Refresh();
        }

        /// <summary>只呈现当前离线地图与远征身份一致的任务，旧远征或联机世界不回退到本地记录。</summary>
        private void Refresh()
        {
            if (!IsShow()) return;
            var expedition = this.GetModel<ExpeditionModel>();
            var map = this.GetModel<MapSceneModel>();
            visibleTasks.Clear();
            if (expedition.IsActive && map.CurrentMode == MapPreparationMode.OfflineFull && map.CurrentMapId == expedition.CurrentMapId)
                visibleTasks.AddRange(this.GetModel<ExpeditionTaskModel>().Tasks.Where(t =>
                    t.MapId == expedition.CurrentMapId && t.ExpeditionId == expedition.CurrentExpeditionId &&
                    (t.Status == ExpeditionTaskStatus.Completed) == completed).OrderBy(t => t.Order));
            if (!visibleTasks.Any(t => t.InstanceId == selectedInstance))
            {
                selectedInstance = visibleTasks.FirstOrDefault()?.InstanceId;
                UI.DetailScroll.verticalNormalizedPosition = 1;
            }
            UI.ActiveSelected.SetActive(!completed); UI.CompletedSelected.SetActive(completed);
            UI.EmptyRoot.SetActive(visibleTasks.Count == 0);
            UI.EmptyTitleText.text = Loc.T(completed ? "ui.tasks.empty_completed" : "ui.tasks.empty_active");
            UI.TaskList.totalCount = visibleTasks.Count; UI.TaskList.RefreshCells();
            var task = visibleTasks.FirstOrDefault(t => t.InstanceId == selectedInstance);
            UI.DetailTitleText.text = task == null ? Loc.T("ui.tasks.select") : Name(task);
            UI.DetailStatusText.text = task == null ? string.Empty : Loc.T(task.Status == ExpeditionTaskStatus.Completed ? "ui.tasks.completed_state" : "ui.tasks.progress_state");
            UI.DetailBodyText.text = task == null ? string.Empty : Loc.T("ui.tasks.objective") + "\n" + Progress(task) + "\n\n" +
                Loc.T(task.ObjectiveType == global::TaskObjectiveType.Kill ? "ui.tasks.kill_description" : "ui.tasks.survive_description") +
                (task.Status == ExpeditionTaskStatus.Completed ? "\n\n" + Loc.T("ui.tasks.completed_description") : string.Empty);
        }

        /// <summary>覆盖复用格的全部显示和点击；回调仅允许当前绑定且仍在筛选结果中的实例。</summary>
        private void RenderTask(Transform item, int index)
        {
            var view = item.GetComponent<ManagementTaskBoxUI>(); var task = visibleTasks[index];
            view.SelectButton.onClick.RemoveAllListeners(); view.SelectButton.interactable = true;
            view.TitleText.text = Name(task); view.ProgressText.text = Progress(task);
            view.SelectedRoot.SetActive(task.InstanceId == selectedInstance);
            var owner = BaseUI; var identity = task.InstanceId;
            view.SelectButton.onClick.AddListener(() =>
            {
                if (!IsShow() || !ReferenceEquals(owner, BaseUI) || !visibleTasks.Any(t => t.InstanceId == identity)) return;
                selectedInstance = identity; UI.DetailScroll.verticalNormalizedPosition = 1; Refresh();
            });
        }

        private string Name(ExpeditionTaskSnapshot task)
        {
            if (this.GetUtility<IRuntimeConfigProvider>().TryGetExpeditionTasks(task.MapId, out var definitions, out _))
            {
                var definition = definitions.FirstOrDefault(d => d.Id == task.TaskId);
                if (definition != null) return string.IsNullOrEmpty(definition.NameKey) ? definition.Name : Loc.T(definition.NameKey);
            }
            return Loc.T("ui.tasks.unavailable");
        }

        private static string Progress(ExpeditionTaskSnapshot task) => task.ObjectiveType == global::TaskObjectiveType.Kill
            ? Loc.T("ui.tasks.kill_goal", Mathf.FloorToInt(task.CurrentAmount), Mathf.FloorToInt(task.TargetAmount))
            : Loc.T("ui.tasks.survive_goal", ExpeditionClockRules.FormatDuration(task.CurrentAmount), ExpeditionClockRules.FormatDuration(task.TargetAmount));
    }
}
