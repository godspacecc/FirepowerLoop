using UnityEngine;
using UnityEngine.UI;
namespace Game
{
    /// <summary>紧凑武器槽的显式图标和余弹外圈引用，数值由HUD控制器提供。</summary>
    public class SurvivorWeaponSlotBoxUI : BaseBoxUI
    {
        public Image Icon;
        public Image AmmoRing;
        public Sprite RifleIcon;
        public Sprite DroneIcon;
    }
}
