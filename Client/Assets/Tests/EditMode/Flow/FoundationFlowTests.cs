using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Boot;

namespace Game.Tests
{
    [TestFixture]
    public class FoundationFlowTests
    {
        private const string PcPackagingMenuPrefix = "Tools/打包/PC/";

        [Test]
        public void BootAssemblyDoesNotReferenceGame()
        {
            var bootAssembly = typeof(Launcher).Assembly;
            var referencedAssemblies = bootAssembly.GetReferencedAssemblies();

            foreach (var referenced in referencedAssemblies)
            {
                Assert.That(referenced.Name, Is.Not.EqualTo("Game"),
                    $"Boot assembly must not reference Game. Found: {referenced.Name}");
            }
        }

        [Test]
        public void GameAssemblyReferencesBootOnlyOneWay()
        {
            var gameReferences = typeof(Game).Assembly.GetReferencedAssemblies();
            Assert.That(gameReferences.Select(item => item.Name), Does.Contain("Boot"));

            var bootReferences = typeof(Launcher).Assembly.GetReferencedAssemblies();
            Assert.That(bootReferences.Select(item => item.Name), Does.Not.Contain("Game"));
        }

        [Test]
        public void BuildSettingsContainsOnlyBoot()
        {
            var enabledScenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToList();

            Assert.That(enabledScenes.Count, Is.EqualTo(1),
                $"Build Settings must enable exactly one scene, actual: {enabledScenes.Count}");
            Assert.That(enabledScenes[0].path, Is.EqualTo("Assets/Game/Scenes/Boot.unity"));
        }

        [Test]
        public void GameIsOnlyHotUpdateAssembly()
        {
            string settingsPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "ProjectSettings", "HybridCLRSettings.asset");

            Assert.That(File.Exists(settingsPath), Is.True,
                $"HybridCLRSettings.asset does not exist: {settingsPath}");

            string content = File.ReadAllText(settingsPath);
            Assert.That(content, Does.Contain("hotUpdateAssemblies"));
            Assert.That(content, Does.Contain("- Game"));

            int gameCount = 0;
            int index = 0;
            while ((index = content.IndexOf("- Game", index)) != -1)
            {
                gameCount++;
                index++;
            }

            Assert.That(gameCount, Is.EqualTo(1), "Game must be the only hot update assembly.");
        }

        [Test]
        public void PcPackagingMenusAreGrouped()
        {
            var packagingMenus = TypeCache.GetMethodsWithAttribute<MenuItem>()
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
                .Where(attr => attr.menuItem.StartsWith(PcPackagingMenuPrefix))
                .Select(attr => attr.menuItem)
                .OrderBy(x => x)
                .ToArray();

            var expected = new[]
            {
                "Tools/打包/PC/Host代码+资源更新",
                "Tools/打包/PC/快速打包/使用最近 Host 结果打包",
                "Tools/打包/PC/快速打包/复用 AOT 快速打包",
                "Tools/打包/PC/最新代码资源完整打包",
                "Tools/打包/PC/首包完整打包",
                "Tools/打包/PC/辅助工具/仅重建完整内置资源",
                "Tools/打包/PC/辅助工具/为已有Player生成当前用户安装器"
            }.OrderBy(x => x).ToArray();

            CollectionAssert.AreEqual(expected, packagingMenus);
            Assert.That(packagingMenus.Distinct().Count(), Is.EqualTo(packagingMenus.Length), "Packaging menus must not be duplicated.");
        }

        [Test]
        public void ResourceServerMenuExistsAndOldBuildMenuIsRemoved()
        {
            var menuItems = TypeCache.GetMethodsWithAttribute<MenuItem>()
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
                .Select(attr => attr.menuItem)
                .ToArray();

            Assert.That(menuItems, Does.Contain("Tools/服务器/PC/启动资源服"));
            Assert.That(menuItems, Does.Contain("Tools/服务器/Android/启动资源服"));
            Assert.That(menuItems, Does.Not.Contain("Tools/服务器/启动资源服"));
            Assert.That(menuItems.Where(item => item.StartsWith("Tools/Build/")).ToArray(), Is.Empty);
        }

        [Test]
        public void GeneratedAotListMatchesDllFiles()
        {
            string dllsPath = Path.Combine(Application.dataPath, "Game", "Dlls");
            Assert.That(Directory.Exists(dllsPath), Is.True, $"DLL directory does not exist: {dllsPath}");

            string listPath = Path.Combine(dllsPath, "HybridCLRAssemblyList.bytes");
            Assert.That(File.Exists(listPath), Is.True, $"AOT list does not exist: {listPath}");

            var listedDlls = File.ReadAllLines(listPath)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line) && !line.StartsWith("//"))
                .Select(line => line.EndsWith(".dll") ? line : line + ".dll")
                .ToList();

            Assert.That(listedDlls, Does.Not.Contain("Game.dll"), "AOT list must not contain Game.dll.");
            Assert.That(listedDlls.Count, Is.GreaterThan(0), "AOT list must not be empty.");

            foreach (var dllName in listedDlls)
            {
                string bytesFile = Path.Combine(dllsPath, $"{dllName}.bytes");
                Assert.That(File.Exists(bytesFile), Is.True,
                    $"AOT bytes file is missing for {dllName}: {bytesFile}");
            }
        }
    }
}
