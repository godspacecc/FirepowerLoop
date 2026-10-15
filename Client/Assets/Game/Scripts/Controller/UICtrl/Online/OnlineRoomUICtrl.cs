using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine.EventSystems;
using UnityEngine;
using TMPro;

namespace Game
{
    /// <summary>展示房间与聊天快照、管理大厅交互及视图生命周期；不裁定成员、聊天或进入世界的业务权威。</summary>
    public sealed class OnlineRoomUICtrl : BaseUICtrl
    {
        private const float ChatBottomFollowTolerance = 0.02f;

        private OnlineRoomUI UI => BaseUI as OnlineRoomUI;
        private int operationGeneration;
        private int chatGeneration;
        private bool chatSubmitInFlight;
        private OnlineRoomSnapshot latest = OnlineRoomSnapshot.Empty();
        private OnlinePublicRoomListSnapshot latestPublicRooms = OnlinePublicRoomListSnapshot.Empty();
        private OnlineRoomChatSnapshot latestChat = OnlineRoomChatSnapshot.Empty();
        private OnlineWorldSnapshot latestWorld = OnlineWorldSnapshot.Empty();
        private readonly List<OnlineRoomChatMessageSnapshot> chatMessages = new List<OnlineRoomChatMessageSnapshot>();
        private long displayedChatRoomId;
        private bool chatListFilled;
        private bool publicRoomListFilled;
        private bool isVisible;
        private bool worldSubmitInFlight;
        private bool transitPreparing;
        private long transitAttemptedRunId;
        private int transitAttemptedArena;
        private OnlineRoomOperationState lastNoticeOperationState;
        private OnlineRoomResultCode lastNoticeResultCode;
        private static bool requestArcadeMode;
        private bool arcadeMode;
        /// <summary>仅属于当前界面的聊天展开状态；隐藏、离房或换房时重置。</summary>
        private bool chatExpanded;
        /// <summary>当前房间已观察的最大消息序号，用于过滤重复快照；0 表示尚未建立历史基线。</summary>
        private long observedChatSequence;
        private bool chatHistoryObserved;
        /// <summary>历史基线后、面板收起时收到的消息数；打开面板即清零，不写入业务 Model。</summary>
        private int unreadChatCount;

        public static void ShowArcadeLobby()
        {
            requestArcadeMode = true;
            UIHelper.ShowUI<OnlineRoomUICtrl>();
        }

        public override UILayer Layer()
        {
            return UILayer.Dialog;
        }

        protected override string Path()
        {
            return "Online/OnlineRoom";
        }

        protected override void OnInit()
        {
            UI.CreateButton.onClick.AddListener(() => SubmitCreate().Forget());
            UI.JoinButton.onClick.AddListener(() => SubmitJoin().Forget());
            UI.RefreshRoomListButton.onClick.AddListener(() => SubmitRefreshOrMatch().Forget());
            UI.LoadMoreButton.onClick.AddListener(() => LoadMorePublicRooms().Forget());
            UI.LeaveButton.onClick.AddListener(() => SubmitLeave().Forget());
            UI.WorldButton.onClick.AddListener(SubmitWorld);
            UI.CloseButton.onClick.AddListener(Hide);
            UI.MemberList.AddItemRefreshListener(RenderMember);
            UI.PublicRoomList.AddItemRefreshListener(RenderPublicRoom);
            UI.ChatMessageList.AddItemRefreshListener(RenderChatMessage);
            UI.ChatSendButton.onClick.AddListener(() => SubmitChat().Forget());
            UI.ChatInput.onSubmit.AddListener(_ => SubmitChat().Forget());
            UI.ChatOpenButton.onClick.AddListener(() => SetChatExpanded(true));
            UI.ChatCloseButton.onClick.AddListener(() => SetChatExpanded(false));
            this.RegisterEvent<OnlineRoomChangedEvent>(evt => Render(evt.Snapshot))
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            // 订阅随当前 BaseUI 实例销毁而注销；隐藏期间事件不触碰 View，OnShow 从 System 最新快照恢复。
            this.RegisterEvent<OnlinePublicRoomListChangedEvent>(evt =>
                {
                    if (isVisible && UI != null && !latest.HasRoom)
                    {
                        RenderPublicRooms(evt.Snapshot);
                    }
                })
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => RefreshDynamicLocalizedText())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineRoomChatChangedEvent>(evt => RenderChat(evt.Snapshot))
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineWorldChangedEvent>(evt =>
                {
                    latestWorld = evt.Snapshot;
                    RefreshWorldButton();
                })
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineArcadeRunChangedEvent>(evt => OnArcadeRunChanged(evt.Snapshot))
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            UI.ChatInput.lineType = TMPro.TMP_InputField.LineType.SingleLine;
        }

        protected override void OnShow()
        {
            isVisible = true;
            SetChatExpanded(false);
            OnlineRoomSnapshot snapshot = this.GetSystem<OnlineRoomSystem>().Snapshot;
            arcadeMode = true;
            requestArcadeMode = false;
            latestPublicRooms = this.GetSystem<OnlineRoomSystem>().PublicRooms;
            latestWorld = this.GetSystem<OnlineWorldSystem>()?.Snapshot ?? OnlineWorldSnapshot.Empty();
            lastNoticeOperationState = snapshot.OperationState;
            lastNoticeResultCode = snapshot.LastResultCode;
            Render(snapshot);
            RenderChat(this.GetSystem<OnlineRoomChatSystem>().Snapshot);
            transitPreparing = false; transitAttemptedRunId = 0; transitAttemptedArena = 0;
            var arcadeRun = this.GetSystem<OnlineArcadeRunSystem>().Snapshot;
            if (arcadeRun.HasRun) OpenArcadeSnapshot(arcadeRun);
            ApplyModeVisibility();
            if (!snapshot.HasRoom && !arcadeMode)
            {
                RefreshPublicRooms().Forget();
            }
        }

        protected override void OnHide()
        {
            SetChatExpanded(false);
            InvalidateViewOperations();
            ClearChatList();
            EventSystem.current?.SetSelectedGameObject(null);
        }

        protected override void OnUnload()
        {
            InvalidateViewOperations();
            chatMessages.Clear();
            latest = OnlineRoomSnapshot.Empty();
            latestChat = OnlineRoomChatSnapshot.Empty();
            latestWorld = OnlineWorldSnapshot.Empty();
            displayedChatRoomId = 0;
            chatListFilled = false;
            publicRoomListFilled = false;
            chatExpanded = false;
            observedChatSequence = 0;
            chatHistoryObserved = false;
            unreadChatCount = 0;
        }

        private void InvalidateViewOperations()
        {
            isVisible = false;
            operationGeneration++;
            chatGeneration++;
            chatSubmitInFlight = false;
            worldSubmitInFlight = false;
        }

        /// <summary>等待街机资源期间绑定视图与账号会话，过期操作不能替新账号创建或加入房间。</summary>
        private async UniTask<bool> PrepareArcadeResources(int generation)
        {
            if (!arcadeMode) return true;
            var owner = BaseUI;
            var account = this.GetSystem<OnlineAccountSystem>();
            var identity = account?.Snapshot ?? OnlineAccountSnapshot.Empty();
            bool IsCurrent() => generation == operationGeneration && ReferenceEquals(owner, BaseUI) && IsShow() &&
                arcadeMode && account != null && account.Snapshot.IsAuthenticated &&
                account.Snapshot.AccountId == identity.AccountId && account.Snapshot.SessionEpoch == identity.SessionEpoch;
            if (!IsCurrent()) return false;
            var preparation = this.GetUtility<IGameplayResourcePreparation>();
            return (preparation == null || await preparation.PrepareAsync(MapIds.Arcade, IsCurrent)) && IsCurrent();
        }

        private async UniTask SubmitCreate()
        {
            int generation = ++operationGeneration;
            try
            {
                if (!await PrepareArcadeResources(generation)) return;
                if (arcadeMode && TryGetArcadeIdentity(out string version, out string fingerprint))
                    await this.GetSystem<OnlineRoomSystem>().CreateArcadeRoomAsync(version, fingerprint);
                else if (!arcadeMode) await this.GetSystem<OnlineRoomSystem>().CreateRoomAsync();
                else Notify.ShowFloatTips(Loc.T("ui.arcade.error.open"));
            }
            catch (System.Exception exception)
            {
                Log.Error($"[OnlineRoomUICtrl] 创建提交失败：generation={generation}，异常={exception}");
                if (generation == operationGeneration && UI != null)
                {
                    Notify.ShowFloatTips(OnlineRoomText.Result(OnlineRoomResultCode.UnknownFailure));
                }
            }
        }

        /// <summary>街机模式提交快速匹配；普通模式仍复用同一按钮刷新公开房间列表。</summary>
        private async UniTask SubmitRefreshOrMatch()
        {
            if (!arcadeMode)
            {
                await RefreshPublicRooms();
                return;
            }

            int generation = ++operationGeneration;
            try
            {
                if (!await PrepareArcadeResources(generation)) return;
                if (TryGetArcadeIdentity(out string version, out string fingerprint))
                {
                    await this.GetSystem<OnlineRoomSystem>().MatchArcadeRoomAsync(version, fingerprint);
                }
                else
                {
                    Notify.ShowFloatTips(Loc.T("ui.arcade.error.open"));
                }
            }
            catch (System.Exception exception)
            {
                Log.Error($"[OnlineRoomUICtrl] 快速匹配提交失败：generation={generation}，异常={exception}");
                if (generation == operationGeneration && UI != null)
                {
                    Notify.ShowFloatTips(OnlineRoomText.Result(OnlineRoomResultCode.UnknownFailure));
                }
            }
        }

        private async UniTask SubmitJoin()
        {
            await SubmitJoin(UI.RoomCodeInput.text);
        }

        private async UniTask SubmitJoin(string roomCode)
        {
            int generation = ++operationGeneration;
            try
            {
                if (!await PrepareArcadeResources(generation)) return;
                if (arcadeMode && TryGetArcadeIdentity(out string version, out string fingerprint))
                    await this.GetSystem<OnlineRoomSystem>().JoinArcadeRoomAsync(roomCode, version, fingerprint);
                else if (!arcadeMode) await this.GetSystem<OnlineRoomSystem>().JoinRoomAsync(roomCode);
                else Notify.ShowFloatTips(Loc.T("ui.arcade.error.open"));
                if (generation == operationGeneration && UI != null)
                {
                    UI.RoomCodeInput.text = OnlineRoomSystem.NormalizeRoomCode(roomCode);
                }
            }
            catch (System.Exception exception)
            {
                Log.Error($"[OnlineRoomUICtrl] 加入提交失败：generation={generation}，异常={exception}");
                if (generation == operationGeneration && UI != null)
                {
                    Notify.ShowFloatTips(OnlineRoomText.Result(OnlineRoomResultCode.UnknownFailure));
                }
            }
        }

        private async UniTask SubmitLeave()
        {
            int generation = ++operationGeneration;
            try
            {
                await this.GetSystem<OnlineRoomSystem>().LeaveRoomAsync();
            }
            catch (System.Exception exception)
            {
                Log.Error($"[OnlineRoomUICtrl] 离开提交失败：generation={generation}，异常={exception}");
                if (generation == operationGeneration && UI != null)
                {
                    Notify.ShowFloatTips(OnlineRoomText.Result(OnlineRoomResultCode.UnknownFailure));
                }
            }
        }

        private void SubmitWorld()
        {
            if (arcadeMode)
            {
                SubmitArcade().Forget();
                return;
            }
            OnlineRoomUI owner = UI;
            if (owner == null || !CanEnterWorld())
            {
                RefreshWorldButton();
                return;
            }

            if (!MapSelectionUICtrl.TryShowOnline(out string error))
            {
                Notify.ShowFloatTips(error);
            }
        }

        private async UniTaskVoid SubmitArcade()
        {
            if (!CanUseArcadeButton()) { RefreshWorldButton(); return; }
            OnlineArcadeRunSystem system = this.GetSystem<OnlineArcadeRunSystem>();
            if (!system.Snapshot.HasRun)
            {
                OnlineRoomUI owner = UI;
                OnlineArcadeOperationResult result = await system.StartAsync();
                // 只允许发起操作的同一界面实例显示晚返回，避免销毁重载后污染新界面。
                if (!result.Success && UI == owner && IsShow())
                {
                    Notify.ShowFloatTips(ArcadeUiFormatter.OnlineResult(result.Code));
                }
            }
            else if (system.Snapshot.Phase == OnlineArcadeRunPhase.SafeShop &&
                system.Snapshot.Players.All(p => p.ShopOffers.Count == 0))
            {
                transitAttemptedRunId = 0;
                PrepareTransitAsync(system.Snapshot).Forget();
            }
            else OpenArcadeSnapshot(system.Snapshot);
        }

        private void OnArcadeRunChanged(OnlineArcadeRunClientSnapshot snapshot)
        {
            if (!arcadeMode || !IsShow()) return;
            RefreshWorldButton();
            if (snapshot.HasRun) OpenArcadeSnapshot(snapshot);
        }

        private void OpenArcadeSnapshot(OnlineArcadeRunClientSnapshot snapshot)
        {
            // 普通安全节点复用房间准备与重试入口，不弹出商品界面；服务器通常已直接开始下一场。
            if (snapshot.Phase == OnlineArcadeRunPhase.SafeShop && snapshot.Players.All(p => p.ShopOffers.Count == 0))
            { PrepareTransitAsync(snapshot).Forget(); return; }
            Hide();
            if (snapshot.Phase is OnlineArcadeRunPhase.Arena or OnlineArcadeRunPhase.Challenge)
                UIHelper.ShowUI<ArcadeHudUICtrl>();
            else if (snapshot.Phase is OnlineArcadeRunPhase.SafeShop or OnlineArcadeRunPhase.Victory) UIHelper.ShowUI<ArcadeShopUICtrl>();
        }

        /// <summary>普通节点自动准备资源和地图后提交现有 ready；失败留在房间，可用原按钮重试，不提前开战。</summary>
        private async UniTaskVoid PrepareTransitAsync(OnlineArcadeRunClientSnapshot snapshot)
        {
            if (!IsShow() || transitPreparing || snapshot.OperationState != OnlineArcadeOperationState.Idle ||
                transitAttemptedRunId == snapshot.RunId && transitAttemptedArena == snapshot.NextArena) return;
            long selfId = this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            var player = snapshot.Players.FirstOrDefault(p => p.PlayerId == selfId);
            if (player.PlayerId == 0 || player.Ready) return;
            transitAttemptedRunId = snapshot.RunId; transitAttemptedArena = snapshot.NextArena;
            transitPreparing = true; var owner = BaseUI; int generation = ++operationGeneration;
            bool Current() => generation == operationGeneration && ReferenceEquals(owner,BaseUI) && IsShow() &&
                this.GetSystem<OnlineArcadeRunSystem>().Snapshot.RunId == snapshot.RunId &&
                this.GetSystem<OnlineArcadeRunSystem>().Snapshot.NextArena == snapshot.NextArena &&
                this.GetSystem<OnlineArcadeRunSystem>().Snapshot.Phase == OnlineArcadeRunPhase.SafeShop;
            RefreshWorldButton();
            try
            {
                if (!await PrepareArcadeResources(generation) || !Current()) return;
                var map = await this.GetSystem<ArcadeSceneFlowSystem>().EnterArenaMapAsync();
                if (!Current()) return;
                if (!map.Success) { Notify.ShowFloatTips(map.Error); return; }
                if (player.Lives <= 0) return; // 现有零命观战成员不发 ready，也不在这里增加生命。
                var result = await this.GetSystem<OnlineArcadeRunSystem>().SetReadyAsync(true);
                if (!result.Success && Current()) Notify.ShowFloatTips(ArcadeUiFormatter.OnlineResult(result.Code));
            }
            catch (System.Exception e) { if (Current()) Notify.ShowFloatTips(e.Message); }
            finally { if (generation == operationGeneration && ReferenceEquals(owner,BaseUI)) {transitPreparing=false;RefreshWorldButton();} }
        }

        private bool TryGetArcadeIdentity(out string version, out string fingerprint)
        {
            version = Application.version;
            fingerprint = string.Empty;
            return this.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out var catalog, out _) &&
                   !string.IsNullOrWhiteSpace(version) &&
                   !string.IsNullOrWhiteSpace(fingerprint = catalog.Rule.ConfigFingerprint);
        }

        private void ApplyModeVisibility()
        {
            if (UI == null) return;
            UI.PublicRoomList.gameObject.SetActive(!arcadeMode);
            UI.PublicRoomListStateText.gameObject.SetActive(!arcadeMode);
            UI.RefreshRoomListButton.gameObject.SetActive(true);
            UI.LoadMoreButton.gameObject.SetActive(!arcadeMode);
            SetButtonText(UI.RefreshRoomListButton,
                arcadeMode ? Loc.T("ui.arcade.online.match") : Loc.T("ui.online.room.list.refresh"));
            SetButtonText(UI.CreateButton, arcadeMode ? Loc.T("ui.arcade.online.create") : Loc.T("ui.online.room.create"));
            SetButtonText(UI.JoinButton, arcadeMode ? Loc.T("ui.arcade.online.join") : Loc.T("ui.online.room.join"));
            SetButtonText(UI.WorldButton, arcadeMode ? Loc.T("ui.arcade.online.open") : Loc.T("ui.online.world.enter"));
        }

        private void Render(OnlineRoomSnapshot snapshot)
        {
            bool wasInRoom = latest.HasRoom;
            // 隐藏期间只记住权威快照，不能刷新已隐藏或待重载的 Unity 对象。
            if (!isVisible)
            {
                latest = snapshot;
                return;
            }
            if (snapshot.HasRoom && latest.HasRoom && snapshot.RoomId != latest.RoomId)
            {
                ClearChatList();
            }
            latest = snapshot;
            if (UI == null)
            {
                return;
            }

            UI.EmptyRoot.SetActive(!snapshot.HasRoom);
            UI.JoinedRoot.SetActive(snapshot.HasRoom);
            if (!snapshot.HasRoom)
            {
                ClearChatList();
            }
            UI.CurrentRoomCodeText.text = snapshot.RoomCode;
            ShowSnapshotNotice(snapshot);
            UI.DisconnectHintRoot.SetActive(snapshot.ConnectionLost);
            UI.MemberList.totalCount = snapshot.Members.Count;
            UI.MemberList.RefreshCells();
            SetButtons();
            UI.ChatInput.interactable = snapshot.HasRoom && !snapshot.ConnectionLost;
            UI.ChatSendButton.interactable = snapshot.HasRoom && !snapshot.ConnectionLost && !chatSubmitInFlight;
            RefreshChatPresentation();
            if (!snapshot.HasRoom)
            {
                if (!arcadeMode) RenderPublicRooms(this.GetSystem<OnlineRoomSystem>().PublicRooms);
                if (isVisible && wasInRoom && !arcadeMode)
                {
                    RefreshPublicRooms().Forget();
                }
            }
        }

        private async UniTask RefreshPublicRooms()
        {
            int generation = operationGeneration;
            try
            {
                await this.GetSystem<OnlineRoomSystem>().RefreshPublicRoomsAsync();
            }
            catch (System.Exception exception)
            {
                if (generation == operationGeneration && isVisible && UI != null && !latest.HasRoom)
                {
                    Log.Error($"[OnlineRoomUICtrl] 刷新公开房间失败：generation={generation}，异常={exception}");
                }
            }
        }

        private async UniTask LoadMorePublicRooms()
        {
            int generation = operationGeneration;
            try
            {
                await this.GetSystem<OnlineRoomSystem>().LoadMorePublicRoomsAsync();
            }
            catch (System.Exception exception)
            {
                if (generation == operationGeneration && isVisible && UI != null && !latest.HasRoom)
                {
                    Log.Error($"[OnlineRoomUICtrl] 加载更多公开房间失败：generation={generation}，异常={exception}");
                }
            }
        }

        private void RenderPublicRooms(OnlinePublicRoomListSnapshot snapshot)
        {
            latestPublicRooms = snapshot ?? OnlinePublicRoomListSnapshot.Empty();
            if (UI == null || !isVisible || latest.HasRoom)
            {
                return;
            }

            UI.PublicRoomListStateText.text = OnlinePublicRoomListText.State(latestPublicRooms);
            UI.PublicRoomList.totalCount = latestPublicRooms.Items.Count;
            bool replaced = latestPublicRooms.State == OnlinePublicRoomListState.Idle &&
                            latestPublicRooms.LastOperation == OnlinePublicRoomListOperation.Refresh &&
                            latestPublicRooms.LastResultCode == OnlineRoomResultCode.Success;
            if (!publicRoomListFilled || replaced)
            {
                UI.PublicRoomList.RefillCells(0);
                publicRoomListFilled = true;
            }
            else
            {
                UI.PublicRoomList.RefreshCells();
            }

            RefreshInteractionState();
        }

        private void RenderPublicRoom(UnityEngine.Transform item, int index)
        {
            if (index < 0 || index >= latestPublicRooms.Items.Count)
            {
                item.gameObject.SetActive(false);
                return;
            }

            OnlineRoomListItemBoxUI row = item.GetComponent<OnlineRoomListItemBoxUI>();
            if (row == null)
            {
                item.gameObject.SetActive(false);
                Log.Error("[OnlineRoomUICtrl] 公开房间行 Prefab 契约损坏：缺少 OnlineRoomListItemBoxUI。");
                return;
            }

            OnlinePublicRoomItem room = latestPublicRooms.Items[index];
            item.gameObject.SetActive(true);
            row.RoomCodeText.text = room.RoomCode;
            row.OwnerText.text = Loc.T("ui.online.room.list.owner", room.OwnerDisplayName);
            row.MemberCountText.text = Loc.T("ui.online.room.list.members", room.MemberCount, room.Capacity);
            row.JoinButton.gameObject.SetActive(true);
            // LoopScrollRect 会复用同一个行实例；绑定当前房间前移除旧监听，避免点击后加入上一轮绑定的房间。
            row.JoinButton.onClick.RemoveAllListeners();
            string roomCode = room.RoomCode;
            row.JoinButton.onClick.AddListener(() => SubmitJoin(roomCode).Forget());
            row.JoinButton.interactable = CanJoinPublicRoom(room);
        }

        private async UniTask SubmitChat()
        {
            if (chatSubmitInFlight || UI == null)
            {
                return;
            }

            chatSubmitInFlight = true;
            int generation = ++chatGeneration;
            string content = UI.ChatInput.text;
            UI.ChatSendButton.interactable = false;
            try
            {
                OnlineRoomChatSendResult result = await this.GetSystem<OnlineRoomChatSystem>().SendAsync(content);
                if (generation != chatGeneration || UI == null)
                {
                    return;
                }

                if (result.ShouldClearInput)
                {
                    UI.ChatInput.SetTextWithoutNotify(string.Empty);
                }
                string message = OnlineRoomChatText.Result(result.Code);
                if (!string.IsNullOrEmpty(message))
                {
                    Notify.ShowFloatTips(message);
                }
            }
            catch (System.Exception exception)
            {
                Log.Error($"[OnlineRoomUICtrl] 聊天提交失败：generation={generation}，异常={exception}");
                if (generation == chatGeneration && UI != null)
                {
                    Notify.ShowFloatTips(OnlineRoomChatText.Result(OnlineRoomChatResultCode.UnknownFailure));
                }
            }
            finally
            {
                if (generation == chatGeneration && UI != null)
                {
                    chatSubmitInFlight = false;
                    UI.ChatSendButton.interactable = latest.HasRoom && !latest.ConnectionLost;
                }
            }
        }

        private void RenderChat(OnlineRoomChatSnapshot snapshot)
        {
            if (UI == null)
            {
                return;
            }

            if (!isVisible)
            {
                latestChat = snapshot;
                ClearChatList();
                return;
            }

            if (!latest.HasRoom || snapshot.RoomId == 0)
            {
                latestChat = snapshot;
                ClearChatList();
                UI.ChatStateText.text = OnlineRoomChatText.State(snapshot);
                return;
            }

            if (snapshot.RoomId != latest.RoomId)
            {
                Log.Warning($"[OnlineRoomUICtrl] 已忽略过期聊天快照：roomId={snapshot.RoomId}，currentRoomId={latest.RoomId}。");
                return;
            }

            if (!TryGetChatRowPrefab(out _))
            {
                return;
            }

            bool roomChanged = displayedChatRoomId != snapshot.RoomId;
            ObserveChat(snapshot, roomChanged);
            bool followBottom = !chatListFilled || roomChanged ||
                                UI.ChatMessageList.verticalNormalizedPosition <= ChatBottomFollowTolerance;
            latestChat = snapshot;
            chatMessages.Clear();
            chatMessages.AddRange(snapshot.Messages);
            displayedChatRoomId = snapshot.RoomId;

            UI.ChatStateText.text = OnlineRoomChatText.State(snapshot);
            UI.ChatInput.interactable = latest.HasRoom && !snapshot.ConnectionLost;
            UI.ChatSendButton.interactable = latest.HasRoom && !snapshot.ConnectionLost &&
                                             snapshot.SendState == OnlineRoomChatSendState.Idle && !chatSubmitInFlight;

            UI.ChatMessageList.totalCount = chatMessages.Count;
            RefreshChatPresentation();
            if (!chatExpanded)
            {
                // 循环列表只在展开时填充；收起时预览直接读取同一快照，避免隐藏 Viewport 的布局计算。
                return;
            }
            if (followBottom)
            {
                UI.ChatMessageList.RefillCellsFromEnd(0);
                chatListFilled = true;
            }
            else
            {
                UI.ChatMessageList.RefreshCells();
            }
        }

        private void RenderChatMessage(UnityEngine.Transform item, int index)
        {
            if (index < 0 || index >= chatMessages.Count)
            {
                item.gameObject.SetActive(false);
                Log.Error($"[OnlineRoomUICtrl] 聊天消息行跳过刷新：index={index} 超出快照 count={chatMessages.Count}，行已隐藏。");
                return;
            }

            OnlineRoomChatMessageBoxUI row = item.GetComponent<OnlineRoomChatMessageBoxUI>();
            if (row == null)
            {
                item.gameObject.SetActive(false);
                Log.Error("[OnlineRoomUICtrl] 聊天消息行 Prefab 契约损坏：缺少 OnlineRoomChatMessageBoxUI，行已隐藏。");
                return;
            }

            if (!TryGetChatRowPrefab(out OnlineRoomChatMessageBoxUI design) ||
                row.SenderText == null || row.ContentText == null || row.TimeText == null || row.Background == null ||
                design.SenderText == null || design.ContentText == null || design.TimeText == null || design.Background == null)
            {
                item.gameObject.SetActive(false);
                Log.Error("[OnlineRoomUICtrl] 聊天消息行 Prefab 契约损坏：缺少文本和背景绑定，行已隐藏。");
                return;
            }

            var message = chatMessages[index];
            item.gameObject.SetActive(true);
            RestoreChatText(row.SenderText, design.SenderText, message.SenderDisplayName, design.SenderText.gameObject.activeSelf);
            RestoreChatText(row.ContentText, design.ContentText, message.Content, design.ContentText.gameObject.activeSelf);
            string timeText;
            try
            {
                timeText = DateTimeOffset.FromUnixTimeMilliseconds(message.ServerTime).ToLocalTime().ToString("HH:mm");
            }
            catch (ArgumentOutOfRangeException)
            {
                timeText = string.Empty;
            }

            RestoreChatText(row.TimeText, design.TimeText, timeText,
                !string.IsNullOrEmpty(timeText) && design.TimeText.gameObject.activeSelf);
            row.Background.gameObject.SetActive(design.Background.gameObject.activeSelf);
            row.Background.color = design.Background.color;
            row.Background.raycastTarget = false;
        }

        private bool TryGetChatRowPrefab(out OnlineRoomChatMessageBoxUI rowPrefab)
        {
            rowPrefab = null;
            if (UI.ChatMessageList != null && UI.ChatMessageList.prefabSource != null &&
                UI.ChatMessageList.prefabSource.prefab != null)
            {
                rowPrefab = UI.ChatMessageList.prefabSource.prefab.GetComponent<OnlineRoomChatMessageBoxUI>();
                if (rowPrefab != null)
                {
                    return true;
                }
            }

            Log.Error("[OnlineRoomUICtrl] 聊天列表 Prefab 契约损坏：LoopVerticalScrollRect.prefabSource.prefab 必须引用 OnlineRoomChatMessage.prefab，跳过刷新。");
            return false;
        }

        private static void RestoreChatText(TMPro.TMP_Text target, TMPro.TMP_Text design, string value, bool visible)
        {
            target.text = value;
            target.color = design.color;
            target.alignment = design.alignment;
            target.raycastTarget = design.raycastTarget;
            target.gameObject.SetActive(visible);
        }

        private void ClearChatList()
        {
            chatMessages.Clear();
            displayedChatRoomId = 0;
            chatListFilled = false;
            latestChat = OnlineRoomChatSnapshot.Empty();
            observedChatSequence = 0;
            chatHistoryObserved = false;
            unreadChatCount = 0;
            if (UI == null || UI.ChatMessageList == null)
            {
                return;
            }

            UI.ChatMessageList.totalCount = 0;
            UI.ChatMessageList.RefillCellsFromEnd(0);
            SetChatExpanded(false);
        }

        /// <summary>建立历史基线后只计算新序号；推送与 RPC 的重复快照不能重复累计未读。</summary>
        private void ObserveChat(OnlineRoomChatSnapshot snapshot, bool roomChanged)
        {
            if (roomChanged)
            {
                observedChatSequence = 0;
                chatHistoryObserved = false;
                unreadChatCount = 0;
                chatExpanded = false;
            }
            if (chatHistoryObserved && !chatExpanded)
            {
                unreadChatCount += snapshot.Messages.Count(message => message.ChatSequence > observedChatSequence);
            }
            foreach (var message in snapshot.Messages)
            {
                observedChatSequence = Math.Max(observedChatSequence, message.ChatSequence);
            }
            // 首次历史返回以前收到的内容也纳入基线，不把历史补齐标成未读推送。
            chatHistoryObserved |= snapshot.HistoryState == OnlineRoomChatHistoryState.Ready;
        }

        /// <summary>切换聊天面板并清理其输入焦点；收起保留草稿，展开读取当前消息并滚动到底部。</summary>
        private void SetChatExpanded(bool expanded)
        {
            chatExpanded = expanded && latest.HasRoom;
            if (UI == null) return;
            if (chatExpanded)
            {
                unreadChatCount = 0;
            }
            else
            {
                UI.ChatInput.DeactivateInputField();
                var selected = EventSystem.current?.currentSelectedGameObject;
                if (selected != null && selected.transform.IsChildOf(UI.ChatPanelRoot.transform))
                    EventSystem.current.SetSelectedGameObject(null);
            }
            RefreshChatPresentation();
            if (chatExpanded)
            {
                Canvas.ForceUpdateCanvases();
                UI.ChatMessageList.totalCount = chatMessages.Count;
                UI.ChatMessageList.RefillCellsFromEnd(0);
                chatListFilled = true;
            }
        }

        /// <summary>用同一聊天快照渲染两条预览及未读提示，不创建额外消息对象或保存聊天业务状态。</summary>
        private void RefreshChatPresentation()
        {
            if (UI == null) return;
            UI.ChatPanelRoot.SetActive(latest.HasRoom && chatExpanded);
            UI.ChatPreviewRoot.SetActive(latest.HasRoom && !chatExpanded);
            UI.ChatOpenButton.gameObject.SetActive(latest.HasRoom && !chatExpanded);
            UI.ChatUnreadText.gameObject.SetActive(!chatExpanded && unreadChatCount > 0);
            UI.ChatUnreadText.text = unreadChatCount > 99 ? "99+" : unreadChatCount.ToString();
            UI.ChatPreviewText.text = string.Join("\n", chatMessages.Skip(Math.Max(0, chatMessages.Count - 2))
                .Select(message => message.SenderDisplayName + ": " + message.Content));
            if (chatMessages.Count == 0) UI.ChatPreviewText.text = OnlineRoomChatText.State(latestChat);
        }

        private void ShowSnapshotNotice(OnlineRoomSnapshot snapshot)
        {
            bool changed = snapshot.OperationState != lastNoticeOperationState ||
                           snapshot.LastResultCode != lastNoticeResultCode;
            lastNoticeOperationState = snapshot.OperationState;
            lastNoticeResultCode = snapshot.LastResultCode;

            if (!isVisible || !changed)
            {
                return;
            }

            string message = snapshot.OperationState == OnlineRoomOperationState.Idle
                ? OnlineRoomText.Result(snapshot.LastResultCode)
                : OnlineRoomText.Operation(snapshot.OperationState);
            Notify.ShowFloatTips(message);
        }

        /// <summary>绑定可复用成员行；每次刷新完整覆盖姓名、身份、连接状态和模型色调，不创建玩家实体。</summary>
        private void RenderMember(UnityEngine.Transform item, int index)
        {
            if (index < 0 || index >= latest.Members.Count)
            {
                return;
            }

            OnlineRoomMemberBoxUI memberBox = item.GetComponent<OnlineRoomMemberBoxUI>();
            if (memberBox == null)
            {
                Log.Error("[OnlineRoomUICtrl] 房间成员行 Prefab 契约损坏：每个 MemberList 项都必须包含 OnlineRoomMemberBoxUI。");
                return;
            }

            var member = latest.Members[index];
            memberBox.DisplayNameText.text = member.DisplayName;
            memberBox.PlayerIdText.text = Loc.T("ui.online.room.member_player", member.PlayerId);
            memberBox.OwnerText.text = member.IsOwner ? Loc.T("ui.online.room.owner") : string.Empty;
            memberBox.ConnectionText.text = member.ConnectionState == OnlineRoomMemberConnectionState.DisconnectedGrace
                ? Loc.T("ui.online.room.member_disconnected")
                : Loc.T("ui.online.room.member_online");
            memberBox.CharacterImage.color = member.ConnectionState == OnlineRoomMemberConnectionState.DisconnectedGrace
                ? new Color(0.5f, 0.55f, 0.6f, 0.75f)
                : Color.white;
        }

        private void RefreshDynamicLocalizedText()
        {
            if (UI == null)
            {
                return;
            }

            UI.ChatStateText.text = OnlineRoomChatText.State(latestChat);
            RefreshChatPresentation();
            UI.MemberList.RefreshCells();
            UI.PublicRoomListStateText.text = OnlinePublicRoomListText.State(latestPublicRooms);
            UI.PublicRoomList.RefreshCells();
            ApplyModeVisibility();
        }

        private void SetButtons()
        {
            if (UI == null)
            {
                return;
            }

            UI.LeaveButton.interactable = latest.OperationState == OnlineRoomOperationState.Idle;
            RefreshInteractionState();
            RefreshWorldButton();
            UI.CloseButton.interactable = true;
        }

        private void RefreshInteractionState()
        {
            if (UI == null)
            {
                return;
            }

            bool roomIdle = latest.OperationState == OnlineRoomOperationState.Idle;
            bool disconnectedList = latestPublicRooms.LastResultCode == OnlineRoomResultCode.Disconnected;
            UI.CreateButton.interactable = roomIdle;
            UI.RoomCodeInput.interactable = roomIdle;
            UI.JoinButton.interactable = roomIdle && (arcadeMode || !disconnectedList);

            bool refreshing = latestPublicRooms.State == OnlinePublicRoomListState.Refreshing;
            UI.RefreshRoomListButton.interactable = arcadeMode
                ? roomIdle
                : disconnectedList || !refreshing;
            bool canLoadMore = !arcadeMode && !disconnectedList && latestPublicRooms.HasMore &&
                               latestPublicRooms.NextCursorRoomId != 0;
            UI.LoadMoreButton.gameObject.SetActive(canLoadMore);
            UI.LoadMoreButton.interactable = canLoadMore &&
                                             latestPublicRooms.State == OnlinePublicRoomListState.Idle;
            UI.PublicRoomList.RefreshCells();
        }

        private bool CanJoinPublicRoom(OnlinePublicRoomItem room)
        {
            return latest.OperationState == OnlineRoomOperationState.Idle &&
                   latestPublicRooms.LastResultCode != OnlineRoomResultCode.Disconnected &&
                   room.MemberCount < room.Capacity;
        }

        private bool CanEnterWorld()
        {
            return !worldSubmitInFlight && latest.HasRoom && !latest.ConnectionLost &&
                   latest.OperationState == OnlineRoomOperationState.Idle &&
                   latestWorld.OperationState == OnlineWorldOperationState.Idle && !latestWorld.HasWorld;
        }

        private void RefreshWorldButton()
        {
            if (UI?.WorldButton != null)
            {
                UI.WorldButton.interactable = arcadeMode ? CanUseArcadeButton() : CanEnterWorld();
            }
        }

        private bool CanUseArcadeButton()
        {
            if (transitPreparing) return false;
            if (!latest.HasRoom || latest.Purpose != OnlineRoomPurpose.Arcade || latest.ConnectionLost) return false;
            OnlineArcadeRunClientSnapshot run = this.GetSystem<OnlineArcadeRunSystem>().Snapshot;
            if (run.HasRun) return run.OperationState == OnlineArcadeOperationState.Idle;
            long selfId = this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            return latest.OwnerPlayerId == selfId && latest.Members.Count == 2 &&
                   latest.Members.All(x => x.ConnectionState == OnlineRoomMemberConnectionState.Online);
        }

        private static void SetButtonText(UnityEngine.UI.Button button, string value)
        {
            TMP_Text text = button?.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = value ?? string.Empty;
        }
    }

    public static class OnlinePublicRoomListText
    {
        public static string State(OnlinePublicRoomListSnapshot snapshot)
        {
            if (snapshot.State == OnlinePublicRoomListState.Refreshing ||
                snapshot.State == OnlinePublicRoomListState.LoadingMore)
            {
                return Loc.T("ui.online.room.list.loading");
            }

            if (snapshot.LastResultCode == OnlineRoomResultCode.Disconnected)
            {
                return snapshot.LastOperation == OnlinePublicRoomListOperation.Refresh
                    ? Loc.T("ui.online.room.list.refresh_failed")
                    : Loc.T("ui.online.room.list.disconnected");
            }

            if (snapshot.LastResultCode != OnlineRoomResultCode.None &&
                snapshot.LastResultCode != OnlineRoomResultCode.Success)
            {
                if (snapshot.LastOperation == OnlinePublicRoomListOperation.LoadMore)
                {
                    return Loc.T("ui.online.room.list.load_more_failed");
                }

                return snapshot.Items.Count == 0
                    ? Loc.T("ui.online.room.list.first_load_failed")
                    : Loc.T("ui.online.room.list.refresh_failed");
            }

            return snapshot.Items.Count == 0
                ? Loc.T("ui.online.room.list.empty")
                : Loc.T("ui.online.room.list.ready", snapshot.Items.Count);
        }
    }

    public static class OnlineRoomChatText
    {
        public static string State(OnlineRoomChatSnapshot snapshot)
        {
            if (snapshot.SendState == OnlineRoomChatSendState.Sending)
            {
                return Loc.T("ui.online.room.chat.sending");
            }
            if (snapshot.HistoryState == OnlineRoomChatHistoryState.Loading)
            {
                return Loc.T("ui.online.room.chat.loading");
            }
            if (snapshot.Messages.Count == 0 && snapshot.HistoryState == OnlineRoomChatHistoryState.Ready)
            {
                return Loc.T("ui.online.room.chat.empty");
            }
            return Result(snapshot.LastResultCode);
        }

        public static string Result(OnlineRoomChatResultCode code)
        {
            switch (code)
            {
                case OnlineRoomChatResultCode.Success: return string.Empty;
                case OnlineRoomChatResultCode.ClientInputInvalid: return Loc.T("ui.online.room.chat.error.text");
                case OnlineRoomChatResultCode.NotLoggedIn: return Loc.T("ui.online.error.not_logged_in");
                case OnlineRoomChatResultCode.NotInRoom: return Loc.T("ui.online.room.error.not_in_room");
                case OnlineRoomChatResultCode.MembershipPending: return Loc.T("ui.online.room.chat.membership_pending");
                case OnlineRoomChatResultCode.RateLimited: return Loc.T("ui.online.room.chat.error.rate_limited");
                case OnlineRoomChatResultCode.OperationConflict: return Loc.T("ui.online.room.chat.error.operation");
                case OnlineRoomChatResultCode.ServiceUnavailable: return Loc.T("ui.online.error.service_unavailable");
                case OnlineRoomChatResultCode.Disconnected: return Loc.T("ui.online.error.disconnected");
                case OnlineRoomChatResultCode.Timeout: return Loc.T("ui.online.error.request_timeout");
                case OnlineRoomChatResultCode.OperationInProgress: return Loc.T("ui.online.error.busy");
                case OnlineRoomChatResultCode.None: return string.Empty;
                default: return Loc.T("ui.online.error.unknown");
            }
        }
    }

    public static class OnlineRoomText
    {
        public static string Result(OnlineRoomResultCode code)
        {
            switch (code)
            {
                case OnlineRoomResultCode.Success: return Loc.T("ui.online.success");
                case OnlineRoomResultCode.ClientInputInvalid:
                case OnlineRoomResultCode.RoomCodeInvalid: return Loc.T("ui.online.room.error.code");
                case OnlineRoomResultCode.RoomNotFound: return Loc.T("ui.online.room.error.not_found");
                case OnlineRoomResultCode.RoomFull: return Loc.T("ui.online.room.error.full");
                case OnlineRoomResultCode.AlreadyInRoom: return Loc.T("ui.online.room.error.already_in_room");
                case OnlineRoomResultCode.NotInRoom: return Loc.T("ui.online.room.error.not_in_room");
                case OnlineRoomResultCode.OperationInvalid: return Loc.T("ui.online.room.error.operation");
                case OnlineRoomResultCode.RateLimited: return Loc.T("ui.online.room.error.rate_limited");
                case OnlineRoomResultCode.RoomClosing: return Loc.T("ui.online.room.error.closing");
                case OnlineRoomResultCode.MembershipPending: return Loc.T("ui.online.room.chat.membership_pending");
                case OnlineRoomResultCode.ServiceUnavailable: return Loc.T("ui.online.error.service_unavailable");
                case OnlineRoomResultCode.NotLoggedIn: return Loc.T("ui.online.error.not_logged_in");
                case OnlineRoomResultCode.Disconnected: return Loc.T("ui.online.error.disconnected");
                case OnlineRoomResultCode.Timeout: return Loc.T("ui.online.error.request_timeout");
                case OnlineRoomResultCode.OperationInProgress: return Loc.T("ui.online.error.busy");
                case OnlineRoomResultCode.PurposeMismatch: return Loc.T("ui.online.room.error.purpose");
                case OnlineRoomResultCode.VersionMismatch: return Loc.T("ui.online.room.error.version");
                case OnlineRoomResultCode.None: return string.Empty;
                default: return Loc.T("ui.online.error.unknown");
            }
        }

        public static string Operation(OnlineRoomOperationState state)
        {
            switch (state)
            {
                case OnlineRoomOperationState.Creating: return Loc.T("ui.online.room.creating");
                case OnlineRoomOperationState.Matching: return Loc.T("ui.arcade.online.matching");
                case OnlineRoomOperationState.Joining: return Loc.T("ui.online.room.joining");
                case OnlineRoomOperationState.Leaving: return Loc.T("ui.online.room.leaving");
                case OnlineRoomOperationState.Restoring: return Loc.T("ui.online.room.restoring");
                default: return string.Empty;
            }
        }
    }
}
