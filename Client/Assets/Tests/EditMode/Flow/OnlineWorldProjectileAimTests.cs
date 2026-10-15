using System.Collections;
using Cysharp.Threading.Tasks;
using System.IO;
using System.Linq;
using ET;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
namespace Game.Tests
{
    public sealed partial class OnlineWorldTests
    {
        private ET.OnlineWorldSnapshot ProjectileWorld()
        {
            var player=WirePlayer(401,4001,Vector3.zero,0,1);player.CurrentHP=100;player.MaxHP=100;player.LifecycleRevision=1;
            var world=WireSnapshot(4001,1,player);
            var index=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/WorldCollision/"+MapId+".json");
            world.CollisionContentHash=JsonUtility.FromJson<WorldProjectileCollisionIndex>(index.text).ContentHash;
            world.SelfInventory.Equipment.ActiveSlot=(int)WeaponSlot.Ranged;world.SelfInventory.Equipment.RangedWeaponItemId=6;world.SelfInventory.Equipment.RangedLoadedAmmo=1;return world;
        }
        private IEnumerator AimArrow(PlayerCameraMode mode,float vertical)
        {
            yield return EnterWorld(ProjectileWorld());
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(()=>new Tables(name=>new Luban.ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config",name+".bytes"))))));
            var root=Game.Interface.GetModel<MapSceneModel>().CurrentHandle.Context.MapContentRoot.transform;
            root.position=new Vector3(50,5,-30);root.rotation=Quaternion.Euler(0,45,0);root.localScale=new Vector3(2,1,3);
            playerPorts.Motor.Move(root.position-playerPorts.Motor.Position);
            Game.Interface.GetModel<PlayerCameraModeModel>().Mode=mode;
            var ray=new Ray(root.TransformPoint(new Vector3(0,2,-3)),root.TransformVector(new Vector3(0,vertical,1).normalized).normalized);
            input.MoveInput=Vector2.zero;input.MeleeSlotPressedThisFrame=input.RangedSlotPressedThisFrame=input.DestroyPressedThisFrame=false;
            PlayerInputSystem.SetPointerRayResolverForTests(()=>ray);input.AttackPressedThisFrame=true;LifeCycle.Update();input.AttackPressedThisFrame=false;
            PlayerInputSystem.SetPointerRayResolverForTests(null);
            Assert.That(network.AttackRequests.Count,Is.EqualTo(1));var q=network.AttackRequests.Single();
            Assert.That(q.HasProjectileOrigin,Is.True);Assert.That(Mathf.Sign(q.AimY),Is.EqualTo(Mathf.Sign(vertical)));
            Assert.That(q.AimX,Is.EqualTo(0).Within(.001f));
            Assert.That(new Vector3(q.OriginX,q.OriginY,q.OriginZ).magnitude,Is.LessThan(.001f));
            Assert.That(q.CollisionContentHash,Is.EqualTo(ProjectileWorld().CollisionContentHash));
            Assert.That(Game.Interface.GetSystem<OnlineWorldSystem>().Snapshot.SelfInventory.Equipment.RangedLoadedAmmo,Is.EqualTo(1),"客户端不能提前消费权威弹药");
        }
        [UnityTest] public IEnumerator OnlineShoulderArrowUp()=>AimArrow(PlayerCameraMode.Shoulder,.5f);
        [UnityTest] public IEnumerator OnlineShoulderArrowDown()=>AimArrow(PlayerCameraMode.Shoulder,-.5f);
        [UnityTest] public IEnumerator OnlineFirstPersonArrowUp()=>AimArrow(PlayerCameraMode.FirstPerson,.5f);
        [UnityTest] public IEnumerator OnlineFirstPersonArrowDown()=>AimArrow(PlayerCameraMode.FirstPerson,-.5f);
        [UnityTest] public IEnumerator OnlineArrowMissingIndexDoesNotSend()
        {
            yield return EnterWorld(ProjectileWorld());WorldProjectileCollisionCatalog.Release(MapId);
            Assert.That(Game.Interface.GetSystem<OnlineWorldSystem>().TryRequestWeaponAttack(6,Vector3.up,Vector3.up,ProjectileWorld().CollisionContentHash),Is.False);
            Assert.That(network.AttackRequests,Is.Empty);
        }
        [UnityTest] public IEnumerator OnlineArrowPrepareFailureRollsBackMap()
        {
            resources.FailAssetPath="Prefabs/Combat/GrayboxArrow";
            var entering=Game.Interface.GetSystem<OnlineWorldSystem>().EnterWorldAsync(MapId);
            yield return WaitUntil(()=>network.PendingCount<C2G_EnterWorld>()==1,"enter");network.Complete<C2G_EnterWorld>(EnterResponse(ProjectileWorld()));
            yield return WaitUntil(()=>network.PendingCount<C2G_LeaveWorld>()==1,"cleanup leave");
            Assert.That(views.LiveInstances,Is.Zero);Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle,Is.Null);
            Assert.That(WorldProjectileCollisionCatalog.TryGet(MapId,out _,out _),Is.False);
            network.Complete<C2G_LeaveWorld>(LeaveResponse(0));yield return Await(entering);Assert.That(entering.Result.Success,Is.False);
        }
        [UnityTest] public IEnumerator OnlineArrowPrepareCancellationReleasesHandleOnce()
        {
            var handle=TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapId,41);
            var ctrl=new ProjectileCtrl(Game.Interface);var wire=ProjectileWorld();var snapshot=OnlineWorldSystem.Copy(wire,false,OnlineWorldOperationState.Idle,OnlineWorldResultCode.Success);
            var context=new MapPreparationContext(MapId,41,handle.Context,System.Array.Empty<MapSpawnDefinition>(),snapshot);
            resources.DelayNextGameObjectLoad=true;var cancellation=new System.Threading.CancellationTokenSource();
            var prepare=ctrl.PrepareMapAsync(context,cancellation.Token).AsTask();yield return WaitUntil(()=>resources.HasDelayedLoad,"arrow delayed");var raw=resources.DelayedRawHandle;
            cancellation.Cancel();yield return Await(prepare);Assert.That(prepare.Result.Success,Is.False);Assert.That(resources.ReleaseCount(raw),Is.EqualTo(1));
            resources.CompleteDelayedLoad();ctrl.RollbackPreparedMap();ctrl.ReleaseAll();Assert.That(resources.ReleaseCount(raw),Is.EqualTo(1));
            cancellation.Dispose();UnityEngine.Object.DestroyImmediate(handle.Root);
        }
        [UnityTest] public IEnumerator OnlineArrowViewDuplicateAndTermination()
        {
            yield return EnterWorld(ProjectileWorld());var baseline=views.LiveInstances;
            var message=G2C_WorldProjectilesChanged.Create();message.WorldId=1;message.MapId=MapId;message.WorldRevision=2;message.ServerTime=100;
            var p=WorldProjectileInfo.Create();p.RuntimeId=101;p.OwnerUnitId=4001;p.OwnerLifecycleRevision=1;p.ProjectileConfigId="projectile.arrow.basic";p.Speed=14;p.RemainingDistance=14;p.DirectionY=1;p.StateRevision=1;p.SampleServerTime=100;message.Projectiles.Add(p);
            network.Emit(message);LifeCycle.Update();network.Emit(message);LifeCycle.Update();
            Assert.That(views.LiveInstances,Is.EqualTo(baseline+1),"重复消息不能重复实例化");
            var end=G2C_WorldProjectilesChanged.Create();end.WorldId=1;end.MapId=MapId;end.WorldRevision=3;end.ServerTime=200;var termination=WorldProjectileTermination.Create();termination.RuntimeId=101;termination.StateRevision=3;end.Terminations.Add(termination);network.Emit(end);LifeCycle.Update();
            Assert.That(views.LiveInstances,Is.EqualTo(baseline));
        }
    }
}
