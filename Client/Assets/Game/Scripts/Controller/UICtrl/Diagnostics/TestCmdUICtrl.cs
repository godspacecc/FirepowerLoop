using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>
    /// 管理测试指令窗口的选择、提交和结果表现，指令规则由领域 System 决定；
    /// 安卓键盘采样只补充诊断证据，不修正 TMP 状态或修改指令事务。
    /// </summary>
    public sealed class TestCmdUICtrl : BaseUICtrl
    {
        /// <summary>仅记录安卓软键盘提交的最近 16 个边界快照，不改变输入与指令执行状态。</summary>
        private const int KeyboardDiagnosticCapacity = 16;
        private static readonly System.Reflection.FieldInfo SoftKeyboardField = typeof(TMPro.TMP_InputField)
            .GetField("m_SoftKeyboard", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private readonly Queue<string> keyboardDiagnosticSamples = new Queue<string>();
        /// <summary>全局日志订阅随 View 实例加载、销毁配对；隐藏后仍捕获提交回调返回时的异常。</summary>
        private bool keyboardDiagnosticsSubscribed;
        private bool keyboardSubmitObserved;
        /// <summary>每次提交最多汇总一次，防止 TMP 连续异常把同一份诊断重复刷屏。</summary>
        private bool keyboardExceptionReported;
        private int keyboardSubmitCallbackDepth;

        private const string NoResultText = "暂无结果。";
        private const string RecentLabelText = "最近";
        private const string EmptyRecentText = "暂无最近指令";
        private const string UnknownHistoryDescriptionText = "该历史指令没有可用的配置说明。";

        private string selectedCategoryId;
        private string selectedEntryId;
        private IReadOnlyList<TestCmdCategoryDefinition> catalogCategories = Array.Empty<TestCmdCategoryDefinition>();
        private IReadOnlyList<TestCmdEntryDefinition> catalogEntries = Array.Empty<TestCmdEntryDefinition>();
        private IReadOnlyList<TestCmdCategoryDefinition> serverCatalogCategories = Array.Empty<TestCmdCategoryDefinition>();
        private IReadOnlyList<TestCmdEntryDefinition> serverCatalogEntries = Array.Empty<TestCmdEntryDefinition>();
        private IReadOnlyList<CategoryData> categories = Array.Empty<CategoryData>();
        private IReadOnlyList<EntryData> entries = Array.Empty<EntryData>();
        private string selectedDetailsText = EmptyRecentText;
        private string selectedUsageText;
        private string selectedExampleText;
        /// <summary>覆盖软键盘等待下一帧及指令执行期，阻止按钮和重复提交重入。</summary>
        private bool executingFromSubmit;
        private bool serverCatalogRequestedForVisibleSession;
        private CancellationTokenSource visibleCancellation;
        private CancellationTokenSource serverCatalogCancellation;
        private CancellationTokenSource executeCancellation;
        private int displayGeneration;
        private int serverCatalogGeneration;
        private int submitGeneration;
        private readonly Dictionary<TestCmdCategoryBoxUI, CategoryData> categoryRows =
            new Dictionary<TestCmdCategoryBoxUI, CategoryData>();
        private readonly Dictionary<TestCmdEntryBoxUI, EntryData> entryRows =
            new Dictionary<TestCmdEntryBoxUI, EntryData>();
        private bool resultAreaReady;
        /// <summary>读取设备软键盘能力；测试替换此窄端口以验证手机收键盘和桌面聚焦两条路径。</summary>
        private Func<bool> softKeyboardSupported = () => TouchScreenKeyboard.isSupported;

        private TestCmdUI UI => BaseUI as TestCmdUI;

        public override bool BlocksGame => true;

        public override UILayer Layer()
        {
            // 测试窗口允许覆盖管理窗口与确认界面，继续由既有阻断门禁保护玩法输入。
            return UILayer.Top;
        }

        protected override BackgroundConfig GetBackgroundConfig()
        {
            return new BackgroundConfig
            {
                ColorType = BackgroundColorType.Transparent,
                ClickEffect = BackgroundClickEffect.None
            };
        }

        protected override string Path()
        {
            return "Diagnostics/TestCmd";
        }

        protected override void OnInit()
        {
            UI.BtnClose.onClick.AddListener(Hide);
            UI.BtnExecute.onClick.AddListener(ExecuteCurrentText);
            UI.InputCommand.onSubmit.AddListener(OnInputSubmit);
            if (Application.platform == RuntimePlatform.Android)
            {
                UI.InputCommand.onEndEdit.AddListener(OnKeyboardEndEdit);
                UI.InputCommand.onDeselect.AddListener(OnKeyboardDeselect);
                UI.InputCommand.onTouchScreenKeyboardStatusChanged.AddListener(OnKeyboardStatusChanged);
                Application.logMessageReceived += OnKeyboardDiagnosticLog;
                keyboardDiagnosticsSubscribed = true;
            }
            UI.CategoryList.AddItemRefreshListener(OnCategoryRefresh);
            UI.EntryList.AddItemRefreshListener(OnEntryRefresh);
            this.RegisterEvent<OnlineAccountChangedEvent>(OnOnlineAccountChanged)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            resultAreaReady = ValidateResultArea();
        }

        protected override void OnShow()
        {
            keyboardDiagnosticSamples.Clear();
            keyboardSubmitObserved = false;
            keyboardExceptionReported = false;
            CancelVisibleRequests();
            visibleCancellation = new CancellationTokenSource();
            selectedCategoryId = TestCmdModel.RecentCategoryId;
            selectedEntryId = null;
            selectedDetailsText = EmptyRecentText;
            selectedUsageText = null;
            selectedExampleText = null;
            serverCatalogRequestedForVisibleSession = false;
            serverCatalogCategories = Array.Empty<TestCmdCategoryDefinition>();
            serverCatalogEntries = Array.Empty<TestCmdEntryDefinition>();
            ClearRowBindings();
            RefreshCatalog();
            RefreshAll(updateSelectionForCategory: true);
            FocusInput();
            RefreshServerCatalogIfAuthenticated();
        }

        protected override void OnHide()
        {
            TraceKeyboardState("hide.beforeSelectionClear");
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            TraceKeyboardState("hide.afterSelectionClear");

            ResetSubmitGuard();
            CancelVisibleRequests();
            ClearRowBindings();
        }

        protected override void OnUnload()
        {
            // 销毁回调只解除外部订阅，不读取正在销毁的 View 或输入框。
            if (keyboardDiagnosticsSubscribed)
            {
                Application.logMessageReceived -= OnKeyboardDiagnosticLog;
                keyboardDiagnosticsSubscribed = false;
            }
            keyboardDiagnosticSamples.Clear();
            keyboardSubmitObserved = false;
            keyboardExceptionReported = false;
            ResetSubmitGuard();
            CancelVisibleRequests();
            ClearRowMappings();
        }

        private void ResetSubmitGuard()
        {
            executingFromSubmit = false;
            executeCancellation?.Cancel();
            executeCancellation?.Dispose();
            executeCancellation = null;
            submitGeneration++;
        }

        private void CancelVisibleRequests()
        {
            CancelServerCatalogRequest();
            serverCatalogRequestedForVisibleSession = false;
            visibleCancellation?.Cancel();
            visibleCancellation?.Dispose();
            visibleCancellation = null;
            displayGeneration++;
        }

        private void CancelServerCatalogRequest()
        {
            serverCatalogCancellation?.Cancel();
            serverCatalogCancellation?.Dispose();
            serverCatalogCancellation = null;
            serverCatalogGeneration++;
        }

        private void RefreshCatalog()
        {
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            if (!provider.TryGetTestCmdCatalog(out catalogCategories, out catalogEntries, out var error))
            {
                catalogCategories = Array.Empty<TestCmdCategoryDefinition>();
                catalogEntries = Array.Empty<TestCmdEntryDefinition>();
                selectedDetailsText = UnknownHistoryDescriptionText;
                SetResultText(error);
            }

            RebuildMergedCatalog();
        }

        private void RebuildMergedCatalog()
        {
            categories = new[] { new CategoryData(TestCmdModel.RecentCategoryId, 0, RecentLabelText) }
                .Concat(catalogCategories.Concat(serverCatalogCategories)
                .Select(item => new CategoryData(item.Id, item.Order, item.LabelText))
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Id, StringComparer.Ordinal))
                .ToArray();

            if (string.IsNullOrEmpty(selectedCategoryId) ||
                !categories.Any(item => string.Equals(item.Id, selectedCategoryId, StringComparison.Ordinal)))
            {
                selectedCategoryId = TestCmdModel.RecentCategoryId;
            }
        }

        private void RefreshServerCatalogIfAuthenticated()
        {
            if (!IsAuthenticated() || serverCatalogRequestedForVisibleSession)
            {
                return;
            }

            CancelServerCatalogRequest();
            serverCatalogRequestedForVisibleSession = true;
            serverCatalogCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                visibleCancellation?.Token ?? CancellationToken.None);
            int generation = serverCatalogGeneration;
            RefreshServerCatalogAsync(displayGeneration, generation, serverCatalogCancellation.Token).Forget();
        }

        private async UniTask RefreshServerCatalogAsync(int visibleGeneration, int requestGeneration, CancellationToken cancellationToken)
        {
            ServerTestCmdCatalogSnapshot snapshot = await this.GetSystem<ServerTestCmdSystem>().RefreshCatalogAsync(cancellationToken);
            if (visibleGeneration != displayGeneration || requestGeneration != serverCatalogGeneration ||
                cancellationToken.IsCancellationRequested || !IsShow() || UnityObjectState.IsMissing(UI) || !IsAuthenticated()) return;
            if (snapshot.Status == ServerTestCmdCatalogStatus.Available)
            {
                serverCatalogCategories = snapshot.Categories.Select(x => new TestCmdCategoryDefinition(x.Id, x.Order, x.LabelText)).ToArray();
                serverCatalogEntries = snapshot.Entries.Select(x => new TestCmdEntryDefinition(x.Id, x.CategoryId, x.Order, x.LabelText,
                    x.CommandTemplate, x.DescriptionText, x.UsageText, x.ExampleText)).ToArray();
            }
            else
            {
                string status = string.IsNullOrWhiteSpace(snapshot.StatusText) ? "服务器目录获取失败" : snapshot.StatusText;
                serverCatalogCategories = new[] { new TestCmdCategoryDefinition("server.status", int.MaxValue - 1, "服务器") };
                serverCatalogEntries = new[] { new TestCmdEntryDefinition("server.status", "server.status", 0, status, string.Empty, status, string.Empty, string.Empty) };
            }
            RebuildMergedCatalog();
            RefreshAll(updateSelectionForCategory: false);
        }

        private void OnOnlineAccountChanged(OnlineAccountChangedEvent _)
        {
            if (!IsShow() || UnityObjectState.IsMissing(UI))
            {
                return;
            }

            if (IsAuthenticated())
            {
                RefreshServerCatalogIfAuthenticated();
                RefreshAll(updateSelectionForCategory: false);
                return;
            }

            if (!serverCatalogRequestedForVisibleSession &&
                serverCatalogCategories.Count == 0 &&
                serverCatalogEntries.Count == 0)
            {
                return;
            }

            CancelServerCatalogRequest();
            serverCatalogRequestedForVisibleSession = false;
            serverCatalogCategories = Array.Empty<TestCmdCategoryDefinition>();
            serverCatalogEntries = Array.Empty<TestCmdEntryDefinition>();
            RebuildMergedCatalog();
            RefreshAll(updateSelectionForCategory: false);
        }

        private bool IsAuthenticated()
        {
            return this.GetModel<OnlineAccountModel>().Snapshot.Value.IsAuthenticated;
        }

        private void RefreshAll(bool updateSelectionForCategory)
        {
            if (UnityObjectState.IsMissing(UI))
            {
                return;
            }

            SetResultText(this.GetModel<TestCmdModel>().HasLastResult
                ? this.GetModel<TestCmdModel>().LastResult.Message
                : NoResultText);

            RefreshEntries(updateSelectionForCategory);
            RefreshDetails();
            RefreshCategories();
        }

        private void RefreshCategories()
        {
            ClearCategoryBindings();
            UI.CategoryList.totalCount = categories.Count;
            UI.CategoryList.RefreshCells();
        }

        private void RefreshEntries(bool updateSelectionForCategory)
        {
            ClearEntryBindings();
            if (selectedCategoryId == TestCmdModel.RecentCategoryId)
            {
                entries = this.GetModel<TestToolSettingsModel>().RecentCommands
                    .Where(command => IsAuthenticated() || !command.TrimStart().StartsWith("$", StringComparison.Ordinal))
                    .Select(command =>
                    {
                        var details = ResolveHistoryDetails(command);
                        return new EntryData(
                            command,
                            command,
                            command,
                            details.DescriptionText,
                            details.UsageText,
                            details.ExampleText,
                            true);
                    })
                    .ToArray();
            }
            else
            {
                entries = catalogEntries.Concat(serverCatalogEntries)
                    .Where(item => string.Equals(item.CategoryId, selectedCategoryId, StringComparison.Ordinal))
                    .OrderBy(item => item.Order)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .Select(item => new EntryData(
                        item.Id,
                        item.LabelText,
                        item.CommandTemplate,
                        item.DescriptionText,
                        item.UsageText,
                        item.ExampleText,
                        false))
                    .ToArray();
            }

            if (updateSelectionForCategory)
            {
                SelectDefaultEntryForCurrentCategory(writeTemplate: selectedCategoryId != TestCmdModel.RecentCategoryId);
            }
            else if (!entries.Any(item => string.Equals(item.Id, selectedEntryId, StringComparison.Ordinal)))
            {
                SelectDefaultEntryForCurrentCategory(writeTemplate: false);
            }
            else
            {
                foreach (var entry in entries)
                {
                    if (string.Equals(entry.Id, selectedEntryId, StringComparison.Ordinal))
                    {
                        SelectEntry(entry, writeTemplate: false, focusInput: false);
                        break;
                    }
                }
            }

            UI.EntryList.totalCount = entries.Count;
            UI.EntryList.RefreshCells();
        }

        private void SelectDefaultEntryForCurrentCategory(bool writeTemplate)
        {
            if (entries.Count == 0)
            {
                selectedEntryId = null;
                selectedDetailsText = selectedCategoryId == TestCmdModel.RecentCategoryId
                    ? EmptyRecentText
                    : UnknownHistoryDescriptionText;
                selectedUsageText = null;
                selectedExampleText = null;
                return;
            }

            SelectEntry(entries[0], writeTemplate, focusInput: false);
        }

        private void OnCategoryRefresh(Transform transform, int index)
        {
            var row = transform.GetComponent<TestCmdCategoryBoxUI>();
            if (row == null)
            {
                Log.Error("[TestCmdUICtrl] 分类行缺少 View，已停止该行绑定。");
                return;
            }

            ClearCategoryBinding(row);
            if (index < 0 || index >= categories.Count)
            {
                Log.Error("[TestCmdUICtrl] 分类行索引无效，已解除旧行绑定。");
                return;
            }

            var category = categories[index];
            var isSelected = string.Equals(category.Id, selectedCategoryId, StringComparison.Ordinal);
            row.SelectedRoot.SetActive(isSelected);
            row.TextLabel.text = category.LabelText;
            categoryRows[row] = category;
            row.BtnSelect.OnClickEvent(OnCategoryRowClick, row);
        }

        private void OnEntryRefresh(Transform transform, int index)
        {
            var row = transform.GetComponent<TestCmdEntryBoxUI>();
            if (row == null)
            {
                Log.Error("[TestCmdUICtrl] 指令行缺少 View，已停止该行绑定。");
                return;
            }

            ClearEntryBinding(row);
            if (index < 0 || index >= entries.Count)
            {
                Log.Error("[TestCmdUICtrl] 指令行索引无效，已解除旧行绑定。");
                return;
            }

            var entry = entries[index];
            var isSelected = string.Equals(entry.Id, selectedEntryId, StringComparison.Ordinal);
            row.SelectedRoot.SetActive(isSelected);
            row.TextCommand.text = entry.Label;
            entryRows[row] = entry;
            row.BtnSelect.OnClickEvent(OnEntryRowClick, row);
        }

        private void OnCategoryRowClick(object[] args)
        {
            if (!IsShow() || !TryGetMappedRow(args, categoryRows, out var category))
            {
                return;
            }

            SelectCategory(category);
        }

        private void OnEntryRowClick(object[] args)
        {
            if (!IsShow() || !TryGetMappedRow(args, entryRows, out var entry))
            {
                return;
            }

            SelectEntryFromRow(entry);
        }

        private void SelectCategory(CategoryData category)
        {
            selectedCategoryId = category.Id;
            selectedEntryId = null;
            RefreshAll(updateSelectionForCategory: true);
            FocusInput();
        }

        private void SelectEntryFromRow(EntryData entry)
        {
            SelectEntry(entry, writeTemplate: true, focusInput: true);
            UI.EntryList.RefreshCells();
        }

        private void SelectEntry(EntryData entry, bool writeTemplate, bool focusInput)
        {
            selectedEntryId = entry.Id;
            selectedDetailsText = entry.DescriptionText;
            selectedUsageText = entry.UsageText;
            selectedExampleText = entry.ExampleText;
            if (writeTemplate)
            {
                UI.InputCommand.text = entry.Command;
                UI.InputCommand.caretPosition = UI.InputCommand.text.Length;
            }

            RefreshDetails();
            if (focusInput)
            {
                FocusInput();
            }
        }

        private void ExecuteCurrentText()
        {
            if (executingFromSubmit)
            {
                return;
            }

            ExecuteCurrentTextAsync(UI.InputCommand.text).Forget();
        }

        /// <summary>执行当前指令；日志窗口请求先隐藏顶层指令窗，再显示 Dialog 日志窗。</summary>
        private async UniTask ExecuteCurrentTextAsync(string submittedText)
        {
            executingFromSubmit = true;
            int generation = ++submitGeneration;
            executeCancellation?.Cancel(); executeCancellation?.Dispose();
            executeCancellation = CancellationTokenSource.CreateLinkedTokenSource(visibleCancellation?.Token ?? CancellationToken.None);
            try
            {
                bool wasRecent = selectedCategoryId == TestCmdModel.RecentCategoryId;
                TestCmdExecutionResult result = await this.SendCommand(new ExecuteTestCmdCommand
                { RawText = submittedText, CancellationToken = executeCancellation.Token });
                if (generation != submitGeneration || !IsShow() || UnityObjectState.IsMissing(UI)) return;
                TraceKeyboardState($"execute.result.{result.Code}");
                if (result.Success && result.Code == TestCmdResultCode.AppearanceGalleryOpened)
                {
                    Hide();
                    return;
                }
                if (result.Success && result.Code == TestCmdResultCode.RuntimeLogOpenRequested)
                {
                    Hide();
                    UIHelper.ShowUI<RuntimeLogUICtrl>();
                    return;
                }
                SetResultText(result.Message);
                TryShowCloneGainTip(result);
                RefreshEntries(updateSelectionForCategory: wasRecent);
                TraceKeyboardState("execute.afterEntryRefresh");
                RefreshDetails();
                RestoreInputAfterExecution();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Log.Error($"[TestCmdUICtrl] 执行失败: {exception}"); }
            finally
            {
                if (generation == submitGeneration)
                {
                    executingFromSubmit = false;
                    executeCancellation?.Dispose(); executeCancellation = null;
                }
            }
        }

        /// <summary>提交回调只捕获文本并预约执行，避免在 TMP LateUpdate 内停用输入框或重开软键盘。</summary>
        private void OnInputSubmit(string submittedText)
        {
            if (executingFromSubmit || !IsShow() || UnityObjectState.IsMissing(UI)) return;
            executingFromSubmit = true;
            int generation = ++submitGeneration;
            var owner = UI;
            var token = visibleCancellation?.Token ?? CancellationToken.None;
            keyboardSubmitObserved = true;
            keyboardExceptionReported = false;
            keyboardSubmitCallbackDepth++;
            TraceKeyboardState("submit.enter");
            try
            {
                ExecuteSubmittedTextNextFrameAsync(submittedText, owner, generation, token).Forget();
            }
            finally
            {
                TraceKeyboardState("submit.exit");
                keyboardSubmitCallbackDepth--;
            }
        }

        /// <summary>让 TMP 先完成软键盘状态处理；隐藏、销毁或重开后，过期提交不执行也不解除新请求的门禁。</summary>
        private async UniTask ExecuteSubmittedTextNextFrameAsync(string submittedText, TestCmdUI owner,
            int generation, CancellationToken cancellationToken)
        {
            try
            {
                await UniTask.NextFrame(PlayerLoopTiming.Update, cancellationToken);
                if (cancellationToken.IsCancellationRequested || generation != submitGeneration || !IsShow() ||
                    UnityObjectState.IsMissing(owner) || !ReferenceEquals(UI, owner)) return;

                TraceKeyboardState("submit.deferredExecute");
                await ExecuteCurrentTextAsync(submittedText);
            }
            catch (OperationCanceledException)
            {
                // 正常关闭取消等待；旧回调不得触碰已重开窗口的执行状态。
            }
            finally
            {
                if (generation == submitGeneration) executingFromSubmit = false;
            }
        }

        private void OnKeyboardEndEdit(string _) => TraceKeyboardState("event.endEdit");
        private void OnKeyboardDeselect(string _) => TraceKeyboardState("event.deselect");
        private void OnKeyboardStatusChanged(TouchScreenKeyboard.Status status) => TraceKeyboardState($"event.keyboard.{status}");

        /// <summary>在主线程读取 TMP 3.0.7 的键盘引用；读取失败只记诊断缺口，不影响原提交回调。</summary>
        private void TraceKeyboardState(string phase)
        {
            if (!keyboardDiagnosticsSubscribed) return;
            try
            {
                var field = UnityObjectState.IsMissing(UI) ? null : UI.InputCommand;
                var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
                var keyboard = field == null ? null : SoftKeyboardField?.GetValue(field) as TouchScreenKeyboard;
                var sample = $"frame={Time.frameCount} phase={phase} callbackDepth={keyboardSubmitCallbackDepth} " +
                    $"generation={submitGeneration} selected={(selected == null ? "null" : selected.name + ":" + selected.GetInstanceID())} " +
                    (field == null ? "field=missing" :
                    $"field={field.GetInstanceID()} active={field.gameObject.activeInHierarchy} enabled={field.enabled} " +
                    $"focused={field.isFocused} interactable={field.interactable} textLength={field.text.Length} " +
                    $"keyboard={(SoftKeyboardField == null ? "unavailable" : keyboard == null ? "null" : keyboard.status + "/active=" + keyboard.active)}");
                AppendKeyboardDiagnostic(sample);
            }
            catch (Exception exception)
            {
                // 不递归报告采样异常，也不让诊断本身中断 TMP 的键盘状态处理。
                AppendKeyboardDiagnostic($"frame={Time.frameCount} phase={phase} readFailed={exception.GetType().Name}");
            }
        }

        private void AppendKeyboardDiagnostic(string sample)
        {
            if (keyboardDiagnosticSamples.Count >= KeyboardDiagnosticCapacity) keyboardDiagnosticSamples.Dequeue();
            keyboardDiagnosticSamples.Enqueue(sample);
        }

        /// <summary>只补充已观察提交后的目标 TMP 异常；汇总进入既有错误导出和落盘链路。</summary>
        private void OnKeyboardDiagnosticLog(string message, string stackTrace, LogType type)
        {
            if (!keyboardSubmitObserved || keyboardExceptionReported || (type != LogType.Exception && type != LogType.Error) ||
                (message ?? string.Empty).StartsWith("[TestCmdKeyboardDiagnostic]", StringComparison.Ordinal) ||
                string.IsNullOrEmpty(stackTrace) ||
                !stackTrace.Contains("TMPro.TMP_InputField.SendTouchScreenKeyboardStatusChanged")) return;

            TraceKeyboardState("exception.observed");
            keyboardExceptionReported = true;
            Log.Error("[TestCmdKeyboardDiagnostic] 安卓提交软键盘异常的最近状态：\n" +
                string.Join("\n", keyboardDiagnosticSamples));
        }

        private void RefreshDetails()
        {
            UI.TextDetails.text = selectedDetailsText;
            if (UI.TextUsage != null)
            {
                UI.TextUsage.text = string.IsNullOrWhiteSpace(selectedUsageText) ? string.Empty : selectedUsageText;
            }

            if (UI.TextExample != null)
            {
                UI.TextExample.text = string.IsNullOrWhiteSpace(selectedExampleText) ? string.Empty : selectedExampleText;
            }
        }

        private void SetResultText(string text)
        {
            if (!resultAreaReady)
            {
                return;
            }

            UI.TextResult.text = string.IsNullOrWhiteSpace(text) ? NoResultText : text;
            RefreshResultTextHeight();
        }

        private void TryShowCloneGainTip(TestCmdExecutionResult result)
        {
            if (!result.Success ||
                (result.Code != TestCmdResultCode.CloneComplete && result.Code != TestCmdResultCode.ClonePartial) ||
                result.AffectedItemId <= 0 ||
                result.AffectedAmount <= 0)
            {
                return;
            }

            try
            {
                Notify.ShowFloatTips(Texts.TipItemGained(ResolveAffectedItemName(result.AffectedItemId), result.AffectedAmount));
            }
            catch (Exception exception)
            {
                Log.Error($"[TestCmdUICtrl] 显示克隆获得提示失败: {exception}");
            }
        }

        private string ResolveAffectedItemName(int itemId)
        {
            try
            {
                if (this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out _))
                {
                    if (!string.IsNullOrWhiteSpace(item.NameKey))
                    {
                        var localized = Loc.T(item.NameKey);
                        if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, item.NameKey, StringComparison.Ordinal))
                        {
                            return localized;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(item.Name))
                    {
                        return item.Name;
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error($"[TestCmdUICtrl] 解析克隆物品名称失败: {exception}");
            }

            return itemId.ToString(CultureInfo.InvariantCulture);
        }

        private void RefreshResultTextHeight()
        {
            if (!resultAreaReady)
            {
                return;
            }

            var width = UI.ResultViewportRect.rect.width > 1f ? UI.ResultViewportRect.rect.width : 476f;
            var preferredHeight = UI.TextResult.GetPreferredValues(UI.TextResult.text, width, 0f).y;
            var height = Mathf.Max(UI.ResultViewportRect.rect.height, Mathf.Ceil(preferredHeight));
            UI.ResultContentRect.sizeDelta = new Vector2(UI.ResultContentRect.sizeDelta.x, height);
            UI.TextResult.rectTransform.sizeDelta = new Vector2(0f, height);
            UI.ResultContentRect.anchoredPosition = Vector2.zero;
            UI.ResultScrollRect.verticalNormalizedPosition = 1f;
        }

        private bool ValidateResultArea()
        {
            if (UI.ResultScrollRect == null || UI.ResultViewportRect == null ||
                UI.ResultContentRect == null || UI.TextResult == null ||
                UI.ResultScrollRect.viewport != UI.ResultViewportRect ||
                UI.ResultScrollRect.content != UI.ResultContentRect ||
                UI.TextResult.transform.parent != UI.ResultContentRect)
            {
                Log.Error("[TestCmdUICtrl] 结果区域 Prefab 接线缺失或不一致，已停止结果区域更新。");
                return false;
            }

            return true;
        }

        private static bool TryGetMappedRow<TRow, TData>(object[] args, Dictionary<TRow, TData> rows, out TData value)
            where TRow : class
        {
            value = default;
            return args != null && args.Length == 1 && args[0] is TRow row && rows.TryGetValue(row, out value);
        }

        private void ClearRowBindings()
        {
            ClearCategoryBindings();
            ClearEntryBindings();
        }

        private void ClearRowMappings()
        {
            categoryRows.Clear();
            entryRows.Clear();
        }

        private void ClearCategoryBindings()
        {
            foreach (var row in categoryRows.Keys.ToArray())
            {
                ClearCategoryBinding(row);
            }

            categoryRows.Clear();
        }

        private void ClearEntryBindings()
        {
            foreach (var row in entryRows.Keys.ToArray())
            {
                ClearEntryBinding(row);
            }

            entryRows.Clear();
        }

        private void ClearCategoryBinding(TestCmdCategoryBoxUI row)
        {
            categoryRows.Remove(row);
            if (row != null)
            {
                row.BtnSelect.RemoveAllOnClickEvents();
            }
        }

        private void ClearEntryBinding(TestCmdEntryBoxUI row)
        {
            entryRows.Remove(row);
            if (row != null)
            {
                row.BtnSelect.RemoveAllOnClickEvents();
            }
        }

        /// <summary>手机完成指令后释放输入焦点，让用户点击输入框再编辑；桌面保留连续输入的自动聚焦。</summary>
        private void RestoreInputAfterExecution()
        {
            if (!softKeyboardSupported())
            {
                FocusInput();
                return;
            }

            UI.InputCommand.DeactivateInputField();
            var events = EventSystem.current;
            // 只清理本输入框的选择，不夺走按钮或其它窗口已取得的焦点。
            if (events != null && events.currentSelectedGameObject == UI.InputCommand.gameObject)
                events.SetSelectedGameObject(null);
            TraceKeyboardState("execute.keyboardReleased");
        }

        /// <summary>桌面自动定位输入光标；软键盘设备仅由用户点击输入框激活，窗口或模板刷新不主动弹键盘。</summary>
        private void FocusInput()
        {
            if (UI.InputCommand == null || softKeyboardSupported())
            {
                return;
            }

            TraceKeyboardState("focus.beforeSelection");

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(UI.InputCommand.gameObject);
            }

            TraceKeyboardState("focus.afterSelection");

            UI.InputCommand.ActivateInputField();
            TraceKeyboardState("focus.afterActivate");
            UI.InputCommand.MoveTextEnd(false);
            UI.InputCommand.stringPosition = UI.InputCommand.text.Length;
            UI.InputCommand.selectionStringAnchorPosition = UI.InputCommand.text.Length;
            UI.InputCommand.selectionStringFocusPosition = UI.InputCommand.text.Length;
            UI.InputCommand.selectionAnchorPosition = UI.InputCommand.text.Length;
            UI.InputCommand.selectionFocusPosition = UI.InputCommand.text.Length;
            UI.InputCommand.caretPosition = UI.InputCommand.text.Length;
        }

        private EntryDetails ResolveHistoryDetails(string command)
        {
            var value = (command ?? string.Empty).Trim();
            var match = catalogEntries.Concat(serverCatalogEntries)
                .Select(entry => new
                {
                    Entry = entry,
                    Prefix = (entry.CommandTemplate ?? string.Empty).TrimEnd()
                })
                .Where(item => item.Prefix.Length > 0 &&
                    (string.Equals(value, item.Prefix, StringComparison.Ordinal) ||
                     value.StartsWith(item.Prefix + " ", StringComparison.Ordinal) ||
                     value.StartsWith(item.Prefix + "\t", StringComparison.Ordinal) ||
                     value.StartsWith(item.Prefix + "\r", StringComparison.Ordinal) ||
                     value.StartsWith(item.Prefix + "\n", StringComparison.Ordinal)))
                .OrderByDescending(item => item.Prefix.Length)
                .ThenBy(item => item.Entry.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            return match == null
                ? new EntryDetails(UnknownHistoryDescriptionText, null, null)
                : new EntryDetails(match.Entry.DescriptionText, match.Entry.UsageText, match.Entry.ExampleText);
        }

        private readonly struct CategoryData
        {
            public CategoryData(string id, int order, string labelText)
            {
                Id = id;
                Order = order;
                LabelText = labelText;
            }

            public string Id { get; }
            public int Order { get; }
            public string LabelText { get; }
        }

        private readonly struct EntryData
        {
            public EntryData(
                string id,
                string label,
                string command,
                string descriptionText,
                string usageText,
                string exampleText,
                bool isHistory)
            {
                Id = string.IsNullOrEmpty(id) ? command ?? string.Empty : id;
                Label = label ?? command ?? string.Empty;
                Command = command ?? string.Empty;
                DescriptionText = string.IsNullOrWhiteSpace(descriptionText)
                    ? UnknownHistoryDescriptionText
                    : descriptionText;
                UsageText = usageText;
                ExampleText = exampleText;
                IsHistory = isHistory;
            }

            public string Id { get; }
            public string Label { get; }
            public string Command { get; }
            public string DescriptionText { get; }
            public string UsageText { get; }
            public string ExampleText { get; }
            public bool IsHistory { get; }
        }

        private readonly struct EntryDetails
        {
            public EntryDetails(string descriptionText, string usageText, string exampleText)
            {
                DescriptionText = descriptionText;
                UsageText = usageText;
                ExampleText = exampleText;
            }

            public string DescriptionText { get; }
            public string UsageText { get; }
            public string ExampleText { get; }
        }

    }
}
