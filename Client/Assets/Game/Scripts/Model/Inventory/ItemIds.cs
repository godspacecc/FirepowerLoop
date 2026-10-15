namespace Game
{
    public static class ItemIds
    {
        public const int MetalParts = 1;
        public const int Cloth = 2;
        public const int Medkit = 3;
        public const int Ration = 4;
        public const int ShortSword = 5;
        public const int Bow = 6;
        public const int Arrow = 7;
        public const int BottledWater = 8;
        public const int Wood = 9;
        public const int BaseMap = 10;
        public const int SurvivalSampleMap = 11;
        public const int MissionSampleMap = 12;
        public const int HeJiaVillageMap = 13;
        public const int HeJiaVillageLocalMap = 14;
        /// <summary>河谷资料道具，指向当次地图探索；与已发布物品 ID 保持独立。</summary>
        public const int CivilisationValleyMap = 47;
    }

    public static class InventoryDefaults
    {
        /// <summary>独立口袋固定四格；不占用背包容量，也不限制物品类别。</summary>
        public const int PlayerPocketCapacity = 4;
        public const int PlayerBagCapacity = 12;
        public const int WarehouseCapacity = 40;
        public const int WarehouseMaximumCapacity = 80;
        public const int WarehouseExpansionMetalPartsCost = 4;
        public const int WarehouseExpansionClothCost = 3;
        public const int LegacyMetalPartsMaxStack = 20;
    }
}
