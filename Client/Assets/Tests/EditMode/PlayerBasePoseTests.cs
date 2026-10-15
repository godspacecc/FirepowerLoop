using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    /// <summary>基地姿态分区的格式和内存更新边界。</summary>
    public sealed class PlayerBasePoseTests
    {
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayPlayerBasePoseTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Architecture<Game>.OnRegisterPatch = architecture =>
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(new PosePortProvider());
        }

        [TearDown]
        public void TearDown()
        {
            Architecture<Game>.OnRegisterPatch = null;
            ResourceLoader.ResetForTests();
            ResetArchitecture();
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
        [Test]
        public void CaptureAndRestoreUseStrictOptionalSectionShape()
        {
            var model = new PlayerBasePoseModel();
            Assert.That(model.Capture().ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"hasPose\":false}"));
            Assert.IsTrue(model.TrySet(new Vector3(1f, 2f, 3f), -10f, out var changed, out var error), error);
            Assert.IsTrue(changed);
            var json = model.Capture();
            Assert.That((float)json["rotationY"], Is.EqualTo(350f));
            Assert.IsTrue(model.Validate(json, out error), error);
            Assert.IsFalse(model.Validate(JObject.Parse("{\"hasPose\":false,\"positionX\":0}"), out _));
            Assert.IsFalse(model.Validate(JObject.Parse("{\"hasPose\":true,\"positionX\":1,\"positionY\":2,\"positionZ\":3,\"rotationY\":360}"), out _));
        }

        [Test]
        public void ThresholdAndNonFiniteInputDoNotOverwritePose()
        {
            var model = new PlayerBasePoseModel();
            Assert.IsTrue(model.TrySet(new Vector3(1f, 2f, 3f), 359.95f, out _, out _));
            Assert.IsTrue(model.TrySet(new Vector3(1.0005f, 2f, 3f), 0.02f, out var changed, out var error), error);
            Assert.IsFalse(changed);
            Assert.IsFalse(model.TrySet(new Vector3(float.NaN, 0f, 0f), 0f, out _, out _));
            Assert.IsTrue(model.TryGet(out var pose));
            Assert.That(pose.LocalPosition, Is.EqualTo(new Vector3(1f, 2f, 3f)));
        }

        [Test]
        public void MissingSectionAndFalseSectionClearPose()
        {
            var model = new PlayerBasePoseModel();
            model.TrySet(Vector3.one, 10f, out _, out _);
            model.Restore(null);
            Assert.IsFalse(model.HasPose);
            model.TrySet(Vector3.one, 10f, out _, out _);
            model.Restore(JObject.Parse("{\"hasPose\":false}"));
            Assert.IsFalse(model.HasPose);
        }

        [Test]
        public void BaseUnitChangedEventsCaptureOnlyValidActiveLocalPlayerAndFlushOnlyChanges()
        {
            var save = ConfigureSave();
            var created = CreateCurrentBase(save, out var error);
            var ports = (PosePortProvider)Game.Interface.GetUtility<IPlayerRuntimePortProvider>();
            var map = Game.Interface.GetModel<MapSceneModel>();
            SetValidBaseWorld(20);
            var units = Game.Interface.GetSystem<UnitSystem>();
            var playerId = Game.Interface.GetModel<PlayerModel>().PlayerUnitId;
            Assert.That(units.TryRegister(Unit(playerId, 20, true), out error), Is.True, error);

            ports.Snapshot = new PlayerMapLocalPoseSnapshot(playerId, 20, new Vector3(3f, 1f, 4f), 90f, true);
            Assert.That(units.TryUpdateTransform(playerId, new Vector3(3f, 1f, 4f), Quaternion.Euler(0f, 90f, 0f)), Is.True);
            Assert.That(save.Flush(out error), Is.True, error);
            var persisted = ReadSection();
            Assert.That(persisted["positionX"].Value<float>(), Is.EqualTo(3f));
            var updated = JObject.Parse(File.ReadAllText(SavePath()))["UpdatedAtUtc"].Value<string>();

            ports.Snapshot = new PlayerMapLocalPoseSnapshot(playerId, 20, new Vector3(3.0005f, 1f, 4f), 90.05f, true);
            units.TryUpdateTransform(playerId, new Vector3(3.0005f, 1f, 4f), Quaternion.Euler(0f, 90.05f, 0f));
            Assert.That(save.Flush(out error), Is.True, error);
            Assert.That(JObject.Parse(File.ReadAllText(SavePath()))["UpdatedAtUtc"].Value<string>(), Is.EqualTo(updated));

            map.SetLoaded(MapIds.SurvivalSample, null);
            ports.Snapshot = new PlayerMapLocalPoseSnapshot(playerId, 20, new Vector3(99f, 0f, 0f), 180f, true);
            units.TryUpdateTransform(playerId, new Vector3(99f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f));
            SetValidBaseWorld(20);
            ports.Snapshot = new PlayerMapLocalPoseSnapshot(999, 20, new Vector3(99f, 0f, 0f), 180f, true);
            units.TryUpdateTransform(playerId, new Vector3(100f, 0f, 0f), Quaternion.Euler(0f, 181f, 0f));
            ports.Available = false;
            units.TryUpdateTransform(playerId, new Vector3(101f, 0f, 0f), Quaternion.Euler(0f, 182f, 0f));
            Assert.That(save.Flush(out error), Is.True, error);
            Assert.That(ReadSection()["positionX"].Value<float>(), Is.EqualTo(3f), "exploration, identity mismatch and unavailable port must not overwrite base pose");
        }

        [Test]
        public void MapMismatchInactiveAndNonPlayerEventsLeavePersistedBasePoseUntouched()
        {
            var save = ConfigureSave();
            var created = CreateCurrentBase(save, out var error);
            var model = Game.Interface.GetModel<PlayerBasePoseModel>();
            model.Restore(JObject.Parse("{\"hasPose\":true,\"positionX\":4,\"positionY\":5,\"positionZ\":6,\"rotationY\":70}"));
            save.MarkDirty("seed"); Assert.That(save.Flush(out error), Is.True, error);
            var before = JObject.Parse(File.ReadAllText(SavePath()));
            var ports = (PosePortProvider)Game.Interface.GetUtility<IPlayerRuntimePortProvider>();
            SetValidBaseWorld(50);
            var units = Game.Interface.GetSystem<UnitSystem>(); var player = PlayerModel.LocalPlayerUnitId;
            Assert.That(units.TryRegister(Unit(player, 50, true), out error), Is.True, error);
            ports.Snapshot = new PlayerMapLocalPoseSnapshot(player, 51, new Vector3(1, 1, 1), 1, true);
            units.TryUpdateTransform(player, Vector3.one, Quaternion.Euler(0, 1, 0));
            Assert.That(units.TrySetActive(player, false), Is.True);
            ports.Snapshot = new PlayerMapLocalPoseSnapshot(player, 50, new Vector3(2, 2, 2), 2, true);
            units.TryUpdateTransform(player, new Vector3(2, 2, 2), Quaternion.Euler(0, 2, 0));
            Assert.That(units.TryRegister(new UnitSnapshot(2, 50, UnitType.Enemy, UnitSource.MapPlaced, "enemy", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out error), Is.True, error);
            ports.Snapshot = new PlayerMapLocalPoseSnapshot(2, 50, new Vector3(3, 3, 3), 3, true);
            units.TryUpdateTransform(2, new Vector3(3, 3, 3), Quaternion.Euler(0, 3, 0));
            Assert.That(save.Flush(out error), Is.True, error);
            var after = JObject.Parse(File.ReadAllText(SavePath()));
            Assert.That(after["UpdatedAtUtc"].Value<string>(), Is.EqualTo(before["UpdatedAtUtc"].Value<string>()));
            Assert.That(after["Sections"][PlayerBasePoseModel.SaveSectionId].ToString(), Is.EqualTo(before["Sections"][PlayerBasePoseModel.SaveSectionId].ToString()));
        }

        [Test]
        public void PlayerCtrlPreparationUsesBasePoseOnlyForBaseAndCommitRegistersWorldTransform()
        {
            var save = ConfigureSave();
            var created = CreateCurrentBase(save, out var error);
            Game.Interface.GetModel<PlayerBasePoseModel>().Restore(JObject.Parse("{\"hasPose\":true,\"positionX\":7,\"positionY\":2,\"positionZ\":5,\"rotationY\":135}"));
            var prefab = CreatePlayerPrefab();
            var sword = new GameObject("sword"); var bow = new GameObject("bow");
            ResourceLoader.SetLoaderForTests(new PlayerAppearanceResourceLoader(prefab, sword, bow));
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(new PlayerAppearanceConfigProvider());
            var camera = new GameObject("Main Camera", typeof(Camera)); camera.tag = "MainCamera";
            var root = new GameObject("root").transform; root.position = new Vector3(10f, 0f, 20f); root.rotation = Quaternion.Euler(0f, 45f, 0f);
            var context = CreateContext(MapIds.Base, 41, root, new Vector3(1f, 0f, 2f), 10f);
            var ctrl = new PlayerCtrl(Game.Interface);
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(ctrl);
            try
            {
                Assert.That(ctrl.PrepareMapAsync(context, default).GetAwaiter().GetResult().Success, Is.True);
                Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
                Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var unit), Is.True);
                Assert.That(Vector3.Distance(unit.Position, root.TransformPoint(new Vector3(7f, 2f, 5f))), Is.LessThan(0.001f));
                Assert.That(unit.Rotation.eulerAngles.y, Is.EqualTo((root.rotation * Quaternion.Euler(0f, 135f, 0f)).eulerAngles.y).Within(0.01f));
            }
            finally { ctrl.ReleaseAll(); UnityEngine.Object.DestroyImmediate(camera); DestroyContext(context.Scene); UnityEngine.Object.DestroyImmediate(sword); UnityEngine.Object.DestroyImmediate(bow); UnityEngine.Object.DestroyImmediate(prefab.GetComponent<PlayerView>().CameraProfile); UnityEngine.Object.DestroyImmediate(prefab); }
        }

        [Test]
        public void PlayerCtrlUsesConfiguredSpawnWithoutBasePoseAndIgnoresItOnExploration()
        {
            var save = ConfigureSave(); var created = CreateCurrentBase(save, out var error);
            var prefab = CreatePlayerPrefab(); var sword = new GameObject("sword"); var bow = new GameObject("bow");
            ResourceLoader.SetLoaderForTests(new PlayerAppearanceResourceLoader(prefab, sword, bow));
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(new PlayerAppearanceConfigProvider());
            var camera = new GameObject("Main Camera", typeof(Camera)); camera.tag = "MainCamera";
            var baseRoot = new GameObject("base-root").transform; var baseContext = CreateContext(MapIds.Base, 61, baseRoot, new Vector3(2f, 0f, 3f), 25f);
            var ctrl = new PlayerCtrl(Game.Interface);
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(ctrl);
            try
            {
                Assert.That(ctrl.PrepareMapAsync(baseContext, default).GetAwaiter().GetResult().Success, Is.True);
                Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
                Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var baseUnit), Is.True);
                Assert.That(Vector3.Distance(baseUnit.Position, baseRoot.TransformPoint(new Vector3(2f, 0f, 3f))), Is.LessThan(0.001f));
                ctrl.ReleaseCurrentMap();
                Game.Interface.GetModel<PlayerBasePoseModel>().Restore(JObject.Parse("{\"hasPose\":true,\"positionX\":9,\"positionY\":0,\"positionZ\":9,\"rotationY\":170}"));
                var exploreRoot = new GameObject("explore-root").transform; var exploreContext = CreateContext(MapIds.SurvivalSample, 62, exploreRoot, new Vector3(-2f, 0f, 4f), 35f);
                try
                {
                    Assert.That(ctrl.PrepareMapAsync(exploreContext, default).GetAwaiter().GetResult().Success, Is.True);
                    Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
                    Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var exploreUnit), Is.True);
                    Assert.That(Vector3.Distance(exploreUnit.Position, exploreRoot.TransformPoint(new Vector3(-2f, 0f, 4f))), Is.LessThan(0.001f));
                    Assert.That(Game.Interface.GetModel<PlayerBasePoseModel>().TryGet(out var stored), Is.True);
                    Assert.That(stored.LocalPosition, Is.EqualTo(new Vector3(9f, 0f, 9f)));
                }
                finally { DestroyContext(exploreContext.Scene); }
            }
            finally { ctrl.ReleaseAll(); UnityEngine.Object.DestroyImmediate(camera); DestroyContext(baseContext.Scene); UnityEngine.Object.DestroyImmediate(sword); UnityEngine.Object.DestroyImmediate(bow); UnityEngine.Object.DestroyImmediate(prefab.GetComponent<PlayerView>().CameraProfile); UnityEngine.Object.DestroyImmediate(prefab); }
        }

        [Test]
        public void PlayerCtrlFailureCancellationAndRollbackDoNotChangePoseOrLeaveUnit()
        {
            var save = ConfigureSave(); var created = CreateCurrentBase(save, out var error);
            var pose = Game.Interface.GetModel<PlayerBasePoseModel>(); pose.Restore(JObject.Parse("{\"hasPose\":true,\"positionX\":6,\"positionY\":0,\"positionZ\":7,\"rotationY\":80}")); var expected = pose.Capture().ToString();
            var prefab = CreatePlayerPrefab(); var sword = new GameObject("sword"); var bow = new GameObject("bow");
            ResourceLoader.SetLoaderForTests(new PlayerAppearanceResourceLoader(prefab, sword, bow)); Game.Interface.RegisterUtility<IRuntimeConfigProvider>(new PlayerAppearanceConfigProvider());
            var camera = new GameObject("Main Camera", typeof(Camera)); camera.tag = "MainCamera"; var root = new GameObject("root").transform; var valid = CreateContext(MapIds.Base, 71, root, Vector3.zero, 0f); var ctrl = new PlayerCtrl(Game.Interface);
            try
            {
                var invalid = new MapPreparationContext(MapIds.Base, 70, valid.Scene, Array.Empty<MapSpawnDefinition>());
                Assert.That(ctrl.PrepareMapAsync(invalid, default).GetAwaiter().GetResult().Success, Is.False);
                using (var cts = new System.Threading.CancellationTokenSource()) { cts.Cancel(); Assert.That(ctrl.PrepareMapAsync(valid, cts.Token).GetAwaiter().GetResult().Success, Is.False); }
                Assert.That(ctrl.PrepareMapAsync(valid, default).GetAwaiter().GetResult().Success, Is.True); ctrl.RollbackPreparedMap();
                Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out _), Is.False);
                Assert.That(pose.Capture().ToString(), Is.EqualTo(expected));
            }
            finally { ctrl.ReleaseAll(); UnityEngine.Object.DestroyImmediate(camera); DestroyContext(valid.Scene); UnityEngine.Object.DestroyImmediate(sword); UnityEngine.Object.DestroyImmediate(bow); UnityEngine.Object.DestroyImmediate(prefab.GetComponent<PlayerView>().CameraProfile); UnityEngine.Object.DestroyImmediate(prefab); }
        }

        [Test]
        public void ApplicationQuitCapturesCurrentBasePoseFlushesAndRestoresOnNextArchitecture()
        {
            var save = ConfigureSave(); var created = CreateCurrentBase(save, out var error);
            var player = Game.Interface.GetModel<PlayerModel>().PlayerUnitId;
            SetValidBaseWorld(81);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(Unit(player, 81, true), out error), Is.True, error);
            ((PosePortProvider)Game.Interface.GetUtility<IPlayerRuntimePortProvider>()).Snapshot =
                new PlayerMapLocalPoseSnapshot(player, 81, new Vector3(12f, 3f, -4f), 275f, true);

            InvokeApplicationQuit();
            var persisted = ReadSection();
            Assert.That(persisted["hasPose"].Value<bool>(), Is.True);
            Assert.That(persisted["positionX"].Value<float>(), Is.EqualTo(12f));
            Assert.That(persisted["rotationY"].Value<float>(), Is.EqualTo(275f));

            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = architecture => architecture.RegisterUtility<IPlayerRuntimePortProvider>(new PosePortProvider());
            var reloaded = ConfigureSave(); var prepared = reloaded.PrepareBase();
            Assert.That(prepared.IsValid, Is.True, prepared.Error);
            Assert.That(reloaded.CommitBaseRuntime(prepared.Data, out error), Is.True, error);
            Assert.That(Game.Interface.GetModel<PlayerBasePoseModel>().TryGet(out var restored), Is.True);
            Assert.That(restored.LocalPosition, Is.EqualTo(new Vector3(12f, 3f, -4f)));
        }

        [Test]
        public void ApplicationQuitFromExplorationDoesNotOverwriteSavedBasePoseAndStillDeinitializes()
        {
            var save = ConfigureSave(); var created = CreateCurrentBase(save, out var error);
            var pose = Game.Interface.GetModel<PlayerBasePoseModel>();
            SetValidBaseWorld(82);
            Assert.That(pose.TrySet(new Vector3(4f, 1f, 2f), 30f, out _, out error), Is.True, error);
            save.MarkDirty("seed base pose"); Assert.That(save.Flush(out error), Is.True, error);
            var before = ReadSection().ToString();
            var player = Game.Interface.GetModel<PlayerModel>().PlayerUnitId;
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(Unit(player, 82, true), out error), Is.True, error);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            ((PosePortProvider)Game.Interface.GetUtility<IPlayerRuntimePortProvider>()).Snapshot =
                new PlayerMapLocalPoseSnapshot(player, 82, new Vector3(99f, 0f, 99f), 180f, true);

            Assert.DoesNotThrow(() => InvokeApplicationQuit());
            Assert.That(ReadSection().ToString(), Is.EqualTo(before));
            Assert.DoesNotThrow(() => _ = Game.Interface);
        }

        [Test]
        public void ApplicationQuitCaptureFailureStillFlushesExistingDirtyState()
        {
            var save = ConfigureSave(); var created = CreateCurrentBase(save, out var error);
            var player = Game.Interface.GetModel<PlayerModel>().PlayerUnitId;
            SetValidBaseWorld(83);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(Unit(player, 83, true), out error), Is.True, error);
            var port = (PosePortProvider)Game.Interface.GetUtility<IPlayerRuntimePortProvider>(); port.Available = false;
            Assert.That(Game.Interface.GetModel<PlayerBasePoseModel>().TrySet(new Vector3(6f, 0f, 8f), 60f, out _, out error), Is.True, error);
            save.MarkDirty("pre-existing dirty state");
            LogAssert.Expect(LogType.Assert, "Assertion failed on expression: 'ShouldRunBehaviour()'");
            LogAssert.Expect(LogType.Error, "[GameStart] 退出前基地姿态捕获失败：unavailable");

            InvokeApplicationQuit(false);
            Assert.That(ReadSection()["positionX"].Value<float>(), Is.EqualTo(6f));
        }

        [Test]
        public void ApplicationQuitWithoutCurrentSaveOrActivePlayerSafelySkipsPoseCapture()
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Assert.DoesNotThrow(() => InvokeApplicationQuit());
            Assert.DoesNotThrow(() => _ = Game.Interface);
        }

        [Test]
        public void ApplicationQuitFlushFailureStillDeinitializesAndClearsLifecycle()
        {
            var save = ConfigureSave(); var created = CreateCurrentBase(save, out var error);
            SetValidBaseWorld(84);
            Game.Interface.GetModel<PlayerBasePoseModel>().TrySet(Vector3.one, 10f, out _, out _); save.MarkDirty("force failure");
            Directory.Delete(tempRoot, true); File.WriteAllText(tempRoot, "not a directory");
            LogAssert.Expect(LogType.Assert, "Assertion failed on expression: 'ShouldRunBehaviour()'");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[GameStart\\] 退出前存档刷新失败："));
            InvokeApplicationQuit(false);
            Assert.DoesNotThrow(() => _ = Game.Interface);
        }

        private static void InvokeApplicationQuit(bool expectEditModeAssert = true)
        {
            var gameStart = new GameObject("game-start").AddComponent<GameStart>();
            // EditMode 中 Unity 会在分发应用退出消息时额外记录该受控 Assert；消息仍会进入真实入口。
            if (expectEditModeAssert) LogAssert.Expect(LogType.Assert, "Assertion failed on expression: 'ShouldRunBehaviour()'");
            gameStart.SendMessage("OnApplicationQuit", SendMessageOptions.RequireReceiver);
            UnityEngine.Object.DestroyImmediate(gameStart.gameObject);
        }

        private SaveSystem ConfigureSave()
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            save.ConfigureForTests(tempRoot);
            return save;
        }

        private static BaseSaveData CreateCurrentBase(SaveSystem save, out string error)
        {
            Assert.That(save.CreateBase(out var data, out error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(data, out error), Is.True, error);
            return data;
        }

        private static void SetValidBaseWorld(long mapInstanceId)
        {
            var context = new GameObject("base-context").AddComponent<MapContext>();
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base,
                new TestMapHandle(MapIds.Base, context));
        }

        private string SavePath() => Path.Combine(tempRoot, "Saves", "base.json");
        private JToken ReadSection() => JObject.Parse(File.ReadAllText(SavePath()))["Sections"][PlayerBasePoseModel.SaveSectionId];
        private static UnitSnapshot Unit(int id, long mapId, bool active) => new UnitSnapshot(id, mapId, UnitType.Player, UnitSource.Player, "player", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, active);
        private static MapPreparationContext CreateContext(string mapId, long instanceId, Transform root, Vector3 spawn, float yaw)
        {
            var presentation = new GameObject("presentation"); root.SetParent(presentation.transform, true);
            var go = new GameObject("context"); var context = go.AddComponent<MapContext>();
            typeof(MapContext).GetField("mapContentRoot", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(context, presentation);
            typeof(MapContext).GetField("dynamicObjectRoot", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(context, root);
            return new MapPreparationContext(mapId, instanceId, context, new[] { new MapSpawnDefinition("player", mapId, MapSpawnObjectType.Player, "player", spawn.x, spawn.y, spawn.z, yaw) });
        }
        private static GameObject CreatePlayerPrefab()
        {
            var player = new GameObject("player", typeof(CharacterController), typeof(PlayerView));
            var tili = new GameObject("tili"); tili.transform.SetParent(player.transform);
            var tiliFill = new GameObject("tili-fill", typeof(UnityEngine.UI.Image)).GetComponent<UnityEngine.UI.Image>(); tiliFill.transform.SetParent(tili.transform);
            var melee = new GameObject("melee").transform; melee.SetParent(player.transform);
            var ranged = new GameObject("ranged").transform; ranged.SetParent(player.transform);
            var view = player.GetComponent<PlayerView>();
            Set(view, "cameraProfile", ScriptableObject.CreateInstance<TopDownCameraProfile>()); Set(view, "tiliRingCanvas", tili); Set(view, "tiliRingFill", tiliFill); Set(view, "meleeWeaponAttachRoot", melee); Set(view, "rangedWeaponAttachRoot", ranged);
            return player;
        }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        private static void DestroyContext(MapContext context) { if (context == null) return; UnityEngine.Object.DestroyImmediate(context.MapContentRoot); UnityEngine.Object.DestroyImmediate(context.gameObject); }
        private static void ResetArchitecture() { typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null); LifeCycle.ClearForTests(); }

        private sealed class PosePortProvider : IPlayerRuntimePortProvider
        {
            public bool Available = true;
            public PlayerMapLocalPoseSnapshot Snapshot;
            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port) { port = null; return false; }
            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port) { port = null; return false; }
            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider) => false;
            public bool TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot snapshot, out string error) { snapshot = Snapshot; error = Available ? null : "unavailable"; return Available; }
        }

        private sealed class TestMapHandle : IMapContentHandle
        {
            public TestMapHandle(string mapId, MapContext context) { MapId = mapId; Context = context; }
            public string MapId { get; }
            public MapContext Context { get; }
            public bool IsValid => true;
        }
    }
}
