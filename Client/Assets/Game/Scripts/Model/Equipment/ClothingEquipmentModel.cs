using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    /// <summary>保存身体穿戴物品的唯一状态；库存转移归 System，模型资源和 UI 不属于本类。</summary>
    public sealed class ClothingEquipmentModel : AbstractModel, IScopedSaveSectionProvider
    {
        public const string Section = "another-day.clothing-equipment";
        public string SectionId => Section;
        public SaveSectionScope Scope => SaveSectionScope.SharedPlayer;
        /// <summary>七个固定槽位；0、1 保留旧衣裤身份，新增部位按枚举追加，不是背包格号。</summary>
        public const int SlotCount = 7;
        internal ItemContainer Container { get; private set; } = new ItemContainer(SlotCount);
        /// <summary>运行期变更版本，供卸下请求拒绝旧界面操作，不写入存档。</summary>
        public long Revision { get; private set; }
        protected override void OnInit() { }
        public int GetItemId(global::ClothingSlot slot) => TryIndex(slot,out var index) && Container.TryGet(index,out var item) ? item.ItemId : 0;
        internal static bool TryIndex(global::ClothingSlot slot,out int index)
        { index=(int)slot-1; return index>=0 && index<SlotCount; }
        internal void Commit(ItemContainerSnapshot snapshot) { Container.RestoreSnapshot(snapshot); Revision++; }
        public JToken Capture() => new JObject { ["schemaVersion"]=1, ["slots"]=InventorySaveContracts.CaptureSlots(Container) };
        /// <summary>旧档缺少分区视为空槽；损坏物品和部位直接拒绝，不静默移除。</summary>
        public bool Validate(JToken data,out string error)
        {
            error=null; if(data==null)return true;
            if(data is not JObject obj || obj["schemaVersion"]?.Type!=JTokenType.Integer || obj["schemaVersion"].Value<long>()!=1)
            {error="衣物存档版本无效";return false;}
            var config=this.GetUtility<IRuntimeConfigProvider>();
            if(!InventorySaveContracts.ValidateSlots(obj["slots"],SlotCount,config,out var slots,out error))return false;
            foreach(var slot in slots)
                if(slot.Amount!=1 || slot.LoadedAmmo!=0 || !config.TryGetItem(slot.ItemId,out var item,out error) ||
                    item.ItemType!=global::ItemType.Clothing || (int)item.WearSlot!=slot.Index+1)
                {error="衣物存档物品与部位不匹配";return false;}
            return true;
        }
        public void Restore(JToken data)
        {
            if(!Validate(data,out var error))throw new InvalidOperationException(error);
            if(data==null){Reset();return;}
            InventorySaveContracts.ValidateSlots(data["slots"],SlotCount,this.GetUtility<IRuntimeConfigProvider>(),out var slots,out _);
            Container=InventorySaveContracts.RestoreContainer(SlotCount,slots); Revision++;
        }
        public void Reset() { Container=new ItemContainer(SlotCount); Revision++; }
    }
}
