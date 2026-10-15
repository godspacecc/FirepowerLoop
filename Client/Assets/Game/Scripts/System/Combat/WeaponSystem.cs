using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 根据当前武器装备处理玩家武器意图，负责冷却、钢刀连招缓存、槽位切换和武器攻击规则。
    /// 联机存活状态读取服务器快照，攻击结果由服务器裁决；不使用或修改离线生命存档。
    /// </summary>
    public sealed class WeaponSystem : AbstractSystem
    {
        private const float AimDeadZoneSqr = 0.001f * 0.001f;
        private const int SteelBladeItemId = 34;
        private const int SteelBladeComboLength = 4;
        // 对应 Mixamo 分段 0–44、44–72、72–104、104–136 帧的挥砍落点。
        private static readonly float[] SteelBladeHitProgress = { 32f / 44f, 16f / 28f, 20f / 32f, 10f / 32f };
        /// <summary>连招仅属于当前玩家/地图/手持实例；-1 表示无连招，不写入存档。</summary>
        private int comboStage = -1;
        private float comboElapsedSeconds;
        private WeaponConfigDefinition comboWeapon;
        private Vector3 comboAimDirection;
        private Vector3 bufferedComboAimDirection;
        /// <summary>最多保留一次下一刀意图；同一刀内继续点击只更新方向。</summary>
        private bool comboBuffered;
        private bool comboHitSettled;
        private float cooldownRemainingSeconds;
        private int playerUnitId;
        private long playerMapInstanceId;
        private ICharacterMotorPort playerPort;
        private long reloadActionId;
        private int reloadWeaponItemId;
        private int reloadAmmoItemId;

        internal float CooldownRemainingSeconds => cooldownRemainingSeconds;

        protected override void OnInit()
        {
            this.RegisterEvent<PlayerReadyEvent>(OnPlayerReady);
            this.RegisterEvent<PlayerAttackRequestedEvent>(OnPlayerAttackRequested);
            this.RegisterEvent<PlayerReloadRequestedEvent>(_ => OnPlayerReloadRequested());
            this.RegisterEvent<PlayerActionEndedEvent>(OnPlayerActionEnded);
            this.RegisterEvent<WeaponSlotSwitchRequestedEvent>(OnWeaponSlotSwitchRequested);
            this.RegisterEvent<WeaponEquipmentChangedEvent>(_ => ClearCombo());
            this.RegisterEvent<MapLoadingStartedEvent>(_ => ClearCombo());
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => ClearCombo());
            this.RegisterEvent<CombatantDiedEvent>(evt => { if (evt.UnitId == playerUnitId) ClearCombo(); });
            LifeCycle.RegisterUpdate(OnUpdate);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate);
            ClearCombo();
            playerPort = null;
        }

        private void OnUpdate()
        {
            TickCooldown(Time.deltaTime);
        }

        internal void TickCooldown(float deltaTime)
        {
            if (comboStage >= 0 && !IsComboContextValid()) ClearCombo();
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || !CanAdvanceCooldown()) return;
            if (comboStage >= 0)
            {
                TickCombo(deltaTime);
                return;
            }
            if (cooldownRemainingSeconds <= 0f)
            {
                return;
            }

            cooldownRemainingSeconds = Mathf.Max(0f, cooldownRemainingSeconds - deltaTime);
        }

        private void OnPlayerReady(PlayerReadyEvent evt)
        {
            ClearCombo();
            playerUnitId = evt.UnitId;
            playerMapInstanceId = evt.MapInstanceId;
            TryRefreshPlayerPort();
        }

        private void OnWeaponSlotSwitchRequested(WeaponSlotSwitchRequestedEvent evt)
        {
            if (evt.Slot != WeaponSlot.None && evt.Slot != WeaponSlot.Melee && evt.Slot != WeaponSlot.Ranged)
            {
                return;
            }

            ClearCombo();

            if (this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.OnlinePlayersOnly)
            {
                if(evt.Slot==WeaponSlot.None)this.GetSystem<OnlineWorldInventorySystem>().HolsterAsync();
                else this.GetSystem<OnlineWorldInventorySystem>().SetActiveSlotAsync(evt.Slot);
                return;
            }

            this.GetSystem<WeaponEquipmentSystem>().TrySetActiveSlot(evt.Slot, out _);
        }

        private void OnPlayerAttackRequested(PlayerAttackRequestedEvent evt)
        {
            if(this.GetSystem<PlayerMoveSystem>()?.IsSwimming==true)return;
            this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.Attack);
            var aimDirection = evt.AimDirection;
            aimDirection.y = 0f;
            if (aimDirection.sqrMagnitude < AimDeadZoneSqr ||
                !TryGetActivePlayer(out var attacker, out var port))
            {
                return;
            }

            aimDirection.Normalize();
            if (comboStage >= 0)
            {
                if (IsComboContextValid())
                {
                    if (comboStage + 1 < SteelBladeComboLength)
                    {
                        comboBuffered = true;
                        bufferedComboAimDirection = aimDirection;
                    }
                    return;
                }
                ClearCombo();
            }
            if (cooldownRemainingSeconds > 0f) return;
            // 火把占用手持位置，不能穿透到空手攻击或采集。
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly && this.GetModel<WeaponEquipmentModel>().IsHoldingTorch) return;
            // 失败库存事务可能恢复同身份克隆，读取弹匣前重绑当前库存实例。
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly)
                this.GetSystem<WeaponEquipmentSystem>().ValidateSourceAfterInventoryMutation();
            var equipment = this.GetModel<WeaponEquipmentModel>();
            if(this.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OnlinePlayersOnly)
            {
                OnlineWorldSystem online=this.GetSystem<OnlineWorldSystem>();
                OnlineWorldPlayerSnapshot self=online.Snapshot.Players.FirstOrDefault(x=>x.UnitId==online.Snapshot.SelfUnitId);
                if(self.UnitId==0||self.IsDead)return;
                OnlineEquipmentSnapshot onlineEquipment=online.Snapshot.SelfInventory.Equipment;
                int onlineWeapon;
                if(onlineEquipment.ActiveSlot==(int)WeaponSlot.Ranged)
                {
                    onlineWeapon=onlineEquipment.RangedWeaponItemId;
                    if(onlineWeapon<=0||!this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(onlineWeapon,out var ranged,out _)||ranged.WeaponKind!=global::WeaponKind.Projectile)return;
                    if(onlineEquipment.RangedLoadedAmmo<=0)
                    {
                        this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.DryFire, onlineWeapon, port.Position));
                        string ammoName=ranged.AmmoItemId.ToString();
                        if(this.GetUtility<IRuntimeConfigProvider>().TryGetItem(ranged.AmmoItemId,out var ammo,out _)&&!string.IsNullOrWhiteSpace(ammo.NameKey))ammoName=Loc.T(ammo.NameKey);
                        Notify.ShowFloatTips(Texts.CombatAmmoInsufficient(ammoName), uniqueKey: "combat.ammo_insufficient");return;
                    }
                    if(ranged.ProjectileConfigId=="projectile.arrow.basic")
                    {
                        if(!port.TryGetProjectileOrigin(aimDirection,out var origin))return;
                        var mode=this.GetModel<PlayerCameraModeModel>().Mode;
                        if((mode==PlayerCameraMode.Shoulder||mode==PlayerCameraMode.FirstPerson)&&
                            (!evt.ProjectileAimRay.HasValue||!TryResolveProjectileAim(attacker,origin,evt.ProjectileAimRay.Value,ranged.ProjectileConfigId,out aimDirection)))return;
                        var root=this.GetModel<MapSceneModel>().CurrentHandle?.Context?.MapContentRoot?.transform;
                        if(root==null||!WorldProjectileCollisionCatalog.TryGet(online.Snapshot.MapId,out var index,out _))return;
                        if(!online.TryRequestWeaponAttack(onlineWeapon,root.InverseTransformVector(aimDirection).normalized,root.InverseTransformPoint(origin),index.ContentHash))return;
                    }
                    else if(!online.TryRequestWeaponAttack(onlineWeapon,aimDirection))return;
                    port.PlayProjectileFireFeedback();cooldownRemainingSeconds=ranged.AttackCooldownSeconds;return;
                }
                onlineWeapon=onlineEquipment.MeleeWeaponItemId;
                if (onlineEquipment.ActiveSlot == (int)WeaponSlot.Melee && onlineWeapon == SteelBladeItemId &&
                    this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(onlineWeapon, out var steel, out _) &&
                    IsValidMeleeWeapon(steel))
                {
                    BeginComboStage(port, aimDirection, steel, 0);
                    return;
                }
                if(!online.TryRequestWeaponAttack(onlineWeapon,aimDirection))return;
                if(onlineWeapon>0){port.PlayWeaponSwingFeedback();if(this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(onlineWeapon,out var sword,out _))cooldownRemainingSeconds=sword.AttackCooldownSeconds;}
                else{port.PlayAttackFeedback();if(this.GetUtility<IRuntimeConfigProvider>().TryGetPlayer("player.graybox.default",out var player,out _))cooldownRemainingSeconds=player.UnarmedAttackCooldownSeconds;else cooldownRemainingSeconds=UnarmedCombatRules.CooldownSeconds;}
                return;
            }
            var itemId = equipment.GetSlotItem(equipment.ActiveSlot);
            if (itemId <= 0)
            {
                if (equipment.ActiveSlot == WeaponSlot.Melee)
                {
                    AcceptUnarmedAttack(attacker, port, aimDirection);
                }

                return;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(itemId, out var weapon, out _))
            {
                return;
            }

            if (weapon.WeaponKind == global::WeaponKind.Melee)
            {
                if (weapon.AttackDamage <= 0 ||
                    weapon.AttackCooldownSeconds <= 0f ||
                    weapon.AttackRange <= 0f ||
                    weapon.MeleeArcDegrees <= 0f)
                {
                    return;
                }

                if (weapon.ItemId == SteelBladeItemId) BeginComboStage(port, aimDirection, weapon, 0);
                else AcceptMeleeAttack(attacker, port, aimDirection, weapon);
                return;
            }

            if (weapon.WeaponKind == global::WeaponKind.Projectile)
            {
                AcceptProjectileAttack(attacker, port, aimDirection, weapon, evt.ProjectileAimRay);
            }
        }

        /// <summary>消费共用换弹请求；满弹匣静默拒绝，缺少备用弹药时提示且不启动装填。</summary>
        private void OnPlayerReloadRequested()
        {
            if (this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.OnlinePlayersOnly)
            {
                this.GetSystem<OnlineWorldInventorySystem>().TryBeginRangedReload(out _);
                return;
            }

            var actions = this.GetSystem<PlayerActionSystem>();
            actions.CancelActive(PlayerActionCancelReason.Replaced);
            ClearReloadContext();

            // 火把占用手持位置，不能穿透到空手攻击或采集。
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly && this.GetModel<WeaponEquipmentModel>().IsHoldingTorch) return;
            // 失败库存事务可能恢复同身份克隆，读取弹匣前重绑当前库存实例。
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly)
                this.GetSystem<WeaponEquipmentSystem>().ValidateSourceAfterInventoryMutation();
            var equipment = this.GetModel<WeaponEquipmentModel>();
            if (equipment.ActiveSlot != WeaponSlot.Ranged || equipment.RangedWeaponItemId <= 0 ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(equipment.RangedWeaponItemId, out var weapon, out _) ||
                weapon.WeaponKind != global::WeaponKind.Projectile || weapon.MagazineCapacity <= 0 ||
                weapon.ReloadSeconds <= 0f || weapon.AmmoItemId <= 0 ||
                equipment.RangedLoadedAmmo >= weapon.MagazineCapacity)
            {
                return;
            }

            var iconAddress = string.Empty;
            var ammoName = weapon.AmmoItemId.ToString();
            if (this.GetUtility<IRuntimeConfigProvider>().TryGetItem(weapon.AmmoItemId, out var ammo, out _))
            {
                iconAddress = ammo.IconAddress;
                if (!string.IsNullOrWhiteSpace(ammo.NameKey)) ammoName = Loc.T(ammo.NameKey);
            }

            // 弹匣尚有空间但背包缺弹时复用空枪提示身份，避免连续按下堆叠提示。
            if (!HasReserveAmmo(weapon.AmmoItemId))
            {
                Notify.ShowFloatTips(Texts.CombatAmmoInsufficient(ammoName), uniqueKey: "combat.ammo_insufficient");
                return;
            }

            if (!actions.TryBegin(new PlayerActionRequest(PlayerActionKind.Reload, weapon.ReloadSeconds, iconAddress), out var actionId, out _))
            {
                return;
            }

            reloadActionId = actionId;
            reloadWeaponItemId = equipment.RangedWeaponItemId;
            reloadAmmoItemId = weapon.AmmoItemId;
            this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.ReloadStart, weapon.ItemId, playerPort.Position));
        }

        /// <summary>装填完成后重验装备并原子扣除背包与口袋弹药；取消或扣除失败不改变随身库存及弹匣。</summary>
        private void OnPlayerActionEnded(PlayerActionEndedEvent evt)
        {
            if (evt.Kind != PlayerActionKind.Reload || evt.ActionId != reloadActionId)
            {
                return;
            }

            var weaponItemId = reloadWeaponItemId;
            var ammoItemId = reloadAmmoItemId;
            ClearReloadContext();
            if (!evt.Completed)
            {
                return;
            }

            // 火把占用手持位置，不能穿透到空手攻击或采集。
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly && this.GetModel<WeaponEquipmentModel>().IsHoldingTorch) return;
            // 失败库存事务可能恢复同身份克隆，读取弹匣前重绑当前库存实例。
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly)
                this.GetSystem<WeaponEquipmentSystem>().ValidateSourceAfterInventoryMutation();
            var equipment = this.GetModel<WeaponEquipmentModel>();
            if (equipment.ActiveSlot != WeaponSlot.Ranged || equipment.RangedWeaponItemId != weaponItemId ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(weaponItemId, out var weapon, out _) ||
                weapon.WeaponKind != global::WeaponKind.Projectile || weapon.AmmoItemId != ammoItemId ||
                equipment.RangedLoadedAmmo >= weapon.MagazineCapacity ||
                !HasReserveAmmo(ammoItemId))
            {
                return;
            }

            var bag = this.GetModel<PlayerBagModel>().Container;
            var bagSnapshot = bag.CaptureSnapshot();
            var pocket = this.GetModel<PlayerBagModel>().Pocket;
            var pocketSnapshot = pocket.CaptureSnapshot();
            var equipmentSnapshot = equipment.CaptureSnapshot();
            var missing = weapon.MagazineCapacity - equipment.RangedLoadedAmmo;
            var inventory = this.GetSystem<InventorySystem>();
            var amount = System.Math.Min(weapon.ReloadWholeMagazine ? missing : 1, inventory.GetCarriedItemAmount(ammoItemId));
            // 弹匣和两种随身容器共同提交；装填取消时不会进入此扣除分支。
            var acquiredBefore=inventory.GetAcquiredAmount(ammoItemId);
            if (amount <= 0 || !inventory.TryRemoveCarriedItemAmount(ammoItemId, amount, out _) ||
                !equipment.TrySetRangedLoadedAmmo(equipment.RangedLoadedAmmo + amount, out _))
            {
                bag.RestoreSnapshot(bagSnapshot);
                pocket.RestoreSnapshot(pocketSnapshot);
                equipment.RestoreSnapshot(equipmentSnapshot);
                return;
            }

            var acquiredReloaded=acquiredBefore-inventory.GetAcquiredAmount(ammoItemId);
            if(acquiredReloaded>0&&equipment.HeldSource.HasValue)
            {
                var source=equipment.HeldSource.Value;
                var container=source.ContainerKind==InventoryContainerKind.Pocket?pocket:bag;
                if(container.TryGet(source.SlotIndex,out var held))
                {held.OriginExpeditionId=this.GetModel<ExpeditionModel>().CurrentExpeditionId;held.LoadedAcquiredAmount+=acquiredReloaded;}
            }
            this.GetSystem<WeaponEquipmentSystem>().CommitExternalMutation("weapon reloaded");
            if (playerPort != null)
                this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.ReloadFinish, weaponItemId, playerPort.Position));
        }

        private void ClearReloadContext()
        {
            if (reloadActionId != 0)
                this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.ReloadCancel, reloadWeaponItemId, Vector3.zero));
            reloadActionId = 0;
            reloadWeaponItemId = 0;
            reloadAmmoItemId = 0;
        }

        private void AcceptUnarmedAttack(
            UnitSnapshot attacker,
            ICharacterMotorPort port,
            Vector3 aimDirection)
        {
            port.PlayAttackFeedback();
            this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.Swing, 0, port.Position));

            PlayerConfigDefinition rules=default;bool hasRules=this.GetUtility<IRuntimeConfigProvider>().TryGetPlayer("player.graybox.default",out rules,out _);int damage=hasRules?rules.UnarmedAttackDamage:UnarmedCombatRules.Damage;
            if (TryResolveUnarmedTarget(attacker, port.Position, aimDirection, out var targetUnitId))
            {
                if (this.GetSystem<UnitSystem>().TryGet(targetUnitId, out var hitTarget) &&
                    this.GetSystem<CombatSystem>().TryDamage(attacker.UnitId, targetUnitId, damage))
                    this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.PunchBody, 0, hitTarget.Position));
            }

            cooldownRemainingSeconds = hasRules?rules.UnarmedAttackCooldownSeconds:UnarmedCombatRules.CooldownSeconds;
        }

        private bool TryResolveUnarmedTarget(
            UnitSnapshot attacker,
            Vector3 origin,
            Vector3 aimDirection,
            out int targetUnitId)
        {
            targetUnitId = 0;
            if (!this.GetModel<CombatModel>().TryGetCombatant(attacker.UnitId, out var attackerCombat))
            {
                return false;
            }

            bool hasRules=this.GetUtility<IRuntimeConfigProvider>().TryGetPlayer("player.graybox.default",out PlayerConfigDefinition rules,out _);float range=hasRules?rules.UnarmedAttackRange:UnarmedCombatRules.Range;float arc=hasRules?rules.UnarmedMeleeArcDegrees:UnarmedCombatRules.ArcDegrees;var rangeSqr = range * range;
            var halfArc = arc * 0.5f;
            var bestDistanceSqr = float.PositiveInfinity;
            var candidates = new List<CombatantSnapshot>(this.GetModel<CombatModel>().Combatants.Values);
            foreach (var targetState in candidates)
            {
                if (targetState.IsDead ||
                    targetState.Faction == attackerCombat.Faction ||
                    !this.GetSystem<UnitSystem>().TryGet(targetState.UnitId, out var targetUnit) ||
                    targetUnit.MapInstanceId != attacker.MapInstanceId ||
                    !targetUnit.IsActive)
                {
                    continue;
                }

                var offset = targetUnit.Position - origin;
                offset.y = 0f;
                var distanceSqr = offset.sqrMagnitude;
                if (distanceSqr <= 0f ||
                    distanceSqr > rangeSqr ||
                    Vector3.Angle(aimDirection, offset.normalized) > halfArc)
                {
                    continue;
                }

                if (distanceSqr < bestDistanceSqr &&
                    !Mathf.Approximately(distanceSqr, bestDistanceSqr) ||
                    Mathf.Approximately(distanceSqr, bestDistanceSqr) &&
                    (targetUnitId == 0 || targetState.UnitId < targetUnitId))
                {
                    bestDistanceSqr = distanceSqr;
                    targetUnitId = targetState.UnitId;
                }
            }

            return targetUnitId > 0;
        }

        private void AcceptMeleeAttack(
            UnitSnapshot attacker,
            ICharacterMotorPort port,
            Vector3 aimDirection,
            WeaponConfigDefinition weapon)
        {
            port.PlayWeaponSwingFeedback();
            this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.Swing, weapon.ItemId, port.Position));

            ApplyMeleeHit(attacker, port, aimDirection, weapon);
            cooldownRemainingSeconds = weapon.AttackCooldownSeconds;
        }

        /// <summary>一次挥砍的唯一离线结算入口；连招在落点调用，其他武器保持原调用时机。</summary>
        private void ApplyMeleeHit(UnitSnapshot attacker, ICharacterMotorPort port,
            Vector3 aimDirection, WeaponConfigDefinition weapon)
        {
            // 每次已接受的近战挥击只提交一次采集意图；资源规则与奖励仍由文明采集系统裁决。
            this.GetSystem<CivilisationSearchSystem>()?.TryApplyMeleeHarvest(attacker.MapInstanceId, port.Position, aimDirection, weapon.ItemId);

            var rangeSqr = weapon.AttackRange * weapon.AttackRange;
            var halfArc = weapon.MeleeArcDegrees * 0.5f;
            var damaged = new HashSet<int>();
            var candidates = new List<CombatantSnapshot>(this.GetModel<CombatModel>().Combatants.Values);
            foreach (var targetState in candidates)
            {
                if (targetState.IsDead ||
                    targetState.Faction == CombatFaction.Player ||
                    !damaged.Add(targetState.UnitId) ||
                    !this.GetSystem<UnitSystem>().TryGet(targetState.UnitId, out var targetUnit) ||
                    targetUnit.MapInstanceId != attacker.MapInstanceId ||
                    !targetUnit.IsActive)
                {
                    continue;
                }

                var offset = targetUnit.Position - port.Position;
                offset.y = 0f;
                if (offset.sqrMagnitude <= 0f ||
                    offset.sqrMagnitude > rangeSqr ||
                    Vector3.Angle(aimDirection, offset.normalized) > halfArc)
                {
                    continue;
                }

                if (this.GetSystem<CombatSystem>().TryDamage(attacker.UnitId, targetState.UnitId, weapon.AttackDamage))
                    this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.MeleeBody, weapon.ItemId, targetUnit.Position));
            }
        }

        private static bool IsValidMeleeWeapon(WeaponConfigDefinition weapon) =>
            weapon.WeaponKind == global::WeaponKind.Melee && weapon.AttackDamage > 0 &&
            weapon.AttackCooldownSeconds > 0f && !float.IsInfinity(weapon.AttackCooldownSeconds) &&
            weapon.AttackRange > 0f && weapon.MeleeArcDegrees > 0f;

        /// <summary>开启一刀并消费缓存；时长取现有武器冷却，表现端只执行段号与时长。</summary>
        private void BeginComboStage(ICharacterMotorPort port, Vector3 aim, WeaponConfigDefinition weapon, int stage)
        {
            // 联机仍在接受起手时交服务器裁决；不同刀的落点不能缩短服务器的攻击请求间隔。
            if (this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.OnlinePlayersOnly &&
                !this.GetSystem<OnlineWorldSystem>().TryRequestWeaponAttack(weapon.ItemId, aim))
            { ClearCombo(); return; }
            comboStage = stage;
            comboWeapon = weapon;
            comboAimDirection = aim;
            comboElapsedSeconds = 0f;
            comboBuffered = false;
            comboHitSettled = false;
            cooldownRemainingSeconds = weapon.AttackCooldownSeconds;
            port.PlayMeleeComboStage(stage, weapon.AttackCooldownSeconds);
        }

        /// <summary>沿共享生命周期推进；跨落点或段末的大步长仍按顺序最多结算一次每刀。</summary>
        private void TickCombo(float deltaTime)
        {
            var remaining = deltaTime;
            while (comboStage >= 0)
            {
                if (!IsComboContextValid() || !TryGetActivePlayer(out var attacker, out var port))
                { ClearCombo(); break; }
                var duration = comboWeapon.AttackCooldownSeconds;
                var step = Mathf.Min(remaining, Mathf.Max(0f, duration - comboElapsedSeconds));
                comboElapsedSeconds += step;
                cooldownRemainingSeconds = Mathf.Max(0f, duration - comboElapsedSeconds);
                remaining -= step;
                if (!comboHitSettled && comboElapsedSeconds >= duration * SteelBladeHitProgress[comboStage])
                {
                    comboHitSettled = true; // 先标记再发布事件，防止同步回调重复结算。
                    this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.Swing, comboWeapon.ItemId, port.Position));
                    if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly)
                        ApplyMeleeHit(attacker, port, comboAimDirection, comboWeapon);
                }
                if (comboStage < 0 || !IsComboContextValid()) { ClearCombo(); break; }
                if (comboElapsedSeconds + 0.00001f < duration) break;
                if (comboBuffered && comboStage + 1 < SteelBladeComboLength)
                    BeginComboStage(port, bufferedComboAimDirection, comboWeapon, comboStage + 1);
                else ClearCombo();
                if (remaining <= 0f) break;
            }
            if (comboStage < 0 && remaining > 0f)
                cooldownRemainingSeconds = Mathf.Max(0f, cooldownRemainingSeconds - remaining);
        }

        /// <summary>失活、死亡、地图切换、收起或替换手持后，旧意图不能继续攻击。</summary>
        private bool IsComboContextValid()
        {
            if(this.GetSystem<PlayerMoveSystem>()?.IsSwimming==true)return false;
            if (!TryGetActivePlayer(out _, out _)) return false;
            if (this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.OnlinePlayersOnly)
            {
                var held = this.GetModel<OnlineWorldModel>().Snapshot.Value.SelfInventory.Equipment;
                return held.ActiveSlot == (int)WeaponSlot.Melee && held.MeleeWeaponItemId == SteelBladeItemId;
            }
            this.GetSystem<WeaponEquipmentSystem>().ValidateSourceAfterInventoryMutation();
            return comboStage >= 0 && this.GetModel<WeaponEquipmentModel>().HeldItemId == SteelBladeItemId;
        }

        /// <summary>幂等清空连招；保留剩余全局冷却，换手不能绕过冷却。</summary>
        private void ClearCombo()
        {
            if (comboStage < 0) return;
            comboStage = -1;
            comboBuffered = false;
            comboHitSettled = false;
            comboElapsedSeconds = 0f;
            playerPort?.StopMeleeCombo();
        }

        private void AcceptProjectileAttack(
            UnitSnapshot attacker,
            ICharacterMotorPort port,
            Vector3 aimDirection,
            WeaponConfigDefinition weapon,
            Ray? projectileAimRay)
        {
            if (weapon.AttackDamage <= 0 ||
                weapon.AttackCooldownSeconds <= 0f ||
                string.IsNullOrWhiteSpace(weapon.ProjectileConfigId) ||
                weapon.AmmoItemId <= 0 || weapon.MagazineCapacity <= 0 ||
                !port.TryGetProjectileOrigin(aimDirection, out var origin))
            {
                return;
            }

            if (projectileAimRay.HasValue &&
                (this.GetModel<PlayerCameraModeModel>().Mode == PlayerCameraMode.Shoulder ||
                 this.GetModel<PlayerCameraModeModel>().Mode == PlayerCameraMode.FirstPerson) &&
                !TryResolveProjectileAim(attacker, origin, projectileAimRay.Value, weapon.ProjectileConfigId, out aimDirection))
                return;

            // 火把占用手持位置，不能穿透到空手攻击或采集。
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly && this.GetModel<WeaponEquipmentModel>().IsHoldingTorch) return;
            // 失败库存事务可能恢复同身份克隆，读取弹匣前重绑当前库存实例。
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly)
                this.GetSystem<WeaponEquipmentSystem>().ValidateSourceAfterInventoryMutation();
            var equipment = this.GetModel<WeaponEquipmentModel>();
            if (equipment.RangedLoadedAmmo <= 0)
            {
                this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.DryFire, weapon.ItemId, port.Position));
                var ammoName = weapon.AmmoItemId.ToString();
                if (this.GetUtility<IRuntimeConfigProvider>().TryGetItem(weapon.AmmoItemId, out var ammo, out _) &&
                    !string.IsNullOrWhiteSpace(ammo.NameKey))
                {
                    ammoName = Loc.T(ammo.NameKey);
                }

                Notify.ShowFloatTips(Texts.CombatAmmoInsufficient(ammoName), uniqueKey: "combat.ammo_insufficient");
                return;
            }

            var snapshot = equipment.CaptureSnapshot();
            if (!equipment.TrySetRangedLoadedAmmo(equipment.RangedLoadedAmmo - 1, out _))
            {
                equipment.RestoreSnapshot(snapshot);
                return;
            }

            var request = new ProjectileSpawnRequest(
                attacker.UnitId,
                CombatFaction.Player,
                attacker.MapInstanceId,
                weapon.AttackDamage,
                origin,
                aimDirection,
                weapon.ProjectileConfigId);
            if (!this.GetSystem<ProjectileSystem>().TrySpawn(request, out _, out _))
            {
                equipment.RestoreSnapshot(snapshot);
                return;
            }

            this.GetSystem<WeaponEquipmentSystem>().CommitExternalMutation("arrow fired");
            cooldownRemainingSeconds = weapon.AttackCooldownSeconds;
            port.PlayProjectileFireFeedback();
        }

        /// <summary>在消费弹药前将准星射线转换为发射点方向；忽略自身与非战斗 Trigger，非法或反向目标拒绝射击。</summary>
        private bool TryResolveProjectileAim(UnitSnapshot attacker, Vector3 origin, Ray aimRay,
            string projectileConfigId, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!IsFinite(origin) || !IsFinite(aimRay.origin) || !IsFinite(aimRay.direction) ||
                aimRay.direction.sqrMagnitude < AimDeadZoneSqr ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetProjectile(projectileConfigId, out var projectile, out _) ||
                projectile.MaxTravelDistance <= 0f || float.IsNaN(projectile.MaxTravelDistance) || float.IsInfinity(projectile.MaxTravelDistance))
                return false;

            aimRay = new Ray(aimRay.origin, aimRay.direction.normalized);
            // 镜头通常在发射点后方，补上这段距离，使无命中目标落在发射点前方的有效射程外沿。
            var distance = projectile.MaxTravelDistance + Vector3.Distance(aimRay.origin, origin);
            var target = aimRay.GetPoint(distance);
            var provider = this.GetUtility<IPlayerRuntimePortProvider>();
            var enemies = this.GetUtility<IEnemyColliderResolver>();
            foreach (var hit in Physics.RaycastAll(aimRay, distance, Physics.AllLayers, QueryTriggerInteraction.Collide))
            {
                if (hit.collider == null || provider.IsPlayerCollider(attacker.UnitId, attacker.MapInstanceId, hit.collider) ||
                    (hit.collider.isTrigger && !enemies.TryResolveEnemyUnitId(hit.collider, out _) && !IsOnlineEnemyBody(hit.collider)) || hit.distance >= distance)
                    continue;
                distance = hit.distance;
                target = hit.point;
            }
            var offset = target - origin;
            if (!IsFinite(offset) || offset.sqrMagnitude < AimDeadZoneSqr || Vector3.Dot(offset, aimRay.direction) <= 0f)
                return false;
            direction = offset.normalized;
            return IsFinite(direction);
        }

        // 联机敌人的本体 Trigger 参与准星选点，尸体搜索圈仍被排除；这里只选择目标，不结算伤害。
        private bool IsOnlineEnemyBody(Collider collider)
        {
            if(this.GetModel<MapSceneModel>().CurrentMode!=MapPreparationMode.OnlinePlayersOnly)return false;
            var view=collider.GetComponent<EnemyView>();
            return view!=null&&collider==view.GetComponent<Collider>();
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        /// <summary>开始和完成装填时读取两种随身容器；仓库弹药不作为现场储备。</summary>
        private bool HasReserveAmmo(int ammoItemId) =>
            this.GetSystem<InventorySystem>().GetCarriedItemAmount(ammoItemId) > 0;

        /// <summary>每次攻击或换弹前校验当前地图绑定、活动端口及对应模式的存活状态；失败不发送请求或播放动作。</summary>
        private bool TryGetActivePlayer(out UnitSnapshot player, out ICharacterMotorPort port)
        {
            port = null;
            if (playerUnitId <= 0 ||
                playerMapInstanceId <= 0 ||
                this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing ||
                this.GetModel<ExpeditionModel>().Status == ExpeditionStatus.Defeated ||
                !IsCurrentPlayerAlive() ||
                !this.GetSystem<UnitSystem>().TryGet(playerUnitId, out player) ||
                !player.IsActive ||
                player.MapInstanceId != playerMapInstanceId ||
                player.MapInstanceId <= 0 ||
                !TryRefreshPlayerPort())
            {
                player = default;
                return false;
            }

            port = playerPort;
            return port?.IsValid == true &&
                   port.UnitId == player.UnitId &&
                   port.MapInstanceId == player.MapInstanceId;
        }

        /// <summary>联机只信任当前 World 的本机角色快照；缺失、断线、死亡或零生命均拒绝，离线继续读取 CombatModel。</summary>
        private bool IsCurrentPlayerAlive()
        {
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly)
                return this.GetModel<CombatModel>().PlayerHP.Value > 0;

            var world = this.GetModel<OnlineWorldModel>().Snapshot.Value;
            if (!world.HasWorld || world.ConnectionLost) return false;
            foreach (var player in world.Players)
            {
                if (player.UnitId == world.SelfUnitId)
                    return !player.IsDead && player.CurrentHP > 0;
            }
            return false;
        }

        private bool TryRefreshPlayerPort()
        {
            if (playerUnitId <= 0 || playerMapInstanceId <= 0)
            {
                playerPort = null;
                return false;
            }

            if (!this.GetUtility<IPlayerRuntimePortProvider>().TryGetMotorPort(
                    playerUnitId,
                    playerMapInstanceId,
                    out playerPort))
            {
                playerPort = null;
                return false;
            }

            return playerPort?.IsValid == true;
        }

        private bool CanAdvanceCooldown()
        {
            return this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing &&
                   this.GetModel<ExpeditionModel>().Status != ExpeditionStatus.Defeated;
        }
    }
}
