using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using ET;

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

    internal interface IUdpDatagramSocket : IDisposable
    {
        int Available { get; }
        void Bind(EndPoint localEndPoint);
        int ReceiveFrom(byte[] buffer, int offset, int length, SocketFlags socketFlags, ref EndPoint remoteEndPoint);
        int SendTo(byte[] buffer, int offset, int length, SocketFlags socketFlags, EndPoint remoteEndPoint);
    }

    internal enum KcpClientConnectionState
    {
        NotConnected,
        Connecting,
        Connected,
        Closed
    }

    internal sealed class KcpClientTransport : IKcpTransport
    {
        internal const byte SYN = 1;
        internal const byte ACK = 2;
        internal const byte FIN = 3;
        internal const byte MSG = 4;
        internal const int ConnectRetryMilliseconds = 300;
        internal const int ConnectTimeoutMilliseconds = 20 * 1000;

        private const int HeaderSize = 5;
        private const int HandshakeSize = 9;
        private const int FinSize = 13;
        private const int ReceiveBufferSize = 2048;
        private const int KcpMtu = 470;
        private const int KcpOverhead = 24;

        private readonly Queue<byte[]> completedPackets = new Queue<byte[]>();
        private readonly byte[] kcpOutputBuffer = new byte[HeaderSize + (KcpMtu + KcpOverhead) * 3];
        private readonly byte[] udpReceiveBuffer = new byte[2048];
        private readonly IUdpDatagramSocket socket;
        private readonly uint localConn;
        private EndPoint receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
        private IPEndPoint remoteEndPoint;
        private Kcp kcp;
        private uint remoteConn;
        private long connectStartedAtMilliseconds;
        private long lastSynAtMilliseconds = long.MinValue;
        private bool disposed;

        public KcpClientTransport()
            : this(new UdpDatagramSocket(), CreateLocalConn())
        {
        }

        internal KcpClientTransport(IUdpDatagramSocket socket, uint localConn)
        {
            this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
            this.localConn = localConn == 0 ? 1u : localConn;
            this.socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            State = KcpClientConnectionState.NotConnected;
        }

        public bool IsConnected => State == KcpClientConnectionState.Connected;

        public int SentPacketCount { get; private set; }

        internal KcpClientConnectionState State { get; private set; }

        internal uint LocalConn => localConn;

        internal uint RemoteConn => remoteConn;

        internal bool HasUdpSocket => socket != null && !disposed;

        internal bool HasKcp => kcp != null && kcp.IsSet;

        internal uint KcpConversationId => kcp?.ConversationId ?? 0;

        internal int AcceptedKcpInputCount { get; private set; }

        internal void Connect(IPEndPoint endPoint, long nowMilliseconds)
        {
            ThrowIfDisposed();
            remoteEndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
            remoteConn = 0;
            ResetKcp();
            completedPackets.Clear();
            State = KcpClientConnectionState.Connecting;
            connectStartedAtMilliseconds = Math.Max(0, nowMilliseconds);
            lastSynAtMilliseconds = long.MinValue;
            SendSyn(connectStartedAtMilliseconds);
        }

        public int Pump(long nowMilliseconds)
        {
            ThrowIfDisposed();
            long now = Math.Max(0, nowMilliseconds);
            if (State == KcpClientConnectionState.Connecting)
            {
                PumpConnecting(now);
            }

            DrainSocket();

            if (State == KcpClientConnectionState.Connected && kcp != null)
            {
                kcp.Update((uint)now, kcpOutputBuffer);
                DrainKcp();
            }

            return completedPackets.Count;
        }

        public void Send(byte[] packet)
        {
            ThrowIfDisposed();
            if (packet == null || packet.Length == 0)
            {
                throw new ArgumentException("数据包不能为空。", nameof(packet));
            }
            if (State != KcpClientConnectionState.Connected || kcp == null)
            {
                throw new InvalidOperationException("KCP 通道未连接。");
            }

            int result = kcp.Send(packet);
            if (result < 0)
            {
                throw new InvalidOperationException($"KCP 发送失败：result={result}");
            }

            SentPacketCount++;
        }

        public bool TryReceive(out byte[] packet)
        {
            ThrowIfDisposed();
            if (completedPackets.Count == 0)
            {
                packet = null;
                return false;
            }

            packet = completedPackets.Dequeue();
            return true;
        }

        public void Close()
        {
            if (disposed)
            {
                return;
            }

            SendFin(0);
            ResetChannelState(KcpClientConnectionState.Closed);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Close();
            disposed = true;
            socket.Dispose();
        }

        private static uint CreateLocalConn()
        {
            byte[] bytes = Guid.NewGuid().ToByteArray();
            uint value = BitConverter.ToUInt32(bytes, 0);
            return value == 0 ? 1u : value;
        }

        private void PumpConnecting(long nowMilliseconds)
        {
            if (nowMilliseconds > connectStartedAtMilliseconds + ConnectTimeoutMilliseconds)
            {
                ResetChannelState(KcpClientConnectionState.Closed);
                return;
            }

            if (nowMilliseconds >= lastSynAtMilliseconds + ConnectRetryMilliseconds)
            {
                SendSyn(nowMilliseconds);
            }
        }

        private void SendSyn(long nowMilliseconds)
        {
            if (remoteEndPoint == null)
            {
                return;
            }

            byte[] packet = new byte[HandshakeSize];
            packet[0] = SYN;
            WriteUInt32(packet, 1, localConn);
            WriteUInt32(packet, 5, 0);
            socket.SendTo(packet, 0, packet.Length, SocketFlags.None, remoteEndPoint);
            lastSynAtMilliseconds = nowMilliseconds;
        }

        private void SendFin(int error)
        {
            if (remoteEndPoint == null || State == KcpClientConnectionState.NotConnected)
            {
                return;
            }

            byte[] packet = new byte[FinSize];
            packet[0] = FIN;
            WriteUInt32(packet, 1, localConn);
            WriteUInt32(packet, 5, remoteConn);
            WriteInt32(packet, 9, error);
            for (int i = 0; i < 3; i++)
            {
                socket.SendTo(packet, 0, packet.Length, SocketFlags.None, remoteEndPoint);
            }
        }

        private void DrainSocket()
        {
            while (socket.Available > 0)
            {
                int count = socket.ReceiveFrom(udpReceiveBuffer, 0, udpReceiveBuffer.Length, SocketFlags.None, ref receiveEndPoint);
                if (count <= 0)
                {
                    continue;
                }

                HandleDatagram(udpReceiveBuffer, count);
            }
        }

        private void HandleDatagram(byte[] datagram, int count)
        {
            switch (datagram[0])
            {
                case ACK:
                    HandleAck(datagram, count);
                    break;
                case MSG:
                    HandleMsg(datagram, count);
                    break;
                case FIN:
                    HandleFin(datagram, count);
                    break;
            }
        }

        private void HandleAck(byte[] datagram, int count)
        {
            if (count != HandshakeSize || State != KcpClientConnectionState.Connecting)
            {
                return;
            }

            uint assignedRemoteConn = ReadUInt32(datagram, 1);
            uint acknowledgedLocalConn = ReadUInt32(datagram, 5);
            if (acknowledgedLocalConn != localConn || assignedRemoteConn == 0)
            {
                return;
            }

            remoteConn = assignedRemoteConn;
            kcp = new Kcp(remoteConn, OnKcpOutput);
            ConfigureOuterKcp(kcp);
            State = KcpClientConnectionState.Connected;
        }

        private void HandleMsg(byte[] datagram, int count)
        {
            if (count < HandshakeSize || State != KcpClientConnectionState.Connected || kcp == null)
            {
                return;
            }

            uint incomingRemoteConn = ReadUInt32(datagram, 1);
            uint incomingLocalConn = ReadUInt32(datagram, 5);
            if (incomingRemoteConn != remoteConn || incomingLocalConn != localConn)
            {
                return;
            }

            kcp.Input(datagram, HeaderSize, count - HeaderSize);
            AcceptedKcpInputCount++;
        }

        private void HandleFin(byte[] datagram, int count)
        {
            if (count != FinSize)
            {
                return;
            }

            uint incomingRemoteConn = ReadUInt32(datagram, 1);
            uint incomingLocalConn = ReadUInt32(datagram, 5);
            if (incomingRemoteConn != remoteConn || incomingLocalConn != localConn)
            {
                return;
            }

            ResetChannelState(KcpClientConnectionState.Closed);
        }

        private void DrainKcp()
        {
            while (true)
            {
                byte[] packet = new byte[ReceiveBufferSize];
                int count = kcp.Receive(packet);
                if (count <= 0)
                {
                    return;
                }

                if (count != packet.Length)
                {
                    Array.Resize(ref packet, count);
                }
                completedPackets.Enqueue(packet);
            }
        }

        private void OnKcpOutput(byte[] buffer, int count)
        {
            if (remoteEndPoint == null || State != KcpClientConnectionState.Connected || disposed)
            {
                return;
            }

            buffer[0] = MSG;
            WriteUInt32(buffer, 1, localConn);
            socket.SendTo(buffer, 0, count + HeaderSize, SocketFlags.None, remoteEndPoint);
        }

        private static void ConfigureOuterKcp(Kcp target)
        {
            target.SetNoDelay(1, 10, 2, 1);
            target.SetWindowSize(256, 256);
            target.SetMtu(KcpMtu);
            target.SetMinrto(30);
        }

        private void ResetChannelState(KcpClientConnectionState state)
        {
            remoteEndPoint = null;
            remoteConn = 0;
            completedPackets.Clear();
            ResetKcp();
            State = state;
        }

        private void ResetKcp()
        {
            kcp?.Dispose();
            kcp = null;
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            return BitConverter.ToUInt32(bytes, offset);
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            byte[] valueBytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(valueBytes, 0, bytes, offset, valueBytes.Length);
        }

        private static void WriteInt32(byte[] bytes, int offset, int value)
        {
            byte[] valueBytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(valueBytes, 0, bytes, offset, valueBytes.Length);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(KcpClientTransport));
            }
        }
    }

    internal sealed class UdpDatagramSocket : IUdpDatagramSocket
    {
        private readonly Socket socket;

        public UdpDatagramSocket()
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                Blocking = false
            };
        }

        public int Available => socket.Available;

        public void Bind(EndPoint localEndPoint)
        {
            socket.Bind(localEndPoint);
        }

        public int ReceiveFrom(byte[] buffer, int offset, int length, SocketFlags socketFlags, ref EndPoint remoteEndPoint)
        {
            return socket.ReceiveFrom(buffer, offset, length, socketFlags, ref remoteEndPoint);
        }

        public int SendTo(byte[] buffer, int offset, int length, SocketFlags socketFlags, EndPoint remoteEndPoint)
        {
            return socket.SendTo(buffer, offset, length, socketFlags, remoteEndPoint);
        }

        public void Dispose()
        {
            socket.Dispose();
        }
    }
}
