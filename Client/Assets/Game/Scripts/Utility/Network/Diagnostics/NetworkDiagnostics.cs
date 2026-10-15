using System;
using ET;
using Newtonsoft.Json;

namespace Game.Network
{
    public static class NetworkDiagnostics
    {
#if UNITY_EDITOR
        public static bool Enabled { get; private set; } = true;
#else
        public static bool Enabled { get; private set; }
#endif

        public static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            UnityEngine.Debug.Log($"[Network] 诊断日志已开启={enabled}");
        }

        public static void Log(string message)
        {
            if (!Enabled)
            {
                return;
            }

            UnityEngine.Debug.Log($"运行时操作失败：[Network] {message}");
        }

        internal static void LogSend(IMessage message)
        {
            LogProtocol("Send", message);
        }

        internal static void LogReceive(IMessage message)
        {
            LogProtocol("Receive", message);
        }

        private static void LogProtocol(string direction, IMessage message)
        {
            if (!Enabled)
            {
                return;
            }

            string messageType = message?.GetType().Name ?? "null";
            try
            {
                string json = JsonConvert.SerializeObject(message);
                UnityEngine.Debug.Log($"[Network] {direction} {messageType}={json}");
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning($"[Network] {direction} {messageType} JSON序列化失败 error={exception.GetType().Name}");
            }
        }
    }
}
