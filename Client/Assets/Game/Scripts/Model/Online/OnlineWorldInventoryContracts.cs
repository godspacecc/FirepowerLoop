using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public readonly struct OnlineItemSlotSnapshot
    {
        public OnlineItemSlotSnapshot(int slotIndex,int itemId,int amount,int loadedAmmo=0){SlotIndex=slotIndex;ItemId=itemId;Amount=amount;LoadedAmmo=loadedAmmo;}
        public int SlotIndex{get;} public int ItemId{get;} public int Amount{get;} public int LoadedAmmo{get;}
    }

    public readonly struct OnlineEquipmentSnapshot
    {
        public OnlineEquipmentSnapshot(int sourceSlotIndex,int itemId,int loaded,WeaponSlot kind)
        { SourceSlotIndex=sourceSlotIndex; ItemId=itemId; MeleeWeaponItemId=kind==WeaponSlot.Melee?itemId:0; RangedWeaponItemId=kind==WeaponSlot.Ranged?itemId:0; ActiveSlot=(int)kind; RangedLoadedAmmo=loaded; }
        public int SourceSlotIndex{get;} public int ItemId{get;}
        public OnlineEquipmentSnapshot(int melee,int ranged,int active,int loaded){SourceSlotIndex=-1;ItemId=active==2?ranged:melee;MeleeWeaponItemId=active==1?melee:0;RangedWeaponItemId=active==2?ranged:0;ActiveSlot=active;RangedLoadedAmmo=loaded;}
        public int MeleeWeaponItemId{get;} public int RangedWeaponItemId{get;} public int ActiveSlot{get;} public int RangedLoadedAmmo{get;}
    }

    public readonly struct OnlineInventorySnapshot
    {
        public OnlineInventorySnapshot(int capacity,IReadOnlyList<OnlineItemSlotSnapshot> slots,OnlineEquipmentSnapshot equipment,long revision,int torsoItemId=0,int legsItemId=0,int headItemId=0,int handsItemId=0,int feetItemId=0,int backItemId=0,int armorItemId=0){TorsoItemId=torsoItemId;LegsItemId=legsItemId;HeadItemId=headItemId;HandsItemId=handsItemId;FeetItemId=feetItemId;BackItemId=backItemId;ArmorItemId=armorItemId;Capacity=capacity;Slots=Copy(slots);Equipment=equipment;Revision=revision;}
        public int Capacity{get;} public IReadOnlyList<OnlineItemSlotSnapshot> Slots{get;} public OnlineEquipmentSnapshot Equipment{get;} public long Revision{get;}
        public int TorsoItemId{get;} public int LegsItemId{get;} public int HeadItemId{get;} public int HandsItemId{get;} public int FeetItemId{get;} public int BackItemId{get;} public int ArmorItemId{get;}
        /// <summary>固定枚举顺序的独立副本，供外观与装备页消费。</summary>
        public int[] ClothingItems()=>new[]{TorsoItemId,LegsItemId,HeadItemId,HandsItemId,FeetItemId,BackItemId,ArmorItemId};
        public static OnlineInventorySnapshot Empty=>new(0,Array.Empty<OnlineItemSlotSnapshot>(),default,0);
        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source){if(source==null||source.Count==0)return Array.Empty<T>();T[] copy=new T[source.Count];for(int i=0;i<copy.Length;i++)copy[i]=source[i];return Array.AsReadOnly(copy);}
    }

    public readonly struct OnlineWorldCorpseSnapshot
    {
        public OnlineWorldCorpseSnapshot(long corpseId,int sourceKind,long sourceUnitId,long sourcePlayerId,string sourceConfigId,long worldId,string mapId,Vector3 position,float yaw,long expires,bool contents,long revision){CorpseId=corpseId;SourceKind=sourceKind;SourceUnitId=sourceUnitId;SourcePlayerId=sourcePlayerId;SourceConfigId=sourceConfigId??string.Empty;WorldId=worldId;MapId=mapId??string.Empty;LocalPosition=position;YawDegrees=yaw;ExpiresAtServerTime=expires;HasContents=contents;StateRevision=revision;}
        public long CorpseId{get;} public int SourceKind{get;} public long SourceUnitId{get;} public long SourcePlayerId{get;} public string SourceConfigId{get;} public long WorldId{get;} public string MapId{get;} public Vector3 LocalPosition{get;} public float YawDegrees{get;} public long ExpiresAtServerTime{get;} public bool HasContents{get;} public long StateRevision{get;}
    }

    public readonly struct OnlineWorldCorpseContentsSnapshot
    {
        public OnlineWorldCorpseContentsSnapshot(OnlineWorldCorpseSnapshot corpse,int capacity,IReadOnlyList<OnlineItemSlotSnapshot> slots){Corpse=corpse;Capacity=capacity;Slots=Copy(slots);}
        public OnlineWorldCorpseSnapshot Corpse{get;} public int Capacity{get;} public IReadOnlyList<OnlineItemSlotSnapshot> Slots{get;}
        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source){if(source==null||source.Count==0)return Array.Empty<T>();T[] copy=new T[source.Count];for(int i=0;i<copy.Length;i++)copy[i]=source[i];return Array.AsReadOnly(copy);}
    }

    public readonly struct OnlineWorldSelfDamagedEvent{public OnlineWorldSelfDamagedEvent(long unitId,long lifecycle,int damage){UnitId=unitId;LifecycleRevision=lifecycle;Damage=damage;}public long UnitId{get;}public long LifecycleRevision{get;}public int Damage{get;}}
    public readonly struct OnlineWorldInventoryChangedEvent{public OnlineWorldInventoryChangedEvent(long inventoryRevision,long corpseId,long corpseRevision){InventoryRevision=inventoryRevision;CorpseId=corpseId;CorpseRevision=corpseRevision;}public long InventoryRevision{get;}public long CorpseId{get;}public long CorpseRevision{get;}}
}
