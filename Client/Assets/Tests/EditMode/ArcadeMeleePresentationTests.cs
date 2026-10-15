using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game;
using Luban;
using MemoryPack;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>通过正式玩家模型检查街机攻击反馈，覆盖冷兵器分流和联机去重。</summary>
    public sealed class ArcadeMeleePresentationTests
    {
        private GameObject instance;
        private ArcadeConfigCatalogDefinition catalog;
        private PlayerView view;
        private ArcadeCombatViewController controller;

        [SetUp]
        public void SetUp()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(file => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "Game", "Config", file + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            view = instance.GetComponent<PlayerView>();
            controller = new ArcadeCombatViewController();
            Set(controller, "playerMotor", new PlayerViewCtrl(view, 1, 1, new ViewCtrlRegistry(),
                new UnityViewInstanceProvider()));
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(instance);

        [Test]
        public void FiveMeleeWeaponsTriggerSwingAndRepeatedSequenceDoesNotReplay()
        {
            var sequence = 0;
            foreach (var id in new[] { "weapon.short-sword", "weapon.greatsword", "weapon.knife", "weapon.axe", "weapon.pickaxe" })
            {
                controller.SetPlayerWeapon(Weapon(id));
                view.Animator.Rebind();
                view.Animator.Update(0f);
                view.Animator.ResetTrigger("Attack");
                controller.SetPlayerFireSequence(++sequence);
                for (var frame = 0; frame < 8; frame++) view.Animator.Update(0.01f);
                Assert.That(view.Animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), Is.True,
                    id + " 应播放已接受的一次攻击，不依赖 Trigger 或直接混合的实现细节");
                var previousTime = view.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                controller.SetPlayerFireSequence(sequence);
                view.Animator.Update(0.02f);
                Assert.That(view.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(previousTime),
                    "重复快照不能重置攻击进度");
                controller.SetPlayerFireSequence(++sequence);
                view.Animator.Update(0.1f);
                view.Animator.Update(0.1f);
                Assert.That(view.Animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), Is.True,
                    id + " 应实际进入共享攻击状态");
            }
        }

        [Test]
        public void RemoteLoadBindsLatestWeaponAndSkipsBufferedAttackHistory()
        {
            var entryType = typeof(ArcadeCombatViewController).GetNestedType("RemotePlayerEntry", BindingFlags.NonPublic);
            var entry = Activator.CreateInstance(entryType, true);
            ((IDictionary)Get(controller, "remotePlayers")).Add(2L, entry);
            var sync = typeof(ArcadeCombatViewController).GetMethod("SetRemotePlayerCombat");
            sync.Invoke(controller, new object[] { 2L, Weapon("weapon.knife"), 7 });
            sync.Invoke(controller, new object[] { 2L, Weapon("weapon.greatsword"), 9 });
            Set(entry, "View", new OnlineRemotePlayerViewCtrl(instance));
            Set(entry, "Visible", true);
            typeof(ArcadeCombatViewController).GetMethod("ApplyRemotePlayerCombat",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { entry });
            Assert.That(instance.GetComponent<CharacterWeaponPresenter>().Current.Id, Is.EqualTo("weapon.greatsword"));
            Assert.That(view.Animator.GetBool("Attack"), Is.False, "不能补播加载期间的攻击");
            sync.Invoke(controller, new object[] { 2L, Weapon("weapon.greatsword"), 10 });
            Assert.That(view.Animator.GetBool("Attack"), Is.True);
        }

        [TestCase("weapon.short-sword", 1f)]
        [TestCase("weapon.greatsword", 1f)]
        [TestCase("weapon.knife", 1f)]
        [TestCase("weapon.axe", 1f)]
        [TestCase("weapon.pickaxe", 1f)]
        [TestCase("weapon.short-sword", 1.5f)]
        [TestCase("weapon.knife", 1.5f)]
        public void SinglePlayerRepeatedSwingCompletesRecoveryBeforeNextStrike(string id, float rate)
        {
            var weapon = Weapon(id);
            controller.SetPlayerWeapon(weapon);
            var animator = view.Animator;
            animator.Rebind();
            animator.Update(0f);
            var interval = weapon.FireIntervalSeconds / rate;
            for (var sequence = 1; sequence <= 4; sequence++)
            {
                controller.SetPlayerFireSequence(sequence, interval);
                var reachedRecovery = false;
                var enteredAttack = false;
                for (var elapsed = 0f; elapsed < interval; elapsed += 0.005f)
                {
                    animator.Update(Mathf.Min(0.005f, interval - elapsed));
                    var state = animator.GetCurrentAnimatorStateInfo(0);
                    enteredAttack |= state.IsName("Attack");
                    reachedRecovery |= state.IsName("Attack") && state.normalizedTime >= 0.9f;
                }
                Assert.That(enteredAttack, Is.True, id + " 每次出招都必须播放挥刀");
                Assert.That(reachedRecovery, Is.True, id + " 下一刀之前必须到达收刀段");
            }
            for (var i = 0; i < 40; i++) animator.Update(0.01f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), Is.False,
                "松开攻击后不能留在挥刀状态");
            Assert.That(animator.speed, Is.EqualTo(1f), "不能通过整体加速 Animator 改变移动动画");
        }

        [TestCase("fist")]
        [TestCase("bow")]
        [TestCase("projectile")]
        [TestCase("online-melee")]
        public void OtherAttackEntriesRestoreOriginalSpeedAfterSinglePlayerMelee(string attack)
        {
            controller.SetPlayerWeapon(Weapon("weapon.short-sword"));
            controller.SetPlayerFireSequence(1, 0.35f);
            Assert.That(view.Animator.GetFloat("AttackPlaybackSpeed"), Is.GreaterThan(1f));
            switch (attack)
            {
                case "fist": view.PlayAttackFeedback(); break;
                case "bow": view.PlayBowFireFeedback(); break;
                case "projectile":
                    controller.SetPlayerWeapon(Weapon("arcade.weapon.basic"));
                    controller.SetPlayerFireSequence(2);
                    break;
                default: controller.SetPlayerFireSequence(2); break;
            }
            Assert.That(view.Animator.GetFloat("AttackPlaybackSpeed"), Is.EqualTo(1f),
                "单人街机的挥刀速度不能泄漏到其他攻击入口");
        }

        [Test]
        public void ProjectileWeaponStillTriggersSharedFireAnimation()
        {
            controller.SetPlayerWeapon(Weapon("arcade.weapon.basic"));
            view.Animator.ResetTrigger("Attack");
            controller.SetPlayerFireSequence(1);
            Assert.That(view.Animator.GetBool("Attack"), Is.True);
        }

        [Test]
        public void RemoteWeaponBindsBeforeAttackAndLoadedHistoryDoesNotReplay()
        {
            var entryType = typeof(ArcadeCombatViewController).GetNestedType("RemotePlayerEntry", BindingFlags.NonPublic);
            var entry = Activator.CreateInstance(entryType, true);
            Set(entry, "View", new OnlineRemotePlayerViewCtrl(instance));
            Set(entry, "Visible", true);
            ((IDictionary)Get(controller, "remotePlayers")).Add(2L, entry);
            var sync = typeof(ArcadeCombatViewController).GetMethod("SetRemotePlayerCombat");
            Assert.That(sync, Is.Not.Null, "联机玩家缺少武器与攻击同步入口");
            sync.Invoke(controller, new object[] { 2L, Weapon("weapon.axe"), 7 });
            Assert.That(instance.GetComponent<CharacterWeaponPresenter>().Current.Id, Is.EqualTo("weapon.axe"));
            Assert.That(view.Animator.GetBool("Attack"), Is.False, "首次同步不能补播历史攻击");
            sync.Invoke(controller, new object[] { 2L, Weapon("weapon.axe"), 8 });
            for (var frame = 0; frame < 8; frame++) view.Animator.Update(0.01f);
            Assert.That(view.Animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), Is.True);
            var previousTime = view.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            sync.Invoke(controller, new object[] { 2L, Weapon("weapon.axe"), 8 });
            view.Animator.Update(0.02f);
            Assert.That(view.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(previousTime),
                "同一远端攻击序号不能重新起手");
            sync.Invoke(controller, new object[] { 2L, Weapon("arcade.weapon.spread"), 8 });
            Assert.That(instance.GetComponent<CharacterWeaponPresenter>().Current.Id, Is.EqualTo("arcade.weapon.spread"),
                "临时武器过期后应恢复服务器已购武器模型");
        }

        private ArcadeWeaponDefinition Weapon(string id)
        {
            foreach (var weapon in catalog.Weapons) if (weapon.Id == id) return weapon;
            throw new InvalidOperationException(id);
        }

        [Test]
        public void OnlineProjectionSelectsTemporaryWeaponThenRestoresPurchaseWithoutReplayingHistory()
        {
            var projection = new OnlineArcadePresentationSystem();
            var recorder = new CombatRecorder();
            Set(projection, "view", recorder);
            Set(projection, "catalog", catalog);
            Set(projection, "latest", new OnlineArcadeRunClientSnapshot(true, 1, 1, 1,
                OnlineArcadeRunPhase.Arena, 0, 1, "test", new[] {
                    new OnlineArcadePlayerSnapshot(1, "self", 3, 0, 0, 0, "arcade.weapon.spread", false, null),
                    new OnlineArcadePlayerSnapshot(2, "peer", 3, 0, 0, 0, "weapon.pickaxe", false, null)
                }, OnlineArcadeOperationState.Idle, OnlineArcadeResultCode.Success));
            var sync = typeof(OnlineArcadePresentationSystem).GetMethod("SyncPlayerCombat",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(sync, Is.Not.Null, "快照必须同步武器及攻击");
            OnlineArcadeBattlePlayerSnapshot Self(int sequence, float seconds) => new(1, 0, 0, 0, 1,
                100, 100, true, false, 0, "weapon.knife", seconds, fireSequence: sequence);
            sync.Invoke(projection, new object[] { Self(7, 20), 1L });
            Assert.That(recorder.WeaponId, Is.EqualTo(catalog.Rule.BaseWeaponId), "旧近战临时武器回退基础枪");
            Assert.That(recorder.FireSequences, Is.EqualTo(new[] { 0 }), "首次只建立基线");
            sync.Invoke(projection, new object[] { Self(7, 19), 1L });
            Assert.That(recorder.FireSequences.Count, Is.EqualTo(1));
            sync.Invoke(projection, new object[] { Self(8, 18), 1L });
            Assert.That(recorder.FireSequences, Is.EqualTo(new[] { 0, 8 }));
            sync.Invoke(projection, new object[] { Self(8, 0), 1L });
            Assert.That(recorder.WeaponId, Is.EqualTo("arcade.weapon.spread"));
            sync.Invoke(projection, new object[] { new OnlineArcadeBattlePlayerSnapshot(2, 0, 0, 0, 1,
                100, 100, true, false, 0, fireSequence: 4), 1L });
            Assert.That(recorder.RemoteWeaponId, Is.EqualTo(catalog.Rule.BaseWeaponId), "远端旧装备同样回退");
            Assert.That(recorder.RemoteSequence, Is.EqualTo(4));
        }

        private sealed class CombatRecorder : IArcadeCombatViewPort
        {
            public bool IsActive => true;
            public string WeaponId, RemoteWeaponId;
            public int RemoteSequence;
            public readonly List<int> FireSequences = new();
            public void SetPlayerWeapon(ArcadeWeaponDefinition weapon) => WeaponId = weapon.Id;
            public void SetPlayerFireSequence(int sequence) => FireSequences.Add(sequence);
            public void SetRemotePlayerCombat(long id, ArcadeWeaponDefinition weapon, int sequence)
            { RemoteWeaponId = weapon.Id; RemoteSequence = sequence; }
            public void Begin(int n, float w, float h, IReadOnlyList<ArcadeObstacleDefinition> o) { }
            public void End() { }
            public bool TryScreenToArena(Vector2 screen, out Vector2 position) { position = default; return false; }
            public void SetPlayer(Vector2 p, Vector2 f, bool v, bool i) { }
            public void SetPlayerHealth(int c, int m) { }
            public void SetEnemy(int id, string cfg, Vector2 p, int c, int m, bool t = false, int s = 0, bool h = false) { }
            public void RemoveEnemy(int id, bool death = false) { }
            public void SetProjectile(int id, bool h, Vector2 p, Vector2 d) { }
            public void RemoveProjectile(int id) { }
            public void SetDrop(int id, ArcadeDropDefinition d, Vector2 p) { }
            public void RemoveDrop(int id) { }
            public void SetRemotePlayer(long id, Vector2 p, Vector2 f, bool v, bool i, int c, int m) { }
            public void RemoveRemotePlayer(long id) { }
            public void TickPlayerFeedback(float dt) { }
        }

        [Test]
        public void NetworkAttackSequenceSurvivesClientSnapshotConversion()
        {
            var field = typeof(ET.OnlineArcadeBattlePlayerInfo).GetProperty("FireSequence");
            Assert.That(field, Is.Not.Null, "权威攻击序列必须从服务端传到客户端");
            var player = ET.OnlineArcadeBattlePlayerInfo.Create();
            var battle = ET.OnlineArcadeBattleInfo.Create();
            ET.OnlineArcadeBattlePlayerInfo decoded = null;
            try
            {
                field.SetValue(player, 9);
                decoded = MemoryPackSerializer.Deserialize<ET.OnlineArcadeBattlePlayerInfo>(
                    MemoryPackSerializer.Serialize(player));
                battle.Players.Add(decoded);
                var copy = (OnlineArcadeBattleSnapshot)typeof(OnlineArcadeRunSystem).GetMethod("CopyBattle",
                    BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { battle });
                Assert.That(typeof(OnlineArcadeBattlePlayerSnapshot).GetProperty("FireSequence")?.GetValue(copy.Players[0]),
                    Is.EqualTo(9), "客户端快照转换不能丢掉序列");
            }
            finally { battle.Dispose(); player.Dispose(); decoded?.Dispose(); }
        }

        private static object Get(object target, string field) => target.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value);
    }
}
