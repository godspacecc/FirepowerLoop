using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game.Tests
{
    /// <summary>验证单人视角拾取事务及独立时钟；资源端口替身仅替代实际相机准备。</summary>
    public sealed class ArcadeFirstPersonTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private Game architecture;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        private CameraView view;

        [SetUp]
        public void Setup()
        {
            architecture = new Game();
            var run = new ArcadeRunModel();
            architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            var app = new AppStateModel();
            architecture.RegisterModel(app);
            app.CurrentState.Value = AppState.Playing;
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(
                File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            run.CommitSafeNode(new ArcadeRunSaveData { RunId = "first-person-test", RemainingLives = 3,
                Checkpoint = new ArcadeCheckpointSaveData { ArenaNumber = 1 } });
            run.EnterArena();
            system = new ArcadeCombatSystem();
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
            runtime = new ArcadeCombatRuntime { ArenaNumber = 1, PlayerHealth = 100, PlayerMaxHealth = 100 };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            view = new CameraView();
            typeof(ArcadeCombatSystem).GetField("catalog", Private).SetValue(system, catalog);
            typeof(ArcadeCombatSystem).GetField("arena", Private).SetValue(system, catalog.Arenas[0]);
            typeof(ArcadeCombatSystem).GetField("view", Private).SetValue(system, view);
        }

        private ArcadeDropDefinition Drop => catalog.Drops.Single(d => d.ProductId == "arcade.camera.first_person");
        private float Seconds => (float)typeof(ArcadeCombatRuntime).GetField("FirstPersonRemaining").GetValue(runtime);
        private void Call(string method, params object[] args) =>
            typeof(ArcadeCombatSystem).GetMethod(method, Private).Invoke(system, args);
        private void Add(int id) => runtime.Drops.Add(id,
            new ArcadeDropRuntime { Id = id, Definition = Drop, Position = Vector2.zero });

        [Test]
        public void MobileFirstPerson_RightScreenDragTurnsHorizontallyWithoutFiring()
        {
            var mobile = new MobileInputSystem(); architecture.RegisterSystem(mobile);
            mobile.TrySetEditorSimulation(true);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            using var controls = new PlayerInputCtrl();
            controls.Enable();
            typeof(ArcadeCombatSystem).GetField("input", Private).SetValue(system, controls);
            try
            {
                Add(1); Call("TickDrops", runtime);
                var start = new Vector2(Screen.width * .7f, Screen.height * .5f);
                InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = 71, phase = UnityEngine.InputSystem.TouchPhase.Began, position = start });
                InputSystem.Update();
                Call("ReadFirstPersonInput", runtime, Vector2.up, Vector2.right, true);
                InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = 71, phase = UnityEngine.InputSystem.TouchPhase.Moved, position = start + new Vector2(30, 20) });
                InputSystem.Update();
                Call("ReadFirstPersonInput", runtime, Vector2.up, Vector2.right, true);
                Assert.That(view.LastLook.x, Is.EqualTo(30f * 1080 / Screen.height).Within(.001f));
                Assert.That(view.LastLook.y, Is.Zero, "滑屏上下位移不能改变水平瞄准规则");
                Assert.That(runtime.Firing, Is.False, "滑动和旧右摇杆都不能触发第一人称开火");
                Assert.That(runtime.MoveDirection, Is.EqualTo(Vector2.up));
            }
            finally { InputSystem.RemoveDevice(touchscreen); mobile.TrySetEditorSimulation(false); }
        }

        [Test]
        public void MobileFirstPerson_IndependentPressFiresAndPauseDropsHeldInput()
        {
            var mobile = new MobileInputSystem(); architecture.RegisterSystem(mobile);
            mobile.TrySetEditorSimulation(true);
            try
            {
                Add(1); Call("TickDrops", runtime);
                var press = typeof(ArcadeCombatSystem).GetMethod("SetMobileFirstPersonFireHeld");
                Assert.That(press, Is.Not.Null, "需要独立触屏开火入口");
                press.Invoke(system, new object[] { true });
                Call("ReadFirstPersonInput", runtime, Vector2.zero, Vector2.zero, false);
                Assert.That(runtime.Firing, Is.True, "新按钮按下第一帧即可射击");
                press.Invoke(system, new object[] { false });
                Call("ReadFirstPersonInput", runtime, Vector2.zero, Vector2.zero, false);
                Assert.That(runtime.Firing, Is.False);
                press.Invoke(system, new object[] { true });
                architecture.GetModel<AppStateModel>().CurrentState.Value = AppState.Paused;
                system.Tick(runtime, 0);
                architecture.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
                Call("ReadFirstPersonInput", runtime, Vector2.zero, Vector2.zero, false);
                Assert.That(runtime.Firing, Is.False, "暂停前按住的开火意图不能恢复");
                press.Invoke(system, new object[] { true });
                Call("EndFirstPerson", runtime);
                Add(2); Call("TickDrops", runtime);
                Call("ReadFirstPersonInput", runtime, Vector2.zero, Vector2.zero, false);
                Assert.That(runtime.Firing, Is.False, "重新进入第一人称必须重新按下开火");
            }
            finally { mobile.TrySetEditorSimulation(false); }
        }

        [Test]
        public void MobileFirstPerson_PrefabHasGeneratedFirePressAndReadableLabel()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab");
            var collector = prefab.GetComponent<ReferenceCollector>();
            var entry = collector.data.SingleOrDefault(item => item.key == "FirstPersonFirePress");
            Assert.That(entry, Is.Not.Null, "开火组件必须通过正式引用收集器生成绑定");
            Assert.That(entry.gameObject, Is.InstanceOf<MobileCombatPressView>());
            var field = typeof(ArcadeHudUI).GetField(entry.key);
            Assert.That(field, Is.Not.Null);
            Assert.That(field.GetValue(prefab.GetComponent<ArcadeHudUI>()), Is.SameAs(entry.gameObject));
            var press = (MobileCombatPressView)entry.gameObject;
            Assert.That(press.gameObject.activeSelf, Is.False);
            var label = press.GetComponentInChildren<TMPro.TMP_Text>(true);
            Assert.That(label.text, Is.EqualTo("开火"));
            Assert.That(label.GetComponent<LocalizedText>().Key, Is.EqualTo("ui.mobile_base.fire"));
        }

        [Test]
        public void Config_OnlyFirstArenaHasPositiveTimedCameraDrop()
        {
            Assert.That(Drop.MinArena, Is.EqualTo(1));
            Assert.That(Drop.MaxArena, Is.EqualTo(1));
            Assert.That(Drop.DurationSeconds, Is.GreaterThan(0));
            Assert.That(Drop.Weight, Is.GreaterThan(0));
            var serverData = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Server/GameServer/Config/Luban"));
            Assert.That(Directory.Exists(serverData), Is.True);
            Assert.That(File.Exists(Path.Combine(serverData, "tbarcadesolodrop.bytes")), Is.False);
        }

        [Test]
        public void NormalKills_CanProduceFirstPersonOnlyInFirstArena()
        {
            typeof(ArcadeCombatSystem).GetField("random", Private).SetValue(system, new System.Random(12345));
            bool found = false;
            for (var arenaNumber = 1; arenaNumber <= 2; arenaNumber++)
            {
                runtime.ArenaNumber = arenaNumber;
                for (var index = 0; index < 1000; index++)
                {
                    var enemy = new ArcadeEnemyRuntime { Id = 100, ConfigId = "enemy.arcade.basic",
                        Position = Vector2.zero, MaxHealth = 20 };
                    runtime.Enemies.Add(enemy.Id, enemy);
                    Call("KillEnemy", runtime, enemy, false);
                    foreach (var drop in runtime.Drops.Values)
                    {
                        if (drop.Definition.ProductId != "arcade.camera.first_person") continue;
                        Assert.That(arenaNumber, Is.EqualTo(1), "后续竞技场不能获得第一场视角道具");
                        found = true;
                    }
                    // 内存夹具不创建表现资源；清空掉落预算，持续检查真实权重选择路径。
                    runtime.Drops.Clear();
                }
            }
            Assert.That(found, Is.True, "正式普通击杀掉落路径必须能够产出道具");
        }

        [Test]
        public void FailedCameraPreparation_PreservesDropAndWeapon()
        {
            view.AllowEntry = false; Add(1);
            runtime.TemporaryWeaponId = "arcade.weapon.spread"; runtime.TemporaryWeaponRemaining = 7;
            Call("TickDrops", runtime);
            Assert.That(runtime.Drops.ContainsKey(1), Is.True);
            Assert.That(view.Removed, Is.Empty);
            Assert.That(view.Feedback, Is.Zero);
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(7));
            Assert.That(Seconds, Is.Zero);
        }

        [Test]
        public void MissingCenterAim_RollsBackPreparedCameraWithoutConsumingDrop()
        {
            view.CanReadAim = false; Add(1);
            Call("TickDrops", runtime);
            Assert.That(runtime.Drops.ContainsKey(1), Is.True);
            Assert.That(Seconds, Is.Zero);
            Assert.That(view.IsFirstPersonActive, Is.False);
            Assert.That(view.Feedback, Is.Zero);
            Assert.That(view.Exits, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedPickup_RefreshesIndependentTimerWithoutRecreatingCamera()
        {
            runtime.TemporaryWeaponId = "arcade.weapon.spread"; runtime.TemporaryWeaponRemaining = 7;
            Add(1); Call("TickDrops", runtime);
            Assert.That(Seconds, Is.EqualTo(Drop.DurationSeconds));
            Call("TickTimers", runtime, 2f);
            Add(2); Call("TickDrops", runtime);
            Assert.That(Seconds, Is.EqualTo(Drop.DurationSeconds));
            Assert.That(runtime.TemporaryWeaponId, Is.EqualTo("arcade.weapon.spread"));
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(5));
            Assert.That(view.Entries, Is.EqualTo(1));
            Assert.That(view.Feedback, Is.EqualTo(2));
            Assert.That(view.Removed, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void ExpirationAndRepeatedExit_RestoreOnlyOnceWithoutResettingWeapon()
        {
            Add(1); Call("TickDrops", runtime);
            runtime.TemporaryWeaponRemaining = 40;
            Call("TickTimers", runtime, Drop.DurationSeconds);
            Call("EndFirstPerson", runtime); Call("EndFirstPerson", runtime);
            Assert.That(Seconds, Is.Zero);
            Assert.That(view.Exits, Is.EqualTo(1));
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(40 - Drop.DurationSeconds));
        }

        [Test]
        public void WeaponExpiresFirst_DoesNotEndOrRefreshCamera()
        {
            Add(1); Call("TickDrops", runtime);
            runtime.TemporaryWeaponId = "arcade.weapon.spread";
            runtime.TemporaryWeaponRemaining = 2;
            Call("TickTimers", runtime, 3f);
            Assert.That(runtime.TemporaryWeaponRemaining, Is.Zero);
            Assert.That(Seconds, Is.EqualTo(Drop.DurationSeconds - 3));
            Assert.That(view.IsFirstPersonActive, Is.True);
            Assert.That(view.Exits, Is.Zero);
        }

        [Test]
        public void PausedFrame_DoesNotAdvanceEitherTimerOrAcceptMovement()
        {
            Add(1); Call("TickDrops", runtime);
            runtime.TemporaryWeaponRemaining = 8;
            runtime.Firing = true; runtime.MoveDirection = Vector2.one;
            architecture.GetModel<AppStateModel>().CurrentState.Value = AppState.Paused;
            system.Tick(runtime, 0.05f);
            Assert.That(Seconds, Is.EqualTo(Drop.DurationSeconds));
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(8));
            Assert.That(runtime.Firing, Is.False);
            Assert.That(runtime.MoveDirection, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Hud_HasBoundHiddenCrosshairTimerAndSeparateExit()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab");
            var hud = prefab.GetComponent<ArcadeHudUI>();
            var rootField = typeof(ArcadeHudUI).GetField("FirstPersonRoot");
            Assert.That(rootField, Is.Not.Null, "第一人称 HUD 必须通过正式生成引用绑定");
            var root = (GameObject)rootField.GetValue(hud);
            Assert.That(root, Is.Not.Null);
            Assert.That(root.activeSelf, Is.False);
            Assert.That(typeof(ArcadeHudUI).GetField("TextFirstPerson").GetValue(hud), Is.Not.Null);
            var exit = (UnityEngine.UI.Button)typeof(ArcadeHudUI).GetField("BtnExitFirstPerson").GetValue(hud);
            Assert.That(exit, Is.Not.Null);
            Assert.That(root.transform.Find("Crosshair"), Is.Not.Null);
            Assert.That(root.transform.Find("Crosshair").GetComponentsInChildren<UnityEngine.UI.Image>()
                .Any(image => image.raycastTarget), Is.False);
            foreach (var button in new[] { hud.BtnBomb, hud.BtnDash, hud.BtnPause, exit })
                Assert.That(((RectTransform)button.transform).anchorMax, Is.EqualTo(Vector2.one));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void InvalidCombatState_EndsViewEvenWithoutAdvancingTime(int state)
        {
            Add(1); Call("TickDrops", runtime);
            if (state == 0) runtime.PlayerHealth = 0;
            if (state == 1) runtime.RespawnRemaining = 1;
            if (state == 2) runtime.InTreasureRoom = true;
            system.Tick(runtime, 0);
            Assert.That(Seconds, Is.Zero);
            Assert.That(view.Exits, Is.EqualTo(1));
        }

        [Test]
        public void ViewRelativeMovementAndShotsFollowSameHorizontalDirection()
        {
            Add(1); Call("TickDrops", runtime);
            runtime.MoveDirection = ArcadeCombatSystem.RelativeFirstPersonMovement(Vector2.up, Vector2.right);
            Assert.That(runtime.MoveDirection, Is.EqualTo(Vector2.right));
            Assert.That(ArcadeCombatSystem.RelativeFirstPersonMovement(Vector2.right, Vector2.right),
                Is.EqualTo(Vector2.down));
            runtime.AimDirection = Vector2.right;
            Call("TryFire", runtime);
            Assert.That(runtime.Projectiles.Count, Is.GreaterThan(0));
            var projectile = runtime.Projectiles.Values.First();
            Assert.That(projectile.Velocity.x, Is.GreaterThan(0));
            Assert.That(projectile.Velocity.y, Is.Zero);
            var enemy = new ArcadeEnemyRuntime { Id = 100, Position = new Vector2(2, 0), Health = 100,
                MaxHealth = 100 };
            var offAxis = new ArcadeEnemyRuntime { Id = 101, Position = new Vector2(0, 4), Health = 100,
                MaxHealth = 100 };
            runtime.Enemies.Add(100, enemy); runtime.Enemies.Add(101, offAxis);
            Call("TickProjectiles", runtime, 0.05f);
            Assert.That(enemy.Health, Is.LessThan(100), "真实街机弹丸结算视线前方目标");
            Assert.That(offAxis.Health, Is.EqualTo(100));
        }

        [Test]
        public void DashDirectionSamplingDoesNotApplyCameraLookTwice()
        {
            Add(1); Call("TickDrops", runtime);
            runtime.Dashes = 1;
            Call("OnHudAction", new ArcadeHudActionRequestedEvent(ArcadeHudActionKind.Dash));
            Assert.That(view.LookCalls, Is.Zero, "冲刺回调只读取当前朝向，不能提前重复推进视角");
            Call("ReadInput", runtime);
            Assert.That(view.LookCalls, Is.EqualTo(1));
            Assert.That(runtime.DashDirection, Is.EqualTo(Vector2.up));
        }

        private sealed class CameraView : IArcadeCombatViewPort
        {
            public bool AllowEntry = true;
            public bool CanReadAim = true;
            public bool IsFirstPersonActive { get; private set; }
            public int Entries, Exits, Feedback;
            public Vector2 LastLook;
            public int LookCalls;
            public void ApplyFirstPersonLook(Vector2 delta) { LastLook = delta; LookCalls++; }
            public readonly List<int> Removed = new();
            public bool TryBeginFirstPerson(out string error)
            { error = null; if (!AllowEntry) return false; Entries++; IsFirstPersonActive = true; return true; }
            public void EndFirstPerson() { Exits++; IsFirstPersonActive = false; }
            public void PlayFirstPersonPickup() => Feedback++;
            public bool TryReadFirstPersonAim(out Vector2 aim) { aim = Vector2.up; return CanReadAim; }
            public bool IsActive => true;
            public void Begin(int n, float w, float h, IReadOnlyList<ArcadeObstacleDefinition> obstacles) { }
            public void End() { }
            public bool TryScreenToArena(Vector2 screen, out Vector2 position) { position = default; return false; }
            public void SetPlayer(Vector2 p, Vector2 f, bool v, bool i) { }
            public void SetPlayerHealth(int c, int m) { }
            public void SetEnemy(int id, string config, Vector2 p, int c, int m, bool telegraph = false,
                int attackSequence = 0, bool persistentHealthBar = false) { }
            public void RemoveEnemy(int id, bool playDeath = false) { }
            public void SetProjectile(int id, bool hostile, Vector2 p, Vector2 d) { }
            public void RemoveProjectile(int id) { }
            public void SetDrop(int id, ArcadeDropDefinition d, Vector2 p) { }
            public void RemoveDrop(int id) => Removed.Add(id);
            public void SetRemotePlayer(long id, Vector2 p, Vector2 f, bool v, bool i, int c, int m) { }
            public void RemoveRemotePlayer(long id) { }
            public void TickPlayerFeedback(float seconds) { }
        }
    }
}
