using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using QFramework;
using UnityEngine;
using Boot;

namespace Game
{
    /// <summary>
    /// 订阅 Unity 日志并批量写入 RuntimeLogModel；错误即时落盘，普通日志只保留在内存。
    /// </summary>
    public sealed class RuntimeLogSystem : AbstractSystem
    {
        private readonly ConcurrentQueue<RuntimeLogEntry> pending = new ConcurrentQueue<RuntimeLogEntry>();
        private const int MaxPending = 2000;
        private bool subscribed;
        private bool reportedBackpressure;
        /// <summary>Game 初始化后到注销前的错误落盘器；目录不可用时为 null，不影响游戏运行。</summary>
        private RuntimeErrorLogStore errorLogStore;

        protected override void OnInit()
        {
            try
            {
                errorLogStore = new RuntimeErrorLogStore(Path.Combine(Application.persistentDataPath, "Logs"));
            }
            catch
            {
                errorLogStore = null;
            }
            Subscribe();
            DiagnosticReportSender.RegisterGame();
            LifeCycle.RegisterUpdate(Update);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(Update);
            DiagnosticReportSender.UnregisterGame();
            if (subscribed)
            {
                Application.logMessageReceivedThreaded -= OnLogReceived;
                subscribed = false;
            }

            while (pending.TryDequeue(out _))
            {
            }
            errorLogStore = null;
        }

        public void EnqueueForTests(RuntimeLogEntry entry)
        {
            pending.Enqueue(entry);
        }

        public int DrainForTests(int maxCount)
        {
            return Drain(maxCount);
        }

        public void Clear()
        {
            this.GetModel<RuntimeLogModel>().Clear();
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            Application.logMessageReceivedThreaded += OnLogReceived;
            subscribed = true;
        }

        private void OnLogReceived(string condition, string stackTrace, LogType type)
        {
            var entry = new RuntimeLogEntry(DateTime.Now, type, condition, stackTrace);
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                errorLogStore?.Append(entry);
                DiagnosticReportSender.CaptureRuntime(stackTrace, type);
            }
            pending.Enqueue(entry);
        }

        private void Update()
        {
            Drain(100);
            DiagnosticReportSender.TickGame();
        }

        private int Drain(int maxCount)
        {
            if (pending.IsEmpty)
            {
                return 0;
            }

            ApplyBackpressure();
            var count = 0;
            var batch = new List<RuntimeLogEntry>(Mathf.Max(0, maxCount));
            var model = this.GetModel<RuntimeLogModel>();
            while (count < maxCount && pending.TryDequeue(out var entry))
            {
                batch.Add(entry);
                count++;
            }

            model.AddRange(batch);
            return count;
        }

        private void ApplyBackpressure()
        {
            if (pending.Count <= MaxPending)
            {
                return;
            }

            var drained = new List<RuntimeLogEntry>(pending.Count);
            while (pending.TryDequeue(out var entry))
            {
                drained.Add(entry);
            }

            var dropped = 0;
            for (var i = 0; i < drained.Count && drained.Count - dropped > MaxPending; i++)
            {
                if (drained[i].Type == LogType.Log)
                {
                    drained[i] = default;
                    dropped++;
                }
            }

            if (dropped == 0)
            {
                while (drained.Count - dropped > MaxPending)
                {
                    drained[dropped] = default;
                    dropped++;
                }
            }

            foreach (var entry in drained)
            {
                if (entry.Message != null)
                {
                    pending.Enqueue(entry);
                }
            }

            if (dropped > 0 && !reportedBackpressure)
            {
                pending.Enqueue(new RuntimeLogEntry(DateTime.Now, LogType.Warning, $"RuntimeLog dropped {dropped} low-priority log entries.", string.Empty));
                reportedBackpressure = true;
            }
        }
    }
}
