#if UNITY_EDITOR
using System;
using System.IO;
using Newtonsoft.Json;

namespace Game.Editor
{
    /// <summary>CI结果区分构建和发布；失败保留已验证产物，不将发布失败报告为构建请求成功。</summary>
    [Serializable]
    public sealed class CiBuildReport
    {
        public int SchemaVersion = 1;
        public string ProjectId = "FirepowerLoop", RequestId, Route, Commit, Error, ArtifactPath;
        public string Status = "Running", Stage = "Validate", BuildStatus = "NotStarted", PublishStatus = "NotRequested";
        public int ExitCode = 1;
        public bool RecoveryRequired, CancellationDeferred;
        public string RecoveryJournalPath, Checkpoint;
        public string StartedUtc = DateTime.UtcNow.ToString("O"), CompletedUtc;
    }

    /// <summary>通过明确委托执行四路由，校验模式从不调用构建／发布；原子写入结果并统一释放工作目录锁。</summary>
    public static class CiBuildRunner
    {
        public static string Run(string json, string clientRoot, string actualCommit, string reportPath,
            Func<string, string, string> build, Action<string, string> publish)
            => RunWithPreflight(json, clientRoot, actualCommit, reportPath, null, build, publish);

        /// <summary>生产入口在持锁期间补充Unity及基线检查；失败同样进入结构化报告。</summary>
        public static string RunWithPreflight(string json, string clientRoot, string actualCommit, string reportPath,
            Action<CiBuildRequest> preflight, Func<string, string, string> build, Action<string, string> publish)
        {
            ValidateReportPath(clientRoot, reportPath);
            var report = new CiBuildReport { Commit = actualCommit };
            CiBuildExecution execution = null;
            try
            {
                var request = CiBuildRequest.Parse(json);
                report.RequestId = request.RequestId; report.Route = request.Validate(clientRoot, actualCommit);
                using (WorkspaceBuildLock.Acquire(clientRoot))
                {
                    CiWorkspaceRecovery.EnsureClean(clientRoot);
                    preflight?.Invoke(request);
                    if (CiBuildExecution.IsCancellationRequested(request)) throw new OperationCanceledException("构建开始前已取消");
                    if (request.ValidateOnly) { report.Status = "Validated"; report.ExitCode = 0; }
                    else
                    {
                        using (execution = new CiBuildExecution(request, actualCommit))
                        {
                            CiBuildExecution.Checkpoint("BeforeBuild");
                            report.Stage = "Build"; report.BuildStatus = "Running";
                            if (build == null) throw new InvalidOperationException("缺少独立构建路由");
                            report.ArtifactPath = build(report.Route, request.BaselineDirectory);
                            if (string.IsNullOrWhiteSpace(report.ArtifactPath) || !File.Exists(report.ArtifactPath) || new FileInfo(report.ArtifactPath).Length == 0)
                                throw new InvalidOperationException("构建没有返回本次有效产物清单");
                            WorkspaceBuildLock.EnsureNoLinks(report.ArtifactPath);
                            report.BuildStatus = "Succeeded";
                            CiBuildExecution.Checkpoint("AfterBuild");
                            if (request.Publish)
                            {
                                report.Stage = "Publish"; report.PublishStatus = "Running";
                                CiBuildExecution.Checkpoint("BeforePublish");
                                if (publish == null) throw new InvalidOperationException("缺少独立发布入口");
                                publish(report.ArtifactPath, request.PublishDirectory);
                                report.PublishStatus = "Succeeded";
                            }
                            CiBuildExecution.Checkpoint("AfterWork");
                            report.CancellationDeferred = execution.PublishCommitted && CiBuildExecution.IsCancellationRequested(request);
                            execution.Complete();
                            report.Status = "Succeeded"; report.ExitCode = 0;
                        }
                    }
                }
            }
            catch (OperationCanceledException e)
            {
                report.Status = "Cancelled"; report.ExitCode = 2; report.Error = e.Message;
                if (report.BuildStatus == "Running") report.BuildStatus = "Cancelled";
                if (report.PublishStatus == "Running") report.PublishStatus = "Cancelled";
            }
            catch (Exception e)
            {
                report.Status = "Failed"; report.Error = e.Message;
                if (report.BuildStatus == "Running") report.BuildStatus = "Failed";
                if (report.PublishStatus == "Running") report.PublishStatus = "Failed";
            }
            report.Checkpoint = execution?.CheckpointName ?? "BeforeBuild";
            report.RecoveryRequired = CiWorkspaceRecovery.RequiresRecovery(clientRoot);
            if (report.RecoveryRequired) report.RecoveryJournalPath = CiWorkspaceRecovery.JournalPath(clientRoot);
            report.CompletedUtc = DateTime.UtcNow.ToString("O");
            string result = JsonConvert.SerializeObject(report, Formatting.Indented);
            string path = Path.GetFullPath(reportPath), pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                File.WriteAllText(pending, result);
                if (File.Exists(path)) File.Replace(pending, path, null); else File.Move(pending, path);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
            return result;
        }

        /// <summary>结果文件只能放源资源外，拒绝路径链接，失败也不能覆盖现役Assets或ProjectSettings。</summary>
        public static void ValidateReportPath(string clientRoot, string reportPath)
        {
            if (!Path.IsPathRooted(reportPath ?? "")) throw new ArgumentException("报告路径必须绝对指定");
            string path = Path.GetFullPath(reportPath);
            foreach (string folder in new[] { "Assets", "ProjectSettings", "Packages" })
            {
                string protectedRoot = Path.GetFullPath(Path.Combine(clientRoot, folder)).TrimEnd('\\', '/');
                if (path.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase) || path.StartsWith(protectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("报告不能覆盖工程源目录");
            }
            WorkspaceBuildLock.EnsureNoLinks(path);
            string temp = Path.GetFullPath(Path.Combine(clientRoot, "Temp"));
            string recoveryRoot = CiWorkspaceRecovery.RecoveryRoot(clientRoot);
            if (path.Equals(CiWorkspaceRecovery.JournalPath(clientRoot), StringComparison.OrdinalIgnoreCase) ||
                path.Equals(Path.Combine(temp, "FirepowerLoop.Build.lock"), StringComparison.OrdinalIgnoreCase) ||
                path.Equals(recoveryRoot, StringComparison.OrdinalIgnoreCase) || path.StartsWith(recoveryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("报告或取消信号不能覆盖内部锁、恢复标记或快照目录");
        }
    }
}
#endif
