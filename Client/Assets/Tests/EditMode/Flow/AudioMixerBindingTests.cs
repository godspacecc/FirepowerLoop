using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

namespace Game.Tests
{
    public sealed class AudioMixerBindingTests
    {
        [Test]
        public void MixerContainsRequiredGroupsAndExposedParameters()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Game/Audio/FirepowerLoopAudioMixer.mixer");
            Assert.NotNull(mixer);
            Assert.NotNull(mixer.FindMatchingGroups("Master").FirstOrDefault());
            Assert.NotNull(mixer.FindMatchingGroups("Music").FirstOrDefault());
            Assert.NotNull(mixer.FindMatchingGroups("Sound").FirstOrDefault());

            var yaml = File.ReadAllText(Path.Combine(ProjectRoot, "Assets/Game/Audio/FirepowerLoopAudioMixer.mixer"));
            Assert.That(Count(yaml, "MasterVolumeDb"), Is.EqualTo(1));
            Assert.That(Count(yaml, "MusicVolumeDb"), Is.EqualTo(1));
            Assert.That(Count(yaml, "SoundVolumeDb"), Is.EqualTo(1));
        }

        [Test]
        public void MainSceneHasSingleExplicitAudioRootBinding()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/Main.unity", OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var bindings = roots
                    .SelectMany(root => root.GetComponentsInChildren<AudioRootBindings>(true))
                    .ToArray();
                Assert.That(bindings.Length, Is.EqualTo(1));
                Assert.IsTrue(bindings[0].IsValid);
                Assert.AreNotSame(bindings[0].MusicSourceA, bindings[0].MusicSourceB);
                Assert.AreSame(bindings[0].MusicGroup, bindings[0].MusicSourceA.outputAudioMixerGroup);
                Assert.AreSame(bindings[0].MusicGroup, bindings[0].MusicSourceB.outputAudioMixerGroup);
                var listeners = roots
                    .SelectMany(root => root.GetComponentsInChildren<AudioListener>(true))
                    .ToArray();
                Assert.That(listeners.Length, Is.EqualTo(1));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static int Count(string text, string value)
        {
            return text.Split(new[] { value }, System.StringSplitOptions.None).Length - 1;
        }
    }
}
