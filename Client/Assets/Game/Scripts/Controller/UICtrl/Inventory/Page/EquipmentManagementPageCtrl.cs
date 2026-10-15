using QFramework;
using UnityEngine;
using UnityEngine.UI;
namespace Game
{
    /// <summary>展示权威七部位穿戴状态并提交卸下意图；页面只拥有预览资源，不修改库存。</summary>
    public sealed class EquipmentManagementPageCtrl : BasePageCtrl
    {
        private EquipmentManagementPageUI UI => BaseUI as EquipmentManagementPageUI;
        private int selected;
        private long displayedRevision;
        private IUnRegister language, clothing, online;
        private ClothingPreviewPresenter preview;
        private Vector3 previewView=ClothingPreviewPresenter.DefaultView;
        /// <summary>宿主传递的朝向、俯仰与缩放；仅属于界面会话，不进入存档或联机权威状态。</summary>
        public Vector3 PreviewView
        {get=>preview?.View??previewView;set{previewView=value;preview?.SetView(value);}}
        private readonly string[] parts={"head","torso","hands","legs","feet","back","accessory"};
        private Button[] Buttons()=>new[]{UI.BtnHead,UI.BtnTorso,UI.BtnHands,UI.BtnLegs,UI.BtnFeet,UI.BtnBack,UI.BtnAccessory};
        private global::ClothingSlot SelectedSlot=>new[]{global::ClothingSlot.Head,global::ClothingSlot.Torso,global::ClothingSlot.Hands,global::ClothingSlot.Legs,global::ClothingSlot.Feet,global::ClothingSlot.Back,global::ClothingSlot.Armor}[selected];
        private bool IsOnline=>this.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OnlinePlayersOnly;
        protected override void OnInit()
        {
            selected=0;var bound=BaseUI;var buttons=Buttons();
            for(int i=0;i<buttons.Length;i++){int index=i;buttons[i].onClick.AddListener(()=>{if(IsShow()&&ReferenceEquals(bound,BaseUI)){selected=index;Refresh();}});}
            UI.BtnUnequip.onClick.AddListener(()=>{if(IsShow()&&ReferenceEquals(bound,BaseUI))Unequip();});
        }
        protected override void OnShow()
        {
            language=this.RegisterEvent<LanguageChangedEvent>(_=>Refresh());
            clothing=this.RegisterEvent<ClothingEquipmentChangedEvent>(_=>Refresh());
            online=this.RegisterEvent<OnlineWorldChangedEvent>(_=>Refresh());
            preview=new ClothingPreviewPresenter(UI.CharacterPreviewPrefab,UI.CharacterPreview,previewView);
            Refresh();
        }
        protected override void OnHide()
        {language?.UnRegister();clothing?.UnRegister();online?.UnRegister();language=clothing=online=null;previewView=PreviewView;preview?.Dispose();preview=null;}
        /// <summary>异步卸下结束后只刷新原页面；服务器拒绝时保留原物品。</summary>
        private async void Unequip()
        {
            var bound=BaseUI;bool success;string error=null;
            if(IsOnline)success=await this.GetSystem<OnlineWorldInventorySystem>().UnequipClothingAsync(SelectedSlot,displayedRevision);
            else success=this.GetSystem<ClothingEquipmentSystem>().TryUnequip(SelectedSlot,displayedRevision,out error);
            if(!IsShow()||!ReferenceEquals(bound,BaseUI))return;
            if(!success)Notify.ShowFloatTips(Loc.T(error=="背包已满"?"ui.clothing.full":"ui.inventory.operation_failed"));
            Refresh();
        }
        private void Refresh()
        {
            if(!IsShow())return;
            int[] items;
            if(IsOnline){var state=this.GetSystem<OnlineWorldSystem>().Snapshot.SelfInventory;items=state.ClothingItems();displayedRevision=state.Revision;}
            else
            {
                var state=this.GetModel<ClothingEquipmentModel>();items=new int[ClothingEquipmentModel.SlotCount];
                for(int i=0;i<items.Length;i++)items[i]=state.GetItemId((global::ClothingSlot)(i+1));
                displayedRevision=state.Revision;
            }
            var config=this.GetUtility<IRuntimeConfigProvider>();
            preview?.RefreshItems(config,items);
            var buttons=Buttons();for(int i=0;i<buttons.Length;i++)buttons[i].image.color=i==selected?new Color(.43f,.37f,.22f,1):new Color(.20f,.24f,.25f,1);
            int id=items[(int)SelectedSlot-1];
            UI.DetailIcon.gameObject.SetActive(false);
            UI.BtnUnequip.gameObject.SetActive(id>0);
            UI.DetailNameText.text=Loc.T("ui.equipment.part."+parts[selected]);
            UI.DetailStatsText.text=string.Empty;
            if(config.TryGetItem(id,out var item,out _))
            {
                UI.DetailNameText.text=Loc.T(item.NameKey);UI.DetailSourceText.text=Loc.T("ui.clothing.equipped");UI.DetailDescriptionText.text=Loc.T(item.DescKey);
                UI.DetailIcon.sprite=ResourceLoader.LoadAsset<Sprite>(item.IconAddress);UI.DetailIcon.gameObject.SetActive(UI.DetailIcon.sprite!=null);
            }
            else if(SelectedSlot!=global::ClothingSlot.None)
            {UI.DetailSourceText.text=Loc.T("ui.clothing.empty");UI.DetailDescriptionText.text=Loc.T("ui.clothing.empty_detail");}
            else
            {UI.DetailSourceText.text=Loc.T("ui.equipment.unavailable");UI.DetailDescriptionText.text=Loc.T("ui.equipment.placeholder_detail");}
        }
    }
}
