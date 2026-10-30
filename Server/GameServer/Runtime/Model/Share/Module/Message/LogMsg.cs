using System.Collections.Generic;

using System.Threading;

namespace ET
{
    public enum ProtocolLogDirection
    {
        Send,
        Receive,
    }

    public class LogMsg: Singleton<LogMsg>, ISingletonAwake
    {
        private int fullProtocolLogEnabled;

        private readonly HashSet<ushort> ignore = new()
        {
            OuterMessage.C2G_Ping, 
            OuterMessage.G2C_Ping, 
            OuterMessage.C2G_Benchmark, 
            OuterMessage.G2C_Benchmark,
            OnlineOuter.C2G_Register,
            OnlineOuter.C2G_Login,
            OnlineInner.G2A_Register,
            OnlineInner.G2A_Login,
        };

        public void Awake()
        {
            Interlocked.Exchange(ref this.fullProtocolLogEnabled, 0);
        }

        public bool FullProtocolLogEnabled => Volatile.Read(ref this.fullProtocolLogEnabled) != 0;

        public bool SetFullProtocolLogEnabled(bool enabled)
        {
            int value = enabled ? 1 : 0;
            return Interlocked.Exchange(ref this.fullProtocolLogEnabled, value) != value;
        }

        public void Debug(Fiber fiber, Session session, object msg, ProtocolLogDirection direction)
        {
            ushort opcode = OpcodeType.Instance.GetOpcode(msg.GetType());
            // 完整协议调试也不能输出认证输入；只保留类型用于定位链路。
            if (opcode == OnlineOuter.C2G_Register || opcode == OnlineOuter.C2G_Login ||
                opcode == OnlineInner.G2A_Register || opcode == OnlineInner.G2A_Login)
            {
                fiber.Log.Debug($"[Protocol] {direction} Type={msg.GetType().Name} 凭据已隐藏");
                return;
            }
            if (!this.FullProtocolLogEnabled)
            {
                if (this.ignore.Contains(opcode))
                {
                    return;
                }

                fiber.Log.Debug(msg.ToString());
                return;
            }

            string remoteAddress = session.RemoteAddress?.ToString() ?? "<unknown>";
            string fullMessage = $"[Protocol] {direction} Session={session.Id} Remote={remoteAddress} Type={msg.GetType().FullName} Message={msg}";
            fiber.Log.Debug(fullMessage);
            Log.Console(fullMessage);
        }
    }
}
