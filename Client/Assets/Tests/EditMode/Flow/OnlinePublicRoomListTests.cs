using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class OnlinePublicRoomListTests
    {
        private const string RoomPrefabPath = "Assets/Game/Prefabs/UI/Online/OnlineRoom.prefab";
        private const string ListItemPrefabPath = "Assets/Game/Prefabs/UI/Online/Box/OnlineRoomListItem.prefab";
        private const string ChatItemPrefabPath = "Assets/Game/Prefabs/UI/Online/Box/OnlineRoomChatMessage.prefab";

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            ResetArchitecture();
            foreach (string name in new[] { "UIRoot", "EventSystem", SceneRoot.PoolRootName })
            {
                GameObject obj = GameObject.Find(name);
                if (obj != null)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
        }

        [Test]
        public void SnapshotCopiesItemsAndModelCommitsBeforePublishingEvent()
        {
            var source = new List<OnlinePublicRoomItem> { new OnlinePublicRoomItem("ABC234", "Owner", 1, 4) };
            var snapshot = new OnlinePublicRoomListSnapshot(source, 99, true, OnlinePublicRoomListState.Idle,
                OnlinePublicRoomListOperation.Refresh, OnlineRoomResultCode.Success);
            source[0] = new OnlinePublicRoomItem("ZZZ999", "Changed", 4, 4);
            source.Clear();

            Assert.That(snapshot.Items, Has.Count.EqualTo(1));
            Assert.That(snapshot.Items[0].RoomCode, Is.EqualTo("ABC234"));
            Assert.That(((IList<OnlinePublicRoomItem>)snapshot.Items).IsReadOnly, Is.True);
            Assert.That(OnlinePublicRoomListSnapshot.Empty().LastOperation, Is.EqualTo(OnlinePublicRoomListOperation.None));

            Harness harness = InstallArchitecture();
            OnlinePublicRoomListSnapshot observed = null;
            harness.Game.RegisterEvent<OnlinePublicRoomListChangedEvent>(evt =>
            {
                Assert.AreSame(evt.Snapshot, harness.RoomModel.PublicRooms.Value);
                observed = evt.Snapshot;
            });
            harness.RoomModel.PublishPublicRooms(snapshot);
            Assert.AreSame(snapshot, observed);
        }

        [UnityTest]
        public IEnumerator RefreshAndLoadMoreUseRealNetworkPathPreserveFailuresAndRejectOverlap()
        {
            Harness harness = InstallArchitecture();
            Task<OnlinePublicRoomListResult> firstTask = harness.RoomSystem.RefreshPublicRoomsAsync();
            PendingCall first = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            Assert.That(((C2G_GetPublicRoomList)first.Request).CursorRoomId, Is.Zero);
            Assert.That(harness.RoomSystem.PublicRooms.State, Is.EqualTo(OnlinePublicRoomListState.Refreshing));

            Task<OnlinePublicRoomListResult> blockedLoad = harness.RoomSystem.LoadMorePublicRoomsAsync();
            Assert.That(blockedLoad.Result.Code, Is.EqualTo(OnlineRoomResultCode.OperationInProgress));
            Assert.That(harness.Network.CountPending<C2G_GetPublicRoomList>(), Is.Zero);
            first.Succeed(Room("abc234", "One", 1, 4), Room("BCD234", "Two", 4, 4), next: 80, hasMore: true);
            yield return Wait(firstTask);

            Assert.That(harness.RoomSystem.PublicRooms.Items.Select(item => item.RoomCode),
                Is.EqualTo(new[] { "ABC234", "BCD234" }));
            Task<OnlinePublicRoomListResult> moreTask = harness.RoomSystem.LoadMorePublicRoomsAsync();
            PendingCall more = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            Assert.That(((C2G_GetPublicRoomList)more.Request).CursorRoomId, Is.EqualTo(80));
            Assert.That(harness.RoomSystem.PublicRooms.State, Is.EqualTo(OnlinePublicRoomListState.LoadingMore));
            Task<OnlinePublicRoomListResult> duplicateMore = harness.RoomSystem.LoadMorePublicRoomsAsync();
            Assert.That(duplicateMore.Result.Code, Is.EqualTo(OnlineRoomResultCode.OperationInProgress));
            Assert.That(harness.Network.CountPending<C2G_GetPublicRoomList>(), Is.Zero);
            more.Fail(200117);
            yield return Wait(moreTask);
            Assert.That(moreTask.Result.Code, Is.EqualTo(OnlineRoomResultCode.ServiceUnavailable));
            Assert.That(harness.RoomSystem.PublicRooms.Items.Select(item => item.RoomCode),
                Is.EqualTo(new[] { "ABC234", "BCD234" }));
            Assert.That(harness.RoomSystem.PublicRooms.LastOperation, Is.EqualTo(OnlinePublicRoomListOperation.LoadMore));

            Task<OnlinePublicRoomListResult> retryMoreTask = harness.RoomSystem.LoadMorePublicRoomsAsync();
            PendingCall retryMore = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            Assert.That(((C2G_GetPublicRoomList)retryMore.Request).CursorRoomId, Is.EqualTo(80));
            retryMore.Succeed(Room("abc234", "Duplicate", 2, 4), Room("CDE234", "Three", 2, 4), next: 0, hasMore: false);
            yield return Wait(retryMoreTask);
            Assert.That(harness.RoomSystem.PublicRooms.Items.Select(item => item.RoomCode),
                Is.EqualTo(new[] { "ABC234", "BCD234", "CDE234" }));

            OnlinePublicRoomListSnapshot terminal = harness.RoomSystem.PublicRooms;
            int callsBeforeNoMore = harness.Network.TotalCalls<C2G_GetPublicRoomList>();
            Task<OnlinePublicRoomListResult> noMore = harness.RoomSystem.LoadMorePublicRoomsAsync();
            Assert.That(noMore.Result.Code, Is.EqualTo(OnlineRoomResultCode.Success));
            Assert.AreSame(terminal, noMore.Result.Snapshot);
            Assert.That(harness.Network.TotalCalls<C2G_GetPublicRoomList>(), Is.EqualTo(callsBeforeNoMore));

            Task<OnlinePublicRoomListResult> failedRefresh = harness.RoomSystem.RefreshPublicRoomsAsync();
            harness.Network.TakeNext<C2G_GetPublicRoomList>().Fail(200117);
            yield return Wait(failedRefresh);
            Assert.That(harness.RoomSystem.PublicRooms.Items, Has.Count.EqualTo(3));
            Assert.That(harness.RoomSystem.PublicRooms.LastOperation, Is.EqualTo(OnlinePublicRoomListOperation.Refresh));
            Assert.That(harness.RoomSystem.PublicRooms.LastResultCode, Is.EqualTo(OnlineRoomResultCode.ServiceUnavailable));
        }

        [UnityTest]
        public IEnumerator RefreshCancelsLoadMoreAndOldResponseCannotOverwriteReplacement()
        {
            Harness harness = InstallArchitecture();
            Task<OnlinePublicRoomListResult> seedTask = harness.RoomSystem.RefreshPublicRoomsAsync();
            PendingCall seed = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            seed.Succeed(Room("OLD234", "Old", 1, 4), next: 80, hasMore: true);
            yield return Wait(seedTask);

            Task<OnlinePublicRoomListResult> oldTask = harness.RoomSystem.LoadMorePublicRoomsAsync();
            PendingCall old = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            Assert.That(((C2G_GetPublicRoomList)old.Request).CursorRoomId, Is.EqualTo(80));
            Task<OnlinePublicRoomListResult> replacementTask = harness.RoomSystem.RefreshPublicRoomsAsync();
            PendingCall replacement = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            Assert.That(((C2G_GetPublicRoomList)replacement.Request).CursorRoomId, Is.Zero);
            old.Succeed(Room("STALE2", "Stale", 1, 4), next: 0, hasMore: false);
            replacement.Succeed(Room("NEW234", "New", 1, 4), next: 0, hasMore: false);
            yield return Wait(oldTask);
            yield return Wait(replacementTask);
            Assert.That(oldTask.Result.Code, Is.EqualTo(OnlineRoomResultCode.OperationInProgress));
            Assert.That(harness.RoomSystem.PublicRooms.Items.Single().RoomCode, Is.EqualTo("NEW234"));
        }

        [UnityTest]
        public IEnumerator DisconnectedRefreshFailureReturnsActualErrorButKeepsStaleGateUntilSuccess()
        {
            Harness harness = InstallArchitecture();
            Task<OnlinePublicRoomListResult> seedTask = harness.RoomSystem.RefreshPublicRoomsAsync();
            harness.Network.TakeNext<C2G_GetPublicRoomList>()
                .Succeed(Room("OLD234", "Old", 1, 4), next: 80, hasMore: true);
            yield return Wait(seedTask);

            harness.Network.RaiseDisconnected();
            Assert.That(harness.RoomSystem.PublicRooms.Items, Has.Count.EqualTo(1));
            Assert.That(harness.RoomSystem.PublicRooms.LastResultCode, Is.EqualTo(OnlineRoomResultCode.Disconnected));
            Assert.That(harness.RoomSystem.LoadMorePublicRoomsAsync().Result.Code, Is.EqualTo(OnlineRoomResultCode.Disconnected));
            Assert.That(harness.Network.CountPending<C2G_GetPublicRoomList>(), Is.Zero);

            harness.Network.SetConnected();
            Task<OnlinePublicRoomListResult> retry = harness.RoomSystem.RefreshPublicRoomsAsync();
            PendingCall retryCall = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            Assert.That(harness.RoomSystem.PublicRooms.LastResultCode, Is.EqualTo(OnlineRoomResultCode.Disconnected));
            retryCall.Fail(200117);
            yield return Wait(retry);
            Assert.That(retry.Result.Code, Is.EqualTo(OnlineRoomResultCode.ServiceUnavailable));
            Assert.That(harness.RoomSystem.PublicRooms.Items.Single().RoomCode, Is.EqualTo("OLD234"));
            Assert.That(harness.RoomSystem.PublicRooms.LastOperation, Is.EqualTo(OnlinePublicRoomListOperation.Refresh));
            Assert.That(harness.RoomSystem.PublicRooms.LastResultCode, Is.EqualTo(OnlineRoomResultCode.Disconnected));

            Task<OnlinePublicRoomListResult> success = harness.RoomSystem.RefreshPublicRoomsAsync();
            PendingCall successCall = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            successCall.Succeed(Room("LIVE234", "Live", 2, 4), next: 0, hasMore: false);
            yield return Wait(success);
            Assert.That(harness.RoomSystem.PublicRooms.LastResultCode, Is.EqualTo(OnlineRoomResultCode.Success));
            Assert.That(harness.RoomSystem.PublicRooms.Items.Single().RoomCode, Is.EqualTo("LIVE234"));
        }

        [UnityTest]
        public IEnumerator FirstRefreshFailureKeepsEmptyAndCarriesRefreshFailureSource()
        {
            Harness harness = InstallArchitecture();
            Task<OnlinePublicRoomListResult> refresh = harness.RoomSystem.RefreshPublicRoomsAsync();
            harness.Network.TakeNext<C2G_GetPublicRoomList>().Fail(200117);
            yield return Wait(refresh);

            Assert.That(refresh.Result.Code, Is.EqualTo(OnlineRoomResultCode.ServiceUnavailable));
            Assert.That(harness.RoomSystem.PublicRooms.Items, Is.Empty);
            Assert.That(harness.RoomSystem.PublicRooms.State, Is.EqualTo(OnlinePublicRoomListState.Idle));
            Assert.That(harness.RoomSystem.PublicRooms.LastOperation, Is.EqualTo(OnlinePublicRoomListOperation.Refresh));
            Assert.That(harness.RoomSystem.PublicRooms.LastResultCode, Is.EqualTo(OnlineRoomResultCode.ServiceUnavailable));
        }

        [UnityTest]
        public IEnumerator ActualButtonsDriveRefreshCreateDirectJoinAndPublicJoinWithUnifiedBlocking()
        {
            EnsureUiRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));
            Harness harness = InstallArchitecture();
            using (Log.CaptureForTests((_, _) => { }))
            {
                UIHelper.ShowUI<OnlineRoomUICtrl>();
            }

            OnlineRoomUI ui = UnityEngine.Object.FindObjectOfType<OnlineRoomUI>();
            Assert.NotNull(ui);
            PendingCall opening = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            Assert.That(ui.RefreshRoomListButton.interactable, Is.False);
            opening.Succeed(Room("ABC234", "Owner", 1, 4), Room("FULL24", "Full", 4, 4), next: 70, hasMore: true);
            yield return null;
            yield return null;

            Assert.That(ui.CreateButton.interactable, Is.True);
            Assert.That(ui.JoinButton.interactable, Is.True);
            Assert.That(ui.LoadMoreButton.gameObject.activeSelf, Is.True);
            ui.LoadMoreButton.onClick.Invoke();
            PendingCall loadingMore = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            Assert.That(((C2G_GetPublicRoomList)loadingMore.Request).CursorRoomId, Is.EqualTo(70));
            Assert.That(harness.RoomSystem.PublicRooms.State, Is.EqualTo(OnlinePublicRoomListState.LoadingMore));
            Assert.That(ui.LoadMoreButton.interactable, Is.False);
            Assert.That(ui.RefreshRoomListButton.interactable, Is.True);
            Assert.That(harness.Network.CountPending<C2G_GetPublicRoomList>(), Is.Zero);

            // Refresh is the only list action enabled during LoadingMore and replaces that request.
            ui.RefreshRoomListButton.onClick.Invoke();
            PendingCall refresh = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            Assert.That(((C2G_GetPublicRoomList)refresh.Request).CursorRoomId, Is.Zero);
            Assert.That(ui.RefreshRoomListButton.interactable, Is.False);
            Assert.That(ui.LoadMoreButton.interactable, Is.False);
            loadingMore.Succeed(Room("STALE2", "Stale", 1, 4), next: 0, hasMore: false);
            refresh.Succeed(Room("NEW234", "New", 1, 4), next: 0, hasMore: false);
            yield return null;
            yield return null;
            Assert.That(harness.RoomSystem.PublicRooms.Items.Single().RoomCode, Is.EqualTo("NEW234"));

            ui.RoomCodeInput.text = "new234";
            ui.JoinButton.onClick.Invoke();
            PendingCall directJoin = harness.Network.TakeNext<C2G_JoinRoom>();
            Assert.That(((C2G_JoinRoom)directJoin.Request).RoomCode, Is.EqualTo("NEW234"));
            Assert.That(ui.CreateButton.interactable, Is.False);
            Assert.That(ui.RoomCodeInput.interactable, Is.False);
            directJoin.Fail(200110);
            yield return null;
            yield return null;

            // LoopScrollRect deliberately skips pooling while Application.isPlaying is false. Obtain a row from
            // its configured source and invoke the public data-source callback that the Controller registered.
            Transform rowTransform = ui.PublicRoomList.prefabSource.GetObject(0).transform;
            rowTransform.SetParent(ui.PublicRoomList.GetContentTransform(), false);
            rowTransform.gameObject.SetActive(true);
            ui.PublicRoomList.dataSource.ProvideData(rowTransform, 0);
            OnlineRoomListItemBoxUI row = rowTransform.GetComponent<OnlineRoomListItemBoxUI>();
            Assert.NotNull(row);

            ui.RefreshRoomListButton.onClick.Invoke();
            PendingCall reboundRefresh = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            reboundRefresh.Succeed(Room("CDE234", "Rebound", 2, 4), next: 0, hasMore: false);
            yield return null;
            yield return null;
            ui.PublicRoomList.dataSource.ProvideData(rowTransform, 0);
            row.JoinButton.onClick.Invoke();
            PendingCall rowJoin = harness.Network.TakeNext<C2G_JoinRoom>();
            Assert.That(((C2G_JoinRoom)rowJoin.Request).RoomCode, Is.EqualTo("CDE234"));
            Assert.That(harness.Network.CountPending<C2G_JoinRoom>(), Is.Zero,
                "A reused row must not retain the previous room callback.");
            rowJoin.Fail(200111);
            yield return null;
            yield return null;

            ui.RefreshRoomListButton.onClick.Invoke();
            PendingCall fullRefresh = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            fullRefresh.Succeed(Room("FULL24", "Full", 4, 4), next: 0, hasMore: false);
            yield return null;
            yield return null;
            ui.PublicRoomList.dataSource.ProvideData(rowTransform, 0);
            Assert.That(row.JoinButton.interactable, Is.False);
            Assert.That(harness.Network.CountPending<C2G_JoinRoom>(), Is.Zero);

            ui.CreateButton.onClick.Invoke();
            PendingCall create = harness.Network.TakeNext<C2G_CreateRoom>();
            Assert.That(ui.CloseButton.interactable, Is.True);
            create.Fail(200117);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisconnectedUiBlocksJoinAndLoadMoreThroughFailureUntilRefreshSucceeds()
        {
            EnsureUiRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));
            Harness harness = InstallArchitecture();
            using (Log.CaptureForTests((_, _) => { }))
            {
                UIHelper.ShowUI<OnlineRoomUICtrl>();
            }
            OnlineRoomUI ui = UnityEngine.Object.FindObjectOfType<OnlineRoomUI>();
            PendingCall opening = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            opening.Succeed(Room("OLD234", "Old", 1, 4), next: 80, hasMore: true);
            yield return null;
            yield return null;
            OnlineRoomListItemBoxUI row = CreateAndBindRow(ui, 0);

            harness.Network.RaiseDisconnected();
            ui.PublicRoomList.dataSource.ProvideData(row.transform, 0);
            Assert.That(ui.RoomCodeInput.interactable, Is.True);
            Assert.That(ui.JoinButton.interactable, Is.False);
            Assert.That(row.JoinButton.interactable, Is.False);
            Assert.That(ui.LoadMoreButton.gameObject.activeSelf, Is.False);
            Assert.That(ui.LoadMoreButton.interactable, Is.False);
            Assert.That(ui.RefreshRoomListButton.interactable, Is.True);
            Assert.That(harness.Network.CountPending<C2G_JoinRoom>(), Is.Zero);
            Assert.That(harness.Network.CountPending<C2G_GetPublicRoomList>(), Is.Zero);

            harness.Network.SetConnected();
            ui.RefreshRoomListButton.onClick.Invoke();
            PendingCall failedRefresh = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            failedRefresh.Fail(200117);
            yield return null;
            yield return null;
            ui.PublicRoomList.dataSource.ProvideData(row.transform, 0);
            Assert.That(harness.RoomSystem.PublicRooms.LastOperation, Is.EqualTo(OnlinePublicRoomListOperation.Refresh));
            Assert.That(harness.RoomSystem.PublicRooms.LastResultCode, Is.EqualTo(OnlineRoomResultCode.Disconnected));
            Assert.That(ui.JoinButton.interactable, Is.False);
            Assert.That(row.JoinButton.interactable, Is.False);
            Assert.That(ui.LoadMoreButton.gameObject.activeSelf, Is.False);
            Assert.That(harness.Network.CountPending<C2G_JoinRoom>(), Is.Zero);
            Assert.That(harness.Network.CountPending<C2G_GetPublicRoomList>(), Is.Zero);

            ui.RefreshRoomListButton.onClick.Invoke();
            PendingCall successfulRefresh = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            successfulRefresh.Succeed(Room("LIVE24", "Live", 2, 4), next: 60, hasMore: true);
            yield return null;
            yield return null;
            ui.PublicRoomList.dataSource.ProvideData(row.transform, 0);
            Assert.That(ui.JoinButton.interactable, Is.True);
            Assert.That(row.JoinButton.interactable, Is.True);
            Assert.That(ui.LoadMoreButton.gameObject.activeSelf, Is.True);
            Assert.That(ui.LoadMoreButton.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator HiddenLateResponseDoesNotTouchViewAndNextShowRestoresSystemSnapshot()
        {
            EnsureUiRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));
            Harness harness = InstallArchitecture();
            OnlineRoomUICtrl ctrl;
            using (Log.CaptureForTests((_, _) => { }))
            {
                ctrl = UIHelper.ShowUI<OnlineRoomUICtrl>();
            }
            OnlineRoomUI ui = UnityEngine.Object.FindObjectOfType<OnlineRoomUI>();
            PendingCall opening = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            string hiddenText = ui.PublicRoomListStateText.text;
            ctrl.Hide();
            Assert.That(ctrl.IsShow(), Is.False);

            opening.Succeed(Room("LATE24", "Late", 1, 4), next: 0, hasMore: false);
            yield return null;
            yield return null;
            Assert.That(harness.RoomSystem.PublicRooms.Items.Single().RoomCode, Is.EqualTo("LATE24"));
            Assert.That(ui.PublicRoomListStateText.text, Is.EqualTo(hiddenText));
            Assert.That(ui.PublicRoomList.totalCount, Is.Zero);

            ctrl.Show();
            Assert.That(ui.PublicRoomList.totalCount, Is.EqualTo(1));
            Assert.That(ui.PublicRoomListStateText.text, Is.EqualTo(Loc.T("ui.online.room.list.loading")));
            PendingCall reopenedRefresh = harness.Network.TakeNext<C2G_GetPublicRoomList>();
            Assert.That(((C2G_GetPublicRoomList)reopenedRefresh.Request).CursorRoomId, Is.Zero);
            reopenedRefresh.Succeed(Room("LATEST", "Latest", 1, 4), next: 0, hasMore: false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FailureTextsAndLanguageEventRebuildDynamicPresentationFromSnapshot()
        {
            EnsureUiRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));
            Harness harness = InstallArchitecture();
            using (Log.CaptureForTests((_, _) => { }))
            {
                UIHelper.ShowUI<OnlineRoomUICtrl>();
            }
            OnlineRoomUI ui = UnityEngine.Object.FindObjectOfType<OnlineRoomUI>();

            harness.Network.TakeNext<C2G_GetPublicRoomList>().Fail(200117);
            yield return null;
            yield return null;
            Assert.That(ui.PublicRoomListStateText.text, Is.EqualTo(Loc.T("ui.online.room.list.first_load_failed")));

            ui.RefreshRoomListButton.onClick.Invoke();
            harness.Network.TakeNext<C2G_GetPublicRoomList>()
                .Succeed(Room("TEXT24", "Owner", 1, 4), next: 70, hasMore: true);
            yield return null;
            yield return null;
            OnlineRoomListItemBoxUI row = CreateAndBindRow(ui, 0);

            ui.RefreshRoomListButton.onClick.Invoke();
            harness.Network.TakeNext<C2G_GetPublicRoomList>().Fail(200117);
            yield return null;
            yield return null;
            Assert.That(ui.PublicRoomListStateText.text, Is.EqualTo(Loc.T("ui.online.room.list.refresh_failed")));

            ui.RefreshRoomListButton.onClick.Invoke();
            harness.Network.TakeNext<C2G_GetPublicRoomList>()
                .Succeed(Room("TEXT24", "Owner", 1, 4), next: 70, hasMore: true);
            yield return null;
            yield return null;
            ui.LoadMoreButton.onClick.Invoke();
            harness.Network.TakeNext<C2G_GetPublicRoomList>().Fail(200117);
            yield return null;
            yield return null;
            Assert.That(ui.PublicRoomListStateText.text, Is.EqualTo(Loc.T("ui.online.room.list.load_more_failed")));

            ui.PublicRoomListStateText.text = "stale state";
            row.RoomCodeText.text = "stale room";
            row.OwnerText.text = "stale owner";
            row.MemberCountText.text = "stale count";
            harness.Game.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            Assert.That(ui.PublicRoomListStateText.text,
                Is.EqualTo(OnlinePublicRoomListText.State(harness.RoomSystem.PublicRooms)));
            ui.PublicRoomList.dataSource.ProvideData(row.transform, 0);
            Assert.That(row.RoomCodeText.text, Is.EqualTo("TEXT24"));
            Assert.That(row.OwnerText.text, Is.EqualTo(Loc.T("ui.online.room.list.owner", "Owner")));
            Assert.That(row.MemberCountText.text, Is.EqualTo(Loc.T("ui.online.room.list.members", 1, 4)));
        }

        [Test]
        public void RealPrefabsKeepThreeSectionsGeneratedViewsLocalizationAndChatContract()
        {
            GameObject roomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoomPrefabPath);
            GameObject itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ListItemPrefabPath);
            GameObject chatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChatItemPrefabPath);
            Assert.NotNull(roomPrefab);
            Assert.NotNull(itemPrefab);
            Assert.NotNull(chatPrefab);

            OnlineRoomUI ui = roomPrefab.GetComponent<OnlineRoomUI>();
            OnlineRoomListItemBoxUI item = itemPrefab.GetComponent<OnlineRoomListItemBoxUI>();
            Assert.NotNull(ui);
            Assert.NotNull(item);
            Assert.That(item, Is.InstanceOf<BaseBoxUI>());
            Assert.That(ui.RootPanel.sizeDelta, Is.EqualTo(new Vector2(1080, 680)));
            AssertRect(roomPrefab.transform.Find("EmptyRoot") as RectTransform, new Vector2(1000, 520), new Vector2(0, -8));
            AssertRect(roomPrefab.transform.Find("EmptyRoot/CreateSection") as RectTransform, new Vector2(470, 120), new Vector2(-255, 190));
            AssertRect(roomPrefab.transform.Find("EmptyRoot/DirectJoinSection") as RectTransform, new Vector2(470, 120), new Vector2(255, 190));
            AssertRect(roomPrefab.transform.Find("EmptyRoot/PublicRoomListSection") as RectTransform, new Vector2(980, 360), new Vector2(0, -70));
            Assert.That(ui.RoomCodeInput.transform.IsChildOf(roomPrefab.transform.Find("EmptyRoot/DirectJoinSection")), Is.True);
            Assert.That(ui.CreateButton.transform.IsChildOf(roomPrefab.transform.Find("EmptyRoot/CreateSection")), Is.True);
            Assert.AreSame(itemPrefab, ui.PublicRoomList.prefabSource.prefab);
            Assert.AreSame(chatPrefab, ui.ChatMessageList.prefabSource.prefab);
            Assert.NotNull(chatPrefab.GetComponent<OnlineRoomChatMessageBoxUI>());
            foreach (Component component in new Component[] { ui.ChatInput, ui.ChatMessageList, ui.ChatSendButton, ui.ChatStateText })
            {
                Assert.That(component.transform.IsChildOf(roomPrefab.transform.Find("JoinedRoot")), Is.True, component.name);
            }

            AssertFixed(roomPrefab.transform.Find("EmptyRoot/CreateSection/CreateTitleText"), "ui.online.room.create.title", "创建新房间");
            AssertFixed(roomPrefab.transform.Find("EmptyRoot/CreateSection/CreateHintText"), "ui.online.room.create.hint", "无需房间号");
            AssertFixed(roomPrefab.transform.Find("EmptyRoot/DirectJoinSection/DirectJoinTitleText"), "ui.online.room.direct_join.title", "通过房间号加入");
            AssertFixed(roomPrefab.transform.Find("EmptyRoot/PublicRoomListSection/PublicRoomListTitleText"), "ui.online.room.list.title", "公开房间");
            AssertFixed(ui.RefreshRoomListButton.transform.Find("Text"), "ui.online.room.list.refresh", "刷新");
            AssertFixed(ui.LoadMoreButton.transform.Find("Text"), "ui.online.room.list.load_more", "加载更多");
            AssertFixed(item.JoinButton.transform.Find("Text"), "ui.online.room.join", "加入");

            string[] generatedFields = typeof(OnlineRoomListItemBoxUI)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(field => field.Name).OrderBy(name => name).ToArray();
            Assert.That(generatedFields, Is.EqualTo(new[] { "JoinButton", "MemberCountText", "OwnerText", "RoomCodeText" }));
        }

        private static OnlineRoomListItemBoxUI CreateAndBindRow(OnlineRoomUI ui, int index)
        {
            // EditMode 不进入 LoopScrollRect 的运行时建池；仍使用其正式 Source 和 Controller 已注册的 Data Source。
            Transform rowTransform = ui.PublicRoomList.prefabSource.GetObject(index).transform;
            rowTransform.SetParent(ui.PublicRoomList.GetContentTransform(), false);
            rowTransform.gameObject.SetActive(true);
            ui.PublicRoomList.dataSource.ProvideData(rowTransform, index);
            OnlineRoomListItemBoxUI row = rowTransform.GetComponent<OnlineRoomListItemBoxUI>();
            Assert.NotNull(row);
            return row;
        }

        private static void AssertRect(RectTransform rect, Vector2 size, Vector2 position)
        {
            Assert.NotNull(rect);
            Assert.That(rect.sizeDelta, Is.EqualTo(size));
            Assert.That(rect.anchoredPosition, Is.EqualTo(position));
        }

        private static void AssertFixed(Transform transform, string key, string preview)
        {
            Assert.NotNull(transform, key);
            TMP_Text text = transform.GetComponent<TMP_Text>();
            LocalizedText localized = transform.GetComponent<LocalizedText>();
            Assert.NotNull(text, key);
            Assert.NotNull(localized, key);
            Assert.That(localized.Key, Is.EqualTo(key));
            Assert.That(localized.Texts, Is.Empty);
            Assert.That(text.text, Is.EqualTo(preview));
            Assert.That(text.text, Does.Not.Contain("ui."));
        }

        private static PublicRoomSummary Room(string code, string owner, int count, int capacity)
        {
            PublicRoomSummary room = PublicRoomSummary.Create();
            room.RoomCode = code;
            room.OwnerDisplayName = owner;
            room.MemberCount = count;
            room.Capacity = capacity;
            return room;
        }

        private static IEnumerator Wait(Task task)
        {
            for (int i = 0; i < 20 && !task.IsCompleted; i++)
            {
                yield return null;
            }
            Assert.That(task.IsCompleted, Is.True);
        }

        private static Harness InstallArchitecture()
        {
            ResetArchitecture();
            var game = new global::Game.Game();
            var account = new OnlineAccountModel();
            var roomModel = new OnlineRoomModel();
            var chatModel = new OnlineRoomChatModel();
            var localizationModel = new LocalizationModel();
            var network = new FakeNetwork();
            var roomSystem = new OnlineRoomSystem();
            var chatSystem = new OnlineRoomChatSystem();
            game.RegisterModel(localizationModel);
            game.RegisterModel(account);
            game.RegisterModel(roomModel);
            game.RegisterModel(chatModel);
            game.RegisterUtility<INetworkRuntime>(network);
            game.RegisterUtility<IOnlineAccountPreferenceStore>(new FakePreferences());
            game.RegisterUtility(new LocalizationUtility(loadConfiguredTables: false));
            game.RegisterSystem(roomSystem);
            game.RegisterSystem(chatSystem);
            foreach (ICanInit target in new ICanInit[] { localizationModel, account, roomModel, chatModel, roomSystem, chatSystem })
            {
                target.Init();
                target.Initialized = true;
            }
            typeof(Architecture<global::Game.Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, game);
            return new Harness(game, roomModel, roomSystem, network);
        }

        private static void ResetArchitecture()
        {
            FieldInfo field = typeof(Architecture<global::Game.Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            if (field?.GetValue(null) is IArchitecture architecture)
            {
                architecture.Deinit();
            }
            field?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static void EnsureUiRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIRoot));
            new GameObject("HUD", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Dialog", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Popup", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Tips", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Top", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject(SceneRoot.PoolRootName);
            Type inputModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), inputModule);
            eventSystem.transform.SetParent(root.transform, false);
        }

        private sealed class Harness
        {
            public Harness(global::Game.Game game, OnlineRoomModel roomModel, OnlineRoomSystem roomSystem, FakeNetwork network)
            {
                Game = game;
                RoomModel = roomModel;
                RoomSystem = roomSystem;
                Network = network;
            }
            public global::Game.Game Game { get; }
            public OnlineRoomModel RoomModel { get; }
            public OnlineRoomSystem RoomSystem { get; }
            public FakeNetwork Network { get; }
        }

        private sealed class FakeNetwork : INetworkRuntime
        {
            private readonly List<PendingCall> calls = new List<PendingCall>();
            private readonly List<IRequest> requestHistory = new List<IRequest>();
            private readonly List<Action<NetworkDisconnected>> disconnected = new List<Action<NetworkDisconnected>>();
            public bool IsInitialized { get; private set; } = true;
            public NetworkConnectionState ConnectionState { get; private set; } = NetworkConnectionState.Connected;
            public int ConnectionGeneration => 1;
            public void Initialize() => IsInitialized = true;
            public void Update() { }
            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken) where TResponse : class, IResponse
            {
                var call = new PendingCall(request);
                calls.Add(call);
                requestHistory.Add(request);
                return call.Task.ContinueWith(task => (TResponse)task.GetAwaiter().GetResult(), CancellationToken.None);
            }
            public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler) where TMessage : class, IMessage => new Subscription(() => { });
            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler)
            {
                disconnected.Add(handler);
                return new Subscription(() => disconnected.Remove(handler));
            }
            public void Disconnect(NetworkDisconnectReason reason) => ConnectionState = NetworkConnectionState.Disconnected;
            public void Dispose() { IsInitialized = false; ConnectionState = NetworkConnectionState.Disposed; }
            public void SetConnected() => ConnectionState = NetworkConnectionState.Connected;
            public void RaiseDisconnected()
            {
                ConnectionState = NetworkConnectionState.Disconnected;
                var evt = new NetworkDisconnected(1, NetworkDisconnectReason.TransportError, "test");
                foreach (Action<NetworkDisconnected> handler in disconnected.ToArray()) handler(evt);
            }
            public PendingCall TakeNext<T>() where T : class, IRequest
            {
                int index = calls.FindIndex(call => call.Request is T);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), typeof(T).Name);
                PendingCall call = calls[index];
                calls.RemoveAt(index);
                return call;
            }
            public int CountPending<T>() where T : class, IRequest => calls.Count(call => call.Request is T);
            public int TotalCalls<T>() where T : class, IRequest => requestHistory.Count(request => request is T);
        }

        private sealed class PendingCall
        {
            private readonly TaskCompletionSource<IResponse> source = new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            public PendingCall(IRequest request) => Request = request;
            public IRequest Request { get; }
            public Task<IResponse> Task => source.Task;
            public void Fail(int error)
            {
                if (Request is C2G_GetPublicRoomList) source.TrySetResult(new G2C_GetPublicRoomList { Error = error });
                else if (Request is C2G_JoinRoom) source.TrySetResult(new G2C_JoinRoom { Error = error });
                else source.TrySetResult(new G2C_CreateRoom { Error = error });
            }
            public void Succeed(PublicRoomSummary first, PublicRoomSummary second = null, long next = 0, bool hasMore = false)
            {
                var response = new G2C_GetPublicRoomList { Error = 0, NextCursorRoomId = next, HasMore = hasMore };
                response.Rooms.Add(first);
                if (second != null) response.Rooms.Add(second);
                source.TrySetResult(response);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private Action dispose;
            public Subscription(Action dispose) => this.dispose = dispose;
            public void Dispose() { dispose?.Invoke(); dispose = null; }
        }

        private sealed class FakePreferences : IOnlineAccountPreferenceStore
        {
            public string LoadLastUserName() => string.Empty;
            public bool SaveLastUserName(string userName, out string error) { error = string.Empty; return true; }
        }
    }
}
