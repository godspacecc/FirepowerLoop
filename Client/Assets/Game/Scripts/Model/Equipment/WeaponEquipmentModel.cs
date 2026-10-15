using System;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    // 仅表示攻击类型及旧档槽位身份；不再代表两个储存位置。
    public enum WeaponSlot { None = 0, Melee = 1, Ranged = 2 }

    /// <summary>单一手持快照；来源为空代表空手，Revision 仅在当前运行期识别过期操作。</summary>
    public readonly struct HeldItemSnapshot
    {
        public HeldItemSnapshot(InventorySlotRef? source, int itemId, long revision) { Source=source; ItemId=itemId; Revision=revision; }
        public InventorySlotRef? Source { get; }
        public int ItemId { get; }
        public long Revision { get; }
    }
    /// <summary>既有战斗消费者的只读类型投影；至多一个非零武器，不持有备用武器。</summary>
    public readonly struct WeaponEquipmentSnapshot
    {
        public WeaponEquipmentSnapshot(int meleeWeaponItemId,int rangedWeaponItemId,WeaponSlot activeSlot,int rangedLoadedAmmo)
        { MeleeWeaponItemId=activeSlot==WeaponSlot.Melee?meleeWeaponItemId:0; RangedWeaponItemId=activeSlot==WeaponSlot.Ranged?rangedWeaponItemId:0; ActiveSlot=activeSlot; RangedLoadedAmmo=rangedLoadedAmmo; Source=null; HeldItemId=activeSlot==WeaponSlot.Ranged?rangedWeaponItemId:meleeWeaponItemId; TorchLit=false; }
        internal WeaponEquipmentSnapshot(HeldItemSnapshot held, WeaponSlot kind,int loaded, bool torchLit = false) : this(kind==WeaponSlot.Melee?held.ItemId:0,kind==WeaponSlot.Ranged?held.ItemId:0,kind,loaded) { Source=held.Source; HeldItemId=held.ItemId; TorchLit=torchLit; }
        public int HeldItemId { get; }
        public bool TorchLit { get; }
        public int MeleeWeaponItemId { get; }
        public int RangedWeaponItemId { get; }
        public WeaponSlot ActiveSlot { get; }
        public int RangedLoadedAmmo { get; }
        public InventorySlotRef? Source { get; }
    }
    /// <summary>只保存随身库存内当前手持引用；库存数量归 PlayerBagModel，拿出规则归 WeaponEquipmentSystem。</summary>
    public sealed class WeaponEquipmentModel : AbstractModel, IScopedSaveSectionProvider
    {
        public SaveSectionScope Scope => SaveSectionScope.SharedPlayer;
        public const string Section="another-day.weapon-equipment";
        public const string MeleeWeaponItemIdKey="meleeWeaponItemId", RangedWeaponItemIdKey="rangedWeaponItemId", ActiveSlotKey="activeSlot", RangedLoadedAmmoKey="rangedLoadedAmmo";
        public string SectionId => Section;
        public InventorySlotRef? HeldSource { get; private set; }
        public int HeldItemId { get; private set; }
        public long Revision { get; private set; }
        // 只在运行期绑定库存实例；克隆事务保留此身份，同类替换不得继承旧手持。
        internal Guid HeldIdentity { get; private set; }
        private ItemStack heldStack;
        public bool TorchLit { get; internal set; }
        public bool IsHoldingTorch => HeldItemId > 0 && this.GetUtility<IRuntimeConfigProvider>().TryGetItem(HeldItemId, out var item, out _) && item.ActionKind == global::ItemActionKind.ToggleTorch;
        public WeaponSlot ActiveSlot => IsHoldingTorch ? WeaponSlot.None : HeldItemId>0 && this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(HeldItemId,out var weapon,out _) && weapon.WeaponKind==global::WeaponKind.Projectile ? WeaponSlot.Ranged : WeaponSlot.Melee;
        public int MeleeWeaponItemId => ActiveSlot==WeaponSlot.Melee?HeldItemId:0;
        public int RangedWeaponItemId => ActiveSlot==WeaponSlot.Ranged?HeldItemId:0;
        public int RangedLoadedAmmo => ActiveSlot==WeaponSlot.Ranged?heldStack?.LoadedAmmo??0:0;
        protected override void OnInit() { }
        public bool Validate(JToken data,out string error)
        {
            if(data is JObject obj && obj.Property("schemaVersion")!=null)
            {
                if (!TryReadHeld(obj,out _,out var id,out error) || id!=0 && !ValidateHeldWeapon(id,this.GetUtility<IRuntimeConfigProvider>(),out error)) return false;
                var lit=obj["torchLit"];
                if (lit != null && (lit.Type != JTokenType.Boolean || lit.Value<bool>() && (id<=0 || !this.GetUtility<IRuntimeConfigProvider>().TryGetItem(id,out var item,out _) || item.ActionKind != global::ItemActionKind.ToggleTorch))) { error="火把点燃状态无效"; return false; }
                return true;
            }
            return ValidateLegacy(data,this.GetUtility<IRuntimeConfigProvider>(),out error);
        }
        internal static bool ValidateLegacy(JToken data,IRuntimeConfigProvider config,out string error)
        { return TryReadState(data,out var state,out error)&&ValidateState(state.MeleeWeaponItemId,state.RangedWeaponItemId,state.ActiveSlot,state.RangedLoadedAmmo,config,out error); }
        public JToken Capture() => new JObject { ["schemaVersion"]=1,["sourceContainerKind"]=HeldSource.HasValue?(int)HeldSource.Value.ContainerKind:-1,["sourceSlotIndex"]=HeldSource?.SlotIndex??-1,["itemId"]=HeldItemId, ["torchLit"]=TorchLit };
        /// <summary>载入已校验候选；旧双槽须与库存一起迁移，禁止留下隐藏储存位置。</summary>
        public void Restore(JToken data)
        {
            if(!TryReadHeld(data,out var source,out var id,out var error)) throw new InvalidOperationException(error);
            SetHeld(source,id);
            TorchLit = data["torchLit"]?.Value<bool>() ?? false;
        }
        internal static bool TryReadHeld(JToken data,out InventorySlotRef? source,out int id,out string error)
        {
            source=null;id=0;error="手持分区格式无效";
            if(data is not JObject obj || obj["schemaVersion"]?.Type!=JTokenType.Integer || obj["schemaVersion"].Value<int>()!=1 || obj["sourceContainerKind"]?.Type!=JTokenType.Integer || obj["sourceSlotIndex"]?.Type!=JTokenType.Integer || obj["itemId"]?.Type!=JTokenType.Integer) return false;
            int kind=obj["sourceContainerKind"].Value<int>(),index=obj["sourceSlotIndex"].Value<int>(); id=obj["itemId"].Value<int>();
            if(id==0) { if(kind!=-1||index!=-1)return false;error=null;return true; }
            if(id<=0||index<0||!InventorySystem.IsCarriedContainer((InventoryContainerKind)kind))return false;
            source=new InventorySlotRef((InventoryContainerKind)kind,index);error=null;return true;
        }
        /// <summary>按武器配置或火把动作配置验证拿出资格；不从物品 ID 或界面标签推断能力。</summary>
        internal static bool ValidateHeldWeapon(int itemId, IRuntimeConfigProvider config, out string error)
        {
            error=null;
            if (config != null && config.TryGetItem(itemId, out var tool, out _) && tool.ItemType == global::ItemType.Tool && tool.ActionKind == global::ItemActionKind.ToggleTorch && tool.BurnSeconds > 0f) return true;
            if(config==null || !config.TryGetItem(itemId,out var item,out error) || item.ItemType!=global::ItemType.Weapon || !config.TryGetWeapon(itemId,out var weapon,out error) || (weapon.WeaponKind!=global::WeaponKind.Melee && weapon.WeaponKind!=global::WeaponKind.Projectile))
            { error??="物品没有可用的手持武器配置";return false; }
            return true;
        }
        public void Reset() => SetHeld(null,0);
        internal void SetHeld(InventorySlotRef? source,int itemId, ItemStack stack = null)
        { TorchLit=false; HeldSource=source;HeldItemId=itemId;heldStack=stack;HeldIdentity=stack?.RuntimeIdentity??Guid.Empty;Revision++; }
        /// <summary>库存快照恢复后系统重绑同一身份；不改变手持选择或代次。</summary>
        internal void BindSource(ItemStack stack) {heldStack=stack;HeldIdentity=stack?.RuntimeIdentity??Guid.Empty;}
        internal bool SourceIsCurrent(InventorySlotRef source) => HeldSource.HasValue && HeldSource.Value.ContainerKind==source.ContainerKind && HeldSource.Value.SlotIndex==source.SlotIndex && source.RuntimeContainerId==0;
        public HeldItemSnapshot CaptureHeldSnapshot()=>new HeldItemSnapshot(HeldSource,HeldItemId,Revision);
        internal WeaponEquipmentSnapshot CaptureSnapshot()=>new WeaponEquipmentSnapshot(CaptureHeldSnapshot(),ActiveSlot,RangedLoadedAmmo,TorchLit);
        internal void RestoreSnapshot(WeaponEquipmentSnapshot snapshot)
        {
            var stack = snapshot.Source.HasValue && SourceIsCurrent(snapshot.Source.Value) ? heldStack : null;
            if (stack != null) stack.LoadedAmmo = snapshot.RangedLoadedAmmo;
            if(snapshot.Source.HasValue)SetHeld(snapshot.Source,snapshot.HeldItemId>0?snapshot.HeldItemId:snapshot.ActiveSlot==WeaponSlot.Ranged?snapshot.RangedWeaponItemId:snapshot.MeleeWeaponItemId, stack);else SetHeld(null,0);
            TorchLit=snapshot.TorchLit;
        }
        internal int GetSlotItem(WeaponSlot slot)=>slot==ActiveSlot?HeldItemId:0;
        internal bool TrySetRangedLoadedAmmo(int amount,out string error)
        {error=null;if(heldStack==null||ActiveSlot!=WeaponSlot.Ranged||amount<0||!this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(HeldItemId,out var weapon,out error)||amount>weapon.MagazineCapacity){error??="当前手持装填无效";return false;}heldStack.LoadedAmmo=amount;return true;}
        private static bool ValidateState(
            int melee,
            int ranged,
            WeaponSlot activeSlot,
            int rangedLoadedAmmo,
            IRuntimeConfigProvider config,
            out string error)
        {
            if (melee > 0 &&
                !ValidateSlotWeapon(melee, WeaponSlot.Melee, config, out error))
            {
                return false;
            }

            if (ranged > 0 &&
                !ValidateSlotWeapon(ranged, WeaponSlot.Ranged, config, out error))
            {
                return false;
            }

            if (melee > 0 && melee == ranged)
            {
                error = "武器装备槽位不能包含重复的 ItemId";
                return false;
            }

            if (activeSlot != WeaponSlot.None && activeSlot != WeaponSlot.Melee && activeSlot != WeaponSlot.Ranged)
            {
                error = "活动武器槽位无效";
                return false;
            }

            if ((melee > 0 || ranged > 0) && activeSlot == WeaponSlot.None)
            {
                error = "装备存在时活动武器槽位不能为 none";
                return false;
            }

            if (activeSlot == WeaponSlot.Ranged && ranged == 0)
            {
                error = "活动武器槽位指向空槽位";
                return false;
            }

            if (rangedLoadedAmmo < 0)
            {
                error = "rangedLoadedAmmo 不能为负数";
                return false;
            }

            if (ranged == 0 && rangedLoadedAmmo != 0)
            {
                error = "远程槽位为空时 rangedLoadedAmmo 必须为 0";
                return false;
            }

            if (ranged > 0 && config.TryGetWeapon(ranged, out var rangedWeapon, out _) && rangedLoadedAmmo > rangedWeapon.MagazineCapacity)
            {
                error = "rangedLoadedAmmo 超出弹匣容量";
                return false;
            }

            error = null;
            return true;
        }

        private static bool ValidateSlotWeapon(int itemId, WeaponSlot slot, IRuntimeConfigProvider config, out string error)
        {
            if (config == null)
            {
                error = "运行时配置提供器 缺少";
                return false;
            }

            if (!config.TryGetItem(itemId, out var item, out error))
            {
                return false;
            }

            if (item.ItemType != global::ItemType.Weapon)
            {
                error = "装备物品必须是武器";
                return false;
            }

            if (!config.TryGetWeapon(itemId, out var weapon, out error))
            {
                return false;
            }

            if (slot == WeaponSlot.Melee && weapon.WeaponKind != global::WeaponKind.Melee ||
                slot == WeaponSlot.Ranged && weapon.WeaponKind != global::WeaponKind.Projectile)
            {
                error = "武器类型与装备槽位不匹配";
                return false;
            }

            error = null;
            return true;
        }

        private readonly struct SaveState
        {
            public SaveState(int meleeWeaponItemId, int rangedWeaponItemId, WeaponSlot activeSlot, int rangedLoadedAmmo)
            {
                MeleeWeaponItemId = meleeWeaponItemId;
                RangedWeaponItemId = rangedWeaponItemId;
                ActiveSlot = activeSlot;
                RangedLoadedAmmo = rangedLoadedAmmo;
            }

            public int MeleeWeaponItemId { get; }
            public int RangedWeaponItemId { get; }
            public WeaponSlot ActiveSlot { get; }
            public int RangedLoadedAmmo { get; }
        }

        private static bool TryReadState(JToken data, out SaveState state, out string error)
        {
            state = default;
            if (data == null || data.Type != JTokenType.Object)
            {
                error = "武器装备分区必须为对象";
                return false;
            }

            if (!TryReadItemId(data[MeleeWeaponItemIdKey], MeleeWeaponItemIdKey, out var melee, out error) ||
                !TryReadItemId(data[RangedWeaponItemIdKey], RangedWeaponItemIdKey, out var ranged, out error) ||
                !TryReadActiveSlot(data[ActiveSlotKey], out var activeSlot, out error))
            {
                return false;
            }

            var loadedAmmoToken = data[RangedLoadedAmmoKey];
            var rangedLoadedAmmo = 0;
            if (loadedAmmoToken != null && !TryReadItemId(loadedAmmoToken, RangedLoadedAmmoKey, out rangedLoadedAmmo, out error))
            {
                return false;
            }

            state = new SaveState(melee, ranged, activeSlot, rangedLoadedAmmo);
            error = null;
            return true;
        }

        private static bool TryReadItemId(JToken token, string key, out int value, out string error)
        {
            value = 0;
            if (token == null)
            {
                error = $"{key} 缺少";
                return false;
            }

            if (token.Type != JTokenType.Integer)
            {
                error = $"{key} 必须为整数";
                return false;
            }

            value = token.Value<int>();
            if (value < 0)
            {
                error = $"{key} 不能为负数";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryReadActiveSlot(JToken token, out WeaponSlot slot, out string error)
        {
            slot = WeaponSlot.None;
            if (token?.Type != JTokenType.String ||
                !Enum.TryParse(token.Value<string>(), out slot) ||
                !Enum.IsDefined(typeof(WeaponSlot), slot))
            {
                error = "activeSlot 无效";
                return false;
            }

            error = null;
            return true;
        }

        private static WeaponSlot NormalizeRestoredActiveSlot(SaveState state)
        {
            return state.ActiveSlot == WeaponSlot.None &&
                   state.MeleeWeaponItemId == 0 &&
                   state.RangedWeaponItemId == 0
                ? WeaponSlot.Melee
                : state.ActiveSlot;
        }
    }
}
