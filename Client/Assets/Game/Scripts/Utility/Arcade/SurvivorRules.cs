using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    /// <summary>幸存者本局纯状态；由模式Model持有，不读写闯关进度或场景对象。</summary>
    public sealed class SurvivorRunState
    {
        /// <summary>经验曲线来自模式配置，测试可传入小阈值。</summary>
        private readonly int baseExperience, experienceGrowth;
        /// <summary>当前展示的稳定选项，返回副本防止UI修改权威数据。</summary>
        private int[] offer=Array.Empty<int>();
        /// <summary>本局强化层数，退出本局即丢弃。</summary>
        private readonly Dictionary<int,int> upgrades=new Dictionary<int,int>();
        public int Level { get; private set; }=1;
        public long Experience { get; private set; }
        public int PendingChoices { get; private set; }
        public int ChoiceRevision { get; private set; }
        public double ElapsedSeconds { get; private set; }
        public bool Finished { get; private set; }
        public long RequiredExperience => (long)baseExperience+(long)(Level-1)*experienceGrowth;

        public SurvivorRunState(int baseExperience,int experienceGrowth)
        {
            if(baseExperience<=0 || experienceGrowth<0) throw new ArgumentOutOfRangeException(nameof(baseExperience));
            this.baseExperience=baseExperience; this.experienceGrowth=experienceGrowth;
        }

        /// <summary>使用等差级数及二分处理巨额经验，避免大量连升阻塞主线程。</summary>
        public void AddExperience(long amount)
        {
            if(Finished || amount<=0) return;
            long available=amount>long.MaxValue-Experience?long.MaxValue:Experience+amount;
            int low=0,high=int.MaxValue-Level;
            decimal first=RequiredExperience;
            while(low<high)
            {
                int count=low+(int)(((long)high-low+1)/2);
                decimal cost=(decimal)count*first+(decimal)experienceGrowth*count*(count-1)/2;
                if(cost<=available) low=count; else high=count-1;
            }
            long consumed=(long)((decimal)low*first+(decimal)experienceGrowth*low*(low-1)/2);
            Experience=available-consumed; Level+=low; PendingChoices+=low;
        }

        /// <summary>暂停、选择强化、结束以及非法时间均不推进生存时间。</summary>
        public void Advance(double seconds,bool paused)
        {
            if(Finished || paused || PendingChoices>0 || seconds<=0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            double next=ElapsedSeconds+seconds;
            if(!double.IsInfinity(next)) ElapsedSeconds=next;
        }

        /// <summary>只从调用方提供的仍可升级配置中抽取三项；重复展示不重抽。</summary>
        public int[] Offer(int[] eligibleIds,int seed)
        {
            if(Finished || PendingChoices==0) return Array.Empty<int>();
            if(offer.Length==3) return (int[])offer.Clone();
            var candidates=(eligibleIds??Array.Empty<int>()).Where(id=>id>0).Distinct().OrderBy(id=>id).ToArray();
            if(candidates.Length<3) return Array.Empty<int>();
            var random=new Random(seed);
            for(int i=0;i<3;i++)
            {
                int j=random.Next(i,candidates.Length);
                int temp=candidates[i]; candidates[i]=candidates[j]; candidates[j]=temp;
            }
            offer=candidates.Take(3).ToArray();
            return (int[])offer.Clone();
        }

        /// <summary>选择事务以版本防连点；只允许当前真实选项，每次消耗一个升级机会。</summary>
        public bool Choose(int revision,int upgradeId)
        {
            if(Finished || PendingChoices<=0 || revision!=ChoiceRevision || !offer.Contains(upgradeId)) return false;
            upgrades.TryGetValue(upgradeId,out int count); upgrades[upgradeId]=count+1;
            PendingChoices--; ChoiceRevision++; offer=Array.Empty<int>(); return true;
        }

        /// <summary>强化效果按本局层数计算，UI只能读取。</summary>
        public int UpgradeLevel(int id)=>upgrades.TryGetValue(id,out int count)?count:0;

        /// <summary>死亡或主动退出仅结束一次；结果保存事务由模式System负责。</summary>
        public bool Finish()
        {
            if(Finished) return false;
            Finished=true; offer=Array.Empty<int>(); return true;
        }
    }
}
