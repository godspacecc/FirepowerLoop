using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using QFramework;
using TMPro;
using UnityEngine;

namespace Game
{
    /// <summary>渲染安全节点与四项商店报价并提交购买、继续、结算和放弃意图；不直接改存档或发奖励。</summary>
    public sealed class ArcadeShopUICtrl : BaseUICtrl
    {
        private ArcadeRunSaveData current;
        private bool awaitingAbandonConfirmation;
        private bool operationPending;
        private bool onlineMode;
        private OnlineArcadeRunClientSnapshot online;
        private const string TransferOfferId = "__online_transfer_25";
        private ArcadeShopUI UI => BaseUI as ArcadeShopUI;

        protected override string Path() => "Arcade/ArcadeShop";
        public override UILayer Layer() => UILayer.Dialog;
        public override bool BlocksGame => true;

        protected override void OnInit()
        {
            UI.OfferList.AddItemRefreshListener(RefreshOffer);
            // 商品卡本身承担购买入口，底部区域只展示本局已经持有的内容。
            UI.BtnBuy.gameObject.SetActive(false);
            UI.BtnContinue.onClick.AddListener(ContinueRun);
            UI.BtnChallenge.onClick.AddListener(StartChallenge);
            UI.BtnSettle.onClick.AddListener(() => SettleAsync().Forget());
            UI.BtnAbandon.onClick.AddListener(() => AbandonAsync().Forget());
            this.RegisterEvent<ArcadeRunChangedEvent>(OnRunChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineArcadeRunChangedEvent>(evt => OnOnlineRunChanged(evt.Snapshot))
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ =>
                { if (onlineMode) RenderOnline(online); else Render(current); })
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            awaitingAbandonConfirmation = false;
            operationPending = false;
            online = this.GetSystem<OnlineArcadeRunSystem>().Snapshot;
            onlineMode = online.HasRun && online.Phase is OnlineArcadeRunPhase.SafeShop or OnlineArcadeRunPhase.Victory;
            if (onlineMode) RenderOnline(online); else Render(this.GetModel<ArcadeRunModel>().Current);

        }

        protected override void OnHide()
        {
            ClearOffers();
            awaitingAbandonConfirmation = false;
        }

        protected override void OnUnload() => ClearOffers();

        private void OnRunChanged(ArcadeRunChangedEvent evt)
        {
            if (onlineMode) return;
            if (!IsShow()) return;
            if (evt.Phase is ArcadeRunPhase.SafeShop or ArcadeRunPhase.Victory) Render(evt.Snapshot);
            else if (evt.Phase == ArcadeRunPhase.None) Hide();
        }

        private void OnOnlineRunChanged(OnlineArcadeRunClientSnapshot snapshot)
        {
            if (!onlineMode || !IsShow()) return;
            online = snapshot;
            if (!snapshot.HasRun) { Hide(); return; }
            if (snapshot.Phase is OnlineArcadeRunPhase.Arena or OnlineArcadeRunPhase.Challenge)
            { Hide(); UIHelper.ShowUI<ArcadeHudUICtrl>(); return; }
            RenderOnline(snapshot);

        }

        private void Render(ArcadeRunSaveData snapshot)
        {
            if (UI == null || snapshot == null) return;
            current = snapshot.Clone();
            UI.BtnSettle.gameObject.SetActive(current.HasResult);
            UI.BtnContinue.gameObject.SetActive(!current.HasResult);
            UI.BtnAbandon.gameObject.SetActive(!current.Victory);
            if (current.Shop?.Offers == null) current.Shop = new ArcadeShopSaveData();
            Set(UI.TextProgress, current.Victory ? Loc.T("ui.arcade.victory") : Loc.T("ui.arcade.shop.progress", current.CompletedArenaCount, current.NextArenaNumber));
            current.EmergencyResources.TryGetValue(ArcadeRunDefaults.BombResourceId, out var bombs);
            current.EmergencyResources.TryGetValue(ArcadeRunDefaults.DashResourceId, out var dashes);
            Set(UI.TextResources, Loc.T("ui.arcade.shop.resources", current.RemainingLives, current.RunGold, bombs, dashes));
            Set(UI.TextRewards, Loc.T("ui.arcade.hud.score", current.Score));
            UI.OfferList.totalCount = current.Shop.Offers.Count;
            UI.OfferList.RefreshCells();
            bool showChallenge = !current.Victory && ShouldShowChallengeButton(current.Shop.ChallengeId, current.Shop.ChallengeAttempted);
            UI.BtnChallenge.gameObject.SetActive(showChallenge);
            if (showChallenge)
            {
                SetButton(UI.BtnChallenge, Loc.T("ui.arcade.challenge.enter"));
                UI.BtnChallenge.interactable = true;
            }
            RenderOwnedContent(current.PurchasedUpgradeStacks, current.Consumables, current.EquipmentBySlot);
        }

        private void RefreshOffer(Transform transform, int index)
        {
            if (onlineMode) { RefreshOnlineOffer(transform, index); return; }
            if (current?.Shop?.Offers == null || index < 0 || index >= current.Shop.Offers.Count) return;
            var box = transform.GetComponent<ArcadeShopOfferBoxUI>();
            var offer = current.Shop.Offers[index];
            if (box == null || offer == null) return;
            Set(box.TextType, ArcadeUiFormatter.OfferType(offer.Kind));
            Set(box.TextName, ArcadeUiFormatter.ProductName(offer.ProductId));
            Set(box.TextEffect, ArcadeUiFormatter.OfferEffect(offer, this.GetUtility<IRuntimeConfigProvider>()));
            Set(box.TextPrice, Loc.T("ui.arcade.price", offer.Price));
            Set(box.TextState, Loc.T(offer.Purchased ? "ui.arcade.purchased" : "ui.arcade.buy"));
            box.SelectedRoot.SetActive(false);
            box.DisabledRoot.SetActive(offer.Purchased);
            box.BtnSelect.interactable = !offer.Purchased && current.RunGold >= offer.Price;
            box.BtnSelect.onClick.RemoveAllListeners();
            string offerId = offer.OfferId;
            box.BtnSelect.onClick.AddListener(() => BuyOffer(offerId));
        }

        /// <summary>由商品卡直接提交单机购买；保存成功后播放购买音，失败只显示原因且不改变展示状态。</summary>
        private void BuyOffer(string offerId)
        {
            if (string.IsNullOrWhiteSpace(offerId)) return;
            if (!this.GetSystem<ArcadeRunSystem>().TryPurchase(offerId, out var snapshot, out var error))
            {
                Set(UI.TextMessage, error);
                return;
            }
            awaitingAbandonConfirmation = false;
            this.GetSystem<AudioSystem>().PlayUiSound("ArcadeUiPurchase");
            Set(UI.TextMessage, Loc.T("ui.arcade.purchase_saved"));
            Render(snapshot);
        }

        private void ContinueRun()
        {
            if (onlineMode) { ToggleOnlineReady().Forget(); return; }
            if (!this.GetSystem<ArcadeRunSystem>().TryContinue(out _, out var error))
            {
                Set(UI.TextMessage, error);
                return;
            }
            if (this.GetModel<ArcadeRunModel>().Phase != ArcadeRunPhase.Arena)
            {
                Set(UI.TextMessage, this.GetSystem<ArcadeCombatSystem>().LastEntryError ?? Loc.T("ui.arcade.error.open"));
                return;
            }
            Hide();
            UIHelper.ShowUI<ArcadeHudUICtrl>();
        }

        /// <summary>在安全节点原子记录挑战已尝试后进入单波限时现场；保存失败时保持商店不变。</summary>
        private void StartChallenge()
        {
            if (onlineMode) { ToggleOnlineChallengeReady().Forget(); return; }
            if (!this.GetSystem<ArcadeRunSystem>().TryStartChallenge(out _, out var error))
            {
                Set(UI.TextMessage, error);
                return;
            }
            if (this.GetModel<ArcadeRunModel>().Phase != ArcadeRunPhase.Challenge)
            {
                Set(UI.TextMessage, this.GetSystem<ArcadeCombatSystem>().LastEntryError ?? Loc.T("ui.arcade.error.open"));
                return;
            }
            Hide();
            UIHelper.ShowUI<ArcadeHudUICtrl>();
        }

        private async UniTaskVoid SettleAsync()
        {
            if (onlineMode)
            {
                if (operationPending) return;
                operationPending = true;
                long submittedRevision = online.Revision;
                var system = this.GetSystem<OnlineArcadeRunSystem>();
                OnlineArcadeOperationResult result = await system.ConfirmSettlementAsync();
                // 两人同时自动结算时，先提交者会更新版本；只在已收到新权威版本后补交一次。
                if (!result.Success && online.HasRun && online.Phase == OnlineArcadeRunPhase.Victory &&
                    online.Revision > submittedRevision && !LocalOnlinePlayer().SettlementConfirmed)
                    result = await system.ConfirmSettlementAsync();
                operationPending = false;
                if (!result.Success) Set(UI.TextMessage, ArcadeUiFormatter.OnlineResult(result.Code));
                return;
            }
            if (operationPending) return;
            operationPending = true;
            var mapResult = await this.GetSystem<ArcadeSceneFlowSystem>().ReturnToBaseAsync();
            if (!mapResult.Success)
            {
                operationPending = false;
                Set(UI.TextMessage, mapResult.Error);
                return;
            }
            if (!this.GetSystem<ArcadeRunSystem>().TrySettle(out _, out var error))
            {
                operationPending = false;
                Set(UI.TextMessage, error);
                return;
            }
            Hide();
        }

        private async UniTaskVoid AbandonAsync()
        {
            if (!awaitingAbandonConfirmation)
            {
                awaitingAbandonConfirmation = true;
                Set(UI.TextMessage, Loc.T("ui.arcade.abandon_confirm"));
                return;
            }
            if (onlineMode)
            {
                if (operationPending) return;
                operationPending = true;
                OnlineArcadeOperationResult result = await this.GetSystem<OnlineArcadeRunSystem>().ConfirmAbandonAsync();
                operationPending = false;
                if (!result.Success) Set(UI.TextMessage, ArcadeUiFormatter.OnlineResult(result.Code));
                return;
            }
            if (operationPending) return;
            operationPending = true;
            var mapResult = await this.GetSystem<ArcadeSceneFlowSystem>().ReturnToBaseAsync();
            if (!mapResult.Success)
            {
                operationPending = false;
                Set(UI.TextMessage, mapResult.Error);
                return;
            }
            if (!this.GetSystem<ArcadeRunSystem>().TryAbandon(out var error))
            {
                operationPending = false;
                Set(UI.TextMessage, error);
                return;
            }
            Hide();
        }

        private void ClearOffers()
        {
            if (UI?.OfferList == null) return;
            foreach (Transform child in UI.OfferList.transform)
                if (child.TryGetComponent<ArcadeShopOfferBoxUI>(out var box)) box.BtnSelect.onClick.RemoveAllListeners();
            UI.OfferList.ClearCells();
        }

        private OnlineArcadePlayerSnapshot LocalOnlinePlayer() => online.Players.FirstOrDefault(x =>
            x.PlayerId == this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId);

        private void RenderOnline(OnlineArcadeRunClientSnapshot snapshot)
        {
            if (UI == null || !snapshot.HasRun) return;
            online = snapshot;
            OnlineArcadePlayerSnapshot self = LocalOnlinePlayer();
            if (self.PlayerId == 0) return;
            OnlineArcadePlayerSnapshot teammate = snapshot.Players.FirstOrDefault(x => x.PlayerId != self.PlayerId);
            bool victory = snapshot.Phase == OnlineArcadeRunPhase.Victory;
            UI.BtnSettle.gameObject.SetActive(victory);
            UI.BtnContinue.gameObject.SetActive(!victory);
            UI.BtnAbandon.gameObject.SetActive(!victory);
            Set(UI.TextProgress, Loc.T("ui.arcade.online.shop.progress", snapshot.CompletedArena, snapshot.NextArena,
                string.IsNullOrWhiteSpace(teammate.DisplayName) ? Loc.T("ui.arcade.online.waiting_teammate") : teammate.DisplayName,
                teammate.Ready ? Loc.T("ui.arcade.online.ready") : Loc.T("ui.arcade.online.not_ready")));
            Set(UI.TextResources, Loc.T("ui.arcade.shop.resources", self.Lives, self.RunGold, self.Bombs, self.Dashes));
            Set(UI.TextRewards, Loc.T("ui.arcade.hud.score", self.Score));
            if (victory) Set(UI.TextProgress, Loc.T("ui.arcade.victory"));
            UI.OfferList.totalCount = victory ? 0 : self.ShopOffers.Count + 1;
            UI.OfferList.RefreshCells();
            RenderOwnedContent(self.UpgradeStacks, self.Consumables, self.Equipment);
            SetButton(UI.BtnContinue, self.Ready ? Loc.T("ui.arcade.online.cancel_ready") : Loc.T("ui.arcade.online.ready"));
            UI.BtnContinue.interactable = snapshot.OperationState == OnlineArcadeOperationState.Idle && self.SettlementState == 0;
            bool showChallenge = !victory && ShouldShowChallengeButton(snapshot.ChallengeId, snapshot.ChallengeAttempted);
            UI.BtnChallenge.gameObject.SetActive(showChallenge);
            if (showChallenge)
            {
                string key = self.ChallengeReady ? "ui.arcade.challenge.cancel_ready" : "ui.arcade.challenge.ready";
                SetButton(UI.BtnChallenge, Loc.T(key));
                UI.BtnChallenge.interactable = self.Lives > 0 &&
                                               snapshot.OperationState == OnlineArcadeOperationState.Idle &&
                                               self.SettlementState == 0;
            }
        }

        /// <summary>挑战在当前安全节点仅允许尝试一次；已结束后隐藏入口，避免无效按钮继续占用底栏。</summary>
        internal static bool ShouldShowChallengeButton(string challengeId, bool attempted) =>
            !string.IsNullOrWhiteSpace(challengeId) && !attempted;

        private void RefreshOnlineOffer(Transform transform, int index)
        {
            var box = transform.GetComponent<ArcadeShopOfferBoxUI>();
            if (box == null) return;
            OnlineArcadePlayerSnapshot self = LocalOnlinePlayer();
            bool transfer = index == self.ShopOffers.Count;
            string offerId = transfer ? TransferOfferId : self.ShopOffers[index].OfferId;
            if (transfer)
            {
                Set(box.TextType, Loc.T("ui.arcade.online.transfer"));
                Set(box.TextName, Loc.T("ui.arcade.online.transfer_25"));
                Set(box.TextEffect, Loc.T("ui.arcade.online.transfer_hint"));
                Set(box.TextPrice, Loc.T("ui.arcade.price", Math.Min(25, self.RunGold)));
                Set(box.TextState, Loc.T("ui.arcade.buy"));
                box.DisabledRoot.SetActive(self.RunGold <= 0);
            }
            else
            {
                OnlineArcadeShopOfferSnapshot offer = self.ShopOffers[index];
                ArcadeShopOfferSaveData display = ToDisplayOffer(offer);
                Set(box.TextType, ArcadeUiFormatter.OfferType(display.Kind));
                Set(box.TextName, ArcadeUiFormatter.ProductName(display.ProductId));
                Set(box.TextEffect, ArcadeUiFormatter.OfferEffect(display, this.GetUtility<IRuntimeConfigProvider>()));
                Set(box.TextPrice, Loc.T("ui.arcade.price", offer.Price));
                Set(box.TextState, Loc.T(offer.PurchaseCount >= offer.MaxPurchases ? "ui.arcade.purchased" : "ui.arcade.buy"));
                box.DisabledRoot.SetActive(offer.PurchaseCount >= offer.MaxPurchases);
            }
            box.SelectedRoot.SetActive(false);
            box.BtnSelect.onClick.RemoveAllListeners();
            bool idle = online.OperationState == OnlineArcadeOperationState.Idle && !operationPending;
            box.BtnSelect.interactable = transfer
                ? self.RunGold > 0 && idle
                : self.ShopOffers[index].PurchaseCount < self.ShopOffers[index].MaxPurchases &&
                  self.RunGold >= self.ShopOffers[index].Price && idle;
            box.BtnSelect.onClick.AddListener(() => BuyOnlineOffer(offerId).Forget());
        }

        /// <summary>由联机商品卡直接提交购买或金币转赠；等待服务器快照期间禁用所有商品卡，避免重复请求。</summary>
        private async UniTaskVoid BuyOnlineOffer(string offerId)
        {
            if (operationPending || string.IsNullOrWhiteSpace(offerId)) return;
            operationPending = true;
            UI.OfferList.RefreshCells();
            OnlineArcadePlayerSnapshot self = LocalOnlinePlayer();
            OnlineArcadeOperationResult result = offerId == TransferOfferId
                ? await this.GetSystem<OnlineArcadeRunSystem>().TransferGoldAsync(Math.Min(25, self.RunGold))
                : await this.GetSystem<OnlineArcadeRunSystem>().PurchaseAsync(offerId);
            operationPending = false;
            if (!result.Success)
            {
                Set(UI.TextMessage, ArcadeUiFormatter.OnlineResult(result.Code));
                UI.OfferList.RefreshCells();
                return;
            }
            Set(UI.TextMessage, Loc.T("ui.arcade.purchase_saved"));
            RenderOnline(result.Snapshot);
        }

        /// <summary>刷新底部本局持有清单；该区域只读权威快照，不参与购买选择。</summary>
        private void RenderOwnedContent(IReadOnlyDictionary<string, int> upgrades,
            IReadOnlyDictionary<string, int> consumables, IReadOnlyDictionary<string, string> equipment)
        {
            Set(UI.TextDetailName, Loc.T("ui.arcade.shop.owned_title"));
            Set(UI.TextDetailEffect, FormatOwnedContent(upgrades, consumables, equipment));
        }

        internal static string FormatOwnedContent(IReadOnlyDictionary<string, int> upgrades,
            IReadOnlyDictionary<string, int> consumables, IReadOnlyDictionary<string, string> equipment)
        {
            List<string> entries = new();
            AddAmounts(entries, upgrades);
            AddAmounts(entries, consumables);
            if (equipment != null)
            {
                entries.AddRange(equipment.Values
                    .Where(value => !string.IsNullOrWhiteSpace(value) &&
                                    !string.Equals(value, ArcadeRunDefaults.BasicWeaponId, StringComparison.Ordinal))
                    .GroupBy(value => value, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => $"{ArcadeUiFormatter.ProductName(group.Key)} × {group.Count()}"));
            }
            return entries.Count == 0 ? Loc.T("ui.arcade.shop.owned_empty") : string.Join("  ·  ", entries);
        }

        private static void AddAmounts(List<string> entries, IReadOnlyDictionary<string, int> amounts)
        {
            if (amounts == null) return;
            entries.AddRange(amounts
                .Where(entry => entry.Value > 0 && !string.IsNullOrWhiteSpace(entry.Key))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{ArcadeUiFormatter.ProductName(entry.Key)} × {entry.Value}"));
        }

        private async UniTaskVoid ToggleOnlineReady()
        {
            if (operationPending) return;
            operationPending = true;
            OnlineArcadePlayerSnapshot self = LocalOnlinePlayer();
            OnlineArcadeOperationResult result = await this.GetSystem<OnlineArcadeRunSystem>().SetReadyAsync(!self.Ready);
            operationPending = false;
            if (!result.Success) Set(UI.TextMessage, ArcadeUiFormatter.OnlineResult(result.Code));
        }

        private async UniTaskVoid ToggleOnlineChallengeReady()
        {
            if (operationPending) return;
            operationPending = true;
            OnlineArcadePlayerSnapshot self = LocalOnlinePlayer();
            OnlineArcadeOperationResult result = await this.GetSystem<OnlineArcadeRunSystem>()
                .SetChallengeReadyAsync(!self.ChallengeReady);
            operationPending = false;
            if (!result.Success) Set(UI.TextMessage, ArcadeUiFormatter.OnlineResult(result.Code));
        }

        private static ArcadeShopOfferSaveData ToDisplayOffer(OnlineArcadeShopOfferSnapshot offer) => new()
        {
            OfferId = offer.OfferId, ProductId = offer.ProductId,
            Kind = (ArcadePurchaseKind)Mathf.Clamp(offer.ProductType - 1, 0, 3), Price = offer.Price,
            Amount = offer.Amount, Purchased = offer.PurchaseCount >= offer.MaxPurchases,
        };

        private static void SetButton(UnityEngine.UI.Button button, string value) =>
            Set(button?.GetComponentInChildren<TextMeshProUGUI>(true), value);

        private static void Set(TextMeshProUGUI target, string value) { if (target != null) target.text = value ?? string.Empty; }
    }
}
