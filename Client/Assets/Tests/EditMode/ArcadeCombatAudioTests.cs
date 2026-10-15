using System;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>检查街机七种武器的试听音效均能从正式资源目录加载。</summary>
    public sealed class ArcadeCombatAudioTests
    {
        [Test]
        public void SevenWeapons_ResolveImportedFireClips()
        {
            var resolve = typeof(ArcadeCombatViewController).GetMethod("ResolveFireSoundName",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resolve, Is.Not.Null, "街机表现端需要按武器选择试听音效");

            var ids = new[]
            {
                "arcade.weapon.basic", "arcade.weapon.rapid", "arcade.weapon.spread",
                "arcade.weapon.piercing", "arcade.weapon.rocket", "arcade.weapon.grenade",
                "arcade.weapon.raygun"
            };
            foreach (var id in ids)
            {
                var name = resolve.Invoke(null, new object[] { id }) as string;
                Assert.That(name, Is.Not.Null.And.Not.Empty, $"{id} 缺少开火音效");
                var path = $"Assets/Game/Audio/Sound/{name}.wav";
                Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(path), Is.Not.Null,
                    $"{id} 的开火音效未导入正式资源目录：{path}");
            }
        }

        [Test]
        public void FastWeapons_LimitRepeatedShotSounds()
        {
            var canPlay = typeof(ArcadeCombatViewController).GetMethod("CanPlayFireSound",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(canPlay, Is.Not.Null, "高速武器需要限制长尾音效叠加");

            bool Check(string id, float elapsed) =>
                (bool)canPlay.Invoke(null, new object[] { id, elapsed });
            Assert.That(Check("arcade.weapon.rapid", 0.10f), Is.False);
            Assert.That(Check("arcade.weapon.rapid", 0.20f), Is.True);
            Assert.That(Check("arcade.weapon.raygun", 0.18f), Is.False);
            Assert.That(Check("arcade.weapon.raygun", 0.30f), Is.True);
            Assert.That(Check("arcade.weapon.basic", 0.10f), Is.True);
        }
    }
}
