using UnityEngine;

namespace Game
{
    /// <summary>碰撞对象声明的命中音色；不参与碰撞、伤害或地表移动规则，未知对象保持 Unknown。</summary>
    public enum CombatImpactMaterial { Unknown, Body, Metal, Ice }
    public sealed class CombatImpactSurface : MonoBehaviour
    {
        [SerializeField] private CombatImpactMaterial material;
        public CombatImpactMaterial Material => material;
    }
}
