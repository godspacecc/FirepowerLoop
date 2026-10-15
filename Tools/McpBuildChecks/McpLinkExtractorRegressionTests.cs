using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using com.IvanMurzak.Unity.MCP.Editor.Utils;

// 独立进程中的 Unity 边界替身：构建期间异步包管理服务不可用，已加载包仍可读取。
namespace UnityEditor.PackageManager
{
    public enum StatusCode { Success, Failure }
    public class PackageCollection : List<PackageInfo> { }
    public class PackageInfo
    {
        public string resolvedPath;
        public static PackageInfo[] Registered = Array.Empty<PackageInfo>();
        public static PackageInfo[] GetAllRegisteredPackages() => Registered;
    }
    public class ListRequest
    {
        public bool IsCompleted => false;
        public StatusCode Status => StatusCode.Failure;
        public PackageCollection Result => new PackageCollection();
        public RequestError Error => new RequestError();
    }
    public class RequestError { public string message => "异步包管理服务不可用"; }
    public static class Client
    {
        public static ListRequest List()
        {
            throw new InvalidOperationException("构建预处理不得启动异步列包请求，否则可能永久阻塞主线程。");
        }
    }
}
namespace UnityEditor.Build.Reporting { public class BuildReport { } }
namespace UnityEditor.Build
{
    public interface IPreprocessBuildWithReport
    {
        int callbackOrder { get; }
        void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report);
    }
    public interface IPostprocessBuildWithReport
    {
        int callbackOrder { get; }
        void OnPostprocessBuild(UnityEditor.Build.Reporting.BuildReport report);
    }
}
namespace UnityEditor { public static class AssetDatabase { public static void Refresh() { } } }
namespace UnityEngine
{
    public static class Application { public static string dataPath; }
    public static class Debug
    {
        public static void Log(object value) { }
        public static void LogWarning(object value) { }
        public static void LogError(object value) { throw new InvalidOperationException(value.ToString()); }
    }
}

/// <summary>直接执行插件源码，验证异步服务不可用时仍能合并并清理真实 link.xml 文件。</summary>
public static class McpLinkExtractorRegressionTests
{
    public static int Run()
    {
        var tests = new Action[] { PreprocessMergesRegisteredPackagesWithoutAsyncService, EmptyPackagesProduceNoMergedFile, PostprocessCleansMergedArtifacts };
        int failed = 0;
        foreach (var test in tests)
        {
            string root = Path.Combine(Path.GetTempPath(), "FirepowerLoop-McpBuild-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            UnityEngine.Application.dataPath = Path.Combine(root, "Assets");
            Directory.CreateDirectory(UnityEngine.Application.dataPath);
            UnityEditor.PackageManager.PackageInfo.Registered = Array.Empty<UnityEditor.PackageManager.PackageInfo>();
            try { test(); Console.WriteLine("PASS " + test.Method.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Method.Name + ": " + error.Message); }
            finally { Directory.Delete(root, true); }
        }
        Console.WriteLine("McpLinkExtractorRegressionTests: " + (tests.Length - failed) + "/" + tests.Length + " passed");
        return failed;
    }

    private static string MergedPath => Path.Combine(UnityEngine.Application.dataPath, "packages-merged-link", "link.xml");

    private static UnityEditor.PackageManager.PackageInfo Package(string name, string xml)
    {
        string root = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), name, "nested");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "link.xml"), xml);
        return new UnityEditor.PackageManager.PackageInfo { resolvedPath = Path.GetDirectoryName(root) };
    }

    private static void PreprocessMergesRegisteredPackagesWithoutAsyncService()
    {
        UnityEditor.PackageManager.PackageInfo.Registered = new[]
        {
            Package("one", "<linker><assembly fullname=\"One\" preserve=\"all\" /></linker>"),
            Package("two", "<linker><assembly fullname=\"One\" preserve=\"all\" /><assembly fullname=\"Two\" preserve=\"all\" /></linker>"),
            new UnityEditor.PackageManager.PackageInfo { resolvedPath = "" }
        };
        new LinkExtractor().OnPreprocessBuild(null);
        var assemblies = XDocument.Load(MergedPath).Root.Elements("assembly").Select(element => (string)element.Attribute("fullname")).ToArray();
        if (assemblies.Length != 2 || !assemblies.Contains("One") || !assemblies.Contains("Two"))
            throw new Exception("合并结果必须保留两个包的程序集，并去掉重复项。");
    }

    private static void EmptyPackagesProduceNoMergedFile()
    {
        new LinkExtractor().OnPreprocessBuild(null);
        if (File.Exists(MergedPath)) throw new Exception("没有已注册包时不应生成合并文件。");
    }

    private static void PostprocessCleansMergedArtifacts()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MergedPath));
        File.WriteAllText(MergedPath, "<linker />");
        string meta = Path.GetDirectoryName(MergedPath) + ".meta";
        File.WriteAllText(meta, "temporary-generated-meta");
        new LinkExtractor().OnPostprocessBuild(null);
        if (Directory.Exists(Path.GetDirectoryName(MergedPath)) || File.Exists(meta))
            throw new Exception("构建后必须清理插件生成的临时合并目录及其 meta。");
    }
}
