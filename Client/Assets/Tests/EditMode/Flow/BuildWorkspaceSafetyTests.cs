using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>工作区互斥必须覆盖异常清理，未取锁不能开始或删除固定临时产物。</summary>
    public sealed class BuildWorkspaceSafetyTests
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateSymbolicLink(string link, string target, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool RemoveDirectory(string path);
        private string client;
        [SetUp] public void Setup() { client = Path.Combine(Path.GetTempPath(), "FirepowerLock-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(client, "Temp")); }
        [TearDown] public void Cleanup()
        {
            string link = Path.Combine(client, "Temp/FirepowerLoop.Build.lock");
            Assert.That(Path.GetFullPath(client), Does.StartWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\FirepowerLock-"));
            if (Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) Assert.That(RemoveDirectory(link), Is.True);
            if (Directory.Exists(client)) Directory.Delete(client, true);
        }
        private void Run(Action action)
        {
            var type = Type.GetType("Game.Editor.WorkspaceBuildLock, Assembly-CSharp-Editor");
            var method = type.GetMethod("RunExclusive", BindingFlags.Static | BindingFlags.Public); Assert.That(method, Is.Not.Null);
            try { method.Invoke(null, new object[] { client, action }); } catch (TargetInvocationException error) { throw error.InnerException; }
        }
        [Test] public void LockFailureDoesNotRunWorkOrCleanup()
        {
            bool entered = false;
            using (var held = new FileStream(Path.Combine(client, "Temp/FirepowerLoop.Build.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<IOException>(() => Run(() => { entered = true; }));
            Assert.That(entered, Is.False);
        }
        [Test] public void ExceptionCleanupStillHoldsLockUntilItFinishes()
        {
            bool cleanupBlockedCompetitor = false;
            Assert.Throws<InvalidOperationException>(() => Run(() =>
            {
                try { throw new InvalidOperationException("simulated failure"); }
                finally
                {
                    cleanupBlockedCompetitor = Task.Run(() => { try { Run(() => { }); return false; } catch (IOException) { return true; } }).Result;
                }
            }));
            Assert.That(cleanupBlockedCompetitor, Is.True);
            Run(() => { });
        }
        [Test] public void LockPathLinkIsRejectedWithoutChangingItsTarget()
        {
            string target = Path.Combine(client, "keep.txt"), link = Path.Combine(client, "Temp/FirepowerLoop.Build.lock");
            File.WriteAllText(target, "must remain unchanged");
            if (!CreateSymbolicLink(link, target, 2) && !CreateSymbolicLink(link, target, 0))
            {
                // 文件符号链接权限不足时，实际建立同名NTFS目录链接，仍必须在打开前按重解析点拒绝。
                string directory = Path.Combine(client, "keep-directory"); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "keep.txt"), "must remain unchanged");
                string command = "New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + directory.Replace("'", "''") + "' | Out-Null";
                using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"" + command + "\"") { UseShellExecute = false, CreateNoWindow = true }))
                { process.WaitForExit(); Assert.That(process.ExitCode, Is.Zero); }
                TestContext.Progress.WriteLine("文件符号链接权限不足：实际覆盖同名NTFS Junction拒绝，不宣称文件symlink实测。");
            }
            var type = Type.GetType("Game.Editor.WorkspaceBuildLock, Assembly-CSharp-Editor");
            Assert.Throws<IOException>(() =>
            {
                IDisposable lease;
                try { lease = (IDisposable)type.GetMethod("Acquire").Invoke(null, new object[] { client }); }
                catch (TargetInvocationException error) { throw error.InnerException; }
                lease.Dispose();
            });
            Assert.That(File.ReadAllText(target), Is.EqualTo("must remain unchanged"));
        }
    }
}
