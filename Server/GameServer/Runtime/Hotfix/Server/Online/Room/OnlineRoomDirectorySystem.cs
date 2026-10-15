using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace ET.Server
{
    [EntitySystemOf(typeof(OnlineRoomDirectoryComponent))]
    [FriendOf(typeof(OnlineRoomDirectoryComponent))]
    public static partial class OnlineRoomDirectoryComponentSystem
    {
        private const long TombstoneMilliseconds = 10 * 60 * 1000;
        private const long RateWindowMilliseconds = 10 * 1000;
        private const int PlayerRateLimit = 6;
        private const int SourceRateLimit = 30;
        private const string CodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        [EntitySystem]
        private static void Awake(this OnlineRoomDirectoryComponent self)
        {
        }

        public static async ETTask<(int Error, string Message, long RoomId, OnlineRoomSnapshot Room)> CreateRoom(
            this OnlineRoomDirectoryComponent self,
            long playerId,
            string displayName,
            long sessionEpoch,
            int purpose,
            int visibility,
            string clientVersion,
            string arcadeConfigFingerprint)
        {
            if (!OnlineRoomSnapshotHelper.TryValidateCreation(purpose, visibility, clientVersion,
                    arcadeConfigFingerprint, out _, out _, out _, out string validationError))
            {
                return (ErrorCode.ERR_OnlineRoomOperationInvalid, validationError, 0, null);
            }
            self.CleanupExpired();
            string roomCode = self.ReserveRoomCode();
            long roomId = IdGenerater.Instance.GenerateId();
            try
            {
                ActorId roomScene = ServerConfig.Instance.GetSceneByName(self.Zone(), "OnlineRoom").ActorId;
                D2R_CreateRoom request = D2R_CreateRoom.Create();
                request.RoomId = roomId;
                request.RoomCode = roomCode;
                request.PlayerId = playerId;
                request.DisplayName = displayName;
                request.SessionEpoch = sessionEpoch;
                request.Purpose = purpose;
                request.Visibility = visibility;
                request.ClientVersion = clientVersion ?? string.Empty;
                request.ArcadeConfigFingerprint = arcadeConfigFingerprint ?? string.Empty;
                using R2D_CreateRoom response = await self.Root().GetComponent<MessageSender>().Call(roomScene, request, false) as R2D_CreateRoom;
                bool committed = response.Error == ErrorCode.ERR_Success || response.Error == ErrorCode.ERR_OnlineChatMembershipPending;
                if (!committed)
                {
                    self.ReservedCodes.Remove(roomCode);
                    return (response.Error, response.Message, 0, null);
                }

                self.ReservedCodes.Remove(roomCode);
                self.ActiveRooms[roomCode] = roomId;
                return (response.Error, response.Message, roomId, OnlineRoomSnapshotHelper.CloneSnapshot(response.Room));
            }
            catch (Exception e)
            {
                self.ReservedCodes.Remove(roomCode);
                Log.Error($"online room create failed stage=CreateRoom playerId={playerId} roomId={roomId} exception={e}");
                return (ErrorCode.ERR_OnlineRoomServiceUnavailable, "房间服务暂不可用", 0, null);
            }
        }

        /// <summary>
        /// 原子领取兼容的街机等待房；没有候选时创建私有双人房并登记为等待房。
        /// 领取即移出池，后续加入失败由调用方重新匹配，避免第三名玩家竞争同一容量。
        /// </summary>
        public static async ETTask<(int Error, string Message, long RoomId, bool Created, OnlineRoomSnapshot Room)>
            MatchArcadeRoom(this OnlineRoomDirectoryComponent self, long playerId, string displayName,
                long sessionEpoch, string clientVersion, string arcadeConfigFingerprint)
        {
            if (!OnlineRoomSnapshotHelper.TryValidateCreation((int)OnlineRoomPurpose.Arcade,
                    (int)OnlineRoomVisibility.Private, clientVersion, arcadeConfigFingerprint,
                    out _, out _, out _, out string validationError))
            {
                return (ErrorCode.ERR_OnlineRoomOperationInvalid, validationError, 0, false, null);
            }

            string matchKey = BuildArcadeMatchKey(clientVersion, arcadeConfigFingerprint);
            if (self.ArcadeMatchRooms.Remove(matchKey, out long waitingRoomId))
            {
                self.ArcadeMatchKeysByRoomId.Remove(waitingRoomId);
                return (ErrorCode.ERR_Success, string.Empty, waitingRoomId, false, null);
            }

            var created = await self.CreateRoom(playerId, displayName, sessionEpoch,
                (int)OnlineRoomPurpose.Arcade, (int)OnlineRoomVisibility.Private,
                clientVersion, arcadeConfigFingerprint);
            bool committed = created.Error == ErrorCode.ERR_Success ||
                             created.Error == ErrorCode.ERR_OnlineChatMembershipPending;
            if (committed)
            {
                self.ArcadeMatchRooms[matchKey] = created.RoomId;
                self.ArcadeMatchKeysByRoomId[created.RoomId] = matchKey;
            }

            return (created.Error, created.Message, created.RoomId, true, created.Room);
        }

        public static (int Error, string Message, long RoomId) ResolveRoom(
            this OnlineRoomDirectoryComponent self,
            long playerId,
            string roomCode,
            string sourceAddress)
        {
            self.CleanupExpired();
            if (!OnlineRoomSnapshotHelper.TryNormalizeRoomCode(roomCode, out string normalized))
            {
                return (ErrorCode.ERR_OnlineRoomCodeInvalid, "房间号格式错误", 0);
            }

            if (!self.AllowJoin(playerId, sourceAddress))
            {
                return (ErrorCode.ERR_OnlineRoomRateLimited, "操作过于频繁，请稍后重试", 0);
            }

            if (!self.ActiveRooms.TryGetValue(normalized, out long roomId))
            {
                return (ErrorCode.ERR_OnlineRoomNotFound, "房间不存在或已失效", 0);
            }

            return (ErrorCode.ERR_Success, string.Empty, roomId);
        }

        public static void CloseRoom(this OnlineRoomDirectoryComponent self, long roomId, string roomCode)
        {
            if (!OnlineRoomSnapshotHelper.TryNormalizeRoomCode(roomCode, out string normalized))
            {
                return;
            }

            if (self.ActiveRooms.TryGetValue(normalized, out long activeRoomId) && activeRoomId == roomId)
            {
                self.ActiveRooms.Remove(normalized);
            }

            self.ReservedCodes.Remove(normalized);
            if (self.ArcadeMatchKeysByRoomId.Remove(roomId, out string matchKey) &&
                self.ArcadeMatchRooms.TryGetValue(matchKey, out long waitingRoomId) && waitingRoomId == roomId)
            {
                self.ArcadeMatchRooms.Remove(matchKey);
            }
            self.Tombstones[normalized] = new OnlineRoomCloseRecord
            {
                RoomId = roomId,
                TombstoneUntil = TimeInfo.Instance.ServerNow() + TombstoneMilliseconds,
            };
        }

        private static string BuildArcadeMatchKey(string clientVersion, string arcadeConfigFingerprint)
        {
            string version = clientVersion ?? string.Empty;
            string fingerprint = arcadeConfigFingerprint ?? string.Empty;
            return $"{version.Length}:{version}{fingerprint.Length}:{fingerprint}";
        }

        private static string ReserveRoomCode(this OnlineRoomDirectoryComponent self)
        {
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                string code = CreateCode();
                if (self.ActiveRooms.ContainsKey(code) || self.ReservedCodes.Contains(code) || self.Tombstones.ContainsKey(code))
                {
                    continue;
                }

                self.ReservedCodes.Add(code);
                return code;
            }

            throw new InvalidOperationException("unable to reserve room code");
        }

        private static string CreateCode()
        {
            Span<byte> bytes = stackalloc byte[6];
            RandomNumberGenerator.Fill(bytes);
            char[] chars = new char[6];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
            }

            return new string(chars);
        }

        private static bool AllowJoin(this OnlineRoomDirectoryComponent self, long playerId, string sourceAddress)
        {
            long now = TimeInfo.Instance.ServerNow();
            return Allow(self.PlayerJoinAttempts, playerId, now, PlayerRateLimit) &&
                   Allow(self.SourceJoinAttempts, (sourceAddress ?? string.Empty).Trim(), now, SourceRateLimit);
        }

        private static bool Allow<TKey>(Dictionary<TKey, OnlineRoomRateBucket> buckets, TKey key, long now, int limit)
        {
            if (!buckets.TryGetValue(key, out OnlineRoomRateBucket bucket) || now - bucket.WindowStart >= RateWindowMilliseconds)
            {
                buckets[key] = new OnlineRoomRateBucket { WindowStart = now, Count = 1 };
                return true;
            }

            if (bucket.Count >= limit)
            {
                return false;
            }

            bucket.Count++;
            return true;
        }

        private static void CleanupExpired(this OnlineRoomDirectoryComponent self)
        {
            long now = TimeInfo.Instance.ServerNow();
            using ListComponent<string> expired = ListComponent<string>.Create();
            foreach (var kv in self.Tombstones)
            {
                if (kv.Value.TombstoneUntil <= now)
                {
                    expired.Add(kv.Key);
                }
            }

            foreach (string key in expired)
            {
                self.Tombstones.Remove(key);
            }
        }
    }
}
