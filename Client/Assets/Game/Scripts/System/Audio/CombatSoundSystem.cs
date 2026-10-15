using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>已经接受的动作或命中结果所对应的声音表现类别；不表示伤害或弹药请求。</summary>
    public enum CombatSoundCueKind { Swing, PunchBody, MeleeBody, BulletBody, BulletMetal, BulletIce,
        ReloadStart, ReloadFinish, ReloadCancel, DryFire, Handling }

    /// <summary>同步动作结果的声音通知，不携带可变 View；Sequence 为 0 时是本机离线结果，否则属于权威战斗序列。</summary>
    public readonly struct CombatSoundCueEvent
    {
        public CombatSoundCueEvent(CombatSoundCueKind kind, int weaponItemId, Vector3 position, long sequence = 0)
        { Kind = kind; WeaponItemId = weaponItemId; Position = position; Sequence = sequence; }
        public CombatSoundCueKind Kind { get; }
        public int WeaponItemId { get; }
        public Vector3 Position { get; }
        public long Sequence { get; }
    }

    /// <summary>只管理战斗声音选择、去重与句柄清理；伤害、动作接受、弹药和换弹成功仍由原系统裁决。</summary>
    public sealed class CombatSoundSystem : AbstractSystem
    {
        private readonly Queue<AudioPlaybackHandle> impacts = new Queue<AudioPlaybackHandle>();
        private readonly HashSet<(CombatSoundCueKind, Vector3)> sequenceCues = new HashSet<(CombatSoundCueKind, Vector3)>();
        /// <summary>当前地图的最大权威序列；切图清零，旧序列与同序列同点同类通知不能重复发声。</summary>
        private long sequence, worldSequence;
        /// <summary>本机换弹操作唯一拥有的加载／播放句柄；取消和完成新阶段前停止。</summary>
        private AudioPlaybackHandle reload;
        private float lastDryTime = float.NegativeInfinity;
        private int lastSwing = -1, lastPunch = -1, lastBody = -1, lastMetal = -1;
        private int equippedWeapon = -1;
        protected override void OnInit()
        {
            this.RegisterEvent<CombatSoundCueEvent>(OnCue);
            this.RegisterEvent<OnlineWorldCombatPresentationEvent>(OnOnline);
            this.RegisterEvent<WeaponEquipmentChangedEvent>(_ => OnEquipment());
            this.RegisterEvent<OnlineWorldInventoryChangedEvent>(_ => OnEquipment());
            this.RegisterEvent<AppStateChangedEvent>(e => { if (e.Current != AppState.Playing) StopSounds(); });
            this.RegisterEvent<MapLoadingStartedEvent>(_ => ResetMap());
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => ResetMap());
        }
        protected override void OnDeinit() => ResetMap();
        private AudioSystem Audio => this.GetSystem<AudioSystem>();
        /// <summary>只映射正式目录中已经存在的枪械身份；弓和发射器不借用手枪弹匣／空击声音。</summary>
        private static bool IsGun(int id) => id == 19 || id == 20 || id == 21 || id == 24;
        private static string Variant(string prefix, ref int previous)
        { previous = previous < 0 ? Random.Range(0, 2) : 1 - previous; return prefix + (previous + 1).ToString("00"); }
        private void OnCue(CombatSoundCueEvent e)
        {
            if (e.Kind == CombatSoundCueKind.ReloadCancel) { Audio.Stop(reload); reload = AudioPlaybackHandle.Empty; return; }
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing) return;
            if (e.Sequence > 0)
            {
                if (e.Sequence < sequence) return;
                if (e.Sequence > sequence) { sequence = e.Sequence; sequenceCues.Clear(); }
                if (!sequenceCues.Add((e.Kind, e.Position))) return;
            }
            string name;
            switch (e.Kind)
            {
                case CombatSoundCueKind.Swing: name = Variant("CombatSwing", ref lastSwing); break;
                case CombatSoundCueKind.PunchBody:
                case CombatSoundCueKind.MeleeBody: name = Variant("CombatPunchHit", ref lastPunch); break;
                case CombatSoundCueKind.BulletBody: name = Variant("CombatBulletBody", ref lastBody); break;
                case CombatSoundCueKind.BulletMetal: name = Variant("CombatBulletMetal", ref lastMetal); break;
                case CombatSoundCueKind.BulletIce: name = "CombatBulletIce"; break;
                case CombatSoundCueKind.ReloadStart:
                case CombatSoundCueKind.ReloadFinish:
                    if (!IsGun(e.WeaponItemId)) return;
                    name = e.WeaponItemId == 24
                        ? e.Kind == CombatSoundCueKind.ReloadStart ? "WeaponShotgunLoad" : "WeaponShotgunCock"
                        : e.Kind == CombatSoundCueKind.ReloadStart ? "WeaponMagazineOut" : "WeaponMagazineIn";
                    Audio.Stop(reload);
                    reload = Audio.PlaySoundAt(name, e.Position, 36f, 10, new SoundPlaybackOptions(0.4f, 16f, 0.25f));
                    return;
                case CombatSoundCueKind.DryFire:
                    if (!IsGun(e.WeaponItemId) || Time.realtimeSinceStartup - lastDryTime < 0.3f) return;
                    lastDryTime = Time.realtimeSinceStartup; name = "WeaponDryFire"; break;
                case CombatSoundCueKind.Handling:
                    if (!IsGun(e.WeaponItemId)) return;
                    name = "WeaponHandling"; break;
                default: return;
            }
            while (impacts.Count >= 12) Audio.Stop(impacts.Dequeue());
            var handle = Audio.PlaySoundAt(name, e.Position, 36f, 10, new SoundPlaybackOptions(0.35f, 16f, 0.2f));
            if (handle.IsValid) impacts.Enqueue(handle);
        }
        /// <summary>从已应用的服务器结果发声，不从 HP 快照变化猜攻击；一条事件重复到达没有副作用。</summary>
        private void OnOnline(OnlineWorldCombatPresentationEvent e)
        {
            if (e.CombatSequence <= worldSequence) return;
            worldSequence = e.CombatSequence;
            var world = this.GetModel<OnlineWorldModel>().Snapshot.Value;
            if (world == null || !world.HasWorld || world.ConnectionLost) return;
            var root = this.GetModel<MapSceneModel>().CurrentHandle?.Context?.DynamicObjectRoot;
            if (root == null) return;
            if (e.ActionKind == OnlineWorldCombatActionKind.PlayerMelee || e.ActionKind == OnlineWorldCombatActionKind.EnemyMelee)
            {
                var actor = world.Players.FirstOrDefault(p => p.UnitId == e.ActorUnitId);
                if (actor.UnitId != 0) OnCue(new CombatSoundCueEvent(CombatSoundCueKind.Swing, e.WeaponItemId, root.TransformPoint(actor.LocalPosition), e.CombatSequence));
                else if (e.ActionKind == OnlineWorldCombatActionKind.EnemyMelee)
                {
                    var enemy = e.TargetEnemies?.FirstOrDefault(p => p.UnitId == e.ActorUnitId) ?? default;
                    if (enemy.UnitId == 0) enemy = world.Enemies.FirstOrDefault(p => p.UnitId == e.ActorUnitId);
                    if (enemy.UnitId != 0) OnCue(new CombatSoundCueEvent(CombatSoundCueKind.Swing, 0, root.TransformPoint(enemy.LocalPosition), e.CombatSequence));
                }
            }
            foreach (var id in (e.TargetUnitIds ?? System.Array.Empty<long>()).Distinct())
            {
                var target = e.TargetEnemies?.FirstOrDefault(p => p.UnitId == id) ?? default;
                if (target.UnitId == 0) target = world.Enemies.FirstOrDefault(p => p.UnitId == id);
                if (target.UnitId == 0) continue;
                var kind = e.ActionKind == OnlineWorldCombatActionKind.PlayerMelee
                    ? e.WeaponItemId == 0 ? CombatSoundCueKind.PunchBody : CombatSoundCueKind.MeleeBody
                    : CombatSoundCueKind.BulletBody;
                if (e.ActionKind != OnlineWorldCombatActionKind.PlayerMelee &&
                    (e.ActionKind != OnlineWorldCombatActionKind.PlayerProjectile || !IsGun(e.WeaponItemId))) continue;
                OnCue(new CombatSoundCueEvent(kind, e.WeaponItemId, root.TransformPoint(target.LocalPosition), e.CombatSequence));
            }
            if (e.ActionKind == OnlineWorldCombatActionKind.EnemyMelee && e.TargetPlayers != null)
                foreach (var target in e.TargetPlayers)
                    OnCue(new CombatSoundCueEvent(CombatSoundCueKind.PunchBody, 0, root.TransformPoint(target.LocalPosition), e.CombatSequence));
        }
        private void OnEquipment()
        {
            var world = this.GetModel<OnlineWorldModel>().Snapshot.Value;
            int weapon; Vector3 position;
            if (world != null && world.HasWorld)
            {
                var equipment = world.SelfInventory.Equipment;
                weapon = equipment.ActiveSlot == (int)WeaponSlot.Ranged ? equipment.RangedWeaponItemId : equipment.MeleeWeaponItemId;
                var root = this.GetModel<MapSceneModel>().CurrentHandle?.Context?.DynamicObjectRoot;
                if (root == null) return;
                position = root.TransformPoint(world.Players.FirstOrDefault(p => p.UnitId == world.SelfUnitId).LocalPosition);
            }
            else
            {
                var equipment = this.GetModel<WeaponEquipmentModel>(); weapon = equipment.GetSlotItem(equipment.ActiveSlot);
                if (!this.GetModel<UnitModel>().TryGet(PlayerModel.LocalPlayerUnitId, out var player)) return;
                position = player.Position;
            }
            if (weapon == equippedWeapon) return;
            equippedWeapon = weapon;
            OnCue(new CombatSoundCueEvent(CombatSoundCueKind.Handling, weapon, position));
        }
        private void StopSounds()
        { Audio.Stop(reload); reload = AudioPlaybackHandle.Empty; while (impacts.Count > 0) Audio.Stop(impacts.Dequeue()); }
        private void ResetMap()
        { StopSounds(); sequence = worldSequence = 0; sequenceCues.Clear(); equippedWeapon = -1; lastDryTime = float.NegativeInfinity; }
    }
}
