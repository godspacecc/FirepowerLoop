using System.Collections.Concurrent;

namespace FirepowerLoop.Accounts;

/// <summary>5分钟内5次失败锁定10分钟；有界记录并定期回收，成功后清理该账号失败窗口。</summary>
public sealed class AccountFailureGuard(TimeProvider? clock = null)
{
    private sealed class Window
    {
        public readonly Queue<DateTimeOffset> Failures = new();
        public DateTimeOffset LockedUntil, LastSeen;
    }
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Window> windows = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private int count;
    public bool IsLocked(string name)
    {
        lock (gate)
        {
            if (!windows.TryGetValue(name, out var state)) return false;
            return state.LockedUntil > time.GetUtcNow();
        }
    }
    public bool Failure(string name)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            if (++count % 256 == 0)
                foreach (var pair in windows)
                    if (now - pair.Value.LastSeen > TimeSpan.FromMinutes(15)) windows.TryRemove(pair.Key, out _);
            if (!windows.TryGetValue(name, out var state))
            {
                if (windows.Count >= 100000) return true;
                state = new Window(); windows[name] = state;
            }
            state.LastSeen = now;
            if (state.LockedUntil > now) return true;
            if (state.LockedUntil != default) { state.LockedUntil = default; state.Failures.Clear(); }
            while (state.Failures.TryPeek(out var stamp) && stamp <= now.AddMinutes(-5)) state.Failures.Dequeue();
            state.Failures.Enqueue(now);
            if (state.Failures.Count < 5) return false;
            state.Failures.Clear(); state.LockedUntil = now.AddMinutes(10); return true;
        }
    }
    public void Success(string name) { lock (gate) windows.TryRemove(name, out _); }
}
