using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>只在 Temp 中验证首包 Boot 指纹和更新门禁，不构建 Player、不改正式构建记录。</summary>
    public sealed class AndroidBootBuildGuardTests
    {
        private string root;
        private string bootFile;
        private string sceneFile;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Application.dataPath, "../Temp/AndroidBootGuardTests-" + Guid.NewGuid().ToString("N"));
            root = Path.GetFullPath(root);
            string boot = Path.Combine(root, "Assets/Boot/Runtime");
            Directory.CreateDirectory(boot);
            bootFile = Path.Combine(boot, "Launcher.cs");
            File.WriteAllText(bootFile, "class Launcher { const int Version = 1; }");
            File.WriteAllText(bootFile + ".meta", "guid: original-boot-script");
            sceneFile = Path.Combine(root, "Assets/Game/Scenes/Boot.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(sceneFile));
            File.WriteAllText(sceneFile, "Boot scene contents");
            File.WriteAllText(sceneFile + ".meta", "guid: original-boot-scene");
        }

        [TearDown]
        public void TearDown()
        {
            string temp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp")) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(root).StartsWith(temp, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void MatchingBootAllowsCodeAndResourceUpdate()
        {
            string fingerprint = Fingerprint();
            Assert.DoesNotThrow(() => Validate(CreateState(fingerprint)));
        }

        [TestCase("edit")]
        [TestCase("add")]
        [TestCase("delete")]
        [TestCase("rename")]
        [TestCase("scene")]
        [TestCase("meta")]
        public void ChangedBootRequiresRebuildingFirstPackage(string change)
        {
            string fingerprint = Fingerprint();
            switch (change)
            {
                case "edit": File.AppendAllText(bootFile, " // changed"); break;
                case "add": File.WriteAllText(Path.Combine(Path.GetDirectoryName(bootFile), "NewBoot.cs"), "new Boot source"); break;
                case "delete": File.Delete(bootFile); break;
                case "rename": File.Move(bootFile, bootFile + ".renamed"); break;
                case "scene": File.AppendAllText(sceneFile, " changed scene"); break;
                case "meta": File.WriteAllText(bootFile + ".meta", "guid: replacement-script"); break;
            }
            var error = Assert.Throws<InvalidOperationException>(() => Validate(CreateState(fingerprint)));
            Assert.That(error.Message, Does.Contain("Boot"));
            Assert.That(error.Message, Does.Contain("Tools/打包/Android/完整包"));
        }

        [Test]
        public void ExistingHostWithoutFingerprintRequiresFirstPackage()
        {
            var error = Assert.Throws<InvalidOperationException>(() => Validate(CreateState(null)));
            Assert.That(error.Message, Does.Contain("Tools/打包/Android/完整包"));
        }

        [Test]
        public void GameCodeChangesDoNotInvalidateBootBaseline()
        {
            string fingerprint = Fingerprint();
            string game = Path.Combine(root, "Assets/Game/Scripts/Game.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(game));
            File.WriteAllText(game, "changed hot update code");
            Assert.DoesNotThrow(() => Validate(CreateState(fingerprint)));
        }

        [Test]
        public void FileTimestampsDoNotInvalidateBootBaseline()
        {
            string fingerprint = Fingerprint();
            File.SetLastWriteTimeUtc(bootFile, DateTime.UtcNow.AddHours(1));
            Assert.DoesNotThrow(() => Validate(CreateState(fingerprint)));
        }

        [TestCase("folder")]
        [TestCase("scene")]
        public void MissingBootInputsCannotPassValidation(string missing)
        {
            string fingerprint = Fingerprint();
            if (missing == "scene") File.Delete(sceneFile);
            else Directory.Move(Path.Combine(root, "Assets/Boot"), Path.Combine(root, "Assets/Boot-removed"));
            var error = Assert.Throws<InvalidOperationException>(() => Validate(CreateState(fingerprint)));
            Assert.That(error.Message, Does.Contain("Tools/打包/Android/完整包"));
        }

        private static Type EditorType(string name)
        {
            var type = Type.GetType("Game.Editor." + name + ", Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, "缺少构建门禁类型：" + name);
            return type;
        }

        private string Fingerprint() => (string)Invoke("ComputeFingerprint", root);

        private static object CreateState(string fingerprint)
        {
            var player = Activator.CreateInstance(EditorType("PlayerBuildRecord"));
            var field = player.GetType().GetField("BootFingerprint");
            Assert.That(field, Is.Not.Null, "首包记录必须保存 Boot 指纹。");
            field.SetValue(player, fingerprint);
            var state = Activator.CreateInstance(EditorType("BuildState"));
            state.GetType().GetField("LastAndroidPlayer").SetValue(state, player);
            return state;
        }

        private void Validate(object state) => Invoke("ValidateBeforeHotUpdate", state, root);

        private static object Invoke(string name, params object[] arguments)
        {
            var method = EditorType("AndroidBootBuildGuard").GetMethod(name, BindingFlags.Static | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);
            try { return method.Invoke(null, arguments); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
    }
}
