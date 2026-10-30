using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>本局武器槽与独立等级；只记录稳定武器类型，不持有表现资源。</summary>
    public sealed class SurvivorLoadout
    {
        /// <summary>槽位容量来自幸存者规则表。</summary>
        public int Capacity { get; }
        /// <summary>同种武器只占一个槽，升级不增加占用。</summary>
        public int UsedSlots => levels.Count;
        /// <summary>武器等级仅由幸存者系统的选择事务写入。</summary>
        private readonly Dictionary<string,int> levels=new Dictionary<string,int>(StringComparer.Ordinal);
        /// <summary>槽位顺序按获取顺序固定，升级不会重排。</summary>
        private readonly List<string> equipped=new List<string>();
        public string[] Weapons=>equipped.ToArray();
        /// <summary>新局以一级主武器开始，不继承上局装备。</summary>
        public SurvivorLoadout(int capacity,string initialWeapon)
        {
            if(capacity<1 || string.IsNullOrWhiteSpace(initialWeapon))throw new ArgumentException("武器槽或初始武器无效");
            Capacity=capacity;levels.Add(initialWeapon,1);equipped.Add(initialWeapon);
        }
        /// <summary>未拥有的武器为零级。</summary>
        public int LevelOf(string weapon)=>weapon!=null&&levels.TryGetValue(weapon,out var level)?level:0;
        /// <summary>满槽禁止获取新武器，已有武器未满级仍可升级。</summary>
        public bool CanUpgrade(string weapon,int maximumLevel)=>!string.IsNullOrWhiteSpace(weapon)&&maximumLevel>0&&LevelOf(weapon)<maximumLevel&&(LevelOf(weapon)>0||UsedSlots<Capacity);
        /// <summary>成功获取或升级一次；失败不改变槽位和等级。</summary>
        public bool TryUpgrade(string weapon,int maximumLevel)
        {
            if(!CanUpgrade(weapon,maximumLevel))return false;
            if(LevelOf(weapon)==0)equipped.Add(weapon);levels[weapon]=LevelOf(weapon)+1;return true;
        }
    }
}
