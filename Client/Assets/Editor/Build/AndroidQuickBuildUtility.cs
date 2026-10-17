#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>同一轮首包已经准备好的输入快照；发布失败仍可用于只重建 APK。</summary>
    [Serializable]
    public sealed class AndroidQuickBuildInputs
    {
        /// <summary>完整远端资源版本，快速入口不得另建版本。</summary>
        public string Version;
        /// <summary>本项目 Android 完整远端资源目录。</summary>
        public string PackageDirectory;
        /// <summary>这轮生成对应的 Boot 源输入指纹。</summary>
        public string BootFingerprint;
        /// <summary>运行时代码、AOT、生成文件、资源及内置清单的内容指纹。</summary>
        public string Fingerprint;
        /// <summary>归一化构建设置指纹；仅允许 APK 应用版本变化。</summary>
        public string SettingsFingerprint;
        /// <summary>生成此快照时的 Unity 版本。</summary>
        public string UnityVersion;
        /// <summary>从资源内补充元数据复制的不可变 AOT 目录，不受 Player 构建清空现场目录影响。</summary>
        public string AotDirectory;
    }

    /// <summary>快速 APK 的只读复用校验与快照保存；不调用任何构建或发布入口。</summary>
    public static class AndroidQuickBuildUtility
    {
        private const string RebuildHint = "请执行 Android 首包完整打包，重新生成匹配的 AOT 和资源。";
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string RecordPath => Path.Combine(ProjectRoot, "Library", "FirepowerLoopBuild", "AndroidQuickInputs.json");

        /// <summary>完整首包资源验证成功后立即保存，最终 APK 或发布失败也不会丢失这轮输入。</summary>
        public static AndroidQuickBuildInputs Capture(string version, string packageDirectory, string compilationFingerprint)
        {
            if (compilationFingerprint != CaptureCompilationFingerprint())
                throw new InvalidOperationException("编译 Game DLL 后运行时代码或编译配置发生变化，不能保存复用基线。" + RebuildHint);
            ValidateCompiledSourceTimes();
            string aot = PreserveAotMetadata(version, HybridCLRBuildUtility.GetRuntimeListPath(), HybridCLRBuildUtility.GetGameDllDirectory(),
                Path.Combine(ProjectRoot, "Library", "FirepowerLoopBuild", "AndroidQuickAot"));
            var record = ReadCurrent(version, packageDirectory, aot);
            Directory.CreateDirectory(Path.GetDirectoryName(RecordPath));
            string pending = RecordPath + ".pending";
            try
            {
                File.WriteAllText(pending, JsonUtility.ToJson(record, true));
                if (File.Exists(RecordPath)) File.Replace(pending, RecordPath, null);
                else File.Move(pending, RecordPath);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
            return record;
        }

        /// <summary>优先使用内容快照；旧流程遗留产物必须由最近成功的 Android Player 报告及包内清单佐证。</summary>
        public static AndroidQuickBuildInputs LoadAndValidate()
        {
            if (File.Exists(RecordPath))
            {
                AndroidQuickBuildInputs saved = JsonUtility.FromJson<AndroidQuickBuildInputs>(File.ReadAllText(RecordPath));
                if (saved == null) throw new InvalidOperationException("快速打包快照损坏。" + RebuildHint);
                Validate(saved);
                return saved;
            }

            var report = BuildReport.GetLatestReport();
            if (report == null || report.summary.result != BuildResult.Succeeded || report.summary.platform != BuildTarget.Android)
                throw new InvalidOperationException("没有可验证的成功 Android Player 报告。" + RebuildHint);
            string apk = Path.GetFullPath(report.summary.outputPath);
            string allowed = Path.Combine(ProjectRoot, "Build", "Android") + Path.DirectorySeparatorChar;
            if (!apk.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !File.Exists(apk) || Path.GetExtension(apk) != ".apk")
                throw new InvalidOperationException("最近 Player 不是本项目保留的最终 APK。" + RebuildHint);
            string builtin = YooAssetBuildUtility.GetStreamingAssetsDirectory();
            string version = File.ReadAllText(Path.Combine(builtin, "DefaultPackage.version")).Trim();
            string package = Path.Combine(YooAssetBuildUtility.GetPackageRoot(BuildTarget.Android), version);
            // 旧流程未保存内容快照时，保守拒绝在 Player 开始后编辑过的运行时输入和构建设置。
            // Unity 2022 的报告时间字段携带 UTC 钟面值；不再次按本机时区换算。
            DateTime startedUtc = DateTime.SpecifyKind(report.summary.buildStartedAt, DateTimeKind.Utc);
            ValidateRecoveryInputTimes(RuntimeInputs().Concat(GeneratedInputs()).ToArray(), startedUtc);
            // Player 自身会在启动构建时写入递增后的版本，允许该次构建保存设置，但禁止构建结束后再改设置。
            ValidateRecoveryInputTimes(new[] { Path.Combine(ProjectRoot, "ProjectSettings", "ProjectSettings.asset") },
                DateTime.SpecifyKind(report.summary.buildEndedAt, DateTimeKind.Utc));
            using (var archive = ZipFile.OpenRead(apk))
            {
                foreach (string file in Directory.GetFiles(builtin).Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                {
                    var entry = archive.GetEntry("assets/yoo/DefaultPackage/" + Path.GetFileName(file));
                    if (entry == null) throw new InvalidOperationException("保留 APK 与当前内置清单不匹配。" + RebuildHint);
                    using (var input = entry.Open())
                    using (var buffer = new MemoryStream())
                    {
                        input.CopyTo(buffer);
                        if (!buffer.ToArray().SequenceEqual(File.ReadAllBytes(file)))
                            throw new InvalidOperationException("保留 APK 的清单内容已变化。" + RebuildHint);
                    }
                }
            }
            // 只在实际请求快速打包时建立恢复快照；编辑器重载和测试不会自动建立它。
            ValidateCompiledSourceTimes();
            return Capture(version, package, CaptureCompilationFingerprint());
        }

        /// <summary>在本轮 Game DLL 编译前登记手写输入，正常生成的 HybridCLR 文件单独在资源完成后登记。</summary>
        public static string CaptureCompilationFingerprint() => FingerprintFiles(RuntimeInputs()
            .Where(path => !path.Replace('\\', '/').Contains("/HybridCLRGenerate/")).ToArray()) + "|" + SettingsFingerprint();

        /// <summary>旧构建恢复及快照建立前验证源码不晚于其 DLL；防止编译后、最终 Player 前改码被误登记。</summary>
        private static void ValidateCompiledSourceTimes()
        {
            string directory = Path.Combine(ProjectRoot, "HybridCLRData", "HotUpdateDlls", "Android");
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Where(assembly => !assembly.name.Contains("Test") && (assembly.flags & AssemblyFlags.EditorAssembly) == 0))
            {
                // 热更 Game 使用资源内 DLL 的编译输出；AOT 程序集使用实际 Player 编译缓存，
                // HybridCLR HotUpdateDlls 中的第三方程序集可能是旧缓存，并非最终 Player 使用的版本。
                string dll = assembly.name == "Game" ? Path.Combine(directory, "Game.dll") :
                    Path.Combine(ProjectRoot, "Library", "Bee", "PlayerScriptAssemblies", assembly.name + ".dll");
                if (!File.Exists(dll)) throw new FileNotFoundException("缺少对应的 Android 编译程序集。", dll);
                ValidateRecoveryInputTimes(assembly.sourceFiles.Where(path => !path.Replace('\\', '/').Contains("/HybridCLRGenerate/")).ToArray(),
                    File.GetLastWriteTimeUtc(dll));
            }
            // 编译配置和插件也必须早于本轮热更程序集；新增快照另外使用内容指纹覆盖整个耗时窗口。
            string game = Path.Combine(directory, "Game.dll");
            ValidateRecoveryInputTimes(RuntimeInputs().Where(path => !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !path.Replace('\\', '/').Contains("/HybridCLRGenerate/")).ToArray(), File.GetLastWriteTimeUtc(game));
        }

        /// <summary>在构建前及发布前复核输入，禁止将长流程中修改后的源码登记成旧 AOT 基线。</summary>
        public static void Validate(AndroidQuickBuildInputs saved)
        {
            var current = ReadCurrent(saved.Version, saved.PackageDirectory, saved.AotDirectory);
            if (saved.UnityVersion != current.UnityVersion || saved.Fingerprint != current.Fingerprint ||
                saved.BootFingerprint != current.BootFingerprint || saved.SettingsFingerprint != current.SettingsFingerprint)
                throw new InvalidOperationException("AOT、代码、资源或构建设置已变化，不能复用本次结果。" + RebuildHint);
        }

        /// <summary>校验同平台清单、完整资源及 DLL 对应关系后计算内容快照。</summary>
        private static AndroidQuickBuildInputs ReadCurrent(string version, string packageDirectory, string aotDirectory)
        {
            string expected = Path.GetFullPath(Path.Combine(YooAssetBuildUtility.GetPackageRoot(BuildTarget.Android), version ?? ""));
            if (string.IsNullOrEmpty(version) || version.IndexOfAny(new[] { '/', '\\', '.' }) >= 0 ||
                !string.Equals(Path.GetFullPath(packageDirectory), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("快速打包资源必须来自本项目 Android 版本目录。");
            if (!YooAssetBuildUtility.ValidateManifestOnlyBuiltinPackage(version) || !YooAssetBuildUtility.ValidateBuildResult(expected, version))
                throw new InvalidOperationException("首包资源或内置清单无效。" + RebuildHint);
            string builtin = YooAssetBuildUtility.GetStreamingAssetsDirectory();
            foreach (string extension in new[] { ".bytes", ".hash" })
                RequireSameFile(Path.Combine(builtin, "DefaultPackage_" + version + extension), Path.Combine(expected, "DefaultPackage_" + version + extension));
            HybridCLRBuildUtility.ValidateGeneratedDlls();
            string aot = aotDirectory;
            if (string.IsNullOrEmpty(aot) || !Directory.Exists(aot))
                throw new InvalidOperationException("不可变 AOT 基线缺失。" + RebuildHint);
            foreach (string name in File.ReadAllLines(HybridCLRBuildUtility.GetRuntimeListPath()).Where(name => !string.IsNullOrWhiteSpace(name)))
                RequireSameFile(Path.Combine(aot, name.Trim()), Path.Combine(HybridCLRBuildUtility.GetGameDllDirectory(), name.Trim() + ".bytes"));
            RequireSameFile(Path.Combine(ProjectRoot, "HybridCLRData", "HotUpdateDlls", "Android", "Game.dll"),
                Path.Combine(HybridCLRBuildUtility.GetGameDllDirectory(), "Game.dll.bytes"));
            string generated = Path.GetFullPath(HybridCLR.Editor.SettingsUtil.GeneratedCppDir);
            if (!File.Exists(Path.Combine(generated, "MethodBridge.cpp")))
                throw new InvalidOperationException("缺少 HybridCLR 桥接代码。" + RebuildHint);
            var files = RuntimeInputs().Concat(Directory.GetFiles(aot, "*.dll"))
                .Concat(GeneratedInputs())
                .Concat(Directory.GetFiles(HybridCLRBuildUtility.GetGameDllDirectory(), "*.bytes"))
                .Concat(Directory.GetFiles(Path.Combine(ProjectRoot, "Assets", "StreamingAssets"), "*", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(expected, "*", SearchOption.AllDirectories));
            return new AndroidQuickBuildInputs
            {
                Version = version, PackageDirectory = expected, UnityVersion = Application.unityVersion, AotDirectory = aot,
                BootFingerprint = AndroidBootBuildGuard.ComputeFingerprint(ProjectRoot), Fingerprint = FingerprintFiles(files.ToArray()),
                SettingsFingerprint = SettingsFingerprint()
            };
        }

        /// <summary>收集 Player 源码与 Boot 场景依赖，排除编辑器及测试源码，防止改代码后复用旧热更 DLL。</summary>
        private static string[] RuntimeInputs() => CompilationPipeline.GetAssemblies(AssembliesType.Player)
            .Where(assembly => !assembly.name.Contains("Test") && (assembly.flags & AssemblyFlags.EditorAssembly) == 0)
            .SelectMany(assembly => assembly.sourceFiles)
            .Concat(Directory.GetFiles(Path.Combine(ProjectRoot, "Assets", "Boot"), "*", SearchOption.AllDirectories))
            .Concat(AssetDatabase.GetDependencies("Assets/Game/Scenes/Boot.unity", true).Where(File.Exists))
            .Concat(Directory.GetFiles(Path.Combine(ProjectRoot, "Assets"), "*", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/Editor/") && !path.Replace('\\', '/').Contains("/Tests/") &&
                    new[] { ".asmdef", ".asmref", ".rsp", ".dll" }.Contains(Path.GetExtension(path).ToLowerInvariant())))
            .Concat(Directory.GetFiles(Path.Combine(ProjectRoot, "Assets"), "*HybridCLR*.asset", SearchOption.AllDirectories))
            .Concat(new[] { Path.Combine(ProjectRoot, "Packages", "manifest.json"), Path.Combine(ProjectRoot, "Packages", "packages-lock.json") })
            .SelectMany(path => File.Exists(path + ".meta") ? new[] { path, path + ".meta" } : new[] { path })
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        /// <summary>桥接与链接清单也是编译输入，必须随快照校验并参与旧产物恢复门禁。</summary>
        private static string[] GeneratedInputs() => Directory.GetFiles(Path.GetFullPath(HybridCLR.Editor.SettingsUtil.GeneratedCppDir), "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(ProjectRoot, "Assets", "HybridCLRGenerate"), "*", SearchOption.AllDirectories)).ToArray();

        /// <summary>保留资源实际携带的补充元数据，而非最终 Player 会重新导出的可变裁剪目录。</summary>
        public static string PreserveAotMetadata(string version, string listPath, string dlls, string cacheRoot)
        {
            if (string.IsNullOrEmpty(version) || version.IndexOfAny(new[] { '/', '\\', '.' }) >= 0)
                throw new InvalidOperationException("AOT 基线资源版本无效。");
            string target = Path.Combine(cacheRoot, version);
            string[] names = File.ReadAllLines(listPath).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToArray();
            if (names.Length == 0 || names.Any(name => Path.GetFileName(name) != name || !name.EndsWith(".dll", StringComparison.Ordinal)))
                throw new InvalidOperationException("AOT 基线清单无效。");
            if (Directory.Exists(target))
            {
                foreach (string name in names) RequireSameFile(Path.Combine(target, name), Path.Combine(dlls, name + ".bytes"));
                return target;
            }
            string pending = target + "." + Guid.NewGuid().ToString("N") + ".pending";
            Directory.CreateDirectory(pending);
            try
            {
                foreach (string name in names) File.Copy(Path.Combine(dlls, name + ".bytes"), Path.Combine(pending, name));
                Directory.Move(pending, target);
            }
            finally { if (Directory.Exists(pending)) Directory.Delete(pending, true); }
            return target;
        }

        /// <summary>应用版本与版本码在每次 APK 重建时变化，其余 Player 设置必须与准备阶段一致。</summary>
        private static string SettingsFingerprint()
        {
            string settings = File.ReadAllText(Path.Combine(ProjectRoot, "ProjectSettings", "ProjectSettings.asset"));
            settings = System.Text.RegularExpressions.Regex.Replace(settings, @"(?m)^  (bundleVersion|AndroidBundleVersionCode):.*\r?\n", "");
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(settings)));
        }

        /// <summary>按路径与内容计算哈希，检测同长度修改、文件增删和重命名；不使用修改时间判等。</summary>
        public static string FingerprintFiles(string[] files)
        {
            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
            using (var hash = SHA256.Create())
            {
                foreach (string file in files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal))
                {
                    if (!File.Exists(file)) throw new FileNotFoundException("复用输入缺失。", file);
                    writer.Write(file);
                    using (var input = File.OpenRead(file)) writer.Write(hash.ComputeHash(input));
                }
                writer.Flush(); buffer.Position = 0;
                return Convert.ToBase64String(hash.ComputeHash(buffer));
            }
        }

        /// <summary>旧构建没有内容快照时的保守恢复门禁；不得用于替代后续构建的内容指纹。</summary>
        public static void ValidateRecoveryInputTimes(string[] files, DateTime playerStartedUtc)
        {
            foreach (string file in files)
            {
                if (!File.Exists(file)) throw new FileNotFoundException("恢复输入缺失。", file);
                if (File.GetLastWriteTimeUtc(file) > playerStartedUtc)
                    throw new InvalidOperationException("上次 APK 开始后运行时输入已修改：" + file + "。" + RebuildHint);
            }
        }

        /// <summary>必须字节一致，禁止混用其他平台或后续改写过的 AOT 与资源清单。</summary>
        private static void RequireSameFile(string left, string right)
        {
            if (!File.Exists(left) || !File.Exists(right) || !File.ReadAllBytes(left).SequenceEqual(File.ReadAllBytes(right)))
                throw new InvalidOperationException("复用文件不匹配：" + left + " / " + right + "。" + RebuildHint);
        }
    }
}
#endif
