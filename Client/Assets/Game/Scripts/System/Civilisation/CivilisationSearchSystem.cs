using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    public readonly struct CivilisationSearchFinishedEvent
    {
        public string PointId { get; }
        public string Error { get; }
        /// <summary>已完成事务的动作身份；0 为旧调用未提供身份，表现不能据此认领成功反馈。</summary>
        public long ActionId { get; }
        /// <summary>成功奖励是否为世界掉落；表现直接使用结算事实，不重新推断配置。</summary>
        public bool RewardDropped { get; }
        public CivilisationSearchFinishedEvent(string id, string error, long actionId = 0, bool rewardDropped = false)
        { PointId = id; Error = error; ActionId = actionId; RewardDropped = rewardDropped; }
    }

    /// <summary>裁决文明搜索、工具交互、近战挥击采集与取水，原子提交奖励和访问记录；树矿产物落地，其余奖励入包。不持有资源表现或 UI，不负责战斗伤害、饮用和建筑拆除。</summary>
    public sealed class CivilisationSearchSystem : AbstractSystem
    {
        /// <summary>当前系统拥有的持续动作 ID；0 表示空闲，只处理同一身份的结束事件。</summary>
        private long actionId;
        /// <summary>采集开始时的地图实例身份，卸载只取消本实例的动作。</summary>
        private long mapInstanceId;
        /// <summary>Luban 中的稳定采集点 ID；一次性点成功保存后写入访问记录，可重复水源不写入。</summary>
        private string pointId;
        /// <summary>当前地图实例中逐次挥击累计的有效秒数；不写存档，卸载即清空。</summary>
        private readonly Dictionary<string, float> meleeHarvestSeconds = new Dictionary<string, float>(StringComparer.Ordinal);
        /// <summary>挥击进度归属的地图实例；0 表示没有绑定，不能跨远征沿用。</summary>
        private long meleeHarvestMapInstanceId;
        private IUnRegister buildingChangedUnregister;
        protected override void OnInit()
        {
            this.RegisterEvent<PlayerActionEndedEvent>(OnEnded);
            this.RegisterEvent<MapCurrentReleasedEvent>(e => ReleaseMap(e.MapInstanceId));
            buildingChangedUnregister = this.RegisterEvent<TemporaryBuildingChangedEvent>(e =>
            {
                // 模式切换事件在进入预览/拆除时同步发布，不能等下一帧才取消采集。
                if (actionId != 0 && e.Snapshot.Mode != TemporaryBuildingMode.Normal)
                    this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.Replaced);
            });
            this.RegisterEvent<CombatDamageAppliedEvent>(e => { if (e.TargetUnitId == PlayerModel.LocalPlayerUnitId) this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.Damaged); });
            LifeCycle.RegisterUpdate(OnUpdate);
        }
        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate); buildingChangedUnregister?.UnRegister(); buildingChangedUnregister = null;
            actionId = 0; pointId = null;
            meleeHarvestSeconds.Clear(); meleeHarvestMapInstanceId = 0;
        }

        private bool Validate(long instance, string id, out CivilisationSearchPointDefinition point, out string error, bool meleeHarvest = false)
        {
            point = null; error = null;
            var map = this.GetModel<MapSceneModel>();
            if(this.GetSystem<PlayerMoveSystem>()?.IsSwimming == true) { error="civilisation.error.context";return false; }
            if (UIHelper.HasUIOpen || this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing || map.CurrentMapId != MapIds.CivilisationValley ||
                map.Status.Value != MapLoadStatus.Loaded || map.CurrentMode != MapPreparationMode.OfflineFull || this.GetModel<SaveModel>().CurrentDomain != OfflineSaveDomain.Expedition ||
                !this.GetModel<UnitModel>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) || !player.IsActive || player.MapInstanceId != instance)
            { error = "civilisation.error.context"; return false; }
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetCivilisationCatalog(out var catalog, out error)) return false;
            CivilisationMapPointResolver.TryGetPoint(map.CurrentHandle,catalog,id,out point);
            if (point == null || (!point.Repeatable && this.GetModel<CivilisationExpeditionModel>().HasVisited(id))) { error = "civilisation.error.searched"; return false; }
            if (point.RequiredWeaponItemId != 0 && !meleeHarvest)
            {
                var equipment = this.GetModel<WeaponEquipmentModel>();
                if (equipment == null || equipment.ActiveSlot != WeaponSlot.Melee || !TryGetToolEfficiency(point, equipment.MeleeWeaponItemId, out _))
                { error = point.RequiredWeaponItemId == 18 ? "civilisation.error.pickaxe" : "civilisation.error.axe"; return false; }
            }
            if ((point.RequiredWeaponItemId != 0 || point.Repeatable) && (this.GetModel<TemporaryBuildingModel>()?.Mode != TemporaryBuildingMode.Normal ||
                (this.GetModel<CombatModel>()?.PlayerHP.Value ?? 0) <= 0))
            { error = "civilisation.error.context"; return false; }
            if ((player.Position - point.Position).sqrMagnitude > 2.25f * 2.25f) { error = "civilisation.error.distance"; return false; }
            return true;
        }

        /// <summary>允许同类升级工具替代基础工具；效率低于要求的武器仍只允许挥击采集，不自动成为专用工具。</summary>
        private bool TryGetToolEfficiency(CivilisationSearchPointDefinition point, int itemId, out float efficiency)
        {
            efficiency = 0f;
            var config = this.GetUtility<IRuntimeConfigProvider>();
            if (!config.TryGetWeapon(itemId, out var equipped, out _) || equipped.WeaponKind != global::WeaponKind.Melee ||
                !config.TryGetWeapon(point.RequiredWeaponItemId, out var required, out _)) return false;
            efficiency = point.FellTree ? equipped.TreeHarvestEfficiency : equipped.StoneHarvestEfficiency;
            var minimum = point.FellTree ? required.TreeHarvestEfficiency : required.StoneHarvestEfficiency;
            return efficiency > 0f && !float.IsNaN(efficiency) && !float.IsInfinity(efficiency) && efficiency >= minimum;
        }

        /// <summary>开始可中断调查；身份和距离在完成提交前再次验证。</summary>
        public bool TryBeginSearch(long instance, string id, out string error)
        {
            if (!Validate(instance, id, out var point, out error)) return false;
            var actions = this.GetSystem<PlayerActionSystem>();
            actions.CancelActive(PlayerActionCancelReason.Replaced);
            mapInstanceId = instance; pointId = id;
            // 工具采集与挥击共用奖励事务，动作种类仅区分表现，不执行战斗伤害。
            var kind = point.Repeatable ? PlayerActionKind.CollectWater : point.FellTree ? PlayerActionKind.ChopTree :
                point.RequiredWeaponItemId == 18 ? PlayerActionKind.MineStone : PlayerActionKind.Search;
            var icon = string.Empty;
            if (point.RequiredWeaponItemId != 0 && this.GetUtility<IRuntimeConfigProvider>().TryGetItem(point.RequiredWeaponItemId, out var tool, out _)) icon = tool.IconAddress;
            var seconds = point.Seconds;
            if (point.RequiredWeaponItemId != 0 && TryGetToolEfficiency(point, this.GetModel<WeaponEquipmentModel>().MeleeWeaponItemId, out var efficiency))
                seconds /= efficiency;
            if (actions.TryBegin(new PlayerActionRequest(kind, seconds, icon), out actionId, out error)) return true;
            pointId = null; return false;
        }
        /// <summary>地图释放只取消本实例拥有的行动，重复调用无副作用。</summary>
        public void ReleaseMap(long instance)
        {
            if (actionId != 0 && mapInstanceId == instance) this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.OwnerCanceled);
            if (meleeHarvestMapInstanceId == instance) { meleeHarvestSeconds.Clear(); meleeHarvestMapInstanceId = 0; }
        }
        /// <summary>
        /// 仅由武器系统在接受一次近战挥击后调用；选择扇形内最近资源，每击按武器冷却乘配置效率累计工作秒数。
        /// 不自行接受攻击输入或推进冷却；失败结算保留进度，资源与奖励由同一保存事务提交。
        /// </summary>
        internal bool TryApplyMeleeHarvest(long instance, Vector3 origin, Vector3 aimDirection, int weaponItemId)
        {
            var map = this.GetModel<MapSceneModel>();
            var equipment = this.GetModel<WeaponEquipmentModel>();
            var config = this.GetUtility<IRuntimeConfigProvider>();
            aimDirection.y = 0f;
            if (map.CurrentMapId != MapIds.CivilisationValley || map.CurrentMode != MapPreparationMode.OfflineFull ||
                equipment == null || equipment.ActiveSlot != WeaponSlot.Melee || equipment.MeleeWeaponItemId != weaponItemId ||
                !config.TryGetWeapon(weaponItemId, out var weapon, out _) || weapon.WeaponKind != global::WeaponKind.Melee ||
                weapon.AttackCooldownSeconds <= 0f || weapon.AttackRange <= 0f || weapon.MeleeArcDegrees <= 0f ||
                float.IsNaN(aimDirection.sqrMagnitude) || float.IsInfinity(aimDirection.sqrMagnitude) || aimDirection.sqrMagnitude < .000001f ||
                !config.TryGetCivilisationCatalog(out var catalog, out _)) return false;

            // 生成图只能用当局坐标，不能回退到模板位置；已释放句柄不参与命中。
            var points = catalog.Points;
            if (map.CurrentHandle is GeneratedValleyContentHandle generated)
            {
                if (!generated.IsValid) return false;
                points = generated.Points;
            }
            if (points == null) return false;
            CivilisationSearchPointDefinition selected = null;
            var nearestSqr = Mathf.Min(weapon.AttackRange, 2.25f);
            nearestSqr *= nearestSqr;
            var model = this.GetModel<CivilisationExpeditionModel>();
            foreach (var candidate in points)
            {
                if (candidate.MapId != map.CurrentMapId || candidate.Repeatable ||
                    (!candidate.FellTree && candidate.RequiredWeaponItemId != 18) || model.HasVisited(candidate.Id)) continue;
                var candidateEfficiency = candidate.FellTree ? weapon.TreeHarvestEfficiency : weapon.StoneHarvestEfficiency;
                if (candidateEfficiency <= 0f || float.IsNaN(candidateEfficiency) || float.IsInfinity(candidateEfficiency)) continue;
                var offset = candidate.Position - origin; offset.y = 0f;
                var distanceSqr = offset.sqrMagnitude;
                if (float.IsNaN(distanceSqr) || distanceSqr > nearestSqr ||
                    (distanceSqr > .000001f && Vector3.Angle(aimDirection, offset) > weapon.MeleeArcDegrees * .5f)) continue;
                selected = candidate; nearestSqr = distanceSqr;
            }
            if (selected == null || !Validate(instance, selected.Id, out selected, out _, true)) return false;
            if (meleeHarvestMapInstanceId != instance)
            { meleeHarvestSeconds.Clear(); meleeHarvestMapInstanceId = instance; }
            meleeHarvestSeconds.TryGetValue(selected.Id, out var elapsed);
            // 效率只影响推进速度，不能借战斗伤害改变资源产量或保存语义。
            var efficiency = selected.FellTree ? weapon.TreeHarvestEfficiency : weapon.StoneHarvestEfficiency;
            elapsed = Mathf.Min(selected.Seconds, elapsed + weapon.AttackCooldownSeconds * efficiency);
            meleeHarvestSeconds[selected.Id] = elapsed;
            if (elapsed + .0001f >= selected.Seconds && CommitHarvest(selected, selected.Id, 0, instance))
                meleeHarvestSeconds.Remove(selected.Id);
            return true;
        }
        private void OnUpdate()
        {
            if (actionId != 0 && !Validate(mapInstanceId, pointId, out _, out _)) this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.LeftRange);
        }
        private void OnEnded(PlayerActionEndedEvent e)
        {
            if (actionId == 0 || e.ActionId != actionId) return;
            actionId = 0;
            if (!e.Completed || !Validate(mapInstanceId, pointId, out var point, out _)) { pointId = null; return; }
            var id = pointId; pointId = null;
            if (CommitHarvest(point, id, e.ActionId, mapInstanceId)) meleeHarvestSeconds.Remove(id);
        }
        /// <summary>交互完成与挥击完成共用奖励、访问记录和保存事务；失败原子回滚，成功后才广播表现事件。</summary>
        private bool CommitHarvest(CivilisationSearchPointDefinition point, string id, long completedActionId, long instance)
        {
            var save = this.GetSystem<SaveSystem>();
            var before = save.CaptureRuntimeSnapshot();
            var inventory = this.GetSystem<InventorySystem>();
            var model = this.GetModel<CivilisationExpeditionModel>();
            var dropsToGround = !point.Repeatable && (point.FellTree || point.RequiredWeaponItemId == 18);
            var dropPort = dropsToGround ? this.GetUtility<ICivilisationHarvestDropPort>() : null;
            IReadOnlyList<int> createdLoot = Array.Empty<int>();
            string error = null;
            var committed = false;
            try
            {
                if (point.ItemId != 0)
                {
                    if (dropsToGround)
                    {
                        if (dropPort == null) { error = "采集掉落端口不可用"; return false; }
                        if (!dropPort.TryCreateHarvestDrop(instance, point.Position, point.ItemId, point.Amount, out createdLoot, out error)) return false;
                    }
                    else if (!inventory.TryBuildPlayerBagAddCandidate(point.ItemId, point.Amount, out var candidate, out error) || !inventory.TryApplyPlayerBagCandidate(candidate)) return false;
                }
                var technology = this.GetModel<CivilisationTechnologyModel>().IsUnlocked(point.TechnologyId) ? null : point.TechnologyId;
                // 水源不耗尽；只把新增物品保存，避免访问记录让下次取水或读档后的入口失效。
                if (!point.Repeatable) model.Visit(id, technology, point.ChapterId);
                // 阅读记录与搜索奖励同事务保存；失败时一并回滚，不会出现未成功搜索却已入日志。
                if (!point.Repeatable && (!string.IsNullOrEmpty(point.TechnologyId) || !string.IsNullOrEmpty(point.ChapterId)))
                    model.RecordReading(point.NameKey + ".record");
                save.MarkDirty("civilisation search");
                if (!save.Flush(out error)) return false;
                committed = true;
                if (!dropsToGround) inventory.PublishPlayerBagChanged();
                return true;
            }
            catch (Exception ex) { error = ex.Message; }
            finally
            {
                if (!committed)
                {
                    // 世界创建先撤销，再恢复采集与保存状态，失败后仍可重试且不留下重复产物。
                    try { dropPort?.RollbackHarvestDrop(createdLoot); }
                    finally { save.RestoreRuntimeSnapshot(before, out _); }
                }
                this.SendEvent(new CivilisationSearchFinishedEvent(id, committed ? null : error ?? "civilisation.error.save", completedActionId,
                    committed && dropsToGround && point.ItemId != 0));
            }
            return committed;
        }

        /// <summary>每趟在起点领取一次免费补给；背包、领取标识及免兑换份额同时保存。</summary>
        public bool TryClaimBasicPreparation(out string error)
        {
            if((this.GetModel<MapSceneModel>().CurrentHandle as GeneratedValleyContentHandle)?.Layout.GenerationVersion==3)
            {error="civilisation.error.preparation";return false;}
            error = null;
            if (this.GetModel<MapSceneModel>().CurrentMapId != MapIds.CivilisationValley || this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing ||
                this.GetModel<SaveModel>().CurrentDomain != OfflineSaveDomain.Expedition) { error = "civilisation.error.context"; return false; }
            var model = this.GetModel<CivilisationExpeditionModel>();
            if (model.PreparationClaimed) { error = "civilisation.error.preparation"; return false; }
            var inventory = this.GetSystem<InventorySystem>();
            if (!inventory.TryBuildPlayerBagAddCandidate(29, 1, out var candidate, out error)) return false;
            var save = this.GetSystem<SaveSystem>(); var before = save.CaptureRuntimeSnapshot();
            var committed = false;
            try
            {
                if (!inventory.TryApplyPlayerBagCandidate(candidate)) return false;
                model.ClaimPreparation(); save.MarkDirty("civilisation basic preparation");
                if (!save.Flush(out error)) return false;
                committed = true; inventory.PublishPlayerBagChanged(); return true;
            }
            finally { if (!committed) save.RestoreRuntimeSnapshot(before, out _); }
        }
    }
}
