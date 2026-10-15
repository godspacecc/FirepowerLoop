using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class RemoteFootstepTests
    {
        private GameObject actor, floor;
        private FootstepProfile profile;
        private OnlineRemotePlayerViewCtrl remote;
        private FootstepCadence cadence;
        [SetUp] public void SetUp()
        {
            TestReset.ResetArchitecture();
            actor = new GameObject("Remote", typeof(CharacterController), typeof(PlayerView));
            actor.transform.position = new Vector3(5000f, 0f, 5000f);
            var cc = actor.GetComponent<CharacterController>(); cc.height = 2f; cc.center = Vector3.up;
            profile = ScriptableObject.CreateInstance<FootstepProfile>();
            // 固定计步夹具，专门验证快照幂等、纠正和末帧位移，不跟随正式听感调参。
            profile.WalkStride = 0.65f; profile.RunStride = 0.9f;
            profile.MinimumInterval = 0.12f; profile.FastSpeed = 4.5f;
            typeof(PlayerView).GetField("footstepProfile", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(actor.GetComponent<PlayerView>(), profile);
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            remote = new OnlineRemotePlayerViewCtrl(actor);
            var presenter = typeof(OnlineRemotePlayerViewCtrl).GetField("footsteps", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(remote);
            cadence = (FootstepCadence)typeof(FootstepPresenter).GetField("cadence", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(presenter);
            floor = new GameObject("Ground", typeof(BoxCollider), typeof(FootstepSurface));
            floor.transform.position = actor.transform.position + Vector3.down * 0.5f;
            floor.GetComponent<BoxCollider>().size = new Vector3(10f, 1f, 10f);
            Physics.SyncTransforms();
        }
        [TearDown] public void TearDown()
        {
            remote?.Dispose(); Object.DestroyImmediate(actor); Object.DestroyImmediate(floor); Object.DestroyImmediate(profile);
            TestReset.ResetArchitecture();
        }
        [Test] public void RepeatedAliveSnapshotPreservesPartialStep()
        { cadence.Step(0.4f, 0.2f, true, true); remote.SetAlive(true); remote.SetAlive(true); Assert.True(cadence.Step(0.3f, 0.2f, true, true)); }
        [Test] public void SnapDoesNotCountAsMovement()
        { cadence.Step(0.4f, 0.2f, true, true); remote.Snap(actor.transform.localPosition + Vector3.right * 10f, 0f); Assert.False(cadence.Step(0.3f, 0.2f, true, true)); }
        [Test] public void LargeCorrectionDiscardsPartialStep()
        { cadence.Step(0.4f, 0.2f, true, true); remote.SetTarget(actor.transform.localPosition + Vector3.right * 10f, 0f); Assert.False(cadence.Step(0.3f, 0.2f, true, true)); }
        [Test] public void AliveSnapshotKeepsRemoteMotorDisabled()
        { remote.SetAlive(true); Assert.False(actor.GetComponent<CharacterController>().enabled); }
        [Test] public void FinalInterpolationFrameContributesDistance()
        {
            remote.SetAlive(true);
            cadence.Step(0.4f, 0.2f, true, true);
            remote.SetTarget(actor.transform.localPosition + Vector3.right * 0.3f, 0f);
            remote.Tick(0.1f);
            Assert.True(cadence.Step(0.62f, 0.2f, true, true), "末帧应跨过一步并保留约 0.05 米余量");
        }
    }
}
