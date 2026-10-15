using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class CharacterAnimationEventReceiverTests
    {
        [Test]
        public void GalleryCharacterAnimatorsReceiveSharedFootstepEvents()
        {
            var prefabPaths = new[]
            {
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab",
                "Assets/Game/Prefabs/Characters/ArcadeEnemyBasic.prefab",
                "Assets/Game/Prefabs/Characters/ArcadeEnemyBrute.prefab",
                "Assets/Game/Prefabs/Characters/ArcadeEnemyArcher.prefab",
                "Assets/Game/Prefabs/Characters/ArcadeEnemyBossArcher.prefab",
                "Assets/Game/Art/Characters/Presentation/Samples/PolygonCityBusinessManSample.prefab"
            };

            foreach (var path in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.NotNull(prefab, path);
                var presenter = prefab.GetComponent<CharacterWeaponPresenter>();
                Assert.NotNull(presenter, path);
                var bodyAnimator = presenter.BodyAnimator;
                Assert.NotNull(bodyAnimator, path);
                var receiver = bodyAnimator.GetComponent("CharacterAnimationEventReceiver");
                Assert.NotNull(receiver, path);

                foreach (var eventName in new[] { "FootL", "FootR" })
                {
                    var method = receiver.GetType().GetMethod(eventName,
                        BindingFlags.Instance | BindingFlags.Public);
                    Assert.NotNull(method, path + ": " + eventName);
                    Assert.That(method.ReturnType, Is.EqualTo(typeof(void)), path + ": " + eventName);
                    Assert.That(method.GetParameters(), Is.Empty, path + ": " + eventName);
                }
            }
        }

        [Test]
        public void BruteAttackHitEventHasReceiver()
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(
                    "Assets/Game/Art/Arcade/Enemies/Humanoid/Animations/Sword2H/BruteSwordAttack.fbx")
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate => candidate.name == "2Hand-Sword-Attack1");
            Assert.NotNull(clip);
            Assert.That(AnimationUtility.GetAnimationEvents(clip).Select(evt => evt.functionName),
                Does.Contain("Hit"));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/ArcadeEnemyBrute.prefab");
            Assert.NotNull(prefab);
            var bodyAnimator = prefab.GetComponent<CharacterWeaponPresenter>().BodyAnimator;
            Assert.NotNull(bodyAnimator);
            var receiver = bodyAnimator.GetComponent("CharacterAnimationEventReceiver");
            Assert.NotNull(receiver);
            var hitMethod = receiver.GetType().GetMethod("Hit", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(hitMethod);
            Assert.That(hitMethod.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(hitMethod.GetParameters(), Is.Empty);
        }
    }
}
