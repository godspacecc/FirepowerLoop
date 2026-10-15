using QFramework;

namespace Game
{
    /// <summary>
    /// 保存本地玩家跨地图稳定的 UnitId，当前不写入存档。
    /// </summary>
    public sealed class PlayerModel : AbstractModel
    {
        public const int LocalPlayerUnitId = 1;

        public int PlayerUnitId => LocalPlayerUnitId;

        protected override void OnInit()
        {
        }
    }
}
