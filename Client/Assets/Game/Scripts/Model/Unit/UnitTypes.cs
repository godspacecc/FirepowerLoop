using System;

namespace Game
{
    /// <summary>
    /// 标识运行时单位的公共分类，供玩家、敌人、NPC、掉落物和撤离点共用。
    /// </summary>
    public enum UnitType
    {
        Player,
        Enemy,
        Npc,
        Loot,
        ExtractionPoint,
        Projectile
    }

    /// <summary>
    /// 标识单位的创建来源，避免把地图摆放、运行时生成和存档恢复混在一起。
    /// </summary>
    public enum UnitSource
    {
        Player,
        MapPlaced,
        RuntimeSpawned,
        SaveRestore
    }

    /// <summary>
    /// 标识单位当前由哪一侧拥有权威，本批次只实现本地权威。
    /// </summary>
    public enum UnitAuthorityMode
    {
        Local
    }

    /// <summary>
    /// 标识 UnitSnapshot 的可观察变化范围。
    /// </summary>
    [Flags]
    public enum UnitChangeMask
    {
        None = 0,
        Position = 1 << 0,
        Rotation = 1 << 1,
        Active = 1 << 2
    }

    /// <summary>
    /// 标识单位移除原因，用于事件消费者区分显式移除和会话释放。
    /// </summary>
    public enum UnitRemovedReason
    {
        Explicit,
        MapInstanceReleased,
        InvalidPresentation
    }
}
