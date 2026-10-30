using System.Net;
namespace FirepowerLoop.Accounts;

/// <summary>按可信Socket来源限制每分钟120次认证请求，状态有界且按窗口回收。</summary>
public sealed class AccountRequestGuard(TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly Dictionary<string,(DateTimeOffset Start,int Count)> windows = new();
    private readonly object gate = new();
    private int attempts;
    public bool Allow(IPAddress address)
    {
        string key = (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
        lock (gate)
        {
            var now = time.GetUtcNow();
            if (++attempts % 256 == 0)
                foreach (var pair in windows.ToArray())
                    if (now - pair.Value.Start >= TimeSpan.FromMinutes(1)) windows.Remove(pair.Key);
            if (!windows.TryGetValue(key, out var window))
            {
                if (windows.Count >= 100000) return false;
                window = (now,0);
            }
            if (now - window.Start >= TimeSpan.FromMinutes(1)) window = (now,0);
            if (window.Count >= 120) return false;
            windows[key] = (window.Start, window.Count+1); return true;
        }
    }
}
