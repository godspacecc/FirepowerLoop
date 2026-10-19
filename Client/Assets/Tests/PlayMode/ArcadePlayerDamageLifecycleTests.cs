#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证切轮同帧重建与帧尾销毁的真实时序，不启动游戏架构或修改存档。</summary>
    public sealed class ArcadePlayerDamageLifecycleTests
    {
        private GameObject actor;
        private PlayerView view;

        [SetUp]
        public void SetUp()
        {
            actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            view = actor.GetComponent<PlayerView>();
            view.SetArcadePresentationActive(true);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (actor != null) Object.Destroy(actor);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SameFrameRoundRestartKeepsDamageFeedbackAfterDeferredDestroy()
        {
            var previous = actor.transform.Find("ArcadeDamage");
            view.PlayHurtFeedback();
            // Begin 先 End 再启用同一玩家；Destroy 必须等到帧尾才完成。
            view.SetArcadePresentationActive(false);
            view.SetArcadePresentationActive(true);
            yield return null;

            Assert.That(previous == null, Is.True, "旧轮特效应在帧尾释放");
            Assert.DoesNotThrow(() => view.TickCombatFeedback(.1f));
            var current = actor.transform.Find("ArcadeDamage");
            Assert.That(current, Is.Not.Null, "新轮不能复用等待销毁的旧特效");
            Assert.That(current.Find("Hit").gameObject.activeSelf, Is.False, "切轮不能继承旧受击闪光");
            view.PlayHurtFeedback();
            Assert.That(current.Find("Hit").gameObject.activeSelf, Is.True);
            view.TickCombatFeedback(.3f);
            Assert.That(current.Find("Hit").gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator ReleasedDamageFeedbackCanBeDisposedAgainAfterFrameEnd()
        {
            var feedback = (IDisposable)typeof(PlayerView)
                .GetField("arcadeDamage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            view.SetArcadePresentationActive(false);
            yield return null;

            Assert.DoesNotThrow(feedback.Dispose, "帧尾销毁后重复释放不能访问失效的 Transform");
        }
    }
}
#endif
