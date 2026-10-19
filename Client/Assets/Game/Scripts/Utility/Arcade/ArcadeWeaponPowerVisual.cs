using System;

namespace Game
{
    /// <summary>将发射时层级编码进纯表现资源键；不改变原弹体、碰撞或武器身份。</summary>
    internal static class ArcadeWeaponPowerVisual
    {
        /// <summary>给当前武器外观附加发射时层级，空外观保持为空。</summary>
        public static string WithLevel(string resource,int level)
            =>string.IsNullOrEmpty(resource)?resource:resource+(level>=2?"#Purple":level==1?"#Red":"");

        /// <summary>还原原有载体名并读取层级，使强化不会换掉原枪弹形状。</summary>
        public static string Split(string resource,out int level)
        {
            level=0;if(string.IsNullOrEmpty(resource))return resource;
            if(resource.EndsWith("#Purple",StringComparison.Ordinal)){level=2;return resource.Substring(0,resource.Length-7);}
            if(resource.EndsWith("#Red",StringComparison.Ordinal)){level=1;return resource.Substring(0,resource.Length-4);}
            return resource;
        }
    }
}
