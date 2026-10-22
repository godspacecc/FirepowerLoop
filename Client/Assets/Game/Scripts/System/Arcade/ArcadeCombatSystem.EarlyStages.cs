using System.Linq;
using FirepowerLoop.Arcade;
namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>只按当前配置身份取新场地尺寸；本局累计场数不承担地图身份职责。</summary>
        private ArcadeEarlyStageParameters EarlyStage=>catalog?.EarlyStages.FirstOrDefault(p=>p.ArenaConfigNumber==arena?.ArenaNumber);
        private float ArenaHalfWidth=>EarlyStage?.HalfWidth??catalog.Rule.ArenaHalfWidth;
        private float ArenaHalfHeight=>EarlyStage?.HalfHeight??catalog.Rule.ArenaHalfHeight;
    }
}
