namespace FirepowerLoop.Arcade
{
    /// <summary>双端共用的死亡表现分类；只描述最终伤害来源，不改变伤害、活体位置或奖励。</summary>
    public static class ArcadeDeathImpactRules
    {
        /// <summary>兼容既有死亡事实：由Explosive区分普通倒地与强爆炸。</summary>
        public const int Default = 0;
        /// <summary>射线爆炸保留血雾，但降低尸体速度、抛升和翻滚。</summary>
        public const int EnergyBlast = 1;
        /// <summary>近距霰弹只做低幅后倒，不声明爆炸或喷出爆炸血雾。</summary>
        public const int CloseShotgun = 2;
        /// <summary>牛群击杀沿牛行进方向完整腾空，落地后保持尸体，不产生爆炸血雾。</summary>
        public const int Herd = 3;
        /// <summary>未知或与伤害类别矛盾的扩展值退回旧表现，兼容未携带新字段的死亡记录。</summary>
        public static int Normalize(bool explosive, int kind) =>
            explosive && kind == EnergyBlast || !explosive && (kind == CloseShotgun || kind == Herd) ? kind : Default;
    }
}
