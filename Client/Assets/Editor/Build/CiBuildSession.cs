#if UNITY_EDITOR
using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>同步CI构建作用域：独立状态、指定基线和明确产物，退出即恢复正常菜单语义。</summary>
    internal sealed class CiBuildSession : IDisposable
    {
        internal static CiBuildSession Current { get; private set; }
        internal string StatePath => Path.Combine(root, "build-state.json");
        internal string ArtifactPath { get; private set; }
        private readonly CiBuildRequest request;
        private readonly string root, commit, boot;

        internal CiBuildSession(CiBuildRequest request, string commit)
        {
            if (Current != null) throw new InvalidOperationException("CI构建作用域已占用");
            this.request = request; this.commit = commit;
            boot = AndroidBootBuildGuard.ComputeFingerprint(request.ProjectPath);
            root = Path.Combine(Directory.GetParent(request.ProjectPath).FullName, "Release", "Ci", request.RequestId + "-" + Guid.NewGuid().ToString("N"));
            WorkspaceBuildLock.EnsureNoLinks(root); Directory.CreateDirectory(root);
            if (request.BuildType == "HostUpdate")
            {
                string state = HostBaselineArchive.Restore(request.BaselineDirectory, Path.Combine(root, "baseline-input"), request.Platform, Application.unityVersion, boot);
                File.WriteAllText(StatePath, state);
            }
            Current = this;
        }

        /// <summary>菜单在任何发布或启动服务之前调用；首包同时归档当前成功宿主，随后封存文件清单。</summary>
        internal void Capture(string version, string package, string player = null, string installer = null, string appVersion = null, int versionCode = 0)
        {
            string baseline = null;
            if (request.BuildType == "FirstPackage")
            {
                baseline = Path.Combine(root, "baseline");
                HostBaselineArchive.Create(baseline, request.Platform, commit, Application.unityVersion, boot, File.ReadAllText(StatePath), Path.Combine(request.ProjectPath, "Assets/HybridCLRGenerate"));
            }
            ArtifactPath = CiBuildArtifacts.Seal(Path.Combine(root, "artifact.json"), JsonConvert.SerializeObject(new CiBuildArtifacts
            {
                SchemaVersion = 1, ProjectId = "FirepowerLoop", Platform = request.Platform, BuildType = request.BuildType,
                Version = version, PackageDirectory = Path.GetFullPath(package), PlayerPath = player, InstallerDirectory = installer,
                BaselineDirectory = baseline, AppVersion = appVersion, AndroidVersionCode = versionCode
            }));
        }
        public void Dispose() { if (Current == this) Current = null; }
    }
}
#endif
