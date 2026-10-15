using System;
using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    [FriendOf(typeof(OnlineTestComponent))]
    [FriendOf(typeof(OnlineRoomActor))]
    public static partial class OnlineTestComponentSystem
    {
        private static async ETTask<int> RunRoomList(this OnlineTestComponent self)
        {
            int failed = 0;
            failed += await self.Case("ROOM-LIST-UNAUTH AuthGateRejects", async () =>
            {
                using Session session = self.CreateSession();
                try
                {
                    using G2C_GetPublicRoomList response = await session.Call(C2G_GetPublicRoomList.Create()) as G2C_GetPublicRoomList;
                    Ensure(response.Error == ErrorCode.ERR_OnlineSessionNotLoggedIn,
                        $"unauthenticated list error={response.Error}");
                }
                catch (RpcException e)
                {
                    // GameGate 在记录 200105 后按现有未认证策略关闭 TCP；客户端可能只观察到底层 10054。
                    Ensure(e.Error == ErrorCode.ERR_OnlineSessionNotLoggedIn || e.Error == 10054,
                        $"unauthenticated list exception={e.Error}");
                }
            });

            failed += await self.Case("ROOM-LIST-PAGING AuthoritativeTwoPages", async () =>
            {
                var sessions = new List<Session>();
                var roomCodes = new List<string>();
                var playerIds = new List<long>();
                try
                {
                    for (int i = 0; i < 21; i++)
                    {
                        Session session = self.CreateSession();
                        sessions.Add(session);
                        using G2C_Login login = await self.RegisterAndLogin(session, UniqueUser($"rlist{i:D2}"));
                        using G2C_CreateRoom create = await session.Call(CreateRoom(Op(200 + i))) as G2C_CreateRoom;
                        Ensure(create.HasRoom && create.Room != null,
                            $"create room failed index={i} error={create.Error}");
                        roomCodes.Add(create.Room.RoomCode);
                        playerIds.Add(login.PlayerId);
                    }

                    using G2C_GetCurrentRoom before = await sessions[20].Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                    using G2C_GetPublicRoomList first = await sessions[20].Call(C2G_GetPublicRoomList.Create()) as G2C_GetPublicRoomList;
                    Ensure(first.Error == ErrorCode.ERR_Success && first.Rooms.Count == 20 && first.HasMore && first.NextCursorRoomId != 0,
                        $"first page invalid error={first.Error} count={first.Rooms.Count} more={first.HasMore} cursor={first.NextCursorRoomId}");
                    for (int i = 0; i < 20; i++)
                    {
                        Ensure(first.Rooms[i].RoomCode == roomCodes[20 - i],
                            $"descending order mismatch index={i} code={first.Rooms[i].RoomCode}");
                    }

                    C2G_GetPublicRoomList nextRequest = C2G_GetPublicRoomList.Create();
                    nextRequest.CursorRoomId = first.NextCursorRoomId;
                    using G2C_GetPublicRoomList second = await sessions[20].Call(nextRequest) as G2C_GetPublicRoomList;
                    Ensure(second.Error == ErrorCode.ERR_Success && second.Rooms.Count == 1 && !second.HasMore && second.NextCursorRoomId == 0,
                        $"second page invalid error={second.Error} count={second.Rooms.Count} more={second.HasMore} cursor={second.NextCursorRoomId}");
                    Ensure(second.Rooms[0].RoomCode == roomCodes[0] &&
                           first.Rooms.All(room => room.RoomCode != second.Rooms[0].RoomCode),
                        "pages overlap or oldest room missing");
                    Ensure(typeof(PublicRoomSummary).GetProperty("RoomId") == null, "public summary leaked RoomId");

                    var memberNames = new List<string>();
                    for (int i = 0; i < 3; i++)
                    {
                        string memberName = UniqueUser($"rlm{i}");
                        memberNames.Add(memberName);
                        Session memberSession = self.CreateSession();
                        sessions.Add(memberSession);
                        using G2C_Login _ = await self.RegisterAndLogin(memberSession, memberName);
                        using G2C_JoinRoom join = await memberSession.Call(JoinRoom(Op(230 + i), roomCodes[0])) as G2C_JoinRoom;
                        Ensure(join.Error == ErrorCode.ERR_Success, $"fill join failed index={i} error={join.Error}");
                    }

                    using G2C_GetPublicRoomList fullFirst = await sessions[20].Call(C2G_GetPublicRoomList.Create()) as G2C_GetPublicRoomList;
                    C2G_GetPublicRoomList fullNextRequest = C2G_GetPublicRoomList.Create();
                    fullNextRequest.CursorRoomId = fullFirst.NextCursorRoomId;
                    using G2C_GetPublicRoomList fullSecond = await sessions[20].Call(fullNextRequest) as G2C_GetPublicRoomList;
                    Ensure(fullSecond.Rooms.Single(room => room.RoomCode == roomCodes[0]).MemberCount == 4,
                        "full room was hidden or member count was not four");

                    sessions[21].Dispose();
                    await self.Root().GetComponent<TimerComponent>().WaitAsync(700);
                    using G2C_GetPublicRoomList graceFirst = await sessions[20].Call(C2G_GetPublicRoomList.Create()) as G2C_GetPublicRoomList;
                    C2G_GetPublicRoomList graceNextRequest = C2G_GetPublicRoomList.Create();
                    graceNextRequest.CursorRoomId = graceFirst.NextCursorRoomId;
                    using G2C_GetPublicRoomList graceSecond = await sessions[20].Call(graceNextRequest) as G2C_GetPublicRoomList;
                    Ensure(graceSecond.Rooms.Single(room => room.RoomCode == roomCodes[0]).MemberCount == 4,
                        "disconnected grace member did not occupy capacity");

                    using G2C_LeaveRoom leave = await sessions[0].Call(LeaveRoom(Op(240))) as G2C_LeaveRoom;
                    using G2C_GetPublicRoomList ownerFirst = await sessions[20].Call(C2G_GetPublicRoomList.Create()) as G2C_GetPublicRoomList;
                    C2G_GetPublicRoomList ownerNextRequest = C2G_GetPublicRoomList.Create();
                    ownerNextRequest.CursorRoomId = ownerFirst.NextCursorRoomId;
                    using G2C_GetPublicRoomList ownerSecond = await sessions[20].Call(ownerNextRequest) as G2C_GetPublicRoomList;
                    PublicRoomSummary transferred = ownerSecond.Rooms.Single(room => room.RoomCode == roomCodes[0]);
                    Ensure(leave.Error == ErrorCode.ERR_Success && transferred.OwnerDisplayName == memberNames[1],
                        $"owner transfer summary invalid leave={leave.Error} owner={transferred.OwnerDisplayName}");

                    using G2C_GetCurrentRoom after = await sessions[20].Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                    Ensure(before.Room.RoomId == after.Room.RoomId && before.Room.MembershipRevision == after.Room.MembershipRevision,
                        "list query changed requester room state");
                }
                finally
                {
                    foreach (Session session in sessions)
                    {
                        session?.Dispose();
                    }
                }
            });

            failed += await self.Case("ROOM-LIST-CLOSING IsolatedPredicate", async () =>
            {
                Scene scene = self.Parent as Scene;
                Ensure(scene != null && scene.GetComponent<OnlineRoomManagerComponent>() == null,
                    "OnlineTest scene already owns a room manager");
                OnlineRoomManagerComponent manager = scene.AddComponent<OnlineRoomManagerComponent>();
                long roomId = IdGenerater.Instance.GenerateId();
                OnlineRoomActor room = null;
                try
                {
                    // 该本地 Actor 不进入 Rooms 或目录，只证明生产纯谓词；跨 fiber TCP 证据由前一用例承担。
                    room = manager.AddChildWithId<OnlineRoomActor>(roomId);
                    room.Init(roomId, "ABC234", 991, "Owner", 1, OnlineRoomPurpose.General,
                        OnlineRoomVisibility.Public, 4, "test", string.Empty);
                    Ensure(OnlineRoomSnapshotHelper.TryBuildPublicRoomSummary(room, out PublicRoomSummary visible) &&
                           visible.MemberCount == 1, "visible isolated room did not produce summary");
                    room.Closing = true;
                    Ensure(!OnlineRoomSnapshotHelper.TryBuildPublicRoomSummary(room, out _),
                        "closing isolated room produced summary");
                }
                finally
                {
                    // Actor 由临时 Manager 持有：必须先从 Manager 移除 Actor，再移除 Manager，避免父级释放时重复释放子级。
                    if (room != null && !room.IsDisposed)
                    {
                        manager.RemoveChild(roomId);
                    }
                    scene.RemoveComponent<OnlineRoomManagerComponent>();
                }

                await ETTask.CompletedTask;
            });

            return failed;
        }

        private static async ETTask<int> RunRoom(this OnlineTestComponent self)
        {
            int failed = 0;
            failed += await self.Case("ROOM-01 RoomCreateReturnsSixCharacterCodeAndOwnerSnapshot", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login login = await self.RegisterAndLogin(owner, UniqueUser("rmc"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(1))) as G2C_CreateRoom;
                Ensure(create.Error == ErrorCode.ERR_Success, $"create error={create.Error}");
                Ensure(create.HasRoom && create.Room != null, "create did not return room");
                Ensure(create.Room.RoomCode.Length == 6 && OnlineRoomSnapshotHelper.TryNormalizeRoomCode(create.Room.RoomCode, out _), $"invalid code={create.Room.RoomCode}");
                Ensure(create.Room.OwnerPlayerId == login.PlayerId, "creator is not owner");
                Ensure(create.Room.Members.Count == 1 && create.Room.Members[0].PlayerId == login.PlayerId && create.Room.Members[0].IsOwner, "owner member invalid");
            });

            failed += await self.Case("ROOM-02 RoomCodeIsCaseInsensitiveAndRejectsInvalidCharacters", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("rmi"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(2))) as G2C_CreateRoom;
                Ensure(create.Error == ErrorCode.ERR_Success, $"create error={create.Error}");

                using Session joiner = self.CreateSession();
                using G2C_Login joinerLogin = await self.RegisterAndLogin(joiner, UniqueUser("rmj"));
                using G2C_JoinRoom join = await joiner.Call(JoinRoom(Op(3), create.Room.RoomCode.ToLowerInvariant())) as G2C_JoinRoom;
                Ensure(join.Error == ErrorCode.ERR_Success, $"lowercase join error={join.Error}");
                Ensure(join.Room.Members.Any(m => m.PlayerId == ownerLogin.PlayerId) && join.Room.Members.Any(m => m.PlayerId == joinerLogin.PlayerId),
                    "joined snapshot missing members");

                using Session bad = self.CreateSession();
                using G2C_Login badLogin = await self.RegisterAndLogin(bad, UniqueUser("rmb"));
                using G2C_JoinRoom invalid = await bad.Call(JoinRoom(Op(4), "ABC10Z")) as G2C_JoinRoom;
                Ensure(invalid.Error == ErrorCode.ERR_OnlineRoomCodeInvalid, $"invalid code error={invalid.Error} player={badLogin.PlayerId}");
            });

            failed += await self.Case("ROOM-03 SecondPlayerJoinsAndBothReceiveRevisionedSnapshot", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("rmo"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(5))) as G2C_CreateRoom;
                int before = self.RoomChangedSessionIds.Count;

                using Session joiner = self.CreateSession();
                using G2C_Login joinerLogin = await self.RegisterAndLogin(joiner, UniqueUser("rmn"));
                using G2C_JoinRoom join = await joiner.Call(JoinRoom(Op(6), create.Room.RoomCode)) as G2C_JoinRoom;
                Ensure(join.Error == ErrorCode.ERR_Success, $"join error={join.Error}");
                Ensure(join.Room.MembershipRevision == create.Room.MembershipRevision + 1, "revision did not advance");
                await self.WaitForRoomPush(owner.Id, before, 2000);
                OnlineRoomSnapshot pushed = self.RoomChangedRooms.Last();
                Ensure(pushed.MembershipRevision == join.Room.MembershipRevision, "owner push revision mismatch");
                Ensure(pushed.Members.Any(m => m.PlayerId == ownerLogin.PlayerId) && pushed.Members.Any(m => m.PlayerId == joinerLogin.PlayerId),
                    "owner push missing joined members");
            });

            failed += await self.Case("ROOM-04 RoomCapacityRejectsFifthPlayer", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("rmq"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(7))) as G2C_CreateRoom;
                Session[] members = { self.CreateSession(), self.CreateSession(), self.CreateSession(), self.CreateSession() };
                try
                {
                    for (int i = 0; i < 3; i++)
                    {
                        using G2C_Login _ = await self.RegisterAndLogin(members[i], UniqueUser("rmq"));
                        using G2C_JoinRoom join = await members[i].Call(JoinRoom(Op(8 + i), create.Room.RoomCode)) as G2C_JoinRoom;
                        Ensure(join.Error == ErrorCode.ERR_Success, $"join {i} error={join.Error}");
                    }

                    using G2C_Login fifthLogin = await self.RegisterAndLogin(members[3], UniqueUser("rmq"));
                    using G2C_JoinRoom fifth = await members[3].Call(JoinRoom(Op(11), create.Room.RoomCode)) as G2C_JoinRoom;
                    Ensure(fifth.Error == ErrorCode.ERR_OnlineRoomFull, $"fifth error={fifth.Error} owner={ownerLogin.PlayerId} fifth={fifthLogin.PlayerId}");
                }
                finally
                {
                    foreach (Session member in members)
                    {
                        member.Dispose();
                    }
                }
            });

            failed += await self.Case("ROOM-05 PlayerCannotCreateOrJoinSecondRoom", async () =>
            {
                using Session first = self.CreateSession();
                using G2C_Login login = await self.RegisterAndLogin(first, UniqueUser("rms"));
                using G2C_CreateRoom create = await first.Call(CreateRoom(Op(12))) as G2C_CreateRoom;
                using G2C_CreateRoom createAgain = await first.Call(CreateRoom(Op(13))) as G2C_CreateRoom;
                using G2C_JoinRoom joinAgain = await first.Call(JoinRoom(Op(14), create.Room.RoomCode)) as G2C_JoinRoom;
                Ensure(create.Error == ErrorCode.ERR_Success, $"create error={create.Error} player={login.PlayerId}");
                Ensure(createAgain.Error == ErrorCode.ERR_OnlineAlreadyInRoom, $"create again error={createAgain.Error}");
                Ensure(joinAgain.Error == ErrorCode.ERR_OnlineAlreadyInRoom, $"join again error={joinAgain.Error}");
            });

            failed += await self.Case("ROOM-06 DuplicateOperationIdDoesNotRepeatCreateJoinOrLeave", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("rmd"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(15))) as G2C_CreateRoom;
                using G2C_CreateRoom createDuplicate = await owner.Call(CreateRoom(Op(15))) as G2C_CreateRoom;
                Ensure(create.Error == ErrorCode.ERR_Success && createDuplicate.Error == ErrorCode.ERR_Success, "duplicate create did not return cached success");
                Ensure(create.Room.RoomId == createDuplicate.Room.RoomId, "duplicate create returned different room");

                using Session joiner = self.CreateSession();
                using G2C_Login joinerLogin = await self.RegisterAndLogin(joiner, UniqueUser("rmd"));
                using G2C_JoinRoom join = await joiner.Call(JoinRoom(Op(16), create.Room.RoomCode)) as G2C_JoinRoom;
                using G2C_JoinRoom joinDuplicate = await joiner.Call(JoinRoom(Op(16), create.Room.RoomCode)) as G2C_JoinRoom;
                Ensure(join.Error == ErrorCode.ERR_Success && joinDuplicate.Error == ErrorCode.ERR_Success, "duplicate join did not return cached success");
                Ensure(join.Room.Members.Count == 2 && joinDuplicate.Room.Members.Count == 2, $"duplicate join repeated membership owner={ownerLogin.PlayerId} joiner={joinerLogin.PlayerId}");

                using G2C_LeaveRoom leave = await joiner.Call(LeaveRoom(Op(17))) as G2C_LeaveRoom;
                using G2C_LeaveRoom leaveDuplicate = await joiner.Call(LeaveRoom(Op(17))) as G2C_LeaveRoom;
                Ensure(leave.Error == ErrorCode.ERR_Success && leaveDuplicate.Error == ErrorCode.ERR_Success, $"duplicate leave errors={leave.Error}/{leaveDuplicate.Error}");
            });

            failed += await self.Case("ROOM-07 JoinRateLimitIsServerAuthoritative", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Login login = await self.RegisterAndLogin(session, UniqueUser("rmr"));
                int rateLimited = 0;
                for (int i = 0; i < 8; i++)
                {
                    using G2C_JoinRoom join = await session.Call(JoinRoom(Op(18 + i), "ZZZZZZ")) as G2C_JoinRoom;
                    if (join.Error == ErrorCode.ERR_OnlineRoomRateLimited)
                    {
                        rateLimited++;
                    }
                }
                Ensure(rateLimited > 0, $"rate limit did not trigger player={login.PlayerId}");
            });

            failed += await self.Case("ROOM-08 NonOwnerLeaveKeepsOwnerAndCompactsNoJoinSequence", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("rml"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(30))) as G2C_CreateRoom;
                using Session second = self.CreateSession();
                using G2C_Login secondLogin = await self.RegisterAndLogin(second, UniqueUser("rml"));
                using G2C_JoinRoom secondJoin = await second.Call(JoinRoom(Op(31), create.Room.RoomCode)) as G2C_JoinRoom;
                using Session third = self.CreateSession();
                using G2C_Login thirdLogin = await self.RegisterAndLogin(third, UniqueUser("rml"));
                using G2C_JoinRoom thirdJoin = await third.Call(JoinRoom(Op(32), create.Room.RoomCode)) as G2C_JoinRoom;
                using G2C_LeaveRoom leave = await second.Call(LeaveRoom(Op(33))) as G2C_LeaveRoom;
                using G2C_GetCurrentRoom current = await owner.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(leave.Error == ErrorCode.ERR_Success && current.Error == ErrorCode.ERR_Success, $"leave/current errors={leave.Error}/{current.Error}");
                Ensure(current.Room.OwnerPlayerId == ownerLogin.PlayerId, "owner changed after non-owner leave");
                Ensure(!current.Room.Members.Any(m => m.PlayerId == secondLogin.PlayerId), "left member remains");
                Ensure(current.Room.Members.First(m => m.PlayerId == thirdLogin.PlayerId).JoinSequence == thirdJoin.Room.Members.First(m => m.PlayerId == thirdLogin.PlayerId).JoinSequence,
                    "join sequence compacted");
            });

            failed += await self.Case("ROOM-09 OwnerLeaveTransfersToEarliestOnlineMember", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("rmt"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(34))) as G2C_CreateRoom;
                using Session second = self.CreateSession();
                using G2C_Login secondLogin = await self.RegisterAndLogin(second, UniqueUser("rmt"));
                using G2C_JoinRoom secondJoin = await second.Call(JoinRoom(Op(35), create.Room.RoomCode)) as G2C_JoinRoom;
                using Session third = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(third, UniqueUser("rmt"));
                using G2C_JoinRoom thirdJoin = await third.Call(JoinRoom(Op(36), create.Room.RoomCode)) as G2C_JoinRoom;
                using G2C_LeaveRoom leave = await owner.Call(LeaveRoom(Op(37))) as G2C_LeaveRoom;
                using G2C_GetCurrentRoom current = await second.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(leave.Error == ErrorCode.ERR_Success && current.Room.OwnerPlayerId == secondLogin.PlayerId,
                    $"owner transfer failed old={ownerLogin.PlayerId} secondJoin={secondJoin.Error} thirdJoin={thirdJoin.Error}");
            });

            failed += await self.Case("ROOM-10 OwnerGraceKeepsOwnershipAndShowsDisconnected", async () =>
            {
                using Session owner = self.CreateSession();
                string user = UniqueUser("rmg");
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, user);
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(38))) as G2C_CreateRoom;
                using Session second = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(second, UniqueUser("rmg"));
                using G2C_JoinRoom join = await second.Call(JoinRoom(Op(39), create.Room.RoomCode)) as G2C_JoinRoom;
                owner.Dispose();
                await self.Root().GetComponent<TimerComponent>().WaitAsync(1000);
                using G2C_GetCurrentRoom current = await second.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                RoomMemberInfo ownerMember = current.Room.Members.First(m => m.PlayerId == ownerLogin.PlayerId);
                Ensure(current.Room.OwnerPlayerId == ownerLogin.PlayerId && ownerMember.IsOwner && ownerMember.ConnectionState == (int)OnlineRoomMemberConnectionState.DisconnectedGrace,
                    $"owner grace invalid join={join.Error}");
            });

            failed += await self.Case("ROOM-11 ReconnectWithinGraceRestoresSameRoomAndOnlineState", async () =>
            {
                string user = UniqueUser("rmw");
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, user);
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(40))) as G2C_CreateRoom;
                using Session second = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(second, UniqueUser("rmw"));
                using G2C_JoinRoom join = await second.Call(JoinRoom(Op(41), create.Room.RoomCode)) as G2C_JoinRoom;
                owner.Dispose();
                await self.Root().GetComponent<TimerComponent>().WaitAsync(800);
                int before = self.RoomChangedSessionIds.Count;
                using Session reconnect = self.CreateSession();
                using G2C_Login reconnectLogin = await reconnect.Call(Login(user)) as G2C_Login;
                Ensure(reconnectLogin.Error == ErrorCode.ERR_Success && reconnectLogin.IsPlayerActorReused, $"reconnect error={reconnectLogin.Error} reused={reconnectLogin.IsPlayerActorReused}");
                await self.WaitForRoomPush(second.Id, before, 3000);
                using G2C_GetCurrentRoom current = await reconnect.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(current.HasRoom && current.Room.RoomId == create.Room.RoomId, "reconnect did not restore room");
                Ensure(current.Room.Members.First(m => m.PlayerId == ownerLogin.PlayerId).ConnectionState == (int)OnlineRoomMemberConnectionState.Online,
                    $"reconnect did not set online join={join.Error}");
            });

            failed += await self.Case("ROOM-12 ActiveLogoutImmediatelyRemovesMemberAndTransfersOwner", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("rma"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(42))) as G2C_CreateRoom;
                using Session second = self.CreateSession();
                using G2C_Login secondLogin = await self.RegisterAndLogin(second, UniqueUser("rma"));
                using G2C_JoinRoom join = await second.Call(JoinRoom(Op(43), create.Room.RoomCode)) as G2C_JoinRoom;
                using G2C_Logout logout = await owner.Call(C2G_Logout.Create()) as G2C_Logout;
                await self.Root().GetComponent<TimerComponent>().WaitAsync(300);
                using G2C_GetCurrentRoom current = await second.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(logout.Error == ErrorCode.ERR_Success && current.Room.OwnerPlayerId == secondLogin.PlayerId && current.Room.Members.Count == 1,
                    $"active logout invalid old={ownerLogin.PlayerId} join={join.Error}");
            });

            failed += await self.Case("ROOM-13 OldEpochCannotMarkReconnectedMemberDisconnectedOrRemoveIt", async () =>
            {
                string user = UniqueUser("rme");
                using Session first = self.CreateSession();
                using G2C_Login firstLogin = await self.RegisterAndLogin(first, user);
                using G2C_CreateRoom create = await first.Call(CreateRoom(Op(44))) as G2C_CreateRoom;
                using Session secondMember = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(secondMember, UniqueUser("rme"));
                using G2C_JoinRoom join = await secondMember.Call(JoinRoom(Op(45), create.Room.RoomCode)) as G2C_JoinRoom;
                using Session replacement = self.CreateSession();
                using G2C_Login replacementLogin = await replacement.Call(Login(user)) as G2C_Login;
                await self.Root().GetComponent<TimerComponent>().WaitAsync(1500);
                using G2C_GetCurrentRoom current = await replacement.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(replacementLogin.Error == ErrorCode.ERR_Success && replacementLogin.SessionEpoch > firstLogin.SessionEpoch, "replacement login invalid");
                Ensure(current.HasRoom && current.Room.Members.First(m => m.PlayerId == firstLogin.PlayerId).ConnectionState == (int)OnlineRoomMemberConnectionState.Online,
                    $"old epoch changed current room join={join.Error}");
            });

            failed += await self.Case("ROOM-15 LastMemberLeavingClosesRoomAndCreatesTombstone", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("rmz"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(48))) as G2C_CreateRoom;
                string code = create.Room.RoomCode;
                using G2C_LeaveRoom leave = await owner.Call(LeaveRoom(Op(49))) as G2C_LeaveRoom;
                using G2C_GetCurrentRoom current = await owner.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                using Session joiner = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(joiner, UniqueUser("rmz"));
                using G2C_JoinRoom joinClosed = await joiner.Call(JoinRoom(Op(50), code)) as G2C_JoinRoom;
                Ensure(leave.Error == ErrorCode.ERR_Success && !current.HasRoom && joinClosed.Error == ErrorCode.ERR_OnlineRoomNotFound,
                    $"last leave close invalid player={ownerLogin.PlayerId} errors={leave.Error}/{current.Error}/{joinClosed.Error}");
            });

            failed += await self.Case("ROOM-19 ArcadeQuickMatchPairsCompatiblePlayers", async () =>
            {
                string version = $"room-match-{Guid.NewGuid():N}";
                string fingerprint = ServerConfig.Instance.ArcadeRule.ConfigFingerprint;
                using Session first = self.CreateSession();
                using G2C_Login firstLogin = await self.RegisterAndLogin(first, UniqueUser("rmm"));
                using G2C_MatchArcadeRoom firstMatch = await first.Call(
                    MatchArcadeRoom(Op(95), version, fingerprint)) as G2C_MatchArcadeRoom;
                Ensure(firstMatch.Error == ErrorCode.ERR_Success && firstMatch.HasRoom &&
                       firstMatch.Room != null && firstMatch.Room.Members.Count == 1,
                    $"first match failed error={firstMatch.Error}");
                Ensure(firstMatch.Room.Purpose == (int)OnlineRoomPurpose.Arcade &&
                       firstMatch.Room.Visibility == (int)OnlineRoomVisibility.Private &&
                       firstMatch.Room.Capacity == 2,
                    "match-created room contract invalid");

                using Session second = self.CreateSession();
                using G2C_Login secondLogin = await self.RegisterAndLogin(second, UniqueUser("rmm"));
                using G2C_MatchArcadeRoom secondMatch = await second.Call(
                    MatchArcadeRoom(Op(96), version, fingerprint)) as G2C_MatchArcadeRoom;
                Ensure(secondMatch.Error == ErrorCode.ERR_Success && secondMatch.HasRoom &&
                       secondMatch.Room != null && secondMatch.Room.RoomId == firstMatch.Room.RoomId,
                    $"second match did not join waiting room error={secondMatch.Error}");
                Ensure(secondMatch.Room.Members.Count == 2 &&
                       secondMatch.Room.Members.Any(x => x.PlayerId == firstLogin.PlayerId) &&
                       secondMatch.Room.Members.Any(x => x.PlayerId == secondLogin.PlayerId),
                    "matched room is missing either player");

                using G2C_MatchArcadeRoom duplicate = await second.Call(
                    MatchArcadeRoom(Op(96), version, fingerprint)) as G2C_MatchArcadeRoom;
                Ensure(duplicate.Error == ErrorCode.ERR_Success &&
                       duplicate.Room.RoomId == secondMatch.Room.RoomId && duplicate.Room.Members.Count == 2,
                    "duplicate match operation was not idempotent");

                using Session third = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(third, UniqueUser("rmm"));
                using G2C_MatchArcadeRoom thirdMatch = await third.Call(
                    MatchArcadeRoom(Op(97), version, fingerprint)) as G2C_MatchArcadeRoom;
                Ensure(thirdMatch.Error == ErrorCode.ERR_Success && thirdMatch.HasRoom &&
                       thirdMatch.Room.RoomId != firstMatch.Room.RoomId && thirdMatch.Room.Members.Count == 1,
                    "claimed room remained available to a third player");
            });

            return failed;
        }

        private static async ETTask<int> RunRoomRestartCheck(this OnlineTestComponent self)
        {
            return await self.Case("ROOM-18 ServerRestartDoesNotRestoreRoom", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Login login = await self.RegisterAndLogin(session, Options.Instance.OnlineTestUser);
                using G2C_GetCurrentRoom current = await session.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(login.PlayerId == Options.Instance.OnlineTestExpectedPlayerId, $"player changed {login.PlayerId}");
                Ensure(current.Error == ErrorCode.ERR_Success && !current.HasRoom, $"room restored after restart hasRoom={current.HasRoom}");
            });
        }

        private static async ETTask<int> RunRoomRestartPrepare(this OnlineTestComponent self)
        {
            return await self.Case("ROOM-18-PREPARE CreateRoomBeforeRestart", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Login login = await self.RegisterAndLogin(session, Options.Instance.OnlineTestUser);
                using G2C_CreateRoom create = await session.Call(CreateRoom(Op(80))) as G2C_CreateRoom;
                Ensure(create.Error == ErrorCode.ERR_Success && create.HasRoom, $"prepare create error={create.Error}");
                Console.WriteLine($"ROOM_RESTART_ACCOUNT_ID={login.AccountId}");
                Console.WriteLine($"ROOM_RESTART_PLAYER_ID={login.PlayerId}");
                Console.WriteLine($"ROOM_RESTART_ROOM_ID={create.Room.RoomId}");
                Console.WriteLine($"ROOM_RESTART_ROOM_CODE={create.Room.RoomCode}");
            });
        }

        private static async ETTask<int> RunRoomFaultClose(this OnlineTestComponent self)
        {
            return await self.Case("ROOM-16 ClosingRoomRejectsConcurrentJoin", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("rmf"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(60))) as G2C_CreateRoom;
                using G2C_LeaveRoom leave = await owner.Call(LeaveRoom(Op(61))) as G2C_LeaveRoom;
                using Session joiner = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(joiner, UniqueUser("rmf"));
                using G2C_JoinRoom join = await joiner.Call(JoinRoom(Op(62), create.Room.RoomCode)) as G2C_JoinRoom;
                Ensure(leave.Error == ErrorCode.ERR_Success && join.Error == ErrorCode.ERR_OnlineRoomClosing,
                    $"closing join invalid owner={ownerLogin.PlayerId} errors={leave.Error}/{join.Error}");
                await self.Root().GetComponent<TimerComponent>().WaitAsync(5500);
                using G2C_JoinRoom afterRetry = await joiner.Call(JoinRoom(Op(63), create.Room.RoomCode)) as G2C_JoinRoom;
                Ensure(afterRetry.Error == ErrorCode.ERR_OnlineRoomNotFound, $"close retry did not tombstone error={afterRetry.Error}");
            });
        }

        private static async ETTask<int> RunRoomFaultLeave(this OnlineTestComponent self)
        {
            return await self.Case("ROOM-17 RoomLeaveFailureKeepsPlayerOffliningAndRetriesWithoutGhostMember", async () =>
            {
                string user = UniqueUser("rmv");
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, user);
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(70))) as G2C_CreateRoom;
                using G2C_Logout logout = await owner.Call(C2G_Logout.Create()) as G2C_Logout;
                Ensure(logout.Error == ErrorCode.ERR_Success, $"logout error={logout.Error}");
                await self.Root().GetComponent<TimerComponent>().WaitAsync(6500);
                using Session reconnect = self.CreateSession();
                using G2C_Login login = await reconnect.Call(Login(user)) as G2C_Login;
                using G2C_GetCurrentRoom current = await reconnect.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(login.Error == ErrorCode.ERR_Success && !current.HasRoom,
                    $"room ghost remains player={ownerLogin.PlayerId} room={create.Room.RoomId} login={login.Error} hasRoom={current.HasRoom}");
            });
        }

        private static async ETTask<int> RunRoomRetry(this OnlineTestComponent self)
        {
            int failed = 0;
            failed += await self.Case("ROOM-P03 GetCurrentRoomTemporaryFailureKeepsRoomAndRecovers", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login login = await self.RegisterAndLogin(owner, UniqueUser("rmcur"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(94))) as G2C_CreateRoom;
                using G2C_GetCurrentRoom first = await owner.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                using G2C_GetCurrentRoom second = await owner.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(create.Error == ErrorCode.ERR_Success && create.HasRoom, $"create failed error={create.Error}");
                Ensure(first.Error == ErrorCode.ERR_OnlineRoomServiceUnavailable && !first.HasRoom,
                    $"first get-current should be service unavailable player={login.PlayerId} room={create.Room.RoomId} error={first.Error} hasRoom={first.HasRoom}");
                Ensure(second.Error == ErrorCode.ERR_Success && second.HasRoom && second.Room.RoomId == create.Room.RoomId,
                    $"second get-current did not recover player={login.PlayerId} room={create.Room.RoomId} error={second.Error} hasRoom={second.HasRoom}");
            });

            failed += await self.Case("ROOM-L03 RoomConnectionSyncFailureRetriesWithoutBlockingLogin", async () =>
            {
                string user = UniqueUser("rmsync");
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, user);
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(90))) as G2C_CreateRoom;
                using Session observer = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(observer, UniqueUser("rmsync"));
                using G2C_JoinRoom join = await observer.Call(JoinRoom(Op(91), create.Room.RoomCode)) as G2C_JoinRoom;
                owner.Dispose();
                await self.Root().GetComponent<TimerComponent>().WaitAsync(1000);
                using G2C_Ping ping = await observer.Call(C2G_Ping.Create()) as G2C_Ping;
                Ensure(ping.Error == ErrorCode.ERR_Success, $"observer session dropped during sync retry ping={ping.Error}");
                await self.Root().GetComponent<TimerComponent>().WaitAsync(5500);
                using G2C_GetCurrentRoom current = await observer.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                RoomMemberInfo ownerMember = current.Room.Members.First(m => m.PlayerId == ownerLogin.PlayerId);
                Ensure(join.Error == ErrorCode.ERR_Success && ownerMember.ConnectionState == (int)OnlineRoomMemberConnectionState.DisconnectedGrace,
                    $"room connection sync retry failed join={join.Error} state={ownerMember.ConnectionState}");
            });

            failed += await self.RunRoomFaultLeave();
            return failed;
        }

        private static async ETTask<int> RunRoomTimeout(this OnlineTestComponent self)
        {
            return await self.Case("ROOM-14 GraceTimeoutRemovesMemberAndTransfersOwner", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("rmtime"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(92))) as G2C_CreateRoom;
                using Session observer = self.CreateSession();
                using G2C_Login observerLogin = await self.RegisterAndLogin(observer, UniqueUser("rmtime"));
                using G2C_JoinRoom join = await observer.Call(JoinRoom(Op(93), create.Room.RoomCode)) as G2C_JoinRoom;
                owner.Dispose();
                for (int i = 0; i < 6; i++)
                {
                    await self.Root().GetComponent<TimerComponent>().WaitAsync(10000);
                    using G2C_Ping keepAlive = await observer.Call(C2G_Ping.Create()) as G2C_Ping;
                    Ensure(keepAlive.Error == ErrorCode.ERR_Success, $"observer keepalive failed index={i} ping={keepAlive.Error}");
                }

                await self.Root().GetComponent<TimerComponent>().WaitAsync(1000);
                using G2C_Ping ping = await observer.Call(C2G_Ping.Create()) as G2C_Ping;
                using G2C_GetCurrentRoom current = await observer.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(ping.Error == ErrorCode.ERR_Success, $"observer session not online ping={ping.Error}");
                Ensure(current != null, "current room response is null");
                Ensure(current.Error == ErrorCode.ERR_Success && current.HasRoom && current.Room != null && current.Room.Members != null &&
                       current.Room.OwnerPlayerId == observerLogin.PlayerId &&
                       current.Room.Members.All(m => m.PlayerId != ownerLogin.PlayerId),
                    $"room timeout failed join={join.Error} currentError={current.Error} hasRoom={current.HasRoom} roomNull={current.Room == null} membersNull={current.Room?.Members == null}");
            });
        }

        private static async ETTask<G2C_Login> RegisterAndLogin(this OnlineTestComponent self, Session session, string user)
        {
            using G2C_Register register = await session.Call(Register(user)) as G2C_Register;
            Ensure(register.Error == ErrorCode.ERR_Success || register.Error == ErrorCode.ERR_OnlineUserNameExists, $"register {user} error={register.Error}");
            G2C_Login login = await session.Call(Login(user)) as G2C_Login;
            Ensure(login.Error == ErrorCode.ERR_Success, $"login {user} error={login.Error}");
            return login;
        }

        private static async ETTask WaitForRoomPush(this OnlineTestComponent self, long sessionId, int beforeCount, int timeoutMs)
        {
            long end = TimeInfo.Instance.ClientNow() + timeoutMs;
            while (TimeInfo.Instance.ClientNow() < end)
            {
                for (int i = beforeCount; i < self.RoomChangedSessionIds.Count; i++)
                {
                    if (self.RoomChangedSessionIds[i] == sessionId)
                    {
                        return;
                    }
                }

                await self.Root().GetComponent<TimerComponent>().WaitAsync(50);
            }

            throw new Exception($"room changed push not received by session={sessionId}");
        }

        private static C2G_CreateRoom CreateRoom(string operationId)
        {
            C2G_CreateRoom request = C2G_CreateRoom.Create();
            request.OperationId = operationId;
            return request;
        }

        private static C2G_JoinRoom JoinRoom(string operationId, string roomCode)
        {
            C2G_JoinRoom request = C2G_JoinRoom.Create();
            request.OperationId = operationId;
            request.RoomCode = roomCode;
            return request;
        }

        private static C2G_MatchArcadeRoom MatchArcadeRoom(string operationId, string clientVersion,
            string arcadeConfigFingerprint)
        {
            C2G_MatchArcadeRoom request = C2G_MatchArcadeRoom.Create();
            request.OperationId = operationId;
            request.ClientVersion = clientVersion;
            request.ArcadeConfigFingerprint = arcadeConfigFingerprint;
            return request;
        }

        private static C2G_LeaveRoom LeaveRoom(string operationId)
        {
            C2G_LeaveRoom request = C2G_LeaveRoom.Create();
            request.OperationId = operationId;
            return request;
        }

        private static string Op(int value)
        {
            return $"{value:x32}";
        }
    }
}
