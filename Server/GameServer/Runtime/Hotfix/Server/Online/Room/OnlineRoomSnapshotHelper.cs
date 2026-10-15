using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    [FriendOf(typeof(OnlineRoomActor))]
    public static class OnlineRoomSnapshotHelper
    {
        public const int GeneralCapacity = 4;
        public const int ArcadeCapacity = 2;
        private const int MaxVersionLength = 64;
        private const int MaxFingerprintLength = 128;
        private const string CodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        public static bool TryNormalizeRoomCode(string input, out string normalized)
        {
            normalized = (input ?? string.Empty).Trim().ToUpperInvariant();
            if (normalized.Length != 6)
            {
                return false;
            }

            for (int i = 0; i < normalized.Length; i++)
            {
                if (!CodeAlphabet.Contains(normalized[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsValidOperationId(string operationId)
        {
            if (operationId == null || operationId.Length != 32)
            {
                return false;
            }

            for (int i = 0; i < operationId.Length; i++)
            {
                char c = operationId[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        public static OnlineRoomSnapshot BuildSnapshot(OnlineRoomActor room)
        {
            OnlineRoomSnapshot snapshot = OnlineRoomSnapshot.Create();
            snapshot.RoomId = room.RoomId;
            snapshot.RoomCode = room.RoomCode;
            snapshot.OwnerPlayerId = room.OwnerPlayerId;
            snapshot.MembershipRevision = room.MembershipRevision;
            snapshot.Purpose = (int)room.Purpose;
            snapshot.Visibility = (int)room.Visibility;
            snapshot.Capacity = room.Capacity;
            snapshot.MembershipLocked = room.MembershipLocked;
            snapshot.ClientVersion = room.ClientVersion ?? string.Empty;
            snapshot.ArcadeConfigFingerprint = room.ArcadeConfigFingerprint ?? string.Empty;
            snapshot.Members ??= new List<RoomMemberInfo>();

            foreach (OnlineRoomMember member in room.Members.Values.OrderBy(m => m.JoinSequence))
            {
                RoomMemberInfo info = RoomMemberInfo.Create();
                info.PlayerId = member.PlayerId;
                info.DisplayName = member.DisplayName ?? string.Empty;
                info.IsOwner = member.PlayerId == room.OwnerPlayerId;
                info.ConnectionState = (int)member.ConnectionState;
                info.JoinSequence = member.JoinSequence;
                snapshot.Members.Add(info);
            }

            return snapshot;
        }

        // 公开摘要直接读取 Room Actor 的权威状态；查询只读且不在目录中维护人数或房主缓存。
        // 已释放、关闭中、空房或缺少房主成员的异常 Room 都不会暴露给客户端。
        public static bool TryBuildPublicRoomSummary(OnlineRoomActor room, out PublicRoomSummary summary)
        {
            summary = null;
            if (room == null || room.IsDisposed || room.Closing || room.Members.Count == 0 ||
                room.Visibility != OnlineRoomVisibility.Public)
            {
                return false;
            }

            if (!room.Members.TryGetValue(room.OwnerPlayerId, out OnlineRoomMember owner))
            {
                Log.Error($"online room public summary skipped roomId={room.RoomId} reason=owner-missing");
                return false;
            }

            summary = PublicRoomSummary.Create();
            summary.RoomCode = room.RoomCode ?? string.Empty;
            summary.OwnerDisplayName = owner.DisplayName ?? string.Empty;
            summary.MemberCount = room.Members.Count;
            summary.Capacity = room.Capacity;
            return true;
        }

        public static OnlineRoomSnapshot CloneSnapshot(OnlineRoomSnapshot source)
        {
            if (source == null)
            {
                return null;
            }

            OnlineRoomSnapshot clone = OnlineRoomSnapshot.Create();
            clone.RoomId = source.RoomId;
            clone.RoomCode = source.RoomCode ?? string.Empty;
            clone.OwnerPlayerId = source.OwnerPlayerId;
            clone.MembershipRevision = source.MembershipRevision;
            clone.Purpose = source.Purpose;
            clone.Visibility = source.Visibility;
            clone.Capacity = source.Capacity;
            clone.MembershipLocked = source.MembershipLocked;
            clone.ClientVersion = source.ClientVersion ?? string.Empty;
            clone.ArcadeConfigFingerprint = source.ArcadeConfigFingerprint ?? string.Empty;
            clone.Members ??= new List<RoomMemberInfo>();
            if (source.Members != null)
            {
                foreach (RoomMemberInfo member in source.Members)
                {
                    RoomMemberInfo copy = RoomMemberInfo.Create();
                    copy.PlayerId = member.PlayerId;
                    copy.DisplayName = member.DisplayName ?? string.Empty;
                    copy.IsOwner = member.IsOwner;
                    copy.ConnectionState = member.ConnectionState;
                    copy.JoinSequence = member.JoinSequence;
                    clone.Members.Add(copy);
                }
            }

            return clone;
        }

        /// <summary>规范化创建参数；街机房间强制为私有双人房，并要求稳定版本身份。</summary>
        public static bool TryValidateCreation(int purposeValue, int visibilityValue, string clientVersion,
            string arcadeConfigFingerprint, out OnlineRoomPurpose purpose, out OnlineRoomVisibility visibility,
            out int capacity, out string error)
        {
            purpose = (OnlineRoomPurpose)purposeValue;
            visibility = (OnlineRoomVisibility)visibilityValue;
            capacity = 0;
            error = string.Empty;
            if (!System.Enum.IsDefined(typeof(OnlineRoomPurpose), purpose) ||
                !System.Enum.IsDefined(typeof(OnlineRoomVisibility), visibility))
            {
                error = "房间用途或可见性无效";
                return false;
            }

            if (purpose == OnlineRoomPurpose.General)
            {
                if (visibility != OnlineRoomVisibility.Public)
                {
                    error = "普通房间必须公开";
                    return false;
                }
                capacity = GeneralCapacity;
                return true;
            }

            if (visibility != OnlineRoomVisibility.Private ||
                string.IsNullOrWhiteSpace(clientVersion) || clientVersion.Length > MaxVersionLength ||
                string.IsNullOrWhiteSpace(arcadeConfigFingerprint) || arcadeConfigFingerprint.Length > MaxFingerprintLength)
            {
                error = "街机房间必须提供有效版本与配置指纹";
                return false;
            }
            if (!string.Equals(arcadeConfigFingerprint, ServerConfig.Instance.ArcadeRule.ConfigFingerprint,
                    System.StringComparison.Ordinal))
            {
                error = "街机配置版本与服务器不一致";
                return false;
            }
            capacity = ArcadeCapacity;
            return true;
        }

        /// <summary>加入时校验房间用途及双端版本；失败不修改成员表。</summary>
        public static bool IsCompatible(OnlineRoomActor room, int expectedPurpose, string clientVersion,
            string arcadeConfigFingerprint, out int error)
        {
            if (room == null || expectedPurpose != (int)room.Purpose)
            {
                error = ErrorCode.ERR_OnlineRoomPurposeMismatch;
                return false;
            }
            if (room.Purpose == OnlineRoomPurpose.Arcade &&
                (!string.Equals(room.ClientVersion, clientVersion, System.StringComparison.Ordinal) ||
                 !string.Equals(room.ArcadeConfigFingerprint, arcadeConfigFingerprint, System.StringComparison.Ordinal)))
            {
                error = ErrorCode.ERR_OnlineRoomVersionMismatch;
                return false;
            }
            error = ErrorCode.ERR_Success;
            return true;
        }
    }
}
