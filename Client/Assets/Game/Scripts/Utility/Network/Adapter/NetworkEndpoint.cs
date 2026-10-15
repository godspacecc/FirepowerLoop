using System;

namespace Game.Network
{
    public readonly struct NetworkEndpoint : IEquatable<NetworkEndpoint>
    {
        public NetworkEndpoint(string host, int port)
        {
            Host = (host ?? string.Empty).Trim();
            Port = port;
            if (string.IsNullOrWhiteSpace(Host))
            {
                throw new ArgumentException("网络端点 host 为空。", nameof(host));
            }
            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }
        }

        public string Host { get; }
        public int Port { get; }

        public static bool TryParse(string value, out NetworkEndpoint endpoint)
        {
            endpoint = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string text = value.Trim();
            int separator = text.LastIndexOf(':');
            if (separator <= 0 || separator == text.Length - 1)
            {
                return false;
            }

            string host = text.Substring(0, separator).Trim();
            if (!int.TryParse(text.Substring(separator + 1), out int port))
            {
                return false;
            }

            try
            {
                endpoint = new NetworkEndpoint(host, port);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override string ToString()
        {
            return $"{Host}:{Port}";
        }

        public bool Equals(NetworkEndpoint other)
        {
            return string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase) && Port == other.Port;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkEndpoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Host) ^ Port;
        }
    }
}
