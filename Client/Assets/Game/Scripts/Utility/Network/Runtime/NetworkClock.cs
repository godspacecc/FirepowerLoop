using System;

namespace Game.Network
{
    internal interface INetworkClock
    {
        long ElapsedMilliseconds { get; }
        long DeltaMilliseconds { get; }
        void Update();
    }

    internal sealed class NetworkClock : INetworkClock
    {
        private readonly Func<long> tickProvider;
        private long lastTick;

        public NetworkClock()
            : this(() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
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
