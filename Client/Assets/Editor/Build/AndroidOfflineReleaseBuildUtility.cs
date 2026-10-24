#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor.Analytics;
using UnityEditor.Android;

namespace Game.Editor
{
    /// <summary>纯离线首发的构建作用域、编译标记与生成清单处理；不拥有账号、联机或玩家存档。</summary>
    public static class AndroidOfflineReleaseBuildUtility
    {
        /// <summary>仅通过本次 Player 参数写入稳定 Boot，热更程序集从 Boot 属性读取。</summary>
        public const string ScriptingDefine = "FIREPOWERLOOP_OFFLINE_SINGLE_PLAYER";
        /// <summary>实际 APK 的显式模式标识，和 INTERNET 缺失一起验证。</summary>
        public const string ProfileMetadata = "com.firepowerloop.release-profile";
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
        private const string ToolsNamespace = "http://schemas.android.com/tools";
        private static bool offlineBuildActive;

        /// <summary>联网包统一使用 Host 启动；仅显式离线首发携带离线标记并要求完整内置资源。</summary>
        public static string[] PlayerDefines(bool builtin, bool offline)
        {
            if (offline && !builtin) throw new InvalidOperationException("纯离线首发必须使用完整内置资源。");
            if (offline) return new[] { AndroidBuildUtility.BuiltinResourcesScriptingDefine, ScriptingDefine };
            return Array.Empty<string>();
        }

        /// <summary>构建作用域内关闭引擎统计初始化；正常与异常结束均恢复原编辑器值。</summary>
        public static IDisposable UseBuildScope(bool offline) => new OfflineBuildScope(offline);

        /// <summary>用高优先级 launcher 清单阻止依赖库重新合并网络与安装更新权限，并声明产物模式。</summary>
        public static string PrepareOfflineManifest(string content)
        {
            var document = new XmlDocument(); document.LoadXml(content);
            XmlElement root = document.DocumentElement;
            if (root == null || root.Name != "manifest") throw new InvalidOperationException("Android 清单根节点无效。");
            root.SetAttribute("xmlns:tools", ToolsNamespace);
            XmlElement application = root.SelectSingleNode("application") as XmlElement;
            if (application == null) throw new InvalidOperationException("Android 清单缺少 application，拒绝生成离线标记。");
            foreach (string tag in new[] { "uses-permission", "uses-permission-sdk-23" })
            foreach (string permission in new[] { "android.permission.INTERNET", "android.permission.REQUEST_INSTALL_PACKAGES" })
            {
                foreach (XmlNode node in root.SelectNodes(tag))
                    if ((node as XmlElement)?.GetAttribute("name", AndroidNamespace) == permission) root.RemoveChild(node);
                XmlElement removal = document.CreateElement(tag);
                Attribute(removal, "android", "name", AndroidNamespace, permission);
                Attribute(removal, "tools", "node", ToolsNamespace, "remove");
                root.InsertBefore(removal, application);
            }
            foreach (XmlNode node in application.SelectNodes("meta-data"))
                if ((node as XmlElement)?.GetAttribute("name", AndroidNamespace) == ProfileMetadata) application.RemoveChild(node);
            XmlElement profile = document.CreateElement("meta-data");
            Attribute(profile, "android", "name", AndroidNamespace, ProfileMetadata);
            Attribute(profile, "android", "value", AndroidNamespace, "OfflineSinglePlayer");
            application.AppendChild(profile);
            return document.OuterXml;
        }

        /// <summary>读取 aapt 的单个 meta-data 块，不能把其他节点的 value 误当作发布模式。</summary>
        internal static string ReadManifestProfile(string manifestTree)
        {
            foreach (string block in Regex.Split(manifestTree ?? "", @"(?m)^\s*E: "))
                if (block.StartsWith("meta-data", StringComparison.Ordinal) && Regex.IsMatch(block,
                    @"android:name\([^)]*\)=""com\.firepowerloop\.release-profile"""))
                    return Regex.Match(block, @"android:value\([^)]*\)=""([^""]*)""").Groups[1].Value;
            return "";
        }

        /// <summary>只接受 Unity 已生成的 launcher 路径；不回写工程 Plugins 或生成源配置。</summary>
        internal static void ProcessGeneratedProject(string path)
        {
            if (!offlineBuildActive) return;
            string project = Path.GetFullPath(path);
            if (string.Equals(Path.GetFileName(project), "unityLibrary", StringComparison.OrdinalIgnoreCase))
                project = Path.GetDirectoryName(project);
            string launcher = Path.Combine(project, "launcher/src/main/AndroidManifest.xml");
            if (!File.Exists(launcher)) throw new InvalidOperationException("缺少生成的 launcher 主清单，无法保证离线权限合并。");
            File.WriteAllText(launcher, PrepareOfflineManifest(File.ReadAllText(launcher)));
        }

        /// <summary>显式设置命名空间前缀，保持生成清单可由 Android 合并工具识别。</summary>
        private static void Attribute(XmlElement element, string prefix, string name, string ns, string value)
        {
            XmlAttribute attribute = element.OwnerDocument.CreateAttribute(prefix, name, ns);
            attribute.Value = value; element.Attributes.Append(attribute);
        }

        /// <summary>保留原统计设置和嵌套作用域状态，不把离线构建选项留给后续内部包。</summary>
        private sealed class OfflineBuildScope : IDisposable
        {
            private readonly bool previous = offlineBuildActive;
            private readonly bool enabled = AnalyticsSettings.enabled;
            private readonly bool startup = AnalyticsSettings.initializeOnStartup;
            private readonly bool core = AnalyticsSettings.packageRequiringCoreStatsPresent;
            private readonly bool offline;
            private bool disposed;
            public OfflineBuildScope(bool offline)
            {
                this.offline = offline;
                try
                {
                    offlineBuildActive = offline;
                    if (offline)
                    {
                        AnalyticsSettings.enabled = false;
                        AnalyticsSettings.initializeOnStartup = false;
                        AnalyticsSettings.packageRequiringCoreStatsPresent = false;
                    }
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    if (offline)
                    {
                        AnalyticsSettings.enabled = enabled;
                        AnalyticsSettings.initializeOnStartup = startup;
                        AnalyticsSettings.packageRequiringCoreStatsPresent = core;
                    }
                }
                finally { offlineBuildActive = previous; }
            }
        }
    }

    /// <summary>在 Android 工程生成后应用当前离线作用域；其它打包与手工导出保持原清单。</summary>
    public sealed class OfflineSinglePlayerGradleProcessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => int.MaxValue;
        public void OnPostGenerateGradleAndroidProject(string path) => AndroidOfflineReleaseBuildUtility.ProcessGeneratedProject(path);
    }
}
#endif
