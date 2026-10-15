using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class FourthStageRevisionBoundaryTests
    {
        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [Test]
        public void GameAssemblyReferencesOfficialUniTask()
        {
            var asmdef = File.ReadAllText(Path.Combine(ProjectRoot, "Assets/Game/Game.asmdef"));

            Assert.That(asmdef, Does.Contain("\"UniTask\""));
        }

        [Test]
        public void CustomUniTaskWrapperIsRemoved()
        {
            var scriptsRoot = Path.Combine(ProjectRoot, "Assets/Game/Scripts");
            Assert.That(
                Directory.GetFiles(scriptsRoot, "UniTask.cs", SearchOption.AllDirectories),
                Is.Empty);
        }

        [Test]
        public void MapAndResourceUpdateUseOfficialUniTaskNamespace()
        {
            Assert.That(ReadGameFile("Scripts/Utility/Map/IMapContentLoader.cs"), Does.Contain("using Cysharp.Threading.Tasks;"));
            Assert.That(ReadGameFile("Scripts/Utility/Map/AdditiveSceneMapLoader.cs"), Does.Contain("using Cysharp.Threading.Tasks;"));
            Assert.That(ReadGameFile("Scripts/System/Map/MapSceneSystem.cs"), Does.Contain("using Cysharp.Threading.Tasks;"));
            Assert.That(ReadGameFile("Scripts/System/ResourceUpdate/ResourceUpdateSystem.cs"), Does.Contain("using Cysharp.Threading.Tasks;"));
            Assert.That(ReadGameFile("Scripts/Utility/ResourceUpdate/ResourceUpdateContracts.cs"), Does.Contain("using Cysharp.Threading.Tasks;"));
            Assert.That(ReadGameFile("Scripts/Utility/ResourceUpdate/YooAssetResourceUpdateRuntimeChecker.cs"), Does.Contain("using Cysharp.Threading.Tasks;"));
        }

        [Test]
        public void MapRuntimeDoesNotWriteAppStateDirectly()
        {
            var source = ReadGameFile("Scripts/System/Map/MapSceneSystem.cs");

            Assert.That(source, Does.Not.Contain("CurrentState.Value ="));
            Assert.That(source, Does.Contain("TryRestoreStableStateAfterLoading"));
            Assert.That(source, Does.Contain("CancellationToken"));
            Assert.That(source, Does.Contain("generation"));
        }

        [Test]
        public void AdditiveLoaderChecksUnloadStatusAndError()
        {
            var source = ReadGameFile("Scripts/Utility/Map/AdditiveSceneMapLoader.cs");

            Assert.That(source, Does.Contain("operation.Status == EOperationStatus.Succeed"));
            Assert.That(source, Does.Contain("operation."));
            Assert.That(source, Does.Contain("MapContentUnloadResult.Fail"));
        }

        [Test]
        public void RevisionAvoidanceImplementationsAreRemoved()
        {
            var hotKeySource = ReadGameFile("Scripts/System/Input/HotKeySystem.cs");
            StringAssert.DoesNotMatch(@"(?m)^\s*using\b[^\r\n]*\\u[0-9A-Fa-f]{4}", hotKeySource);
            StringAssert.DoesNotMatch(@"UnityEngine\.\\u[0-9A-Fa-f]{4}", hotKeySource);
            Assert.That(ReadGameFile("Scripts/Utility/Application/ApplicationRestartUtility.cs"),
                Does.Not.Contain("\"Diag\" + \"nostics\""));
            Assert.That(
                Directory.GetFiles(
                    Path.Combine(ProjectRoot, "Assets/Game/Scripts"),
                    "IntSaveSectionProvider.cs",
                    SearchOption.AllDirectories),
                Is.Empty);
            Assert.That(ReadGameFile("Scripts/Controller/UI/Home/StartUI.cs"),
                Does.Not.Contain("public Button BtnStart"));
        }

        [Test]
        public void SourceFilesDoNotHideUnicodeEscapesInImportsOrTypeNames()
        {
            var scriptsRoot = Path.Combine(ProjectRoot, "Assets/Game/Scripts");
            var escapedIdentifier = new Regex(@"(?:^\s*using\b[^\r\n]*\\u[0-9A-Fa-f]{4}|[A-Za-z_][A-Za-z0-9_\\.]*\\u[0-9A-Fa-f]{4})",
                RegexOptions.Multiline);

            foreach (var path in Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(path);
                Assert.That(escapedIdentifier.IsMatch(source), Is.False, path);
            }
        }

        [Test]
        public void RuntimeLogAndSettingControllersDoNotCreateRuntimeFallbackControls()
        {
            Assert.That(ReadGameFile("Scripts/Controller/UICtrl/Home/SettingUICtrl.cs"), Does.Not.Contain("EnsureRuntimeControls"));
            Assert.That(ReadGameFile("Scripts/Controller/UICtrl/Home/SettingUICtrl.cs"), Does.Not.Contain("AddComponent<"));
            Assert.That(ReadGameFile("Scripts/Controller/UICtrl/Diagnostics/RuntimeLogUICtrl.cs"), Does.Not.Contain("EnsureRuntimeControls"));
            Assert.That(ReadGameFile("Scripts/Controller/UICtrl/Diagnostics/RuntimeLogUICtrl.cs"), Does.Not.Contain("AddComponent<Toggle>"));
        }

        [Test]
        public void AudioSystemUsesExplicitBindingsInsteadOfCreatingRoot()
        {
            var source = ReadGameFile("Scripts/System/Audio/AudioSystem.cs");

            Assert.That(ReadGameFile("Scripts/Controller/View/Audio/AudioRootBindings.cs"), Does.Contain("public sealed class AudioRootBindings"));
            Assert.That(source, Does.Contain("FindObjectOfType<AudioRootBindings>"));
            Assert.That(source, Does.Not.Contain("new GameObject(\"AudioRoot\")"));
            Assert.That(source, Does.Not.Contain("FindOrCreateChild"));
        }

        private static string ReadGameFile(string path)
        {
            return File.ReadAllText(Path.Combine(ProjectRoot, "Assets/Game", path.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
