using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Network;
using FirepowerLoop.Arcade;

namespace Game
{
    /// <summary>渲染非阻断街机战斗 HUD 并转发紧急资源意图；双摇杆直接写入 Input System，不裁决战斗。</summary>
    public sealed partial class ArcadeHudUICtrl : BaseUICtrl
    {
        private ArcadeCombatHudSnapshot combat = new ArcadeCombatHudSnapshot();
        private bool hasCombatSnapshot;
        private bool onlineMode;
        private bool awaitingOnlineExit;
        /// <summary>只在本 HUD 可见期间订阅输入阻断及尺寸检查，隐藏或销毁时成对释放。</summary>
        private bool joystickUpdateRegistered;
        private Vector2 joystickAreaSize;
        /// <summary>正式掉落目录，只用来投影效果类型与规则持续时间。</summary>
        private ArcadeConfigCatalogDefinition hudCatalog;
        /// <summary>当前 HUD 玩家行的只读展示快照；每次权威快照刷新时整体重建，本机始终排在首行。</summary>
        private readonly List<PlayerStatusDisplay> playerStatuses = new(4);
        /// <summary>计分已使用的正式财宝规则；只读取提升要求与上限，不累计或修改倍率。</summary>
        private ArcadeTreasureRoomParameters multiplierRules;
        private ArcadeHudUI UI => BaseUI as ArcadeHudUI;

        protected override string Path() => "Arcade/ArcadeHud";
        public override UILayer Layer() => UILayer.HUD;
        public override bool BlocksGame => false;

        protected override void OnInit()
        {
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out var catalog, out var error))
                throw new System.InvalidOperationException("街机 HUD 倍率规则不可用：" + error);
            // 普通财宝计分也读取目录首份规则，HUD 与其共用同一配置来源。
            multiplierRules = catalog.TreasureRooms.FirstOrDefault();
            hudCatalog = catalog;
            UI.EffectList.AddItemRefreshListener(RefreshEffect);
            UI.WeaponEffectList.AddItemRefreshListener(RefreshWeaponEffect);
            UI.PlayerStatusList.AddItemRefreshListener(RefreshPlayerStatus);
            UI.BtnBomb.onClick.AddListener(() => SendAction(ArcadeHudActionKind.Bomb));
            UI.BtnDash.onClick.AddListener(() => SendAction(ArcadeHudActionKind.Dash));
            UI.BtnSlayerMart.onClick.AddListener(OpenSlayerMart);
            UI.BtnFieldUpgrade.onClick.AddListener(()=>SendAction(ArcadeHudActionKind.FieldUpgrade));
            UI.BtnDonateLife.onClick.AddListener(()=>SendAction(ArcadeHudActionKind.DonateLife));
            UI.BtnPause.onClick.AddListener(PauseOrExitOnline);
            UI.BtnRetry.onClick.AddListener(() => SendAction(ArcadeHudActionKind.RetryCompletion));
            UI.BtnExitFirstPerson.onClick.AddListener(() => SendAction(ArcadeHudActionKind.ExitFirstPerson));
            this.RegisterEvent<ArcadeRunChangedEvent>(OnRunChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<ArcadeCombatHudChangedEvent>(OnCombatChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<ArcadeStageNoticeChangedEvent>(_ => TryPresentStageNotice()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<ArcadeArenaCompletedEvent>(_ => OpenSafeCheckpoint()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<ArcadeCombatOperationFailedEvent>(evt => Notify.ShowFloatTips(evt.Error)).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineArcadeRunChangedEvent>(evt => OnOnlineRunChanged(evt.Snapshot))
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MobileInputModeChangedEvent>(_ => { RefreshJoystickVisibility(); RenderFirstPersonStatus(); SyncFieldUpgradeUi(); })
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MobileControlLayoutChangedEvent>(_ => { if (IsShow()) ApplyJoystickLayout(); })
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<HotKeyBindingsChangedEvent>(_=>{SyncFieldUpgradeUi();SyncCoopRescueUi();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => { if(onlineMode)RenderOnline(this.GetSystem<OnlineArcadeRunSystem>().Snapshot);else Render(); SyncFieldUpgradeUi(); RenderStageBannerText(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            OnlineArcadeRunClientSnapshot online = this.GetSystem<OnlineArcadeRunSystem>().Snapshot;
            onlineMode = online.HasRun && online.Phase is OnlineArcadeRunPhase.Arena or OnlineArcadeRunPhase.Challenge;
            awaitingOnlineExit = false;
            UIHelper.BlockingUIChanged += OnBlockingUiChanged;
            LifeCycle.RegisterUpdate(RefreshJoystickArea);
            joystickUpdateRegistered = true;
            Canvas.ForceUpdateCanvases();
            ApplyJoystickLayout();
            RefreshJoystickVisibility();
            ClearStageBanner();
            TryPresentStageNotice();
            if (onlineMode) { RenderOnline(online); return; }
            UI.BtnDonateLife.gameObject.SetActive(false);
            var run = this.GetModel<ArcadeRunModel>();
            if (run.Phase != ArcadeRunPhase.Arena && run.Phase != ArcadeRunPhase.Challenge)
            {
                Hide();
                return;
            }
            combat.ArenaNumber = run.Current?.NextArenaNumber ?? 1;
            Render();
        }

        protected override void OnHide()
        {
            ClearStageBanner();
            ReleaseJoystickUpdate();
            awaitingOnlineExit = false;
            ClearPlayerStatuses();
            ClearTimedEffects();
            UI.FirstPersonRoot.SetActive(false);
        }

        protected override void OnUnload() { ClearStageBanner(); ReleaseJoystickUpdate(); ClearPlayerStatuses(); }

        private void OnRunChanged(ArcadeRunChangedEvent evt)
        {
            if (onlineMode) return;
            bool inCombat = evt.Phase == ArcadeRunPhase.Arena || evt.Phase == ArcadeRunPhase.Challenge;
            if (!inCombat) hasCombatSnapshot = false;
            if (!IsShow()) return;
            if (!inCombat) Hide();
            else Render(evt.Snapshot);
        }

        private void OnOnlineRunChanged(OnlineArcadeRunClientSnapshot snapshot)
        {
            if (!onlineMode || !IsShow()) return;
            if (!snapshot.HasRun) { Hide(); return; }
            if (snapshot.Phase is OnlineArcadeRunPhase.SafeCheckpoint or OnlineArcadeRunPhase.Victory)
            {
                Hide();
                if (snapshot.Phase == OnlineArcadeRunPhase.SafeCheckpoint && string.IsNullOrEmpty(snapshot.ChallengeId))
                    UIHelper.ShowUI<OnlineRoomUICtrl>();
                else UIHelper.ShowUI<ArcadeResumeUICtrl>();
                return;
            }
            RenderOnline(snapshot);
        }

        private void OnCombatChanged(ArcadeCombatHudChangedEvent evt)
        {
            var hadFirstPerson = combat.FirstPersonSeconds > 0;
            if (evt.Snapshot != null)
            {
                combat = evt.Snapshot.Clone();
                hasCombatSnapshot = true;
            }
            if (IsShow() && hadFirstPerson != (combat.FirstPersonSeconds > 0))
            {
                // 两种解释之间切换时释放虚拟轴；旧手指不能携带俯视世界方向进入第一人称。
                UI.MoveJoystickRoot.SetActive(false);
                UI.AimJoystickRoot.SetActive(false);
                RefreshJoystickVisibility();
            }
            if (IsShow()){Render();SyncSlayerMartUi();
            SyncCoopRescueUi();}
        }

        private void Render(ArcadeRunSaveData snapshot = null)
        {
            if (UI == null) return;
            snapshot ??= this.GetModel<ArcadeRunModel>().Current;
            if (snapshot == null) return;
            RefreshLocalPlayerStatuses(snapshot);
            Set(UI.TextRespawn, combat.RespawnSeconds > 0f
                ? Loc.T("ui.arcade.hud.respawn", Mathf.CeilToInt(combat.RespawnSeconds))
                : string.Empty);
            Set(UI.TextArena, combat.IsChallenge
                ? Loc.T("ui.arcade.hud.challenge", Mathf.CeilToInt(combat.ChallengeSeconds))
                : !string.IsNullOrEmpty(combat.IslandMapNameKey) ? Loc.T(combat.IslandMapNameKey)
                    : Loc.T("ui.arcade.hud.arena", combat.ArenaNumber > 0 ? combat.ArenaNumber : snapshot.NextArenaNumber));
            Set(UI.TextWave, combat.AwaitingExit ? Loc.T("ui.arcade.hud.exit") : combat.IsFinalBoss
                ? Loc.T("ui.arcade.hud.final_boss")
                : Loc.T("ui.arcade.hud.wave", combat.WaveNumber, combat.TotalWaves, combat.RemainingEnemies));
            if (combat.InTreasureRoom) Set(UI.TextWave, Loc.T(combat.ArenaNumber == 2 ? "ui.arcade.hud.armory" : "ui.arcade.hud.treasure", Mathf.CeilToInt(combat.TreasureSeconds)));
            else if (combat.Resting) Set(UI.TextWave, Loc.T("ui.arcade.hud.rest", Mathf.CeilToInt(combat.RestSeconds)));
            if (combat.IslandAwaitingRoundExit) Set(UI.TextWave, Loc.T("ui.arcade.island.pickup"));
            if (combat.IslandTransitioning) Set(UI.TextWave, Loc.T("ui.arcade.island.loading"));
            RenderTimedStatus(combat.TemporaryWeaponNameKey, combat.TemporaryWeaponSeconds, combat.Powerups);
            RenderFirstPersonStatus();
            snapshot.EmergencyResources.TryGetValue(ArcadeRunDefaults.BombResourceId, out var bombs);
            snapshot.EmergencyResources.TryGetValue(ArcadeRunDefaults.DashResourceId, out var dashes);
            if (hasCombatSnapshot)
            {
                bombs = combat.Bombs;
                dashes = combat.Dashes;
            }
            RenderAbilityCounts(bombs, dashes);
            UI.BtnBomb.interactable = bombs > 0;
            UI.BtnDash.interactable = dashes > 0;
            UI.BtnRetry.gameObject.SetActive(hasCombatSnapshot && combat.CanRetryCompletion);
        }

        /// <summary>单机复用全局暂停仲裁；联机不能冻结服务器时间，仍以二次确认执行断线退出。</summary>
        private void PauseOrExitOnline()
        {
            if (onlineMode)
            {
                if (!awaitingOnlineExit)
                {
                    awaitingOnlineExit = true;
                    Notify.ShowFloatTips(Loc.T("ui.arcade.online.exit_confirm"));
                    return;
                }
                this.GetUtility<INetworkRuntime>()?.Disconnect(NetworkDisconnectReason.LocalRequest);
                return;
            }
            if (this.GetSystem<HotKeySystem>().TryExecuteAction(HotKeyActionIds.TogglePause))
                this.GetSystem<AudioSystem>().PlayUiSound("ArcadeUiClick");
        }

        private void OpenSafeCheckpoint()
        {
            hasCombatSnapshot = false;
            Hide();
            var current = this.GetModel<ArcadeRunModel>().Current;
            if (current != null && !current.Victory && string.IsNullOrEmpty(current.Checkpoint?.ChallengeId))
            { ContinueOrdinaryArenaAsync().Forget(); return; }
            UIHelper.ShowUI<ArcadeResumeUICtrl>();
        }

        /// <summary>普通场间先保有已落盘节点再进入目的地；失败可从原恢复入口重试，不构造商店。</summary>
        private async UniTaskVoid ContinueOrdinaryArenaAsync()
        {
            var flow = await this.GetSystem<ArcadeSceneFlowSystem>().EnterArenaMapAsync();
            string error = flow.Error;
            if (flow.Success && this.GetSystem<ArcadeRunSystem>().TryContinue(out _, out error) &&
                this.GetModel<ArcadeRunModel>().Phase == ArcadeRunPhase.Arena)
            { UIHelper.ShowUI<ArcadeHudUICtrl>(); return; }
            Notify.ShowFloatTips(error ?? this.GetSystem<ArcadeCombatSystem>().LastEntryError ?? Loc.T("ui.arcade.error.open"));
            UIHelper.ShowUI<ArcadeResumeUICtrl>();
        }

        private void SendAction(ArcadeHudActionKind action) => Game.Interface.SendEvent(new ArcadeHudActionRequestedEvent(action));

        /// <summary>只渲染单人规则剩余时间；桌面由 X 退出以便鼠标保持锁定，触屏使用独立按钮。</summary>
        private void RenderFirstPersonStatus()
        {
            if (UI == null) return;
            var active = !onlineMode && hasCombatSnapshot && combat.FirstPersonSeconds > 0;
            UI.FirstPersonRoot.SetActive(active);
            if (!active) return;
            var mobile = this.GetSystem<MobileInputSystem>().IsMobileInputMode;
            Set(UI.TextFirstPerson, Loc.T(mobile ? "ui.arcade.first_person.remaining_mobile" :
                "ui.arcade.first_person.remaining", Mathf.CeilToInt(combat.FirstPersonSeconds)));
            SetButton(UI.BtnExitFirstPerson, Loc.T(mobile
                ? "ui.arcade.first_person.exit" : "ui.arcade.first_person.exit_pc"));
        }

        /// <summary>移动输入模式显示双摇杆；隐藏时 OnScreenStick 会自动清零对应虚拟轴。</summary>
        private void RefreshJoystickVisibility()
        {
            if (UI == null) return;
            var mobile = this.GetSystem<MobileInputSystem>().IsMobileInputMode;
            ApplyInputLayout(mobile, onlineMode);
            var visible = mobile && !UIHelper.HasUIOpen;
            if (UI.MoveJoystickRoot.activeSelf != visible) UI.MoveJoystickRoot.SetActive(visible);
            if (UI.AimJoystickRoot.activeSelf != visible) UI.AimJoystickRoot.SetActive(visible);
            if (visible) ApplyJoystickLayout();
        }

        /// <summary>安全区或设置变化后适配布局；不写回持久值，不改变输入轴和战斗规则。</summary>
        private void ApplyJoystickLayout()
        {
            joystickAreaSize = UI.JoystickLayoutArea.rect.size;
            if (joystickAreaSize.x <= 0 || joystickAreaSize.y <= 0) return;
            var data = this.GetModel<MobileControlLayoutModel>().ToData();
            var obstacles = new[]
            {
                MobileControlLayoutGeometry.Bounds(UI.JoystickLayoutArea, (RectTransform)UI.BtnDash.transform),
                MobileControlLayoutGeometry.Bounds(UI.JoystickLayoutArea, (RectTransform)UI.BtnBomb.transform),
                MobileControlLayoutGeometry.Bounds(UI.JoystickLayoutArea, (RectTransform)UI.BtnPause.transform),
                MobileControlLayoutGeometry.Bounds(UI.JoystickLayoutArea, (RectTransform)UI.BtnExitFirstPerson.transform)
            };
            MobileControlLayoutGeometry.Render(UI.JoystickLayoutArea, (RectTransform)UI.MoveJoystickRoot.transform, UI.MoveOpacity, data.Move, true, obstacles);
            MobileControlLayoutGeometry.Render(UI.JoystickLayoutArea, (RectTransform)UI.AimJoystickRoot.transform, UI.AimOpacity, data.Aim, false, obstacles);
        }
        private void RefreshJoystickArea()
        {
            SyncSlayerMartUi();
            SyncCoopRescueUi();
            UpdateStageBanner(Time.unscaledTimeAsDouble);
            if (UI.JoystickLayoutArea.rect.size != joystickAreaSize) ApplyJoystickLayout();
        }
        private void OnBlockingUiChanged(bool _) { if (IsShow()) RefreshJoystickVisibility(); }
        private void ReleaseJoystickUpdate()
        {
            if (!joystickUpdateRegistered) return;
            UIHelper.BlockingUIChanged -= OnBlockingUiChanged;
            LifeCycle.UnregisterUpdate(RefreshJoystickArea);
            joystickUpdateRegistered = false;
        }

        private void RenderOnline(OnlineArcadeRunClientSnapshot snapshot)
        {
            if (UI == null || snapshot.Battle == null) return;
            UI.FirstPersonRoot.SetActive(false);
            long selfId = this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            OnlineArcadePlayerSnapshot runPlayer = System.Linq.Enumerable.FirstOrDefault(snapshot.Players, x => x.PlayerId == selfId);
            OnlineArcadeBattlePlayerSnapshot battlePlayer = System.Linq.Enumerable.FirstOrDefault(snapshot.Battle.Players, x => x.PlayerId == selfId);
            int remainingEnemies = snapshot.Battle.TotalInWave - snapshot.Battle.SpawnedInWave + snapshot.Battle.Enemies.Count;
            RefreshOnlinePlayerStatuses(snapshot, selfId);
            Set(UI.TextRespawn, snapshot.BattlePaused
                ? Loc.T("ui.arcade.online.paused")
                : battlePlayer.DownedSeconds>0 ? CoopDownedText(battlePlayer) : battlePlayer.RespawnSeconds > 0f ? Loc.T("ui.arcade.hud.respawn", Mathf.CeilToInt(battlePlayer.RespawnSeconds)) : string.Empty);
            Set(UI.TextArena, snapshot.Battle.IsChallenge
                ? Loc.T("ui.arcade.hud.challenge", Mathf.CeilToInt(snapshot.Battle.ChallengeSeconds))
                : Loc.T("ui.arcade.hud.arena", snapshot.Battle.ArenaNumber));
            Set(UI.TextWave, snapshot.Battle.AwaitingExit ? Loc.T("ui.arcade.hud.exit") : snapshot.Battle.IsFinalBoss
                ? Loc.T("ui.arcade.hud.final_boss") : Loc.T("ui.arcade.hud.wave", snapshot.Battle.WaveNumber, snapshot.Battle.WaveCount, remainingEnemies));
            if (battlePlayer.InTreasureRoom) Set(UI.TextWave, Loc.T(snapshot.Battle.ArenaNumber == 2 ? "ui.arcade.hud.armory" : "ui.arcade.hud.treasure", Mathf.CeilToInt(snapshot.Battle.TreasureSeconds)));
            else if (snapshot.Battle.ArenaNumber == 1 && remainingEnemies == 0 && !snapshot.Battle.IsChallenge && !snapshot.Battle.AwaitingExit)
                Set(UI.TextWave, Loc.T("ui.arcade.hud.rest_online"));
            var powerups = battlePlayer.Powerups.Select(value => new ArcadePowerupHudEntry(
                ProductNameKey(value.ProductId), value.RemainingSeconds, value.Charges)).ToArray();
            RenderTimedStatus(ProductNameKey(battlePlayer.TemporaryWeaponId), battlePlayer.TemporaryWeaponSeconds, powerups);
            RenderAbilityCounts(runPlayer.Bombs, runPlayer.Dashes);
            UI.BtnBomb.interactable = !snapshot.BattlePaused && runPlayer.Bombs > 0;
            UI.BtnDash.interactable = !snapshot.BattlePaused && runPlayer.Dashes > 0;
            UI.MoveJoystick.enabled = !snapshot.BattlePaused;
            UI.AimJoystick.enabled = !snapshot.BattlePaused;
            UI.BtnRetry.gameObject.SetActive(false);
            SyncSlayerMartUi();
            SyncCoopRescueUi();
        }

        /// <summary>用单机权威快照刷新唯一玩家行；战斗首帧尚无血量快照时显示满血占位。</summary>
        private void RefreshLocalPlayerStatuses(ArcadeRunSaveData snapshot)
        {
            int maxHealth = hasCombatSnapshot ? Mathf.Max(1, combat.PlayerMaxHealth) : 1;
            int health = hasCombatSnapshot ? Mathf.Clamp(combat.PlayerHealth, 0, maxHealth) : maxHealth;
            string state = combat.RespawnSeconds > 0f
                ? Loc.T("ui.arcade.hud.respawn_short", Mathf.CeilToInt(combat.RespawnSeconds))
                : string.Empty;
            playerStatuses.Clear();
            playerStatuses.Add(new PlayerStatusDisplay(Loc.T("ui.arcade.hud.self"), health, maxHealth,
                snapshot.RemainingLives, snapshot.RunGold + (hasCombatSnapshot ? combat.ArenaGold : 0L), state, true,
                snapshot.ScoreMultiplier, snapshot.MultiplierProgress, snapshot.Score));
            RefreshPlayerStatusList();
        }

        /// <summary>按本机优先、其余玩家沿服务器顺序刷新联机玩家列表，不复制或修改权威战斗状态。</summary>
        private void RefreshOnlinePlayerStatuses(OnlineArcadeRunClientSnapshot snapshot, long selfId)
        {
            playerStatuses.Clear();
            OnlineArcadePlayerSnapshot self = snapshot.Players.FirstOrDefault(player => player.PlayerId == selfId);
            if (self.PlayerId != 0) AddOnlinePlayerStatus(snapshot, self, true);
            foreach (OnlineArcadePlayerSnapshot player in snapshot.Players)
                if (player.PlayerId != 0 && player.PlayerId != selfId) AddOnlinePlayerStatus(snapshot, player, false);
            RefreshPlayerStatusList();
        }

        private void AddOnlinePlayerStatus(OnlineArcadeRunClientSnapshot snapshot,
            OnlineArcadePlayerSnapshot player, bool isSelf)
        {
            OnlineArcadeBattlePlayerSnapshot battle = snapshot.Battle.Players
                .FirstOrDefault(value => value.PlayerId == player.PlayerId);
            OnlineArcadeConnectionSnapshot connection = snapshot.Connections
                .FirstOrDefault(value => value.PlayerId == player.PlayerId);
            bool connected = connection.PlayerId == 0 || connection.Connected;
            string state = string.Empty;
            if (!connected) state = Loc.T("ui.arcade.hud.reconnecting");
            else if (battle.PlayerId != 0 && !battle.Alive)
                state = battle.DownedSeconds>0 ? CoopDownedText(battle) : player.Lives <= 0
                    ? Loc.T("ui.arcade.hud.eliminated")
                    : battle.RespawnSeconds > 0f
                        ? Loc.T("ui.arcade.hud.respawn_short", Mathf.CeilToInt(battle.RespawnSeconds))
                        : Loc.T("ui.arcade.hud.respawning");

            int maxHealth = battle.PlayerId != 0 ? Mathf.Max(1, battle.MaxHealth) : 1;
            int health = connected && battle.PlayerId != 0 && battle.Alive
                ? Mathf.Clamp(battle.CurrentHealth, 0, maxHealth)
                : 0;
            string displayName = isSelf
                ? Loc.T("ui.arcade.hud.self")
                : string.IsNullOrWhiteSpace(player.DisplayName)
                    ? Loc.T("ui.arcade.hud.teammate")
                    : player.DisplayName;
            playerStatuses.Add(new PlayerStatusDisplay(displayName, health, maxHealth,
                player.Lives, player.RunGold, battle.InTreasureRoom && connected ? Loc.T(snapshot.Battle.ArenaNumber == 2 ? "ui.arcade.hud.armory_member" : "ui.arcade.hud.treasure_member") : state, isSelf,
                player.ScoreMultiplier, player.MultiplierProgress, player.Score));
        }

        /// <summary>列表数量先于刷新更新，确保复用行不会泄漏上一名玩家的文本、颜色或显隐状态。</summary>
        private void RefreshPlayerStatusList()
        {
            UI.PlayerStatusList.totalCount = playerStatuses.Count;
            UI.PlayerStatusList.RefreshCells();
        }

        private void RefreshPlayerStatus(Transform transform, int index)
        {
            if (index < 0 || index >= playerStatuses.Count) return;
            ArcadePlayerStatusBoxUI box = transform.GetComponent<ArcadePlayerStatusBoxUI>();
            if (box == null) return;
            PlayerStatusDisplay status = playerStatuses[index];
            float ratio = status.MaxHealth > 0 ? Mathf.Clamp01((float)status.Health / status.MaxHealth) : 0f;
            Set(box.TextName, status.Name);
            Set(box.TextLives, Loc.T("ui.arcade.hud.life_count", status.Lives));
            Set(box.TextGold, Loc.T("ui.arcade.hud.gold_compact", status.Gold));
            Set(box.TextScore, Loc.T("ui.arcade.hud.score", status.Score));
            Set(box.TextState, status.State);
            box.TextState.gameObject.SetActive(!string.IsNullOrEmpty(status.State));
            ApplyHealthFill(box.HealthFill, ratio);
            RenderMultiplier(box, status);
            box.Background.color = status.IsSelf
                ? new Color(0.055f, 0.12f, 0.18f, 0.92f)
                : new Color(0.055f, 0.075f, 0.09f, 0.88f);
        }

        /// <summary>每次权威刷新覆盖复用行；满档显示满条，普通档只投影计分保留的真实余量。</summary>
        private void RenderMultiplier(ArcadePlayerStatusBoxUI box, PlayerStatusDisplay status)
        {
            var maximum = multiplierRules != null && status.Multiplier >= multiplierRules.MaxMultiplier;
            var progress = maximum ? 1f : multiplierRules?.ProgressPerLevel > 0
                ? Mathf.Clamp01((float)status.MultiplierProgress / multiplierRules.ProgressPerLevel) : 0f;
            box.TextMultiplier.text = Loc.T("ui.arcade.hud.multiplier", status.Multiplier);
            box.TextMultiplierState.text = maximum ? Loc.T("ui.arcade.hud.multiplier_max") : string.Empty;
            box.TextMultiplierState.gameObject.SetActive(maximum);
            box.TextMultiplierState.color = new Color(.12f, .1f, .04f, 1f);
            HealthFillUtility.Apply(box.MultiplierFill, progress);
            box.MultiplierFill.color = maximum ? new Color(1f, .83f, .3f, 1f) : new Color(.95f, .69f, .21f, 1f);
        }

        private void ClearPlayerStatuses()
        {
            playerStatuses.Clear();
            if (UI?.PlayerStatusList != null) UI.PlayerStatusList.ClearCells();
        }

        private static Color HealthColor(float ratio)
        {
            if (ratio <= 0.25f) return new Color(0.95f, 0.2f, 0.18f, 1f);
            if (ratio <= 0.55f) return new Color(1f, 0.72f, 0.16f, 1f);
            return new Color(0.2f, 0.82f, 0.38f, 1f);
        }

        /// <summary>刷新复用玩家行的血条；无 Sprite 时沿用角色血条的锚点裁切，避免 fillAmount 只改数值而画面保持满条。</summary>
        internal static void ApplyHealthFill(Image fill, float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            HealthFillUtility.Apply(fill, ratio);
            fill.color = HealthColor(ratio);
        }

        private static string ProductNameKey(string productId) =>
            string.IsNullOrWhiteSpace(productId) ? string.Empty : $"ui.arcade.product.{productId}.name";

        private readonly struct PlayerStatusDisplay
        {
            public PlayerStatusDisplay(string name, int health, int maxHealth, int lives, long gold,
                string state, bool isSelf, int multiplier, int multiplierProgress, long score)
            {
                Name = name ?? string.Empty;
                Health = health;
                MaxHealth = maxHealth;
                Lives = lives;
                Gold = gold;
                State = state ?? string.Empty;
                IsSelf = isSelf;
                Multiplier = multiplier;
                MultiplierProgress = multiplierProgress;
                Score = score;
            }

            public string Name { get; }
            public int Health { get; }
            public int MaxHealth { get; }
            public int Lives { get; }
            public long Gold { get; }
            public long Score { get; }
            public string State { get; }
            public bool IsSelf { get; }
            public int Multiplier { get; }
            public int MultiplierProgress { get; }
        }

        private static void Set(TextMeshProUGUI target, string value) { if (target != null) target.text = value ?? string.Empty; }
        private static void SetButton(Button button, string value) => Set(button?.GetComponentInChildren<TextMeshProUGUI>(true), value);
    }
}
