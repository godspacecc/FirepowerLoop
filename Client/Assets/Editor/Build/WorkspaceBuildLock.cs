#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Game.Editor
{
    /// <summary>同Client目录的进程间构建锁；允许同线程嵌套，禁止其他线程或进程并行覆盖产物。</summary>
    public static class WorkspaceBuildLock
    {
        private sealed class Owner { public FileStream Stream; public int Thread; public int Count; }
        private static readonly Dictionary<string, Owner> Owners = new Dictionary<string, Owner>(StringComparer.OrdinalIgnoreCase);

        public static IDisposable Acquire(string clientDirectory)
        {
            string key = Path.GetFullPath(clientDirectory).TrimEnd('\\', '/');
            EnsureNoLinks(key);
            if (!Directory.Exists(key)) throw new DirectoryNotFoundException("构建工作目录不存在");
            lock (Owners)
            {
                int thread = Thread.CurrentThread.ManagedThreadId;
                if (Owners.TryGetValue(key, out var existing))
                {
                    if (existing.Thread != thread) throw new IOException("其他线程持有该工作目录构建锁");
                    existing.Count++; return new Lease(key);
                }
                string directory = Path.Combine(key, "Temp"); EnsureNoLinks(directory); Directory.CreateDirectory(directory);
                var stream = new FileStream(Path.Combine(directory, "FirepowerLoop.Build.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes("pid=" + Process.GetCurrentProcess().Id + "\nutc=" + DateTime.UtcNow.ToString("O"));
                    stream.SetLength(0); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                    Owners.Add(key, new Owner { Stream = stream, Thread = thread, Count = 1 });
                }
                catch { stream.Dispose(); throw; }
                return new Lease(key);
            }
        }

        /// <summary>拒绝文件或任一祖先的重解析点，避免锁、报告和基线写入落到另一工程。</summary>
        public static void EnsureNoLinks(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("构建路径不能经过链接：" + current);
        }

        private sealed class Lease : IDisposable
        {
            private string key;
            public Lease(string key) { this.key = key; }
            public void Dispose()
            {
                lock (Owners)
                {
                    if (key == null) return;
                    var owner = Owners[key];
                    if (owner.Thread != Thread.CurrentThread.ManagedThreadId) throw new InvalidOperationException("锁必须由持有线程释放");
                    if (--owner.Count == 0) { owner.Stream.Dispose(); Owners.Remove(key); }
                    key = null;
                }
            }
        }
    }
}
#endif
