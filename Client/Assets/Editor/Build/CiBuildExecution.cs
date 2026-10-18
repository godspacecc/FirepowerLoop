#if UNITY_EDITOR
using System;
using System.IO;

namespace Game.Editor
{
    /// <summary>同步CI取消作用域；长调用只在阶段边界响应，已经公开的发布不伪报为已撤回。</summary>
    public sealed class CiBuildExecution : IDisposable
    {
        internal static CiBuildExecution Current { get; private set; }
        private readonly CiBuildRequest request;
        private bool complete;
        internal bool PublishCommitted { get; private set; }
        internal string CheckpointName { get; private set; } = "BeforeBuild";
        internal CiBuildExecution(CiBuildRequest request, string commit)
        {
            if (Current != null) throw new InvalidOperationException("已有CI取消作用域");
            this.request = request; CiWorkspaceRecovery.Begin(request.ProjectPath, request.RequestId, commit); Current = this;
        }
        internal static bool IsCancellationRequested(CiBuildRequest request) => !string.IsNullOrEmpty(request.CancellationFile) && File.Exists(request.CancellationFile);
        public static void Checkpoint(string stage)
        {
            if (Current == null) return;
            Current.CheckpointName = stage;
            if (!Current.PublishCommitted && IsCancellationRequested(Current.request)) throw new OperationCanceledException("CI在安全检查点取消：" + stage);
        }
        internal static void MarkPublishCommitted() { if (Current != null) Current.PublishCommitted = true; }
        internal void Complete() { CiWorkspaceRecovery.Complete(request.ProjectPath, request.RequestId); complete = true; }
        public void Dispose()
        {
            try { if (!complete) CiWorkspaceRecovery.MarkInterrupted(request.ProjectPath, request.RequestId, CheckpointName); }
            finally { if (Current == this) Current = null; }
        }
    }
}
#endif
