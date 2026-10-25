using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>正式三种丧尸的足端接地、站立姿态、移速步频与生命周期，避免滑步和马步回归。</summary>
    public sealed class ArcadeZombieGroundWalkTests
    {
        private GameObject actor;
        private EnemyView view;

        [TearDown]
        public void Cleanup()
        {
            if (actor != null) Object.DestroyImmediate(actor);
        }

        private Animator Spawn(string kind)
        {
            actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/ArcadeZombie" + kind + ".prefab"));
            actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            view = actor.GetComponent<EnemyView>();
            view.BeginPresentationUse(); view.SetNavigationEnabled(false);
            view.Animator.Rebind(); view.Animator.Update(0);
            return view.Animator;
        }

        [TestCase("Shambler")][TestCase("Gaunt")][TestCase("Bloated")]
        public void GroundStridePlantsFeetWithoutCrouching(string kind)
        {
            var animator = Spawn(kind);
            var controller = (AnimatorController)animator.runtimeAnimatorController;
            var clip = (AnimationClip)controller.layers[0].stateMachine.states.Single(s => s.state.name == "Run").state.motion;
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Art/Enemies/Zombies/Civilian/" +
                (kind == "Gaunt" ? "Run_Arms" : "Walk") + ".anim");
            animator.enabled = false;
            var bones = actor.GetComponentsInChildren<Transform>(true);
            Transform Bone(string name) => bones.Single(b => b.name == name);
            var body = Bone("Body");
            var upper = new[] { Bone("UpperLeg.L"), Bone("UpperLeg.R") };
            var knee = new[] { Bone("LowerLeg.L"), Bone("LowerLeg.R") };
            var foot = new[] { Bone("Foot.L"), Bone("Foot.R") };
            var previous = new Vector3[2]; var lastPhase = new[] { -1f, -1f };
            var minY = new[] { float.MaxValue, float.MaxValue }; var maxY = new[] { float.MinValue, float.MinValue };
            float maxSlip = 0, maxDrop = 0, maxBothKnees = 0;
            for (int i = 0; i <= 164; i++)
            {
                float time = i * .005f;
                actor.transform.position = Vector3.forward * (.9f * time);
                source.SampleAnimation(animator.gameObject, Mathf.Repeat(time / .82f, 1) * source.length);
                float referenceY = body.position.y;
                clip.SampleAnimation(animator.gameObject, time);
                actor.transform.position = Vector3.forward * (.9f * time);
                maxDrop = Mathf.Max(maxDrop, referenceY - body.position.y);
                float leastBent = float.MaxValue;
                for (int side = 0; side < 2; side++)
                {
                    float phase = Mathf.Repeat(time / .82f + side * .5f, 1);
                    if (phase < .6f && lastPhase[side] >= 0 && phase > lastPhase[side] && lastPhase[side] < .6f)
                        maxSlip = Mathf.Max(maxSlip, Vector3.ProjectOnPlane(foot[side].position - previous[side], Vector3.up).magnitude);
                    previous[side] = foot[side].position; lastPhase[side] = phase;
                    minY[side] = Mathf.Min(minY[side], foot[side].position.y);
                    maxY[side] = Mathf.Max(maxY[side], foot[side].position.y);
                    float bend = 180 - Vector3.Angle(upper[side].position - knee[side].position, foot[side].position - knee[side].position);
                    leastBent = Mathf.Min(leastBent, bend);
                }
                maxBothKnees = Mathf.Max(maxBothKnees, leastBent);
            }
            Assert.That(maxSlip, Is.LessThan(.002f), "移动时已着地的脚掌不能随身体滑动");
            Assert.That(maxDrop, Is.LessThan(.051f), "不能用持续矮身换取步幅");
            Assert.That(maxBothKnees, Is.LessThan(12), "每帧至少一条腿自然支撑，不能双膝持续深弯");
            for (int side = 0; side < 2; side++)
                Assert.That(maxY[side] - minY[side], Is.InRange(kind == "Bloated" ? .044f : .058f, .073f), "抬脚要可见且不过度高抬腿");
            clip.SampleAnimation(animator.gameObject, 0); var start = foot.Select(f => f.localRotation).ToArray();
            clip.SampleAnimation(animator.gameObject, clip.length);
            for (int side = 0; side < 2; side++)
                Assert.That(Quaternion.Angle(start[side], foot[side].localRotation), Is.LessThan(.1f), "循环接缝不能突然扭脚");
        }

        [TestCase("Shambler")][TestCase("Gaunt")][TestCase("Bloated")]
        public void MovementSpeedRetimesStrideWithoutRetimingHit(string kind)
        {
            var animator = Spawn(kind);
            float Advance(string state, float speed)
            {
                view.SetMovementSpeed(speed); animator.Play(state, 0, .1f); animator.Update(0);
                float previousTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                animator.Update(.04f);
                return animator.GetCurrentAnimatorStateInfo(0).normalizedTime - previousTime;
            }
            float normal = Advance("Run", .9f), fast = Advance("Run", 1.8f);
            Assert.That(fast / normal, Is.EqualTo(2).Within(.02f), "移动翻倍时步频应同步，避免快走却慢迈腿");
            Assert.That(Advance("Hit", 1.8f) / Advance("Hit", .9f), Is.EqualTo(1).Within(.02f), "移速不能加快受击动作");
            Assert.That(animator.speed, Is.EqualTo(1), "不通过整体Animator加速改变所有动作");
        }

        [TestCase("Shambler")][TestCase("Gaunt")][TestCase("Bloated")]
        public void StopDeathAndPoolReuseRestoreUprightIdle(string kind)
        {
            var animator = Spawn(kind);
            view.SetMovementSpeed(.9f); animator.Play("Run", 0, .7f); animator.Update(0);
            view.Stop(); for (int i = 0; i < 20; i++) animator.Update(.02f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True, "停步不能冻结在半空迈腿姿态");
            var body = actor.GetComponentsInChildren<Transform>().Single(t => t.name == "Body");
            var idle = animator.runtimeAnimatorController.animationClips.Single(c => c.name.EndsWith("|Idle"));
            var stopped = body.localPosition; idle.SampleAnimation(animator.gameObject, 0);
            Assert.That(Vector3.Distance(stopped, body.localPosition), Is.LessThan(.002f), "停步应恢复原站立高度");
            view.TryPlayDeathAnimation(out _); for (int i = 0; i < 10; i++) animator.Update(.02f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Death"), Is.True, "死亡过渡完成后应播放独立死亡片段");
            view.ResetPhysicalFeedback(); view.BeginPresentationUse(); animator.Update(0);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
            Assert.That(animator.GetFloat("MoveSpeed"), Is.Zero);
            Assert.That(animator.applyRootMotion, Is.False);
        }
    }
}
