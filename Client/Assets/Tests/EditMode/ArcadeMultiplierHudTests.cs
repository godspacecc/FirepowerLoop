using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>用正式规则、状态、Prefabs 和 HUD 事件检查倍率投影；测试不发放第二份界面收益。</summary>
    public sealed class ArcadeMultiplierHudTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private Game architecture;
        private GameObject instance;
        private ArcadeHudUICtrl controller;
        private ArcadeHudUI hud;
        private ArcadeTreasureRoomParameters rules;

        [SetUp]
        public void SetUp()
        {
            architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", Private).SetValue(architecture, true);
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new ArcadeRunModel());
            // 正式 HUD 渲染会查询设备模式，倍率夹具也需提供该只读输入上下文。
            architecture.RegisterSystem(new MobileInputSystem());
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            architecture.RegisterUtility(new LocalizationUtility());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            rules = catalog.TreasureRooms.First();
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"));
            hud = instance.GetComponent<ArcadeHudUI>();
            controller = new ArcadeHudUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", Private).SetValue(controller, hud);
            typeof(BaseUICtrl).GetField("_isActive", Private).SetValue(controller, true);
            typeof(ArcadeHudUICtrl).GetMethod("OnInit", Private).Invoke(controller, null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(instance);
            architecture.Deinit();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            ResourceLoader.SetLoaderForTests(null);
            LifeCycle.ClearForTests();
        }

        [Test]
        public void PlayerBoxHasGeneratedMultiplierReferencesAndNonBlockingBar()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/Box/ArcadePlayerStatusBox.prefab");
            var box = prefab.GetComponent<ArcadePlayerStatusBoxUI>();
            foreach (var key in new[] { "TextMultiplier", "TextMultiplierState", "MultiplierFill" })
            {
                var field = typeof(ArcadePlayerStatusBoxUI).GetField(key);
                Assert.That(field, Is.Not.Null, key + " 必须由正式 Box 生成器生成");
                Assert.That(field.GetValue(box), Is.Not.Null, key);
                Assert.That(prefab.GetComponent<ReferenceCollector>().data.Count(row => row.key == key), Is.EqualTo(1));
            }
            Assert.That(Field<Image>(box, "MultiplierFill").raycastTarget, Is.False);
        }

        [Test]
        public void RewardEventProjectsRealCarryAndMultipleLevelCrossing()
        {
            var state = new ArcadeTreasureScoreState(1500, 3, 900, 3);
            Install(state);
            AssertRow(3, .9f, false);
            var reward = new ArcadeTreasureRewardParameters(1, FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Gem, 0, 0, 250, 250, 1);
            Assert.That(ArcadeTreasureScoreRules.TryAward(state, reward, rules, out state), Is.True);
            Install(state);
            Assert.That(state.Score, Is.EqualTo(2250), "先使用领取前倍率计分");
            AssertRow(4, .15f, false);
            reward = new ArcadeTreasureRewardParameters(2, FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Gem, 0, 0, 250, 2450, 1);
            Assert.That(ArcadeTreasureScoreRules.TryAward(state, reward, rules, out state), Is.True);
            Install(state);
            AssertRow(6, .6f, false);
        }

        [Test]
        public void MaximumDeathAndRestoredRunOverwriteEveryReusedField()
        {
            var state = new ArcadeTreasureScoreState(12300, rules.MaxMultiplier, 0, 3);
            Install(state);
            AssertRow(9, 1f, true);
            Install(state.ResetAfterDeath());
            AssertRow(1, 0f, false);
            Install(new ArcadeTreasureScoreState(9000, 3, 450, 3));
            AssertRow(3, .45f, false);
        }

        [Test]
        public void OnlinePlayersUseTheirOwnAuthoritativeMultiplierAndProgress()
        {
            var players = new[]
            {
                new OnlineArcadePlayerSnapshot(1, "self", 3, 0, 2, 2, "arcade.weapon.basic", false, null,
                    score: 1500, scoreMultiplier: 3, multiplierProgress: 350),
                new OnlineArcadePlayerSnapshot(2, "other", 3, 0, 2, 2, "arcade.weapon.basic", false, null,
                    score: 23000, scoreMultiplier: 9, multiplierProgress: 0)
            };
            var battle = new OnlineArcadeBattleSnapshot(1, 4, 4, 1, 1, 0, 1, 1000,
                new[] { new OnlineArcadeBattlePlayerSnapshot(1, 0, 0, 0, 1, 100, 100, true, false, 0),
                    new OnlineArcadeBattlePlayerSnapshot(2, 1, 1, 0, 1, 100, 100, true, false, 0) },
                Array.Empty<OnlineArcadeEnemySnapshot>(), Array.Empty<OnlineArcadeProjectileSnapshot>(),
                Array.Empty<OnlineArcadeDropSnapshot>());
            var snapshot = new OnlineArcadeRunClientSnapshot(true, 1, 2, 1, OnlineArcadeRunPhase.Arena, 0, 1,
                "test", players, OnlineArcadeOperationState.Idle, OnlineArcadeResultCode.Success, battle);
            typeof(ArcadeHudUICtrl).GetMethod("RefreshOnlinePlayerStatuses", Private).Invoke(controller,
                new object[] { snapshot, 1L });
            AssertRow(3, .35f, false);
            var teammate = hud.PlayerStatusList.GetDraggableItems()[1].GetComponent<ArcadePlayerStatusBoxUI>();
            Assert.That(Field<TextMeshProUGUI>(teammate, "TextMultiplier").text, Is.EqualTo("倍率 ×9"));
            Assert.That(Field<TextMeshProUGUI>(teammate, "TextMultiplierState").text, Is.EqualTo("最高倍率"));
            Assert.That(Field<Image>(teammate, "MultiplierFill").fillAmount, Is.EqualTo(1f));
        }

        private void Install(ArcadeTreasureScoreState state)
        {
            var model = architecture.GetModel<ArcadeRunModel>();
            if (!model.HasRun)
            {
                model.CommitSafeNode(new ArcadeRunSaveData { NextArenaNumber = 1, RemainingLives = 3 });
                model.EnterArena();
            }
            model.SetTransientTreasureScore(state);
            architecture.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot { PlayerHealth = 100, PlayerMaxHealth = 100 }));
        }

        private void AssertRow(int multiplier, float progress, bool maximum)
        {
            var box = hud.PlayerStatusList.GetDraggableItems()[0].GetComponent<ArcadePlayerStatusBoxUI>();
            Assert.That(Field<TextMeshProUGUI>(box, "TextMultiplier").text, Is.EqualTo("倍率 ×" + multiplier));
            Assert.That(Field<TextMeshProUGUI>(box, "TextMultiplierState").text, Is.EqualTo(maximum ? "最高倍率" : string.Empty));
            var fill = Field<Image>(box, "MultiplierFill");
            Assert.That(fill.fillAmount, Is.EqualTo(progress).Within(.00001f));
            Assert.That(fill.rectTransform.anchorMax.x, Is.EqualTo(progress).Within(.00001f), "无 Sprite 时画面也应实际改变宽度");
        }

        private static T Field<T>(ArcadePlayerStatusBoxUI box, string name) where T : class
        {
            var field = typeof(ArcadePlayerStatusBoxUI).GetField(name);
            Assert.That(field, Is.Not.Null, name);
            return field.GetValue(box) as T;
        }
    }
}
