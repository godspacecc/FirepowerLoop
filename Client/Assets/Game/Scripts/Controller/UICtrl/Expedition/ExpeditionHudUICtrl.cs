using System;
using Cysharp.Threading.Tasks;
using QFramework;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// 展示远征状态并提交返回基地意图；不负责地图内容或玩家状态。
    /// </summary>
    public sealed class ExpeditionHudUICtrl : BaseUICtrl
    {
        private ExpeditionHudUI UI => BaseUI as ExpeditionHudUI;
        private bool MobileMode => this.GetSystem<MobileInputSystem>().IsMobileInputMode;
        private Button ReturnButton => MobileMode ? UI.MobileBtnReturn : UI.BtnReturn;
        private TMP_Text TimerText => MobileMode ? UI.MobileTimerText : UI.TimerText;
        private TMP_Text StatusText => MobileMode ? UI.MobileStatusText : UI.StatusText;
        private TMP_Text ReturnButtonText => MobileMode ? UI.MobileReturnButtonText : UI.ReturnButtonText;
        private TMP_Text ResourceText => MobileMode ? UI.MobileResourceText : UI.ResourceText;
        private bool returning;
        private IUnRegister expeditionChangedUnregister;
        private IUnRegister extractionCreatedUnregister;
        private IUnRegister extractionRemovedUnregister;
        private int lastDisplayedMinute = -1;
        private int returnGeneration;

        public override UILayer Layer()
        {
            return UILayer.HUD;
        }

        public override bool BlocksGame => false;

        protected override string Path()
        {
            return "Expedition/ExpeditionHud";
        }

        protected override void OnInit()
        {
            UI.BtnReturn.onClick.AddListener(OnReturnClick);
            UI.MobileBtnReturn.onClick.AddListener(OnReturnClick);
            this.RegisterEvent<MobileInputModeChangedEvent>(_ => { if (IsShow()) Refresh(forceTimer: true); })
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(OnLanguageChanged)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            returning = false;
            lastDisplayedMinute = -1;
            expeditionChangedUnregister = Game.Interface
                .RegisterEvent<ExpeditionChangedEvent>(OnExpeditionChanged);
            extractionCreatedUnregister = Game.Interface
                .RegisterEvent<ExtractionCreatedEvent>(_ => Refresh());
            extractionRemovedUnregister = Game.Interface
                .RegisterEvent<ExtractionRemovedEvent>(_ => Refresh());
            LifeCycle.RegisterUpdate(RefreshTimerOnly);
            Refresh(forceTimer: true);
        }

        protected override void OnHide()
        {
            returnGeneration++;
            returning = false;
            ReleaseVisibleSubscriptions();
        }

        protected override void OnUnload()
        {
            ReleaseVisibleSubscriptions();
        }

        private void ReleaseVisibleSubscriptions()
        {
            expeditionChangedUnregister?.UnRegister();
            expeditionChangedUnregister = null;
            extractionCreatedUnregister?.UnRegister();
            extractionCreatedUnregister = null;
            extractionRemovedUnregister?.UnRegister();
            extractionRemovedUnregister = null;
            LifeCycle.UnregisterUpdate(RefreshTimerOnly);
        }

        private void RefreshTimerOnly()
        {
            var model = this.GetModel<ExpeditionModel>();
            var displayedMinute = (int)model.ClockGameMinutes;
            if (displayedMinute != lastDisplayedMinute)
            {
                Refresh(forceTimer: true);
            }
        }

        private void Refresh(bool forceTimer = false)
        {
            var model = this.GetModel<ExpeditionModel>();
            if (!model.IsActive)
            {
                Hide();
                return;
            }

            // 只切换 Prefab 表现；任务进度、撤离资格和返回事务继续读取原模型与系统。
            UI.DesktopHudPanel.SetActive(!MobileMode);
            UI.MobileHudPanel.SetActive(MobileMode);

            if (forceTimer)
            {
                lastDisplayedMinute = (int)model.ClockGameMinutes;
                var tasks = this.GetModel<ExpeditionTaskModel>().Tasks;
                if (tasks.Count == 0)
                {
                    TimerText.text = FormatDayTime(model.ClockGameMinutes);
                }
                else
                {
                    var task = tasks[0];
                    TimerText.text = task.ObjectiveType == global::TaskObjectiveType.Kill
                        ? Texts.ExpeditionTaskClock(FormatDayTime(model.ClockGameMinutes), UnityEngine.Mathf.FloorToInt(task.CurrentAmount), UnityEngine.Mathf.FloorToInt(task.TargetAmount))
                        : Texts.ExpeditionSurvivalClock(FormatDayTime(model.ClockGameMinutes), ExpeditionClockRules.FormatDuration(task.TargetAmount));
                }
            }

            if (model.Status == ExpeditionStatus.Defeated)
            {
                ReturnButton.gameObject.SetActive(true);
                StatusText.text = Texts.ExpeditionStatusDefeated;
                ReturnButtonText.text = Texts.ExpeditionRetryReturn;
                ReturnButton.interactable = !returning;
            }
            else
            {
                var hasEntityExtraction = HasCurrentEntityExtraction();
                ReturnButton.gameObject.SetActive(!hasEntityExtraction);
                StatusText.text = model.CanReturn && hasEntityExtraction
                    ? Texts.ExpeditionStatusGoToExtraction
                    : model.CanReturn
                    ? Texts.ExpeditionStatusReturnOpen
                    : Texts.ExpeditionStatusContinueGoal;
                ReturnButtonText.text = returning
                    ? Texts.ExpeditionReturning
                    : Texts.ExpeditionReturn;
                ReturnButton.interactable = model.CanReturn && !returning;
                if(model.ValleyLayout?.GenerationVersion==3)
                {ReturnButton.gameObject.SetActive(false);StatusText.text=Loc.T("ui.valley.extraction_rule");}
            }

            ResourceText.text = string.Empty;
        }

        private void OnReturnClick()
        {
            ReturnToBaseAsync().Forget();
        }

        private async UniTask ReturnToBaseAsync()
        {
            if (returning)
            {
                return;
            }

            var initialModel = this.GetModel<ExpeditionModel>();
            if (initialModel.Status != ExpeditionStatus.Defeated && HasCurrentEntityExtraction())
            {
                Refresh();
                return;
            }

            var generation = ++returnGeneration;
            returning = true;
            try
            {
                Refresh();
                var model = this.GetModel<ExpeditionModel>();
                var result = model.Status == ExpeditionStatus.Defeated
                    ? await this.GetSystem<ExpeditionFlowSystem>().FailExpeditionAndReturnToBaseAsync()
                    : await this.GetSystem<ExpeditionFlowSystem>().ReturnToBaseAsync();
                if (generation != returnGeneration || UI == null)
                {
                    return;
                }

                if (!result.Success)
                {
                    Log.Error($"[ExpeditionHudUICtrl] 返回基地失败：{result.Error}");
                    return;
                }

                Hide();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Log.Error($"[ExpeditionHudUICtrl] 返回基地异常：generation={generation} exception={exception}");
            }
            finally
            {
                if (generation == returnGeneration && UI != null)
                {
                    returning = false;
                    try
                    {
                        Refresh();
                    }
                    catch (Exception exception)
                    {
                        Log.Error($"[ExpeditionHudUICtrl] 恢复返回按钮状态异常：generation={generation} exception={exception}");
                    }
                }
            }
        }

        private void OnExpeditionChanged(ExpeditionChangedEvent evt)
        {
            Refresh(forceTimer: true);
        }

        private void OnLanguageChanged(LanguageChangedEvent evt)
        {
            Refresh(forceTimer: true);
        }

        private bool HasCurrentEntityExtraction()
        {
            // 该查询只关闭非死亡状态的 HUD 直接返回捷径，并在点击时再次执行以防绕过。
            // 必须以当前 MapInstanceId 和仍活动的撤离 Unit 为准；它不复制或产生第二份撤离资格。
            var mapInstanceId = this.GetSystem<MapSceneSystem>().CurrentMapInstanceId;
            if (mapInstanceId <= 0) return false;
            var units = this.GetSystem<UnitSystem>();
            foreach (var pair in this.GetModel<ExtractionModel>().Extractions)
            {
                var extraction = pair.Value;
                if (extraction.MapInstanceId != mapInstanceId ||
                    !units.TryGet(extraction.UnitId, out var unit)) continue;
                if (unit.UnitType == UnitType.ExtractionPoint && unit.IsActive &&
                    unit.MapInstanceId == mapInstanceId) return true;
            }
            return false;
        }

        private static string FormatDayTime(float elapsedGameMinutes)
        {
            var totalMinutes = UnityEngine.Mathf.FloorToInt(elapsedGameMinutes);
            var day = totalMinutes / (int)PlayerConditionRules.GameMinutesPerDay + 1;
            var minuteOfDay = totalMinutes % (int)PlayerConditionRules.GameMinutesPerDay;
            var hour = minuteOfDay / 60;
            var minute = minuteOfDay % 60;
            return Texts.ExpeditionDayClock(day, $"{hour:00}:{minute:00}");
        }

    }
}
