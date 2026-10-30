using System;

namespace Game.Network
{
    internal interface INetworkClock
    {
        long ElapsedMilliseconds { get; }
        long DeltaMilliseconds { get; }
        void Update();
    }

    /// <summary>使用单调时钟累计网络预算，系统时间调整不能提前或延后 RPC 超时。</summary>
    internal sealed class NetworkClock : INetworkClock
    {
        private readonly Func<long> tickProvider;
        private long lastTick;

        public NetworkClock()
            : this(() => (long)(System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency))
        {
        }

        internal NetworkClock(Func<long> tickProvider)
        {
            this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
            lastTick = this.tickProvider();
        }

        public long ElapsedMilliseconds { get; private set; }

        public long DeltaMilliseconds { get; private set; }

        public void Update()
        {
            long now = tickProvider();
            DeltaMilliseconds = Math.Max(0, now - lastTick);
            ElapsedMilliseconds += DeltaMilliseconds;
            lastTick = now;
        }
    }
}
