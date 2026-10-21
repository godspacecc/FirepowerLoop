using System;
using ET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Network
{
    /// <summary>网络诊断的统一日志出口；仅脱敏日志副本，不修改实际请求、回包或传输内容。</summary>
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

        /// <summary>发送与接收共用脱敏路径；格式化失败只记录异常类型，不回退输出原始对象。</summary>
        private static void LogProtocol(string direction, IMessage message)
        {
            if (!Enabled)
            {
                return;
            }

            string messageType = message?.GetType().Name ?? "null";
            try
            {
                JToken snapshot = message == null ? JValue.CreateNull() : JToken.FromObject(message);
                MaskPasswords(snapshot);
                string json = snapshot.ToString(Formatting.None);
                UnityEngine.Debug.Log($"[Network] {direction} {messageType}={json}");
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning($"[Network] {direction} {messageType} JSON序列化失败 error={exception.GetType().Name}");
            }
        }

        /// <summary>递归隐藏对象与数组中的 Password 字段，忽略字段大小写；普通文本值保持原样。</summary>
        private static void MaskPasswords(JToken snapshot)
        {
            if (snapshot is JObject obj)
            {
                foreach (JProperty property in obj.Properties())
                {
                    if (string.Equals(property.Name, "Password", StringComparison.OrdinalIgnoreCase))
                        property.Value = "***";
                    else
                        MaskPasswords(property.Value);
                }
            }
            else if (snapshot is JArray array)
            {
                foreach (JToken child in array) MaskPasswords(child);
            }
        }
    }
}
