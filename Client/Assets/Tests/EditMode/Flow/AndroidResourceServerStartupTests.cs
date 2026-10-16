using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>通过菜单实际使用的脚本启动临时发布包，验证同一端口提供资源、公告与整包更新。</summary>
    public sealed class AndroidResourceServerStartupTests
    {
        [Test]
        public void PublishedServerProvidesResourcesNoticeAndApkOnOnePort()
        {
            string root = Path.Combine(Path.GetTempPath(), "FirepowerLoopServerStartup", Guid.NewGuid().ToString("N"));
            Process process = null;
            try
            {
                string resources = Path.Combine(root, "ResourceFiles");
                string updates = Path.Combine(root, "AppUpdates", "Android");
                Directory.CreateDirectory(resources);
                Directory.CreateDirectory(updates);
                Directory.CreateDirectory(Path.Combine(root, "Notices"));
                string workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
                File.Copy(Path.Combine(workspace, "Server", "ResourceServer", "resource_server.ps1"), Path.Combine(root, "resource_server.ps1"));
                File.WriteAllText(Path.Combine(resources, "DefaultPackage.version"), "test");
                File.WriteAllText(Path.Combine(resources, "DefaultPackage_test.bytes"), "catalog");
                File.WriteAllText(Path.Combine(resources, "DefaultPackage_test.hash"), "hash");
                File.WriteAllText(Path.Combine(resources, "sample.bundle"), "bundle");
                const string descriptor = "{\"versionName\":\"0.1.15\"}";
                File.WriteAllText(Path.Combine(updates, "app-update.json"), descriptor, new UTF8Encoding(false));
                File.WriteAllBytes(Path.Combine(updates, "sample.apk"), new byte[] { 1, 2, 3 });
                File.WriteAllText(Path.Combine(root, "Notices", "resource-update-notices.json"), "{\"notices\":[]}");
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                string script = Path.Combine(Application.dataPath, "Editor", "YooAsset", "LocalServer", "start_yooasset_local_server.ps1");
                process = Process.Start(new ProcessStartInfo("powershell.exe",
                    $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Port {port} -Root \"{resources}\" -ListenOnLan")
                { UseShellExecute = false, CreateNoWindow = true });
                string url = $"http://127.0.0.1:{port}";
                var clock = Stopwatch.StartNew();
                while (true)
                {
                    try { Assert.That(Read(url + "/DefaultPackage.version"), Is.EqualTo("test")); break; }
                    catch (WebException) { if (clock.ElapsedMilliseconds > 8000) throw; Thread.Sleep(100); }
                }
                Assert.That(Read(url + "/app-update.json"), Is.EqualTo(descriptor), "菜单启动的服务必须提供 APK 描述");
                Assert.That(Read(url + "/resource-update-notices.json"), Does.Contain("notices"));
                using (var client = new WebClient())
                    Assert.That(client.DownloadData(url + "/app-updates/android/sample.apk"), Is.EqualTo(new byte[] { 1, 2, 3 }));
            }
            finally
            {
                if (process != null) { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } process.Dispose(); }
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static string Read(string url)
        {
            var request = WebRequest.CreateHttp(url);
            request.Timeout = 1000;
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream())) return reader.ReadToEnd();
        }
    }
}
