#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Editor
{
    /// <summary>FirepowerLoop四类CI请求；默认只校验且不发布，不允许未知字段静默改变工作流。</summary>
    [Serializable]
    public sealed class CiBuildRequest
    {
        public int SchemaVersion;
        public string ProjectId;
        public string RequestId;
        public string ProjectPath;
        public string Platform;
        public string BuildType;
        public string ExpectedCommit;
        public string BaselineDirectory;
        public string PublishDirectory;
        /// <summary>CI显式安装器编译器路径，避免读取开发者账户EditorPrefs。</summary>
        public string InstallerCompiler;
        public bool Publish;
        public bool ValidateOnly = true;

        public static CiBuildRequest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65536) throw new ArgumentException("CI请求为空或超过64KiB");
            var value = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            var fields = typeof(CiBuildRequest).GetFields(BindingFlags.Instance | BindingFlags.Public).ToDictionary(f => f.Name);
            foreach (var property in value.Properties())
            {
                if (!fields.TryGetValue(property.Name, out var field)) throw new ArgumentException("未知CI字段：" + property.Name);
                var expected = field.FieldType == typeof(bool) ? JTokenType.Boolean : field.FieldType == typeof(int) ? JTokenType.Integer : JTokenType.String;
                if (property.Value.Type != expected) throw new ArgumentException("CI字段类型错误：" + property.Name);
            }
            return value.ToObject<CiBuildRequest>();
        }

        public string Validate(string actualClient, string actualCommit)
        {
            if (SchemaVersion != 1 || ProjectId != "FirepowerLoop") throw new ArgumentException("CI版本或项目不符");
            if (!Regex.IsMatch(RequestId ?? "", @"\A[a-zA-Z0-9_-]{1,80}\z")) throw new ArgumentException("请求ID无效");
            if (!Path.IsPathRooted(ProjectPath ?? "") || !string.Equals(Path.GetFullPath(ProjectPath).TrimEnd('\\', '/'), Path.GetFullPath(actualClient).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("CI必须指向当前独立Client工作目录");
            if (!Regex.IsMatch(ExpectedCommit ?? "", @"\A[0-9a-fA-F]{40}\z") || !string.Equals(ExpectedCommit, actualCommit, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("当前Commit与固定请求不符");
            WorkspaceBuildLock.EnsureNoLinks(ProjectPath);
            if (Platform != "PC" && Platform != "Android") throw new ArgumentException("只支持PC或Android");
            if (BuildType != "FirstPackage" && BuildType != "HostUpdate") throw new ArgumentException("只支持首包或Host更新");
            if (BuildType == "HostUpdate")
            {
                if (!Path.IsPathRooted(BaselineDirectory ?? "") || !Directory.Exists(BaselineDirectory)) throw new ArgumentException("Host更新必须指定现有宿主基线目录");
                WorkspaceBuildLock.EnsureNoLinks(BaselineDirectory);
            }
            if (Publish)
            {
                if (!Path.IsPathRooted(PublishDirectory ?? "")) throw new ArgumentException("发布必须明确目标目录");
                CiBuildRunner.ValidateReportPath(actualClient, PublishDirectory);
                WorkspaceBuildLock.EnsureNoLinks(PublishDirectory);
            }
            if (!string.IsNullOrEmpty(InstallerCompiler))
            {
                if (!Path.IsPathRooted(InstallerCompiler)) throw new ArgumentException("安装器编译器必须使用绝对路径");
                WorkspaceBuildLock.EnsureNoLinks(InstallerCompiler);
            }
            return (Platform == "PC" ? "Pc" : "Android") + (BuildType == "FirstPackage" ? "First" : "Host");
        }
    }
}
#endif
