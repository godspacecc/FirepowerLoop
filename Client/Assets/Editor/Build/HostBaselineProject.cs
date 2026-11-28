using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Game.Editor;

namespace Shared.UnityTools
{
    internal static class HostBaselineProject
    {
        internal const string ProjectId = "FirepowerLoop";

        internal static string ResolveDirectory(string record, BuildTarget target)
        {
            string native = HostBaselineCommand.Resolve(record, ProjectId, target);
            if (!string.Equals(Path.GetFileName(native), "baseline.json", StringComparison.Ordinal))
                throw new BuildFailedException("请指定宿主归档中的 baseline.json 或它的统一记录文件。");
            return Path.GetDirectoryName(native);
        }

        internal static void RegisterInstalledHost(BuildTarget target)
        {
            if (CiBuildSession.Current != null)
                return;
            string client = Directory.GetParent(Application.dataPath).FullName;
            string repo = Directory.GetParent(client).FullName;
            string destination = Path.Combine(repo, "Backup", "Artifacts", "UnityTools", "InstalledHosts",
                target.ToString(), Guid.NewGuid().ToString("N"));
            BuildState state = BuildStateStore.Load(out string warning);
            if (!string.IsNullOrEmpty(warning)) throw new BuildFailedException(warning);
            HostBaselineArchive.Create(destination, target == BuildTarget.Android ? "Android" : "PC",
                CiBuildEntry.ReadCommit(repo), Application.unityVersion, AndroidBootBuildGuard.ComputeFingerprint(client),
                JsonConvert.SerializeObject(state), Path.Combine(client, "Assets", "HybridCLRGenerate"), allowComplete: true);
            string record = HostBaselineCommand.WriteReference(Path.Combine(destination, "baseline.json"), ProjectId, target);
            UnityEngine.Debug.Log("[安装包记录] " + record);
        }

        internal static void Build(string record, BuildTarget target, bool publish)
        {
            if (!Application.isBatchMode || !string.Equals(Path.GetFileName(record), "baseline.json", StringComparison.Ordinal))
                throw new BuildFailedException("请在批处理模式指定宿主归档中的 baseline.json。");
            string client = Directory.GetParent(Application.dataPath).FullName;
            string repo = Directory.GetParent(client).FullName;
            string id = "unity-tools-" + Guid.NewGuid().ToString("N");
            string output = Path.Combine(repo, "Backup", "Artifacts", "UnityTools", "ScriptHotUpdate", id);
            var request = new CiBuildRequest
            {
                SchemaVersion = 1, ProjectId = ProjectId, RequestId = id, ProjectPath = client,
                Platform = target == BuildTarget.Android ? "Android" : "PC", BuildType = "HostUpdate",
                ExpectedCommit = CiBuildEntry.ReadCommit(repo), BaselineDirectory = Path.GetDirectoryName(record),
                Publish = publish, ValidateOnly = false
            };
            if (publish)
                request.PublishDirectory = Path.Combine(output, "ResourceServer");
            string report = Path.Combine(output, "report.json");
            string result = CiBuildEntry.RunRequest(JsonConvert.SerializeObject(request,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }), report);
            if ((int?)JObject.Parse(result)["ExitCode"] != 0)
                throw new BuildFailedException("热更新脚本未完成，详见：" + report);
            UnityEngine.Debug.Log("[热更新] " + report);
        }
    }
}
