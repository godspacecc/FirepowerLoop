// 仅替代独立传输检查不使用的架构/消息标记；Socket、帧和连接代码均编译目标正式源码。
namespace QFramework { public interface IUtility { } }
namespace Boot { internal static class ClientReleasePolicy { internal static bool OfflineSinglePlayer; } }
namespace UnityEngine { internal static class Debug { public static void Log(object value) { } public static void LogWarning(object value) { } public static void LogError(object value) => throw new Exception(value.ToString()); } }
namespace Game.Network
{
    internal interface IKcpTransport : IDisposable
    {
        bool IsConnected { get; }
        int SentPacketCount { get; }
        int Pump(long nowMilliseconds);
        void Send(byte[] packet);
        bool TryReceive(out byte[] packet);
        void Close();
    }
}
