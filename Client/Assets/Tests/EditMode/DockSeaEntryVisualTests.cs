using System;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using MemoryPack;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>正式丧尸按当前权威进度展示攀爬，不能重播、累积位移或污染回池演员。</summary>
    public sealed class DockSeaEntryVisualTests
    {
        private GameObject actor;
        private EnemyView view;
        private Transform visual;
        private Vector3 rest;
        [TearDown] public void Cleanup() { if (actor != null) Object.DestroyImmediate(actor); }
        private void Spawn(string kind = "Shambler")
        {
            actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombie" + kind + ".prefab"));
            actor.transform.SetPositionAndRotation(new Vector3(12.3f, 0, -4.1f), Quaternion.identity);
            view = actor.GetComponent<EnemyView>(); view.BeginPresentationUse(); view.SetNavigationEnabled(false);
            view.Animator.Rebind(); view.Animator.Update(0);
            visual = view.Animator.transform.parent; rest = visual.localPosition;
        }
        private void State(float age) => view.SetDockSeaEntry(new ArcadeDockSeaEntryState { Started = true, Age = age, Lane = -4.1f }, Vector3.left, Vector3.up);
        [TestCase("Shambler")][TestCase("Gaunt")][TestCase("Bloated")]
        public void LateJoinSamplesCurrentClimbWithoutMovingCombatRoot(string kind)
        {
            Spawn(kind); var position = actor.transform.position;
            State(3.4f); view.LateTickCombatFeedback();
            Assert.That(view.Animator.enabled, Is.False, "上岸阶段必须由权威年龄采样，不能自行播放旧行走");
            Assert.That(Quaternion.Angle(visual.localRotation, Quaternion.identity), Is.GreaterThan(30), "必须直接处于抓边撑身的前倾姿态");
            Assert.That(Vector3.Distance(position, actor.transform.position), Is.LessThan(.0001f), "视觉高度不能改业务平面位置");
            var lower = actor.GetComponentsInChildren<Transform>().Single(t => t.name == "LowerLeg.L");
            var upper = lower.parent; var foot = lower.GetComponentsInChildren<Transform>().Single(t => t.name == "Foot.L");
            Assert.That(180 - Vector3.Angle(upper.position - lower.position, foot.position - lower.position), Is.GreaterThan(15), "抓边时腿部应实际收膝");
        }
        /// <summary>三方向均面向平台收膝攀爬，非法方向或中途换方向不能跳岸。</summary>
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void EveryDirectionClimbsFacingItsOwnEdge(int direction)
        {
            Spawn();var state=new ArcadeDockSeaEntryState{Started=true,Age=3.4f,Lane=-4.1f,Direction=direction};
            var forward=ArcadeDockSeaEntryRules.Forward(state);var aim=new Vector3(forward.X,0,forward.Y);
            view.SetDockSeaEntry(state,aim,Vector3.up);view.LateTickCombatFeedback();
            Assert.That(Vector3.Dot(actor.transform.forward,aim),Is.GreaterThan(.999f));
            Assert.That(view.Animator.enabled,Is.False);Assert.That(Quaternion.Angle(visual.localRotation,Quaternion.identity),Is.GreaterThan(30));
            var before=actor.transform.rotation;state.Direction=(direction+1)%3;state.Age=4f;
            view.SetDockSeaEntry(state,-aim,Vector3.up);view.LateTickCombatFeedback();Assert.That(Quaternion.Angle(before,actor.transform.rotation),Is.LessThan(.01f));
        }
        [Test] public void DuplicateOlderAndLocalTicksCannotAdvanceOrAccumulateClimb()
        {
            Spawn(); State(3.4f); view.LateTickCombatFeedback();
            Assert.That(view.Animator.enabled, Is.False, "先确认真实攀爬已接管，防止空实现伪通过");
            var position = visual.localPosition; var rotation = visual.localRotation;
            State(3.4f); view.LateTickCombatFeedback(); State(2f);
            view.TickCombatFeedback(2f); view.LateTickCombatFeedback(); view.LateTickCombatFeedback();
            Assert.That(Vector3.Distance(position, visual.localPosition), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(rotation, visual.localRotation), Is.LessThan(.01f));
            State(6.75f); view.LateTickCombatFeedback();
            Assert.That(view.Animator.enabled, Is.True);
            Assert.That(Vector3.Distance(rest, visual.localPosition), Is.LessThan(.0001f));
            State(3f); view.LateTickCombatFeedback();
            Assert.That(view.Animator.enabled, Is.True, "旧进场快照不能让已上岸敌人重新爬一遍");
        }
        [TestCase(false)][TestCase(true)]
        public void ReuseAfterHideOrDeathRestoresVisualRoot(bool death)
        {
            Spawn(); State(4.1f); view.LateTickCombatFeedback();
            Assert.That(view.Animator.enabled, Is.False);
            if (death)
            {
                var position = visual.localPosition; var rotation = visual.localRotation;
                Assert.That(view.TryPlayPhysicalDeath(Vector3.left, false, out _), Is.True);
                Assert.That(view.Animator.enabled, Is.False, "普通死亡的短暂失衡不能让旧Animator覆盖抓边姿态");
                Assert.That(Vector3.Distance(position, visual.localPosition), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(rotation, visual.localRotation), Is.LessThan(.01f));
            }
            else { actor.SetActive(false); actor.SetActive(true); }
            view.ResetPhysicalFeedback(); view.BeginPresentationUse(); view.LateTickCombatFeedback();
            Assert.That(view.Animator.enabled, Is.True);
            Assert.That(Vector3.Distance(rest, visual.localPosition), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(Quaternion.identity, visual.localRotation), Is.LessThan(.01f));
        }
        [Test] public void InvalidAgeCannotShiftVisualRoot()
        {
            Spawn(); State(float.NaN); view.LateTickCombatFeedback();
            Assert.That(Vector3.Distance(rest, visual.localPosition), Is.LessThan(.0001f));
            Assert.That(view.Animator.enabled, Is.True);
        }
        [Test] public void ProtocolRoundTripAndReadonlyCopyPreserveCurrentEntry()
        {
            var message = new ET.OnlineArcadeBattleInfo();
            message.Enemies.Add(new ET.OnlineArcadeEnemyInfo { RuntimeId = 4, EnemyConfigId = "enemy.arcade.zombie.shambler", DockSeaEntryStarted = true, DockSeaEntryAge = 4.2f, DockSeaEntryLane = -4.1f, DockSeaEntryDirection = 2 });
            var decoded = MemoryPackSerializer.Deserialize<ET.OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(message));
            var copy = (OnlineArcadeBattleSnapshot)typeof(OnlineArcadeRunSystem).GetMethod("CopyBattle", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { decoded });
            decoded.Enemies[0].DockSeaEntryAge = 0;
            Assert.That(copy.Enemies[0].DockSeaEntry.Started, Is.True);
            Assert.That(copy.Enemies[0].DockSeaEntry.Age, Is.EqualTo(4.2f));
            Assert.That(copy.Enemies[0].DockSeaEntry.Lane, Is.EqualTo(-4.1f));
            Assert.That(copy.Enemies[0].DockSeaEntry.Direction,Is.EqualTo(2));
        }
        [Test] public void RippleMeshIsReusedAndReleasedWithoutOwningSharedMaterial()
        {
            Spawn(); State(1f); view.LateTickCombatFeedback();
            var ripple = actor.transform.Find("DockSeaEntryRipples"); Assert.That(ripple, Is.Not.Null);
            var mesh = ripple.GetComponent<MeshFilter>().sharedMesh;
            var material = ripple.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(mesh.vertexCount, Is.EqualTo(168));
            Assert.That(ripple.GetComponent<MeshRenderer>().enabled, Is.True);
            State(3.4f); view.LateTickCombatFeedback(); Assert.That(ripple.gameObject.activeSelf, Is.False);
            view.ResetPhysicalFeedback(); view.BeginPresentationUse(); view.SetNavigationEnabled(false);
            State(1f); view.LateTickCombatFeedback();
            Assert.That(ripple.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            view.ReleasePresentationResources(); Assert.That(mesh == null, Is.True); Assert.That(material != null, Is.True);
        }
        [Test] public void LateCarrierBindsToSameRestMountAndImmediatelyFollowsClimb()
        {
            Spawn();
            var early = new GameObject("TEMP_EarlyCarrier").transform; early.SetParent(actor.transform, false); early.localPosition = new Vector3(0, .45f, -.4f);
            view.AttachCarriedVisual(early); var localPosition = early.localPosition; var localRotation = early.localRotation;
            State(4.1f); view.LateTickCombatFeedback(); Assert.That(view.Animator.enabled, Is.False);
            var late = new GameObject("TEMP_LateCarrier").transform; late.SetParent(actor.transform, false); late.localPosition = new Vector3(0, .45f, -.4f);
            view.AttachCarriedVisual(late);
            Assert.That(late.parent, Is.SameAs(visual));
            Assert.That(Vector3.Distance(late.localPosition, localPosition), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(late.localRotation, localRotation), Is.LessThan(.01f));
            Assert.That(Vector3.Distance(early.position, late.position), Is.LessThan(.0001f));
        }
    }
}
