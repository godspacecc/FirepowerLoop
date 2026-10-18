#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>Jenkins四路由正式入口；只显式命令执行，不注册自动构建或平台切换回调。</summary>
    public static class CiBuildEntry
    {
        /// <summary>Unity -executeMethod Game.Editor.CiBuildEntry.Execute；必须提供请求和报告文件绝对路径。</summary>
        public static void Execute()
        {
            int code = 1;
            try
            {
                string request = Argument("-firepowerRequest"), report = Argument("-firepowerReport");
                if (!Path.IsPathRooted(request ?? "")) throw new ArgumentException("缺少绝对请求路径");
                code = (int)JObject.Parse(RunRequest(File.ReadAllText(request), report))["ExitCode"];
            }
            catch (Exception error) { UnityEngine.Debug.LogError("[FirepowerLoop CI] " + error); }
            finally { if (Application.isBatchMode) EditorApplication.Exit(code); }
        }

        /// <summary>校验模式不调用菜单；真实构建要求批处理且已用-buildTarget选对平台。</summary>
        public static string RunRequest(string json, string reportPath)
        {
            string client = Directory.GetParent(Application.dataPath).FullName, commit = ReadCommit(Directory.GetParent(client).FullName);
            return CiBuildRunner.RunWithPreflight(json, client, commit, reportPath, request =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || BuildUtility.HasActiveWorkflow || AndroidBuildUtility.HasActiveWorkflow)
                    throw new InvalidOperationException("Unity工作区正在编译、运行或构建");
                if (!request.ValidateOnly) ValidateEnvironment(Application.isBatchMode, EditorUserBuildSettings.activeBuildTarget.ToString(), request.Platform, false);
                if (request.BuildType == "HostUpdate") HostBaselineArchive.Validate(request.BaselineDirectory, request.Platform, Application.unityVersion, AndroidBootBuildGuard.ComputeFingerprint(client));
            }, (route, baseline) =>
            {
                var request = CiBuildRequest.Parse(json);
                using (var session = new CiBuildSession(request, commit))
                {
                    try { ResolveRoute(route).Invoke(null, null); }
                    catch (TargetInvocationException error) { throw error.InnerException; }
                    if (string.IsNullOrEmpty(session.ArtifactPath)) throw new InvalidOperationException("构建入口退出但未交付本次产物");
                    return session.ArtifactPath;
                }
            }, CiBuildPublisher.Publish);
        }

        public static void ValidateEnvironment(bool batch, string activeTarget, string platform, bool busy)
        {
            if (!batch || busy || activeTarget != (platform == "PC" ? "StandaloneWindows64" : "Android"))
                throw new InvalidOperationException("CI真实构建必须使用空闲的独立批处理Unity及明确-buildTarget");
        }
        public static MethodInfo ResolveRoute(string route)
        {
            switch (route)
            {
                case "PcFirst": return typeof(BuildUtility).GetMethod(nameof(BuildUtility.BuildSmallHostBaseCodeAndResourcesBatch));
                case "PcHost": return typeof(BuildUtility).GetMethod(nameof(BuildUtility.BuildHostCodeAndResourcesBatch));
                case "AndroidFirst": return typeof(AndroidBuildUtility).GetMethod(nameof(AndroidBuildUtility.BuildSmallHostBaseCodeAndResourcesBatch));
                case "AndroidHost": return typeof(AndroidBuildUtility).GetMethod(nameof(AndroidBuildUtility.BuildHostCodeAndResourcesBatch));
                default: throw new ArgumentException("未知CI构建路由");
            }
        }
        private static string Argument(string name) { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        private static string ReadCommit(string repo)
        {
            using (var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = repo, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                if (process == null) throw new InvalidOperationException("无法启动git读取固定Commit");
                string value = process.StandardOutput.ReadToEnd().Trim(), error = process.StandardError.ReadToEnd(); process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("无法读取Commit：" + error); return value;
            }
        }
    }
}
#endif
