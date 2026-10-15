using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests
{
    public sealed class EditorLayoutTests
    {
        private static readonly string[] RequiredDirectories =
        {
            "Assets/Editor/Build",
            "Assets/Editor/YooAsset",
            "Assets/Editor/UI",
            "Assets/Editor/Localization",
            "Assets/Editor/Diagnostics",
            "Assets/Editor/Scene"
        };

        [Test]
        public void EditorToolsUseFunctionalTopLevelDirectories()
        {
            foreach (string directory in RequiredDirectories)
            {
                Assert.That(Directory.Exists(directory), Is.True, directory);
            }

            var editorDirectories = Directory.GetDirectories("Assets/Editor")
                .Select(Path.GetFileName)
                .ToArray();
            CollectionAssert.AreEquivalent(RequiredDirectories.Select(Path.GetFileName), editorDirectories);
        }

        [Test]
        public void CoreEditorTypesRemainAvailable()
        {
            string[] typeNames =
            {
                "Game.Editor.BuildUtility, Assembly-CSharp-Editor",
                "Game.Editor.YooAssetBuildUtility, Assembly-CSharp-Editor",
                "Game.Editor.LocalResourceServerUtility, Assembly-CSharp-Editor",
                "Game.LocalizationJsonEditorWindow, Assembly-CSharp-Editor",
                "Game.ResourceDiagnosticsWindow, Assembly-CSharp-Editor",
                "Game.SceneSwitcherWindow, Assembly-CSharp-Editor"
            };

            foreach (string typeName in typeNames)
            {
                Assert.That(System.Type.GetType(typeName), Is.Not.Null, typeName);
            }
        }

        [Test]
        public void CustomGridMenuDoesNotExposeProjectName()
        {
            var menus = TypeCache.GetMethodsWithAttribute<MenuItem>()
                .SelectMany(method => method
                    .GetCustomAttributes(typeof(MenuItem), false)
                    .Cast<MenuItem>())
                .Select(item => item.menuItem)
                .ToArray();

            Assert.That(menus,
                Does.Contain("GameObject/UI/Custom Grid Layout Group"));
            Assert.That(menus.Count(menu => menu.EndsWith("Custom Grid Layout Group")), Is.EqualTo(1));
        }
    }
}
