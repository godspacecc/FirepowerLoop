using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using QFramework;

namespace Game.Tests
{
    public sealed class OnlineRoomTests
    {
        [TearDown]
        public void TearDown()
        {
            ResetTestRoomArchitecture();
            UIHelper.ResetForTests();
            ResetGameArchitecture();
        }

        [Test]
        public void RoomSnapshotCopiesCallerOwnedMembers()
        {
            var members = new List<OnlineRoomMemberSnapshot>
            {
                new OnlineRoomMemberSnapshot(1, "one", true, OnlineRoomMemberConnectionState.Online, 1)
            };
            var snapshot = new OnlineRoomSnapshot(true, 10, "ABC234", 1, 1, members, false,
                OnlineRoomOperationState.Idle, OnlineRoomResultCode.Success);

            members[0] = new OnlineRoomMemberSnapshot(2, "two", false, OnlineRoomMemberConnectionState.Online, 2);
            members.Add(new OnlineRoomMemberSnapshot(3, "three", false, OnlineRoomMemberConnectionState.Online, 3));

            Assert.That(snapshot.Members.Count, Is.EqualTo(1));
            Assert.That(snapshot.Members[0].PlayerId, Is.EqualTo(1));
            Assert.That(((IList<OnlineRoomMemberSnapshot>)snapshot.Members).IsReadOnly, Is.True);
        }

        [Test]
        public void RoomChatSnapshotCopiesCallerOwnedMessages()
        {
            var messages = new List<OnlineRoomChatMessageSnapshot>
            {
                new OnlineRoomChatMessageSnapshot("one", 1, 1, "sender", 1, "first")
            };
            var snapshot = new OnlineRoomChatSnapshot(10, messages, 1, OnlineRoomChatHistoryState.Ready,
                OnlineRoomChatSendState.Idle, OnlineRoomChatResultCode.Success, false, string.Empty);

            messages[0] = new OnlineRoomChatMessageSnapshot("two", 2, 2, "other", 2, "second");
            messages.Clear();

            Assert.That(snapshot.Messages.Count, Is.EqualTo(1));
            Assert.That(snapshot.Messages[0].ChatMessageId, Is.EqualTo("one"));
            Assert.That(((IList<OnlineRoomChatMessageSnapshot>)snapshot.Messages).IsReadOnly, Is.True);
        }

        [TestCase("abc234", "ABC234", true)]
        [TestCase(" abc234 ", "ABC234", true)]
        [TestCase("abc10z", "ABC10Z", false)]
        [TestCase("abc23", "ABC23", false)]
        public void RoomCodeNormalizationMatchesServerContract(string input, string normalized, bool valid)
        {
            Assert.That(OnlineRoomSystem.NormalizeRoomCode(input), Is.EqualTo(normalized));
            Assert.That(OnlineRoomSystem.IsValidRoomCode(input), Is.EqualTo(valid));
        }

        [Test]
        public void RoomErrorsMapToStableClientResults()
        {
            Assert.That(OnlineRoomSystem.MapServerError(200109), Is.EqualTo(OnlineRoomResultCode.RoomCodeInvalid));
            Assert.That(OnlineRoomSystem.MapServerError(200110), Is.EqualTo(OnlineRoomResultCode.RoomNotFound));
            Assert.That(OnlineRoomSystem.MapServerError(200111), Is.EqualTo(OnlineRoomResultCode.RoomFull));
            Assert.That(OnlineRoomSystem.MapServerError(200112), Is.EqualTo(OnlineRoomResultCode.AlreadyInRoom));
            Assert.That(OnlineRoomSystem.MapServerError(200113), Is.EqualTo(OnlineRoomResultCode.NotInRoom));
            Assert.That(OnlineRoomSystem.MapServerError(200114), Is.EqualTo(OnlineRoomResultCode.OperationInvalid));
            Assert.That(OnlineRoomSystem.MapServerError(200115), Is.EqualTo(OnlineRoomResultCode.RateLimited));
            Assert.That(OnlineRoomSystem.MapServerError(200116), Is.EqualTo(OnlineRoomResultCode.RoomClosing));
            Assert.That(OnlineRoomSystem.MapServerError(200117), Is.EqualTo(OnlineRoomResultCode.ServiceUnavailable));
            Assert.That(OnlineRoomSystem.MapServerError(200133), Is.EqualTo(OnlineRoomResultCode.PurposeMismatch));
            Assert.That(OnlineRoomSystem.MapServerError(200134), Is.EqualTo(OnlineRoomResultCode.VersionMismatch));
        }

        [Test]
        public void ArcadeRoomIdentityRequiresVersionAndConfigFingerprint()
        {
            Assert.That(OnlineRoomSystem.IsValidArcadeIdentity(null, "config-a"), Is.False);
            Assert.That(OnlineRoomSystem.IsValidArcadeIdentity("1.0.0", " "), Is.False);
            Assert.That(OnlineRoomSystem.IsValidArcadeIdentity(new string('v', 65), "config-a"), Is.False);
            Assert.That(OnlineRoomSystem.IsValidArcadeIdentity("1.0.0", new string('f', 129)), Is.False);
            Assert.That(OnlineRoomSystem.IsValidArcadeIdentity("1.0.0", "config-a"), Is.True);
        }

        [UnityTest]
        public IEnumerator CreateArcadeRoomSendsPrivateTwoPlayerIdentityAndCopiesResponse()
        {
            RoomHarness harness = CreateHarness();
            Task<OnlineRoomOperationResult> task = harness.System.CreateArcadeRoomAsync("1.0.0", "config-a");
            PendingCall call = harness.Network.TakeNext<C2G_CreateRoom>();
            var request = (C2G_CreateRoom)call.Request;

            Assert.That(request.Purpose, Is.EqualTo((int)OnlineRoomPurpose.Arcade));
            Assert.That(request.Visibility, Is.EqualTo((int)OnlineRoomVisibility.Private));
            Assert.That(request.ClientVersion, Is.EqualTo("1.0.0"));
            Assert.That(request.ArcadeConfigFingerprint, Is.EqualTo("config-a"));

            ET.OnlineRoomSnapshot room = ProtocolRoom(101, "ARC234", 10, 1,
                Member(10, "owner", true, 1, 1));
            room.Purpose = (int)OnlineRoomPurpose.Arcade;
            room.Visibility = (int)OnlineRoomVisibility.Private;
            room.Capacity = 2;
            room.ClientVersion = "1.0.0";
            room.ArcadeConfigFingerprint = "config-a";
            call.TrySetResult(new G2C_CreateRoom { Error = 0, HasRoom = true, Room = room });
            yield return WaitTask(task);

            Assert.That(task.Result.Code, Is.EqualTo(OnlineRoomResultCode.Success));
            Assert.That(task.Result.Snapshot.Purpose, Is.EqualTo(OnlineRoomPurpose.Arcade));
            Assert.That(task.Result.Snapshot.Visibility, Is.EqualTo(OnlineRoomVisibility.Private));
            Assert.That(task.Result.Snapshot.Capacity, Is.EqualTo(2));
            Assert.That(task.Result.Snapshot.ClientVersion, Is.EqualTo("1.0.0"));
            Assert.That(task.Result.Snapshot.ArcadeConfigFingerprint, Is.EqualTo("config-a"));
        }

        [UnityTest]
        public IEnumerator MatchArcadeRoomUsesStableOperationAndCopiesMatchedRoom()
        {
            RoomHarness harness = CreateHarness();
            Task<OnlineRoomOperationResult> task = harness.System.MatchArcadeRoomAsync("1.0.0", "config-a");
            PendingCall call = harness.Network.TakeNext<C2G_MatchArcadeRoom>();
            var request = (C2G_MatchArcadeRoom)call.Request;

            Assert.That(request.OperationId, Has.Length.EqualTo(32));
            Assert.That(request.ClientVersion, Is.EqualTo("1.0.0"));
            Assert.That(request.ArcadeConfigFingerprint, Is.EqualTo("config-a"));
            Assert.That(harness.System.Snapshot.OperationState, Is.EqualTo(OnlineRoomOperationState.Matching));

            ET.OnlineRoomSnapshot room = ProtocolRoom(103, "MAT234", 20, 2,
                Member(20, "owner", true, 1, 1),
                Member(10, "joiner", false, 1, 2));
            room.Purpose = (int)OnlineRoomPurpose.Arcade;
            room.Visibility = (int)OnlineRoomVisibility.Private;
            room.Capacity = 2;
            room.ClientVersion = "1.0.0";
            room.ArcadeConfigFingerprint = "config-a";
            call.TrySetResult(new G2C_MatchArcadeRoom { Error = 0, HasRoom = true, Room = room });
            yield return WaitTask(task);

            Assert.That(task.Result.Code, Is.EqualTo(OnlineRoomResultCode.Success));
            Assert.That(task.Result.Snapshot.RoomId, Is.EqualTo(103));
            Assert.That(task.Result.Snapshot.Members.Count, Is.EqualTo(2));
            Assert.That(task.Result.Snapshot.OperationState, Is.EqualTo(OnlineRoomOperationState.Idle));
        }

        [UnityTest]
        public IEnumerator JoinArcadeRoomSendsExpectedIdentity()
        {
            RoomHarness harness = CreateHarness();
            Task<OnlineRoomOperationResult> task = harness.System.JoinArcadeRoomAsync("arc234", "1.0.0", "config-a");
            PendingCall call = harness.Network.TakeNext<C2G_JoinRoom>();
            var request = (C2G_JoinRoom)call.Request;

            Assert.That(request.RoomCode, Is.EqualTo("ARC234"));
            Assert.That(request.ExpectedPurpose, Is.EqualTo((int)OnlineRoomPurpose.Arcade));
            Assert.That(request.ClientVersion, Is.EqualTo("1.0.0"));
            Assert.That(request.ArcadeConfigFingerprint, Is.EqualTo("config-a"));

            ET.OnlineRoomSnapshot room = ProtocolRoom(102, "ARC234", 20, 2,
                Member(20, "owner", true, 1, 1),
                Member(10, "joiner", false, 1, 2));
            room.Purpose = (int)OnlineRoomPurpose.Arcade;
            room.Visibility = (int)OnlineRoomVisibility.Private;
            room.Capacity = 2;
            room.ClientVersion = "1.0.0";
            room.ArcadeConfigFingerprint = "config-a";
            call.TrySetResult(new G2C_JoinRoom { Error = 0, HasRoom = true, Room = room });
            yield return WaitTask(task);

            Assert.That(task.Result.Code, Is.EqualTo(OnlineRoomResultCode.Success));
        }

        [Test]
        public void ProtocolSnapshotIsCopiedAndOlderRevisionIsIgnored()
        {
            RoomHarness harness = CreateHarness();
            harness.Network.RaiseRoomChanged(true, ProtocolRoom(100, "ABC234", 10, 7,
                Member(10, "alpha", true, 1, 1),
                Member(20, "beta", false, 2, 2),
                Member(30, "gamma", false, 1, 3),
                Member(40, "delta", false, 1, 4),
                Member(50, "epsilon", false, 1, 5)));

            OnlineRoomSnapshot snapshot = harness.System.Snapshot;
            Assert.That(snapshot.Members.Count, Is.EqualTo(5));
            Assert.That(snapshot.Members[4].JoinSequence, Is.EqualTo(5));
            Assert.That(snapshot.Members[0].IsOwner, Is.True);

            harness.Network.RaiseRoomChanged(true, ProtocolRoom(100, "ABC234", 10, 6,
                Member(10, "old", true, 1, 1)));
            Assert.That(harness.System.Snapshot.MembershipRevision, Is.EqualTo(7));
            Assert.That(harness.System.Snapshot.Members.Count, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator DisconnectPreservesRoomUntilAuthenticatedRestoreSaysOtherwise()
        {
            RoomHarness harness = CreateHarness();
            harness.Network.RaiseRoomChanged(true, ProtocolRoom(200, "BCD234", 10, 1, Member(10, "owner", true, 1, 1)));
            harness.Network.RaiseDisconnected(NetworkDisconnectReason.TransportError);
            harness.AccountModel.Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Disconnected,
                OnlineOperationState.Idle,
                OnlineAccountResultCode.Disconnected,
                string.Empty,
                0,
                0,
                0,
                false,
                "owner"));

            Assert.That(harness.System.Snapshot.HasRoom, Is.True);
            Assert.That(harness.System.Snapshot.ConnectionLost, Is.True);

            harness.Network.SetConnected();
            harness.AccountModel.Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,
                OnlineOperationState.Idle,
                OnlineAccountResultCode.Success,
                "owner",
                1,
                10,
                2,
                true,
                "owner"));
            PendingCall restore = harness.Network.TakeNext<C2G_GetCurrentRoom>();
            restore.TrySetResult(new G2C_GetCurrentRoom { Error = 200117, HasRoom = false });
            yield return WaitUntil(() => harness.System.Snapshot.LastResultCode == OnlineRoomResultCode.ServiceUnavailable);

            Assert.That(harness.System.Snapshot.HasRoom, Is.True);
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineRoomResultCode.ServiceUnavailable));

            Task<OnlineRoomOperationResult> restoreAgainTask = harness.System.RestoreCurrentRoomAsync();
            PendingCall restoreAgain = harness.Network.TakeNext<C2G_GetCurrentRoom>();
            restoreAgain.TrySetResult(new G2C_GetCurrentRoom { Error = 0, HasRoom = false });
            yield return WaitTask(restoreAgainTask);
            yield return null;

            Assert.That(harness.System.Snapshot.HasRoom, Is.False);
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineRoomResultCode.Success));
        }

        [UnityTest]
        public IEnumerator DuplicateAuthenticatedSessionRestoresOnceAndCreateButtonSendsRequest()
        {
            EnsureUiRootForTest();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));
            RoomHarness harness = InstallMinimalGameArchitectureForUiTest();
            harness.Network.SetConnected();

            harness.AccountModel.Publish(AuthenticatedSnapshot("owner", 1, 10, 42, OnlineAccountResultCode.Success));
            PendingCall restore = harness.Network.TakeNext<C2G_GetCurrentRoom>();

            harness.AccountModel.Publish(AuthenticatedSnapshot("owner", 1, 10, 42, OnlineAccountResultCode.AccountCreated));
            Assert.That(harness.Network.CountPending<C2G_GetCurrentRoom>(), Is.EqualTo(0));

            restore.TrySetResult(new G2C_GetCurrentRoom { Error = 0, HasRoom = false });
            yield return WaitUntil(() => harness.System.Snapshot.OperationState == OnlineRoomOperationState.Idle);

            using (Log.CaptureForTests((_, _) => { }))
            {
                UIHelper.ShowUI<OnlineRoomUICtrl>();
            }

            OnlineRoomUI roomUi = UnityEngine.Object.FindObjectOfType<OnlineRoomUI>();
            Assert.That(roomUi, Is.Not.Null);
            Assert.That(roomUi.CreateButton.interactable, Is.True);

            roomUi.CreateButton.onClick.Invoke();
            PendingCall create = harness.Network.TakeNext<C2G_CreateRoom>();
            Assert.That(harness.Network.CountPending<C2G_CreateRoom>(), Is.EqualTo(0));
            create.TrySetResult(new G2C_CreateRoom
            {
                Error = 0,
                HasRoom = true,
                Room = ProtocolRoom(950, "ABC234", 10, 1, Member(10, "owner", true, 1, 1))
            });
            yield return WaitUntil(() => harness.System.Snapshot.RoomId == 950);

            Assert.That(harness.System.Snapshot.OperationState, Is.EqualTo(OnlineRoomOperationState.Idle));
            Assert.That(harness.System.Snapshot.HasRoom, Is.True);
        }

        [UnityTest]
        public IEnumerator NewSessionEpochAfterDisconnectRestoresAgain()
        {
            RoomHarness harness = CreateHarness();
            harness.Network.SetConnected();

            harness.AccountModel.Publish(AuthenticatedSnapshot("owner", 1, 10, 42, OnlineAccountResultCode.Success));
            PendingCall firstRestore = harness.Network.TakeNext<C2G_GetCurrentRoom>();
            firstRestore.TrySetResult(new G2C_GetCurrentRoom { Error = 0, HasRoom = false });
            yield return WaitUntil(() => harness.System.Snapshot.OperationState == OnlineRoomOperationState.Idle);

            harness.AccountModel.Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Disconnected,
                OnlineOperationState.Idle,
                OnlineAccountResultCode.Disconnected,
                string.Empty,
                0,
                0,
                0,
                false,
                "owner"));

            harness.Network.SetConnected();
            harness.AccountModel.Publish(AuthenticatedSnapshot("owner", 1, 10, 43, OnlineAccountResultCode.Success));
            Assert.That(harness.Network.CountPending<C2G_GetCurrentRoom>(), Is.EqualTo(1));
            PendingCall secondRestore = harness.Network.TakeNext<C2G_GetCurrentRoom>();
            Assert.That(harness.Network.CountPending<C2G_GetCurrentRoom>(), Is.EqualTo(0));
            secondRestore.TrySetResult(new G2C_GetCurrentRoom { Error = 0, HasRoom = false });
            yield return WaitUntil(() => harness.System.Snapshot.OperationState == OnlineRoomOperationState.Idle);
        }

        [UnityTest]
        public IEnumerator CreateTimeoutRetryReusesOperationId()
        {
            RoomHarness harness = CreateHarness();
            yield return AssertRetryReusesOperationId(
                () => harness.System.CreateRoomAsync(),
                () => ((C2G_CreateRoom)harness.Network.TakeNext<C2G_CreateRoom>().Request).OperationId);
            yield return AssertRetryReusesOperationId(
                () => harness.System.JoinRoomAsync("ABC234"),
                () => ((C2G_JoinRoom)harness.Network.TakeNext<C2G_JoinRoom>().Request).OperationId);

            harness.Network.RaiseRoomChanged(true, ProtocolRoom(300, "ABC234", 10, 1, Member(10, "owner", true, 1, 1)));
            yield return AssertRetryReusesOperationId(
                () => harness.System.LeaveRoomAsync(),
                () => ((C2G_LeaveRoom)harness.Network.TakeNext<C2G_LeaveRoom>().Request).OperationId);
        }

        [UnityTest]
        public IEnumerator PushBeforeCreateResponseDoesNotUnlockOperation()
        {
            RoomHarness harness = CreateHarness();
            Task<OnlineRoomOperationResult> createTask = harness.System.CreateRoomAsync();
            PendingCall createCall = harness.Network.TakeNext<C2G_CreateRoom>();
            harness.Network.RaiseRoomChanged(true, ProtocolRoom(400, "CDE234", 10, 1, Member(10, "owner", true, 1, 1)));
            Assert.That(harness.System.Snapshot.OperationState, Is.EqualTo(OnlineRoomOperationState.Creating));
            Task<OnlineRoomOperationResult> blockedJoin = harness.System.JoinRoomAsync("ABC234");
            yield return WaitTask(blockedJoin);
            Assert.That(blockedJoin.Result.Code, Is.EqualTo(OnlineRoomResultCode.OperationInProgress));
            createCall.TrySetResult(new G2C_CreateRoom { Error = 0, HasRoom = true, Room = ProtocolRoom(400, "CDE234", 10, 1, Member(10, "owner", true, 1, 1)) });
            yield return WaitTask(createTask);
            Assert.That(createTask.Result.Code, Is.EqualTo(OnlineRoomResultCode.Success));

            Task<OnlineRoomOperationResult> joinTask = harness.System.JoinRoomAsync("CDE234");
            PendingCall joinCall = harness.Network.TakeNext<C2G_JoinRoom>();
            harness.Network.RaiseRoomChanged(true, ProtocolRoom(400, "CDE234", 10, 1, Member(10, "owner", true, 1, 1)));
            Assert.That(harness.System.Snapshot.OperationState, Is.EqualTo(OnlineRoomOperationState.Joining));
            joinCall.TrySetResult(new G2C_JoinRoom { Error = 0, HasRoom = true, Room = ProtocolRoom(400, "CDE234", 10, 2, Member(10, "owner", true, 1, 1), Member(20, "joiner", false, 1, 2)) });
            yield return WaitTask(joinTask);
            Assert.That(joinTask.Result.Code, Is.EqualTo(OnlineRoomResultCode.Success));
        }

        [UnityTest]
        public IEnumerator AccountIntentCancellationClearsPendingAndNewIntentUsesNewOperationId()
        {
            RoomHarness harness = CreateHarness();
            Task<OnlineRoomOperationResult> createTask = harness.System.CreateRoomAsync();
            PendingCall oldCreate = harness.Network.TakeNext<C2G_CreateRoom>();
            string oldOperationId = ((C2G_CreateRoom)oldCreate.Request).OperationId;

            harness.AccountModel.Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Disconnected,
                OnlineOperationState.Idle,
                OnlineAccountResultCode.Success,
                string.Empty,
                0,
                0,
                0,
                false,
                "owner"));
            oldCreate.TrySetResult(new G2C_CreateRoom { Error = 0, HasRoom = true, Room = ProtocolRoom(900, "ZZZ234", 10, 1, Member(10, "old", true, 1, 1)) });
            yield return WaitTask(createTask);
            Assert.That(harness.System.Snapshot.HasRoom, Is.False);

            harness.Network.SetConnected();
            harness.AccountModel.Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,
                OnlineOperationState.Idle,
                OnlineAccountResultCode.Success,
                "new-owner",
                2,
                20,
                1,
                false,
                "new-owner"));
            PendingCall restore = harness.Network.TakeNext<C2G_GetCurrentRoom>();
            restore.TrySetResult(new G2C_GetCurrentRoom { Error = 0, HasRoom = false });
            yield return null;

            Task<OnlineRoomOperationResult> newCreateTask = harness.System.CreateRoomAsync();
            PendingCall newCreate = harness.Network.TakeNext<C2G_CreateRoom>();
            string newOperationId = ((C2G_CreateRoom)newCreate.Request).OperationId;
            newCreate.TrySetResult(new G2C_CreateRoom { Error = 0, HasRoom = true, Room = ProtocolRoom(901, "YYY234", 20, 1, Member(20, "new", true, 1, 1)) });
            yield return WaitTask(newCreateTask);

            Assert.That(newOperationId, Is.Not.EqualTo(oldOperationId));
            Assert.That(harness.System.Snapshot.RoomId, Is.EqualTo(901));
        }

        [UnityTest]
        public IEnumerator RestoreTimeoutAfterLogoutStartsDoesNotPublishRoomTimeout()
        {
            RoomHarness harness = CreateHarness();
            harness.Network.SetConnected();
            harness.AccountModel.Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,
                OnlineOperationState.Idle,
                OnlineAccountResultCode.Success,
                "owner",
                1,
                10,
                2,
                false,
                "owner"));
            PendingCall restore = harness.Network.TakeNext<C2G_GetCurrentRoom>();

            harness.AccountModel.Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,
                OnlineOperationState.LoggingOut,
                OnlineAccountResultCode.None,
                "owner",
                1,
                10,
                2,
                false,
                "owner"));
            restore.TrySetException(new System.TimeoutException());
            yield return null;

            Assert.That(harness.System.Snapshot.HasRoom, Is.False);
            Assert.That(harness.System.Snapshot.LastResultCode, Is.Not.EqualTo(OnlineRoomResultCode.Timeout));
            Assert.That(harness.System.Snapshot.OperationState, Is.EqualTo(OnlineRoomOperationState.Idle));
        }

        [UnityTest]
        public IEnumerator RestoreSuccessAfterLogoutStartsDoesNotPublishStaleRoom()
        {
            RoomHarness harness = CreateHarness();
            harness.Network.SetConnected();
            harness.AccountModel.Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,
                OnlineOperationState.Idle,
                OnlineAccountResultCode.Success,
                "owner",
                1,
                10,
                2,
                false,
                "owner"));
            PendingCall restore = harness.Network.TakeNext<C2G_GetCurrentRoom>();

            harness.AccountModel.Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,
                OnlineOperationState.LoggingOut,
                OnlineAccountResultCode.None,
                "owner",
                1,
                10,
                2,
                false,
                "owner"));
            restore.TrySetResult(new G2C_GetCurrentRoom { Error = 0, HasRoom = true, Room = ProtocolRoom(920, "OLD234", 10, 1, Member(10, "old", true, 1, 1)) });
            yield return null;

            Assert.That(harness.System.Snapshot.HasRoom, Is.False);
            Assert.That(harness.System.Snapshot.RoomId, Is.EqualTo(0));
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineRoomResultCode.None));
        }

        [UnityTest]
        public IEnumerator UnexpectedExceptionWhileConnectedReturnsUnknownFailureAndEndsPendingOperation()
        {
            RoomHarness harness = CreateHarness();
            var logs = new List<string>();
            using IDisposable capture = Log.CaptureForTests((type, message) =>
            {
                if (type == LogType.Error)
                {
                    logs.Add(message);
                }
            });

            harness.Network.ImmediateException = new InvalidOperationException("boom");
            Task<OnlineRoomOperationResult> failed = harness.System.CreateRoomAsync();
            yield return WaitTask(failed);

            Assert.That(failed.Result.Code, Is.EqualTo(OnlineRoomResultCode.UnknownFailure));
            Assert.That(harness.System.Snapshot.OperationState, Is.EqualTo(OnlineRoomOperationState.Idle));
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineRoomResultCode.UnknownFailure));
            Assert.That(logs.Any(log => log.Contains("stage=CreateRoom") && log.Contains("InvalidOperationException") && log.Contains("boom")), Is.True);

            string failedOperationId = ((C2G_CreateRoom)harness.Network.LastImmediateRequest).OperationId;
            Task<OnlineRoomOperationResult> retry = harness.System.CreateRoomAsync();
            PendingCall retryCall = harness.Network.TakeNext<C2G_CreateRoom>();
            string retryOperationId = ((C2G_CreateRoom)retryCall.Request).OperationId;
            retryCall.TrySetResult(new G2C_CreateRoom { Error = 0, HasRoom = true, Room = ProtocolRoom(910, "GGG234", 10, 1, Member(10, "owner", true, 1, 1)) });
            yield return WaitTask(retry);

            Assert.That(retryOperationId, Is.Not.EqualTo(failedOperationId));
            Assert.That(retry.Result.Code, Is.EqualTo(OnlineRoomResultCode.Success));
        }

        [Test]
        public void GameRegistersSingleRoomModelAndSystem()
        {
            string source = Read("Client/Assets/Game/Scripts/Game.cs");
            Assert.That(source, Does.Contain("RegisterModel(new OnlineRoomModel())"));
            Assert.That(source, Does.Contain("RegisterSystem(new OnlineRoomSystem())"));
            Assert.That(Count(source, "new NetworkRuntime()"), Is.EqualTo(1));
        }

        [Test]
        public void RoomPrefabIsIndependentScrollableAndNotFixedToFourRows()
        {
            GameObject roomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Online/OnlineRoom.prefab");
            GameObject memberPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Online/Box/OnlineRoomMember.prefab");
            Assert.That(roomPrefab, Is.Not.Null);
            Assert.That(memberPrefab, Is.Not.Null);

            OnlineRoomUI roomUi = roomPrefab.GetComponent<OnlineRoomUI>();
            OnlineRoomMemberBoxUI memberUi = memberPrefab.GetComponent<OnlineRoomMemberBoxUI>();
            Assert.That(roomUi, Is.Not.Null);
            Assert.That(memberUi, Is.Not.Null);
            Assert.That(memberUi, Is.InstanceOf<BaseBoxUI>());
            Assert.That(roomUi.RootPanel.sizeDelta, Is.EqualTo(new Vector2(1080, 680)));
            Assert.That(roomUi.EmptyRoot, Is.Not.Null);
            Assert.That(roomUi.JoinedRoot, Is.Not.Null);
            Assert.That(roomUi.DisconnectHintRoot, Is.Not.Null);
            Assert.That(roomUi.CurrentRoomCodeText, Is.Not.Null);
            Assert.That(roomUi.PublicRoomListStateText, Is.Not.Null);
            Assert.That(roomUi.RefreshRoomListButton, Is.Not.Null);
            Assert.That(roomUi.PublicRoomList, Is.Not.Null);
            Assert.That(roomUi.LoadMoreButton, Is.Not.Null);
            Assert.That(roomUi.RoomCodeInput, Is.Not.Null);
            Assert.That(roomUi.CreateButton, Is.Not.Null);
            Assert.That(roomUi.JoinButton, Is.Not.Null);
            Assert.That(roomUi.LeaveButton, Is.Not.Null);
            Assert.That(roomUi.CloseButton, Is.Not.Null);
            Assert.That(roomUi.MemberList, Is.Not.Null);
            Assert.That(roomUi.MemberList.itemPrefab, Is.EqualTo(memberPrefab));
            Assert.That(memberUi.DisplayNameText, Is.Not.Null);
            Assert.That(memberUi.PlayerIdText, Is.Not.Null);
            Assert.That(memberUi.OwnerText, Is.Not.Null);
            Assert.That(memberUi.ConnectionText, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(roomPrefab);
            try
            {
                CustomListView list = instance.GetComponent<OnlineRoomUI>().MemberList;
                list.totalCount = 5;
                list.RefreshCells();
                Assert.That(list.GetContentTransform().Cast<Transform>().Count(child => child.gameObject.activeSelf), Is.EqualTo(5));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void LoginRoomButtonOpensRoomViewThroughControllerLifecycle()
        {
            EnsureUiRootForTest();
            InstallMinimalGameArchitectureForUiTest();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));

            OnlineLoginUICtrl loginCtrl;
            OnlineLoginUI loginUi;
            using (Log.CaptureForTests((_, _) => { }))
            {
                loginCtrl = UIHelper.ShowUI<OnlineLoginUICtrl>();
                Assert.That(loginCtrl.IsShow(), Is.True);
                loginUi = UnityEngine.Object.FindObjectOfType<OnlineLoginUI>();
                Assert.That(loginUi, Is.Not.Null);
                Assert.That(loginUi.RoomButton, Is.Not.Null);
                loginUi.RoomButton.onClick.Invoke();
            }

            OnlineRoomUICtrl roomCtrl = UIHelper.GetUICtrl<OnlineRoomUICtrl>();
            Assert.That(roomCtrl, Is.Not.Null);
            Assert.That(roomCtrl.IsShow(), Is.True);
            Assert.That(UnityEngine.Object.FindObjectOfType<OnlineRoomUI>(), Is.Not.Null);
        }

        [Test]
        public void ArcadeLoginEntryPreservesRequestedRoomModeUntilRoomNavigation()
        {
            EnsureUiRootForTest();
            InstallMinimalGameArchitectureForUiTest();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));

            using (Log.CaptureForTests((_, _) => { }))
            {
                OnlineLoginUICtrl.ShowForArcadeLobby();
            }

            OnlineLoginUICtrl loginCtrl = UIHelper.GetUICtrl<OnlineLoginUICtrl>();
            var modeField = typeof(OnlineLoginUICtrl).GetField("arcadeRoomMode",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(loginCtrl, Is.Not.Null);
            Assert.That(modeField, Is.Not.Null);
            Assert.That(modeField.GetValue(loginCtrl), Is.True,
                "街机入口经过登录界面后必须保留双人房间导航意图。");
        }

        private static IEnumerator AssertRetryReusesOperationId(
            System.Func<Task<OnlineRoomOperationResult>> operation,
            System.Func<string> takeOperationId)
        {
            Task<OnlineRoomOperationResult> first = operation();
            string firstId = takeOperationId();
            PendingCall firstCall = TestRoomArchitecture.Network.LastTakenCall;
            firstCall.TrySetException(new System.TimeoutException());
            yield return WaitTask(first);
            Assert.That(first.Result.Code, Is.EqualTo(OnlineRoomResultCode.Timeout));

            Task<OnlineRoomOperationResult> second = operation();
            string secondId = takeOperationId();
            PendingCall secondCall = TestRoomArchitecture.Network.LastTakenCall;
            secondCall.TrySetResult(ResponseFor(secondCall.Request));
            Assert.That(secondId, Is.EqualTo(firstId));
            yield return WaitTask(second);
            Assert.That(second.Result.Code, Is.EqualTo(OnlineRoomResultCode.Success));
        }

        private static IEnumerator WaitTask(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception;
            }
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = 120)
        {
            for (int frame = 0; frame < maxFrames && !condition(); frame++)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, "Condition did not become true within the bounded EditMode wait.");
        }

        private static OnlineAccountSnapshot AuthenticatedSnapshot(
            string userName,
            long accountId,
            long playerId,
            long sessionEpoch,
            OnlineAccountResultCode result)
        {
            return new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,
                OnlineOperationState.Idle,
                result,
                userName,
                accountId,
                playerId,
                sessionEpoch,
                false,
                userName);
        }

        private static string Read(string relativePath)
        {
            string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            string workspaceRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, ".."));
            string projectPath = System.IO.Path.Combine(projectRoot, relativePath);
            string workspacePath = System.IO.Path.Combine(workspaceRoot, relativePath);
            return System.IO.File.ReadAllText(System.IO.File.Exists(projectPath) ? projectPath : workspacePath);
        }

        private static int Count(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(value, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static RoomHarness CreateHarness()
        {
            ResetTestRoomArchitecture();
            TestRoomArchitecture.Network = new FakeRoomNetwork();
            IArchitecture architecture = TestRoomArchitecture.Interface;
            return new RoomHarness(
                architecture.GetSystem<OnlineRoomSystem>(),
                architecture.GetModel<OnlineAccountModel>(),
                TestRoomArchitecture.Network);
        }

        private static void ResetTestRoomArchitecture()
        {
            var field = typeof(Architecture<TestRoomArchitecture>).GetField("mArchitecture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(null) is IArchitecture architecture)
            {
                architecture.Deinit();
            }
            field?.SetValue(null, null);

            TestRoomArchitecture.Network = null;
        }

        private static void ResetGameArchitecture()
        {
            var field = typeof(Architecture<global::Game.Game>).GetField("mArchitecture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static RoomHarness InstallMinimalGameArchitectureForUiTest()
        {
            ResetGameArchitecture();
            var game = new global::Game.Game();
            var accountModel = new OnlineAccountModel();
            var roomModel = new OnlineRoomModel();
            var roomChatModel = new OnlineRoomChatModel();
            var arcadeRunModel = new OnlineArcadeRunModel();
            var localizationModel = new LocalizationModel();
            var network = new FakeRoomNetwork();
            var preferences = new FakePreferenceStore();
            var accountSystem = new OnlineAccountSystem();
            var roomSystem = new OnlineRoomSystem();
            var roomChatSystem = new OnlineRoomChatSystem();
            var arcadeRunSystem = new OnlineArcadeRunSystem();

            game.RegisterModel(localizationModel);
            game.RegisterModel(accountModel);
            game.RegisterModel(roomModel);
            game.RegisterModel(roomChatModel);
            game.RegisterModel(arcadeRunModel);
            game.RegisterUtility<INetworkRuntime>(network);
            game.RegisterUtility<IOnlineAccountPreferenceStore>(preferences);
            game.RegisterUtility(new LocalizationUtility(loadConfiguredTables: false));
            game.RegisterSystem(accountSystem);
            game.RegisterSystem(roomSystem);
            game.RegisterSystem(roomChatSystem);
            game.RegisterSystem(arcadeRunSystem);
            Init(localizationModel);
            Init(accountModel);
            Init(roomModel);
            Init(roomChatModel);
            Init(arcadeRunModel);
            Init(accountSystem);
            Init(roomSystem);
            Init(roomChatSystem);
            Init(arcadeRunSystem);

            var field = typeof(Architecture<global::Game.Game>).GetField("mArchitecture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(null, game);
            return new RoomHarness(roomSystem, accountModel, network);
        }

        private static void Init(ICanInit target)
        {
            target.Init();
            target.Initialized = true;
        }

        private static void EnsureUiRootForTest()
        {
            GameObject root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIRoot));
            new GameObject("HUD", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Dialog", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Popup", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Tips", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Top", typeof(RectTransform)).transform.SetParent(root.transform, false);
            if (GameObject.Find(SceneRoot.PoolRootName) == null)
            {
                new GameObject(SceneRoot.PoolRootName);
            }
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem.transform.SetParent(root.transform, false);
        }

        private static ET.OnlineRoomSnapshot ProtocolRoom(long roomId, string code, long ownerId, long revision, params RoomMemberInfo[] members)
        {
            ET.OnlineRoomSnapshot room = ET.OnlineRoomSnapshot.Create();
            room.RoomId = roomId;
            room.RoomCode = code;
            room.OwnerPlayerId = ownerId;
            room.MembershipRevision = revision;
            foreach (RoomMemberInfo member in members)
            {
                room.Members.Add(member);
            }

            return room;
        }

        private static RoomMemberInfo Member(long playerId, string name, bool owner, int state, long sequence)
        {
            RoomMemberInfo member = RoomMemberInfo.Create();
            member.PlayerId = playerId;
            member.DisplayName = name;
            member.IsOwner = owner;
            member.ConnectionState = state;
            member.JoinSequence = sequence;
            return member;
        }

        private static IResponse ResponseFor(IRequest request)
        {
            ET.OnlineRoomSnapshot room = ProtocolRoom(500, "EFG234", 10, 3, Member(10, "owner", true, 1, 1));
            if (request is C2G_CreateRoom)
            {
                return new G2C_CreateRoom { Error = 0, HasRoom = true, Room = room };
            }
            if (request is C2G_MatchArcadeRoom)
            {
                return new G2C_MatchArcadeRoom { Error = 0, HasRoom = true, Room = room };
            }
            if (request is C2G_JoinRoom)
            {
                return new G2C_JoinRoom { Error = 0, HasRoom = true, Room = room };
            }

            return new G2C_LeaveRoom { Error = 0 };
        }

        private sealed class TestRoomArchitecture : Architecture<TestRoomArchitecture>
        {
            public static FakeRoomNetwork Network;

            protected override void Init()
            {
                RegisterModel(new OnlineAccountModel());
                RegisterModel(new OnlineRoomModel());
                RegisterUtility<INetworkRuntime>(Network);
                OnlineRoomSystem roomSystem = new OnlineRoomSystem();
                RegisterSystem(roomSystem);
                typeof(OnlineRoomSystem).GetField("accountAllowsRoomOperations",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(roomSystem, true);
                typeof(OnlineRoomSystem).GetField("authenticatedAccountId",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(roomSystem, 1L);
                typeof(OnlineRoomSystem).GetField("authenticatedPlayerId",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(roomSystem, 10L);
                typeof(OnlineRoomSystem).GetField("authenticatedSessionEpoch",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(roomSystem, 1L);
            }
        }

        private sealed class RoomHarness
        {
            public RoomHarness(OnlineRoomSystem system, OnlineAccountModel accountModel, FakeRoomNetwork network)
            {
                System = system;
                AccountModel = accountModel;
                Network = network;
            }

            public OnlineRoomSystem System { get; }
            public OnlineAccountModel AccountModel { get; }
            public FakeRoomNetwork Network { get; }
        }

        private sealed class FakeRoomNetwork : INetworkRuntime
        {
            private readonly List<PendingCall> pendingCalls = new List<PendingCall>();
            private readonly List<System.Action<NetworkDisconnected>> disconnectedHandlers = new List<System.Action<NetworkDisconnected>>();
            private readonly List<System.Action<G2C_RoomChanged>> roomChangedHandlers = new List<System.Action<G2C_RoomChanged>>();

            public bool IsInitialized { get; private set; } = true;
            public NetworkConnectionState ConnectionState { get; private set; } = NetworkConnectionState.Connected;
            public int ConnectionGeneration => 1;
            public PendingCall LastTakenCall { get; private set; }
            public Exception ImmediateException { get; set; }
            public IRequest LastImmediateRequest { get; private set; }
            public int PendingCallCount => pendingCalls.Count;

            public void Initialize()
            {
                IsInitialized = true;
            }

            public void Update()
            {
            }

            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                ConnectionState = NetworkConnectionState.Connected;
                return Task.CompletedTask;
            }

            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
                where TResponse : class, IResponse
            {
                if (ImmediateException != null)
                {
                    Exception exception = ImmediateException;
                    ImmediateException = null;
                    LastImmediateRequest = request;
                    return Task.FromException<TResponse>(exception);
                }

                PendingCall call = new PendingCall(request);
                pendingCalls.Add(call);
                return call.Task.ContinueWith(task => (TResponse)task.GetAwaiter().GetResult(), CancellationToken.None);
            }

            public IDisposable RegisterPushHandler<TMessage>(System.Action<TMessage> handler)
                where TMessage : class, IMessage
            {
                if (typeof(TMessage) == typeof(G2C_RoomChanged))
                {
                    System.Action<G2C_RoomChanged> typed = message => handler((TMessage)(object)message);
                    roomChangedHandlers.Add(typed);
                    return new DelegateSubscription(() => roomChangedHandlers.Remove(typed));
                }

                return new DelegateSubscription(() => { });
            }

            public IDisposable RegisterDisconnectedHandler(System.Action<NetworkDisconnected> handler)
            {
                disconnectedHandlers.Add(handler);
                return new DelegateSubscription(() => disconnectedHandlers.Remove(handler));
            }

            public void Disconnect(NetworkDisconnectReason reason)
            {
                ConnectionState = NetworkConnectionState.Disconnected;
            }

            public void Dispose()
            {
                IsInitialized = false;
                ConnectionState = NetworkConnectionState.Disposed;
            }

            public void RaiseDisconnected(NetworkDisconnectReason reason)
            {
                ConnectionState = NetworkConnectionState.Disconnected;
                NetworkDisconnected disconnected = new NetworkDisconnected(1, reason, reason.ToString());
                foreach (System.Action<NetworkDisconnected> handler in disconnectedHandlers.ToArray())
                {
                    handler(disconnected);
                }
            }

            public void SetConnected()
            {
                ConnectionState = NetworkConnectionState.Connected;
            }

            public void RaiseRoomChanged(bool hasRoom, ET.OnlineRoomSnapshot room)
            {
                G2C_RoomChanged message = G2C_RoomChanged.Create();
                message.HasRoom = hasRoom;
                message.Room = room;
                foreach (System.Action<G2C_RoomChanged> handler in roomChangedHandlers.ToArray())
                {
                    handler(message);
                }
            }

            public PendingCall TakeNext<TRequest>()
                where TRequest : class, IRequest
            {
                int index = pendingCalls.FindIndex(call => call.Request is TRequest);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), $"No pending {typeof(TRequest).Name} call.");
                LastTakenCall = pendingCalls[index];
                pendingCalls.RemoveAt(index);
                return LastTakenCall;
            }

            public int CountPending<TRequest>()
                where TRequest : class, IRequest
            {
                return pendingCalls.Count(call => call.Request is TRequest);
            }
        }

        private sealed class FakePreferenceStore : IOnlineAccountPreferenceStore
        {
            public string LoadLastUserName()
            {
                return string.Empty;
            }

            public bool SaveLastUserName(string userName, out string error)
            {
                error = string.Empty;
                return true;
            }
        }

        private sealed class PendingCall
        {
            private readonly TaskCompletionSource<IResponse> completion = new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            public PendingCall(IRequest request)
            {
                Request = request;
            }

            public IRequest Request { get; }
            public Task<IResponse> Task => completion.Task;

            public void TrySetResult(IResponse response)
            {
                completion.TrySetResult(response);
            }

            public void TrySetException(System.Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private sealed class DelegateSubscription : System.IDisposable
        {
            private System.Action dispose;

            public DelegateSubscription(System.Action dispose)
            {
                this.dispose = dispose;
            }

            public void Dispose()
            {
                dispose?.Invoke();
                dispose = null;
            }
        }
    }
}
