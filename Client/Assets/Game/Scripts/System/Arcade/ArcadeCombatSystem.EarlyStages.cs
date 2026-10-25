using System.Linq;
using FirepowerLoop.Arcade;
namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>仅21—25采用新场地规则；巨石的采用资源不改变旧出生、移动或传送门。</summary>
        private ArcadeEarlyStageParameters EarlyStage=>catalog?.EarlyStages.FirstOrDefault(p=>p.ArenaConfigNumber>=21&&p.ArenaConfigNumber<=25&&p.ArenaConfigNumber==arena?.ArenaNumber);
        private float ArenaHalfWidth=>EarlyStage?.HalfWidth??catalog.Rule.ArenaHalfWidth;
        private float ArenaHalfHeight=>EarlyStage?.HalfHeight??catalog.Rule.ArenaHalfHeight;
    }
}
