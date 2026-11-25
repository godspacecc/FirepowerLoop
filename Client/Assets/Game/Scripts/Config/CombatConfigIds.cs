namespace Game
{
    /// <summary>幸存者和共用战斗资源使用的稳定配置标识。</summary>
    internal static class CombatConfigIds
    {
        /// <summary>战斗支援装备使用的配置槽位。</summary>
        internal const string SupportSlotId = "support";
        /// <summary>未配置独立弹体的枪械使用的基础子弹表现。</summary>
        internal const string ProjectileConfigId = "projectile.bullet.basic";
        /// <summary>呈现落地物资使用的共用拾取载体。</summary>
        internal const string DropPresentationLootConfigId = "loot.arcade.drop";
    }
}
