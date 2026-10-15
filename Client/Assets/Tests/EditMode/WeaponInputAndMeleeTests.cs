using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class WeaponInputAndMeleeTests
    {
        private const long MapInstanceId = 41;
        private string tempRoot;
        private FakePlayerPort playerPort;
        private GameObject blockingUiObject;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            playerPort = new FakePlayerPort();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new TestConfigProvider());
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(new FakePlayerPortProvider(playerPort));
            };

            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayWeaponInputAndMeleeTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 1, 1f, 1f, 1f, 1f, 5f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            RegisterPlayer(Vector3.zero);
            _ = Game.Interface.GetSystem<InventorySystem>();
            _ = Game.Interface.GetSystem<WeaponEquipmentSystem>();
            _ = Game.Interface.GetSystem<WeaponSystem>();
            _ = Game.Interface.GetSystem<CombatSystem>();
            Game.Interface.SendEvent(new PlayerReadyEvent(PlayerModel.LocalPlayerUnitId, MapInstanceId));
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            PlayerInputSystem.SetInputReaderFactoryForTests(null);
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }

            if (blockingUiObject != null)
            {
                UnityEngine.Object.DestroyImmediate(blockingUiObject);
                blockingUiObject = null;
            }
        }

        [Test]
        public void InputBindingsAndAimGateAreFixed()
        {
            var inputCtrl = File.ReadAllText("Assets/Game/Scripts/Controller/Player/PlayerInputCtrl.cs");
            var inputSystem = File.ReadAllText("Assets/Game/Scripts/System/Player/PlayerInputSystem.cs");

            Assert.That(inputCtrl, Does.Contain("\"PlayerAttack\""));
            Assert.That(inputCtrl, Does.Contain("<Mouse>/leftButton"));
            Assert.That(inputCtrl, Does.Contain("<Keyboard>/1"));
            Assert.That(inputCtrl, Does.Contain("<Keyboard>/2"));
            Assert.That(inputCtrl, Does.Not.Contain("<Keyboard>/space"));
            Assert.That(inputSystem, Does.Contain("PlayerAttackRequestedEvent(Vector3 aimDirection)"));
            Assert.That(inputSystem, Does.Contain("WeaponSlotSwitchRequestedEvent(WeaponSlot slot)"));
            Assert.That(inputCtrl, Does.Contain("<Pointer>/position"));
            Assert.That(inputCtrl, Does.Not.Contain("<Touchscreen>/touch*/press"));
            Assert.That(inputCtrl, Does.Contain("<Mouse>/delta"));
            Assert.That(inputSystem, Does.Not.Contain("Camera.main"));
            // 建造触点可以读取鼠标；武器瞄准仍必须通过相机端口。
            var aimMethod = inputSystem.Substring(inputSystem.IndexOf("internal bool TryGetAimRay"), inputSystem.IndexOf("private PlayerAttackRequestedEvent CreateAttackRequest") - inputSystem.IndexOf("internal bool TryGetAimRay"));
            Assert.That(aimMethod, Does.Not.Contain("Mouse.current"));
            Assert.That(inputCtrl, Does.Contain("<Keyboard>/h"));
            Assert.That(inputSystem, Does.Contain("TryCreateAimRay"));
            Assert.That(inputSystem, Does.Contain("new Plane(Vector3.up, origin)"));
            Assert.That(inputSystem, Does.Contain("offset.sqrMagnitude < AimDeadZoneSqr"));
        }

        [Test]
        public void ReloadInputPublishesInGameplayButBlockedUiDoesNotLeak()
        {
            var architecture = (Game)Game.Interface;
            var input = new ReloadInputFake();
            PlayerInputSystem.SetInputReaderFactoryForTests(() => input);
            architecture.RegisterSystem(new PlayerInputSystem());
            var inputSystem = architecture.GetSystem<PlayerInputSystem>();
            InvokePlayerInputUpdate(inputSystem); // 先启用测试输入。

            var bag = architecture.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Bow, 1, 1, out _, out var error), Is.True, error);
            Assert.That(architecture.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            Assert.That(bag.TryAdd(ItemIds.Arrow, 1, 20, out _, out error), Is.True, error);
            var reloadRequests = 0;
            var unregister = architecture.RegisterEvent<PlayerReloadRequestedEvent>(_ => reloadRequests++);
            try
            {
                input.ReloadPressedThisFrame = true;
                InvokePlayerInputUpdate(inputSystem);
                Assert.That(reloadRequests, Is.EqualTo(1));
                Assert.That(architecture.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var action), Is.True);
                Assert.That(action.Kind, Is.EqualTo(PlayerActionKind.Reload));

                architecture.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.UserCanceled);
                input.ReloadPressedThisFrame = false;
                SetBlockingBagUi();
                input.ReloadPressedThisFrame = true;
                InvokePlayerInputUpdate(inputSystem);
                Assert.That(reloadRequests, Is.EqualTo(1), "阻断 UI 打开时 R 不得发布换弹请求。");
                Assert.That(architecture.GetSystem<PlayerActionSystem>().TryGetSnapshot(out _), Is.False);
            }
            finally
            {
                unregister.UnRegister();
                PlayerInputSystem.SetInputReaderFactoryForTests(null);
                UIHelper.ResetForTests();
            }
        }

        [Test]
        public void HolsterInputClearsHeldReferenceAndBlockedUiDoesNotLeak()
        {
            var architecture = (Game)Game.Interface;
            var input = new ReloadInputFake();
            PlayerInputSystem.SetInputReaderFactoryForTests(() => input);
            architecture.RegisterSystem(new PlayerInputSystem());
            var inputSystem = architecture.GetSystem<PlayerInputSystem>();
            InvokePlayerInputUpdate(inputSystem);
            var bag = architecture.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Bow, 1, 1, out _, out var error), Is.True, error);
            var held = architecture.GetSystem<WeaponEquipmentSystem>();
            Assert.That(held.TryEquipFromPlayerBag(0, out error), Is.True, error);
            input.HolsterPressedThisFrame = true;
            InvokePlayerInputUpdate(inputSystem);
            Assert.That(held.CaptureHeldSnapshot().ItemId, Is.Zero);
            Assert.That(bag.Slots.Values.Where(s => s.ItemId == ItemIds.Bow).Sum(s => s.Amount), Is.EqualTo(1));
            Assert.That(held.TryEquipFromPlayerBag(0, out error), Is.True, error);
            SetBlockingBagUi();
            InvokePlayerInputUpdate(inputSystem);
            Assert.That(held.CaptureHeldSnapshot().ItemId, Is.EqualTo(ItemIds.Bow));
        }

        [Test]
        public void ExplicitDamageUsesUnifiedLifeFlow()
        {
            var combat = Game.Interface.GetSystem<CombatSystem>();
            RegisterEnemy(10, MapInstanceId, new Vector3(0f, 0f, 1f), 10);
            RegisterEnemy(11, MapInstanceId + 1, new Vector3(0f, 0f, 1f), 10);
            var requested = 0;
            var applied = 0;
            var deaths = 0;
            Game.Interface.RegisterEvent<CombatDamageRequestedEvent>(evt =>
            {
                if (evt.TargetUnitId == 10 && evt.Damage == 10)
                {
                    requested++;
                }
            });
            Game.Interface.RegisterEvent<CombatDamageAppliedEvent>(evt =>
            {
                if (evt.TargetUnitId == 10 && evt.Damage == 10)
                {
                    applied++;
                }
            });
            Game.Interface.RegisterEvent<CombatantDiedEvent>(evt =>
            {
                if (evt.UnitId == 10 && evt.KillerUnitId == PlayerModel.LocalPlayerUnitId)
                {
                    deaths++;
                }
            });

            Assert.That(combat.TryDamage(PlayerModel.LocalPlayerUnitId, 10, 0), Is.False);
            Assert.That(combat.TryDamage(PlayerModel.LocalPlayerUnitId, 11, 10), Is.False);
            Assert.That(combat.TryDamage(PlayerModel.LocalPlayerUnitId, 10, 10), Is.True);
            Assert.That(combat.TryDamage(PlayerModel.LocalPlayerUnitId, 10, 10), Is.False);

            Assert.That(requested, Is.EqualTo(1));
            Assert.That(applied, Is.EqualTo(1));
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(10, out var after), Is.True);
            Assert.That(after.HP, Is.EqualTo(0));
        }

        [Test]
        public void SwordConeHitsEachValidEnemyOnce()
        {
            var cues = new List<CombatSoundCueEvent>();
            Game.Interface.RegisterEvent<CombatSoundCueEvent>(cues.Add);
            EquipSword();
            RegisterEnemy(10, MapInstanceId, new Vector3(0f, 0f, 2.2f), 30);
            RegisterEnemy(11, MapInstanceId, new Vector3(1.55f, 0f, 1.55f), 30);
            RegisterEnemy(12, MapInstanceId, new Vector3(2.2f, 0f, 0f), 30);
            RegisterEnemy(13, MapInstanceId, new Vector3(0f, 0f, 2.25f), 30);
            RegisterEnemy(14, MapInstanceId + 1, new Vector3(0f, 0f, 1f), 30);
            var damageByTarget = new Dictionary<int, int>();
            Game.Interface.RegisterEvent<CombatDamageAppliedEvent>(evt =>
            {
                damageByTarget[evt.TargetUnitId] = damageByTarget.TryGetValue(evt.TargetUnitId, out var value)
                    ? value + evt.Damage
                    : evt.Damage;
            });

            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));

            Assert.That(damageByTarget.TryGetValue(10, out var firstDamage), Is.True);
            Assert.That(firstDamage, Is.EqualTo(10));
            Assert.That(damageByTarget.TryGetValue(11, out var secondDamage), Is.True);
            Assert.That(secondDamage, Is.EqualTo(10));
            Assert.That(damageByTarget.ContainsKey(12), Is.False);
            Assert.That(damageByTarget.ContainsKey(13), Is.False);
            Assert.That(damageByTarget.ContainsKey(14), Is.False);
            Assert.That(playerPort.FaceCount, Is.EqualTo(0));
            Assert.That(playerPort.SwingCount, Is.EqualTo(1));
            Assert.That(playerPort.AttackFeedbackCount, Is.EqualTo(0));
            Assert.That(cues.Count(c => c.Kind == CombatSoundCueKind.Swing), Is.EqualTo(1));
            Assert.That(cues.Count(c => c.Kind == CombatSoundCueKind.MeleeBody), Is.EqualTo(2));
        }

        [TestCase(16, 10)]
        [TestCase(17, 10)]
        [TestCase(18, 10)]
        [TestCase(32, 14)]
        [TestCase(33, 14)]
        public void SingleSlashWeaponsKeepImmediateSingleHitAndOriginalCooldown(int itemId, int damage)
        {
            // 动作替换后仍用正式物品/武器配置走接受与结算入口，不用表现动画决定伤害时刻。
            ((Game)Game.Interface).RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() =>
                new Tables(name => new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,
                    "Game/Config", name + ".bytes"))))));
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(itemId, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            RegisterEnemy(80, MapInstanceId, Vector3.forward, 100);
            var system = Game.Interface.GetSystem<WeaponSystem>();
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(EnemyHP(80), Is.EqualTo(100 - damage), "普通攻击仍在接受时立即结算");
            Assert.That(playerPort.SwingCount, Is.EqualTo(1));
            Assert.That(playerPort.ComboStages, Is.Empty);
            Assert.That(system.CooldownRemainingSeconds, Is.EqualTo(0.45f));
            system.TickCooldown(0.44f);
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(EnemyHP(80), Is.EqualTo(100 - damage), "冷却中输入不能缓存为连击");
            Assert.That(playerPort.SwingCount, Is.EqualTo(1));
            system.TickCooldown(0.02f);
            Assert.That(EnemyHP(80), Is.EqualTo(100 - damage), "动作结束不能额外命中");
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(EnemyHP(80), Is.EqualTo(100 - damage * 2));
            Assert.That(playerPort.SwingCount, Is.EqualTo(2));
        }

        [Test]
        public void UnarmedPunchHitsNearestValidEnemyOnceWithStableTieBreak()
        {
            var cues = new List<CombatSoundCueEvent>();
            Game.Interface.RegisterEvent<CombatSoundCueEvent>(cues.Add);
            RegisterEnemy(10, MapInstanceId, new Vector3(0f, 0f, 1.2f), 10);
            RegisterEnemy(8, MapInstanceId, new Vector3(0.2f, 0f, 1f), 10);
            RegisterEnemy(7, MapInstanceId, new Vector3(-0.2f, 0f, 1f), 10);
            RegisterEnemy(11, MapInstanceId, new Vector3(0f, 0f, -1f), 10);
            RegisterEnemy(12, MapInstanceId, new Vector3(0f, 0f, 1.41f), 10);
            RegisterEnemy(13, MapInstanceId, new Vector3(0.8f, 0f, 0.8f), 10);
            RegisterEnemy(14, MapInstanceId + 1, new Vector3(0f, 0f, 1f), 10);
            RegisterEnemy(15, MapInstanceId, new Vector3(0f, 0f, 0.9f), 10, CombatFaction.Player);
            RegisterEnemy(16, MapInstanceId, new Vector3(0f, 0f, 0.8f), 10, CombatFaction.Enemy, false);
            RegisterEnemy(17, MapInstanceId, new Vector3(0f, 0f, 0.7f), 1);
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, 17, 1), Is.True);
            var damageByTarget = new Dictionary<int, int>();
            Game.Interface.RegisterEvent<CombatDamageAppliedEvent>(evt =>
            {
                damageByTarget[evt.TargetUnitId] = damageByTarget.TryGetValue(evt.TargetUnitId, out var value)
                    ? value + evt.Damage
                    : evt.Damage;
            });

            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));

            Assert.That(damageByTarget.Count, Is.EqualTo(1));
            Assert.That(damageByTarget.TryGetValue(7, out var damage), Is.True);
            Assert.That(damage, Is.EqualTo(3));
            Assert.That(playerPort.AttackFeedbackCount, Is.EqualTo(1));
            Assert.That(playerPort.SwingCount, Is.EqualTo(0));
            Assert.That(playerPort.FaceCount, Is.EqualTo(0));
            Assert.That(GetWeaponCooldown(), Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(cues.Count(c => c.Kind == CombatSoundCueKind.Swing), Is.EqualTo(1));
            Assert.That(cues.Count(c => c.Kind == CombatSoundCueKind.PunchBody), Is.EqualTo(1));
        }

        [Test]
        public void UnarmedWhiffFeedbackCooldownAndGateFailuresHaveNoDamageSideEffects()
        {
            var cues = new List<CombatSoundCueEvent>();
            Game.Interface.RegisterEvent<CombatSoundCueEvent>(cues.Add);
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));

            Assert.That(playerPort.AttackFeedbackCount, Is.EqualTo(1));
            Assert.That(playerPort.SwingCount, Is.EqualTo(0));
            Assert.That(GetWeaponCooldown(), Is.EqualTo(0.6f).Within(0.001f));

            RegisterEnemy(30, MapInstanceId, new Vector3(0f, 0f, 1f), 10);
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(30, out var target), Is.True);
            Assert.That(target.HP, Is.EqualTo(10));
            Assert.That(playerPort.AttackFeedbackCount, Is.EqualTo(1));

            SetWeaponCooldown(0f);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(playerPort.AttackFeedbackCount, Is.EqualTo(1));
            Assert.That(GetWeaponCooldown(), Is.EqualTo(0f).Within(0.001f));
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(30, out target), Is.True);
            Assert.That(target.HP, Is.EqualTo(10));

            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            playerPort.IsValid = false;
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(playerPort.AttackFeedbackCount, Is.EqualTo(1));
            Assert.That(GetWeaponCooldown(), Is.EqualTo(0f).Within(0.001f));
            Assert.That(cues.Count(c => c.Kind == CombatSoundCueKind.Swing), Is.EqualTo(1));
            Assert.That(cues.Any(c => c.Kind == CombatSoundCueKind.PunchBody), Is.False);
        }

        [Test]
        public void SwitchingCannotBypassCooldown()
        {
            EquipSwordAndBow();
            RegisterEnemy(20, MapInstanceId, new Vector3(0f, 0f, 1f), 40);
            var damage = 0;
            Game.Interface.RegisterEvent<CombatDamageAppliedEvent>(evt =>
            {
                if (evt.TargetUnitId == 20)
                {
                    damage += evt.Damage;
                }
            });

            Game.Interface.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.Melee));
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Game.Interface.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.Ranged));
            Game.Interface.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.Melee));
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));

            Assert.That(damage, Is.EqualTo(10));
            Assert.That(playerPort.SwingCount, Is.EqualTo(1));

            SetWeaponCooldown(0f);
            Game.Interface.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.Melee));
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));

            Assert.That(damage, Is.EqualTo(20));
            Assert.That(playerPort.SwingCount, Is.EqualTo(2));
        }

        [Test]
        public void RepeatedEmptyMagazineAttackShowsOnlyOneAmmoTip()
        {
            TimerMgr.Instance.ClearForTests();
            GameObjectPoolHelper.ClearForTests();
            ResourceLoader.SetLoaderForTests(new AmmoTipResourceLoader());
            LocalizationLanguages.ResetForTests();
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            var poolRoot = new GameObject(SceneRoot.PoolRootName);
            foreach (var name in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
                new GameObject(name, typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("EventSystem", typeof(RectTransform), typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule)).transform.SetParent(root.transform, false);
            root.AddComponent<UIRoot>();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            try
            {
                EquipBow();
                Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.Zero);
                for (var i = 0; i < 30; i++) Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
                var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
                Assert.That(ctrl, Is.Not.Null, "实际武器缺弹分支必须产生提示");
                var ui = (FloatUI)typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ctrl);
                TimerMgr.Instance.TickForTests(.31f);
                Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false), Has.Length.EqualTo(1),
                    $"提示根活动={ui.TipsRoot.gameObject.activeInHierarchy}，界面活动={ui.gameObject.activeInHierarchy}，已显示={ctrl.IsShow()}，全部提示框={ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(true).Length}");
                TimerMgr.Instance.TickForTests(2f);
                Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false), Is.Empty);
                Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
                Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false), Has.Length.EqualTo(1));
            }
            finally
            {
                UIHelper.ResetForTests();
                TimerMgr.Instance.ClearForTests();
                GameObjectPoolHelper.ClearForTests();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(poolRoot);
                LocalizationLanguages.ResetForTests();
            }
        }

        private sealed class AmmoTipResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object =>
                AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                onLoaded?.Invoke(LoadAsset<T>(path));
                return ResourceLoadHandle.Empty;
            }
            public void Release(ResourceLoadHandle handle) { }
        }

        [Test]
        public void UnarmedAndBowShareCooldownWhenSwitchingSlots()
        {
            EquipBow();
            RegisterEnemy(40, MapInstanceId, new Vector3(0f, 0f, 1f), 20);
            var damage = 0;
            Game.Interface.RegisterEvent<CombatDamageAppliedEvent>(evt =>
            {
                if (evt.TargetUnitId == 40)
                {
                    damage += evt.Damage;
                }
            });

            Game.Interface.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.None));
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Game.Interface.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.Ranged));
            Game.Interface.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.None));
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));

            Assert.That(damage, Is.EqualTo(3));
            Assert.That(playerPort.AttackFeedbackCount, Is.EqualTo(1));
            Assert.That(playerPort.SwingCount, Is.EqualTo(0));
        }

        [Test]
        public void PlayerPrefabKeepsLegacySwordHiddenAndProvidesSwingFeedback()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            Assert.NotNull(prefab);
            var view = prefab.GetComponent<PlayerView>();
            Assert.NotNull(view);
            Assert.That(view.Validate(out var error), Is.True, error);

            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var instanceView = instance.GetComponent<PlayerView>();
                var sword = (GameObject)GetPrivateField(instanceView, "shortSwordObject");
                var swingRoot = (Transform)GetPrivateField(instanceView, "weaponSwingRoot");
                Assert.NotNull(sword);
                Assert.NotNull(swingRoot);
                Assert.That(sword.activeSelf, Is.False);

                instanceView.SetWeaponPresentation(WeaponSlot.Melee, true, false);
                Assert.That(sword.activeSelf, Is.False, "weapon appearances are owned by the runtime attachment controller");
                instanceView.SetWeaponPresentation(WeaponSlot.Ranged, false, true);
                Assert.That(sword.activeSelf, Is.False);

                var before = swingRoot.localRotation;
                instanceView.PlayWeaponSwingFeedback();
                Assert.That(swingRoot.localRotation, Is.Not.EqualTo(before));
                instanceView.TickCombatFeedback(1f);
                Assert.That(Quaternion.Angle(swingRoot.localRotation, before), Is.LessThan(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void SteelBladeSingleClickSettlesOneSlashAtHitTime()
        {
            EquipSteelBlade();
            RegisterEnemy(80, MapInstanceId, Vector3.forward, 100);
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(EnemyHP(80), Is.EqualTo(100), "起手不应提前扣血。");
            var system = Game.Interface.GetSystem<WeaponSystem>();
            system.TickCooldown(0.38f);
            Assert.That(EnemyHP(80), Is.EqualTo(90));
            system.TickCooldown(3f);
            Assert.That(EnemyHP(80), Is.EqualTo(90), "不再点击不能自动播后续刀。");
        }

        [Test]
        public void SteelBladeRapidClicksBufferOnlyOneNextSlash()
        {
            EquipSteelBlade();
            RegisterEnemy(80, MapInstanceId, Vector3.forward, 100);
            for (var i = 0; i < 12; i++)
                Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Game.Interface.GetSystem<WeaponSystem>().TickCooldown(3f);
            Assert.That(EnemyHP(80), Is.EqualTo(80), "第一刀内的连点最多缓存第二刀。");
        }

        [Test]
        public void SteelBladeFourClicksChainFourSlashesThenRestart()
        {
            EquipSteelBlade();
            RegisterEnemy(80, MapInstanceId, Vector3.forward, 100);
            var system = Game.Interface.GetSystem<WeaponSystem>();
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            for (var i = 0; i < 3; i++)
            {
                Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
                system.TickCooldown(0.45f);
            }
            system.TickCooldown(0.45f);
            Assert.That(EnemyHP(80), Is.EqualTo(60));
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            system.TickCooldown(0.45f);
            Assert.That(EnemyHP(80), Is.EqualTo(50));
            Assert.That(playerPort.ComboStages, Is.EqualTo(new[] { 0, 1, 2, 3, 0 }));
        }

        [Test]
        public void SteelBladePresentationSupportsFourStagesAndReturnsToIdle()
        {
            var parent = new GameObject("SteelBladeTestRoot");
            parent.SetActive(false);
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
                var go = UnityEngine.Object.Instantiate(prefab, parent.transform);
                foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                var presenter = go.GetComponent<CharacterWeaponPresenter>();
                presenter.SetManualPoseSampling(true);
                var blade = presenter.Catalog.FindByWorldItemId(34);
                Assert.That(presenter.SetWeapon(blade), Is.True, presenter.LastError);
                var animator = presenter.BodyAnimator;
                animator.enabled = true;
                parent.SetActive(true);
                animator.Rebind();
                animator.Update(0f);
                var view = go.GetComponent<PlayerView>();
                for (var stage = 0; stage < 4; stage++)
                {
                    view.PlayMeleeComboStage(stage, 0.45f);
                    animator.Update(0.1f);
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("SteelBladeSlash" + (stage + 1)), Is.True);
                    Assert.That(animator.GetCurrentAnimatorClipInfo(0)[0].clip, Is.EqualTo(blade.MeleeCombo[stage].Clip));
                    Assert.That(presenter.Visual.activeInHierarchy, Is.True);
                }
                view.StopMeleeCombo();
                animator.Update(0.1f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
                view.SetAlivePresentation(false);
                animator.Update(0f); // 条件转换先进入过渡，再推进一个表现帧。
                animator.Update(0.1f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Death"), Is.True);
                view.SetAlivePresentation(true);
                Assert.That(presenter.SetWeapon(presenter.Catalog.FindByWorldItemId(ItemIds.ShortSword)), Is.True);
                animator.Update(0f);
                Assert.That(animator.HasState(0, Animator.StringToHash("Base Layer.SteelBladeSlash1")), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(parent); }
        }

        [TestCase("switch", 0.1f, 100)]
        [TestCase("death", 0.1f, 100)]
        [TestCase("map", 0.1f, 100)]
        [TestCase("switch", 0.38f, 90)]
        [TestCase("death", 0.38f, 90)]
        [TestCase("map", 0.38f, 90)]
        public void SteelBladeInterruptedComboDiscardsBufferedSlash(string interruption, float elapsed, int expectedHP)
        {
            EquipSteelBlade();
            RegisterEnemy(80, MapInstanceId, Vector3.forward, 100);
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Game.Interface.GetSystem<WeaponSystem>().TickCooldown(elapsed);
            if (interruption == "switch")
                Game.Interface.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.None));
            else if (interruption == "death")
                Game.Interface.GetModel<CombatModel>().SetPlayerHP(0);
            else
                Game.Interface.SendEvent(new MapCurrentReleasedEvent(MapIds.Base, MapInstanceId));
            Game.Interface.GetSystem<WeaponSystem>().TickCooldown(3f);
            Assert.That(EnemyHP(80), Is.EqualTo(expectedHP));
        }

        private static int EnemyHP(int id)
        {
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(id, out var state), Is.True);
            return state.HP;
        }

        private static void EquipSteelBlade()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(34, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
        }

        private static void RegisterPlayer(Vector3 position)
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId,
                MapInstanceId,
                UnitType.Player,
                UnitSource.Player,
                "player",
                position,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out var error), Is.True, error);
        }

        private static void RegisterEnemy(
            int unitId,
            long mapInstanceId,
            Vector3 position,
            int health,
            CombatFaction faction = CombatFaction.Enemy,
            bool active = true)
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                unitId,
                mapInstanceId,
                UnitType.Enemy,
                UnitSource.MapPlaced,
                "enemy",
                position,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                active), out var error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().RegisterOrResetCombatant(
                unitId,
                faction,
                health,
                1,
                PlayerModel.LocalPlayerUnitId);
        }

        private static void EquipSword()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.ShortSword, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().ActiveSlot, Is.EqualTo(WeaponSlot.Melee));
        }

        private static void EquipSwordAndBow()
        {
            EquipSword();
            EquipBow();
        }

        private static void EquipBow()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Bow, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(bag.Slots.Last(x=>x.Value.ItemId==ItemIds.Bow).Key, out error), Is.True, error);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().ActiveSlot, Is.EqualTo(WeaponSlot.Ranged));
        }

        private static void SetWeaponCooldown(float value)
        {
            typeof(WeaponSystem)
                .GetField("cooldownRemainingSeconds", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(Game.Interface.GetSystem<WeaponSystem>(), value);
        }

        private static void InvokePlayerInputUpdate(PlayerInputSystem system)
        {
            typeof(PlayerInputSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(system, null);
        }

        private void SetBlockingBagUi()
        {
            blockingUiObject = new GameObject("ReloadBlockingBag", typeof(RectTransform), typeof(BagUI));
            var ui = blockingUiObject.GetComponent<BagUI>();
            var ctrl = new BlockingTestUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ctrl, ui);
            typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ctrl, true);
            var dict = typeof(UIHelper).GetField("UICtrlDict", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as Dictionary<string, BaseUICtrl>;
            Assert.That(dict, Is.Not.Null);
            dict[typeof(BlockingTestUICtrl).Name] = ctrl;
        }

        // 输入门禁只需要活跃阻断窗口，不能使用未初始化的业务背包控制器。
        private sealed class BlockingTestUICtrl : BaseUICtrl
        {
            public override UILayer Layer() => UILayer.Top;
            protected override string Path() => string.Empty;
            protected override void OnInit() { }
            protected override void OnShow() { }
            protected override void OnHide() { }
        }

        private static float GetWeaponCooldown()
        {
            return (float)typeof(WeaponSystem)
                .GetField("cooldownRemainingSeconds", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Game.Interface.GetSystem<WeaponSystem>());
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            return target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(target);
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private sealed class FakePlayerPort : ICharacterMotorPort
        {
            public int UnitId => PlayerModel.LocalPlayerUnitId;
            public long MapInstanceId => WeaponInputAndMeleeTests.MapInstanceId;
            public bool IsValid { get; set; } = true;
            public bool IsEnabled => true;
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; private set; } = Quaternion.identity;
            public float MovementYawDegrees => 0f;
            public bool IsGrounded => true;
            public int FaceCount { get; private set; }
            public int SwingCount { get; private set; }
            public int AttackFeedbackCount { get; private set; }
            public List<int> ComboStages { get; } = new List<int>();
            public Vector3 LastFacedDirection { get; private set; }

            public void Move(Vector3 motion)
            {
                Position += motion;
            }

            public void RotateTowards(Quaternion rotation, float maxDegreesDelta)
            {
                Rotation = rotation;
            }

            public void FaceDirection(Vector3 direction)
            {
                FaceCount++;
                LastFacedDirection = direction;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    Rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
            }

            public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
            public void SetWeaponPresentation(WeaponSlot activeSlot, bool showShortSword, bool showBow) { }
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin)
            {
                origin = Position;
                return true;
            }
            public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
            public void PlayAttackFeedback() { AttackFeedbackCount++; }
            public void PlayWeaponSwingFeedback() { SwingCount++; }
            public void PlayMeleeComboStage(int stage, float durationSeconds) { ComboStages.Add(stage); }
            public void PlayBowFireFeedback() { }
            public void PlayHurtFeedback() { }
            public void TickCombatFeedback(float deltaTime) { }
        }

        private sealed class ReloadInputFake : PlayerInputSystem.IPlayerInputReader
        {
            public Vector2 MoveInput => Vector2.zero;
            public bool IsEnabled { get; private set; }
            public bool AttackPressedThisFrame => false;
            public bool HolsterPressedThisFrame { get; set; }
            public bool MeleeSlotPressedThisFrame => false;
            public bool RangedSlotPressedThisFrame => false;
            public bool InteractPressedThisFrame => false;
            public bool SprintHeld => false;
            public bool BuildPressedThisFrame => false;
            public bool DestroyPressedThisFrame => false;
            public bool CancelPressedThisFrame => false;
            public bool ReloadPressedThisFrame { get; set; }
            public void Enable() => IsEnabled = true;
            public void Disable() => IsEnabled = false;
            public void Dispose() { }
        }

        private sealed class FakePlayerPortProvider : IPlayerRuntimePortProvider
        {
            private readonly FakePlayerPort port;

            public FakePlayerPortProvider(FakePlayerPort port)
            {
                this.port = port;
            }

            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort motorPort)
            {
                motorPort = unitId == port.UnitId && mapInstanceId == port.MapInstanceId && port.IsValid
                    ? port
                    : null;
                return motorPort != null;
            }

            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort cameraPort)
            {
                cameraPort = unitId == port.UnitId && mapInstanceId == port.MapInstanceId && port.IsValid
                    ? new FakeCameraPort()
                    : null;
                return cameraPort != null;
            }

            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider)
            {
                return false;
            }
        }

        private sealed class FakeCameraPort : IPlayerCameraPort
        {
            public bool IsValid => true;
            public bool SupportsMode(PlayerCameraMode mode, out string error) { error = null; return true; }
            public void ApplyProfile() { }
            public void LateUpdate() { }

            public bool TryGetCameraPose(out Vector3 position, out Vector3 up)
            {
                position = Vector3.back;
                up = Vector3.up;
                return true;
            }
        }

        private sealed class TestConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                switch (id)
                {
                    case 34:
                    case ItemIds.ShortSword:
                        value = new ItemDefinition(id, "Short Sword", string.Empty, string.Empty, string.Empty, string.Empty, ItemType.Weapon, 1, ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    case ItemIds.Bow:
                        value = new ItemDefinition(id, "Bow", string.Empty, string.Empty, string.Empty, string.Empty, ItemType.Weapon, 1, ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    case ItemIds.Arrow:
                        value = new ItemDefinition(id, "Arrow", string.Empty, string.Empty, string.Empty, string.Empty, ItemType.Ammunition, 20, ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    default:
                        value = default;
                        error = "missing";
                        return false;
                }
            }

            public bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
            {
                if (itemId == ItemIds.ShortSword || itemId == 34)
                {
                    value = new WeaponConfigDefinition(itemId, WeaponKind.Melee, 10, 0.45f, 2.2f, 90f, string.Empty, 0);
                    error = null;
                    return true;
                }

                if (itemId == ItemIds.Bow)
                {
                    value = new WeaponConfigDefinition(itemId, WeaponKind.Projectile, 10, 0.75f, 14f, 0f, "projectile.arrow.basic", ItemIds.Arrow, 1, 1f);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, id);
                error = null;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = new PlayerConfigDefinition(id, string.Empty, 100, 1, 1f, 1f, 1f, 1f, 5f);
                error = null;
                return true;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }
        }
    }
}
