#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEditor;

namespace Game.Editor
{
    /// <summary>两端共用的构建版本、重载门禁及失败文本规则；不切平台、不触发构建、不改变产物。</summary>
    public static class BuildWorkflowUtility
    {
        /// <summary>按本地构建时间生成秒级资源版本；旧版本读取与应用版本递增由原入口负责。</summary>
        public static string CreateResourceVersion(DateTime timestamp) =>
            timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        /// <summary>必须由真正的程序集重载确认，且编译和导入结束后才能恢复原操作。</summary>
        public static bool CanResume(bool reloaded, bool compiling, bool updating) =>
            reloaded && !compiling && !updating;

        /// <summary>统一错误支持信息；保留路径由调用方在产物校验完成后提供，空值不宣称有可用产物。</summary>
        public static string FormatFailure(BuildStageTiming timing, string error, string retainedPath = null)
        {
            string retained = string.IsNullOrEmpty(retainedPath) ? string.Empty : $"\n已保留产物: {retainedPath}";
            return $"入口: {timing.WorkflowName}\n失败阶段: {timing.CurrentStage ?? "（无）"}\n原因: {error}" +
                retained + "\n\n" + timing.BuildSummary();
        }
    }

    /// <summary>Current and legacy state are scoped to the open Unity editor session.</summary>
    internal sealed class BuildWorkflowSession
    {
        private readonly string pendingKey;
        private readonly string reloadKey;
        private readonly string legacyPendingKey;
        private readonly string legacyReloadKey;

        internal BuildWorkflowSession(string prefix, string legacyPrefix)
        {
            pendingKey = prefix + ".PendingWorkflow";
            reloadKey = prefix + ".ReloadCompleted";
            legacyPendingKey = legacyPrefix + ".PendingWorkflow";
            legacyReloadKey = legacyPrefix + ".ReloadCompleted";
        }

        private string CurrentWorkflow => SessionState.GetString(pendingKey, string.Empty);
        private string LegacyWorkflow => SessionState.GetString(legacyPendingKey, string.Empty);

        internal string PendingWorkflow
        {
            get
            {
                string current = CurrentWorkflow;
                return string.IsNullOrEmpty(current) ? LegacyWorkflow : current;
            }
        }

        internal bool ReloadCompleted =>
            !string.IsNullOrEmpty(CurrentWorkflow)
                ? SessionState.GetBool(reloadKey, false)
                : !string.IsNullOrEmpty(LegacyWorkflow) && SessionState.GetBool(legacyReloadKey, false);

        internal void Schedule(string workflow)
        {
            SessionState.SetBool(reloadKey, false);
            SessionState.SetString(pendingKey, workflow);
            ClearLegacy();
        }

        internal void MarkReloadCompleted()
        {
            if (!string.IsNullOrEmpty(CurrentWorkflow))
                SessionState.SetBool(reloadKey, true);
            else if (!string.IsNullOrEmpty(LegacyWorkflow))
                SessionState.SetBool(legacyReloadKey, true);
        }

        internal void Clear()
        {
            SessionState.EraseString(pendingKey);
            SessionState.EraseBool(reloadKey);
            ClearLegacy();
        }

        private void ClearLegacy()
        {
            SessionState.EraseString(legacyPendingKey);
            SessionState.EraseBool(legacyReloadKey);
        }
    }
}
#endif
