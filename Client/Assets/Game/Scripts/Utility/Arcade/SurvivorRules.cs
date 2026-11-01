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
        /// <summary>整局共用刷新次数；升级不补充，仅创建新局时由权威配置赋值。</summary>
        public int RemainingRefreshes { get; private set; }
        public double ElapsedSeconds { get; private set; }
        public bool Finished { get; private set; }
        public long RequiredExperience => (long)baseExperience+(long)(Level-1)*experienceGrowth;

        /// <summary>保留不含刷新预算的纯规则构造入口；正式本局必须显式传入配置预算。</summary>
        public SurvivorRunState(int baseExperience,int experienceGrowth):this(baseExperience,experienceGrowth,0) { }

        /// <summary>创建独立本局规则，刷新预算由调用方权威配置提供。</summary>
        public SurvivorRunState(int baseExperience,int experienceGrowth,int refreshes)
        {
            if(baseExperience<=0 || experienceGrowth<0) throw new ArgumentOutOfRangeException(nameof(baseExperience));
            if(refreshes<0)throw new ArgumentOutOfRangeException(nameof(refreshes));
            this.baseExperience=baseExperience; this.experienceGrowth=experienceGrowth;
            RemainingRefreshes=refreshes;
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

        /// <summary>按配置权重无放回抽取；首次保底仅影响新一轮候选，已展示选择保持稳定。</summary>
        public int[] OfferWeighted(int[] eligibleIds,int[] weights,int seed,int preferredId)
        {
            if(Finished || PendingChoices==0)return Array.Empty<int>();
            if(offer.Length>0)return (int[])offer.Clone();
            if(eligibleIds==null || weights==null || eligibleIds.Length!=weights.Length)throw new ArgumentException("候选与权重必须对应");
            var candidates=new List<(int Id,int Weight)>();
            var seen=new HashSet<int>();
            for(int i=0;i<eligibleIds.Length;i++)
            {
                if(eligibleIds[i]<=0 || weights[i]<=0 || !seen.Add(eligibleIds[i]))throw new ArgumentException("候选编号和权重必须为正且不重复");
                candidates.Add((eligibleIds[i],weights[i]));
            }
            // 后期有限强化耗尽时按剩余数量提供选择，不能阻塞升级事务。
            if(candidates.Count==0)return Array.Empty<int>();
            int choiceCount=Math.Min(3,candidates.Count);
            var chosen=new List<int>();var random=new Random(seed);
            int preferred=candidates.FindIndex(x=>x.Id==preferredId);
            if(preferred>=0){chosen.Add(preferredId);candidates.RemoveAt(preferred);}
            while(chosen.Count<choiceCount)
            {
                long total=candidates.Sum(x=>(long)x.Weight);double roll=random.NextDouble()*total;
                int index=0;while(index<candidates.Count-1&&roll>=candidates[index].Weight){roll-=candidates[index].Weight;index++;}
                chosen.Add(candidates[index].Id);candidates.RemoveAt(index);
            }
            offer=chosen.ToArray();return (int[])offer.Clone();
        }

        /// <summary>查询指定卡槽能否刷新；排除整组已显示项，保持另外两张及候选唯一。</summary>
        public bool CanRefresh(int revision,int slot,int[] eligibleIds,int[] weights)
            => RefreshCandidates(revision,slot,eligibleIds,weights).Length>0;

        /// <summary>成功替换单槽后才扣共享次数并更新版本；不消费经验或升级机会。</summary>
        public bool TryRefresh(int revision,int slot,int[] eligibleIds,int[] weights,int seed)
        {
            var candidates=RefreshCandidates(revision,slot,eligibleIds,weights);
            if(candidates.Length==0)return false;
            double roll=new Random(seed).NextDouble()*candidates.Sum(i=>(long)weights[i]);
            int selected=candidates[0];
            foreach(int index in candidates){selected=index;if(roll<weights[index])break;roll-=weights[index];}
            offer[slot]=eligibleIds[selected];RemainingRefreshes--;ChoiceRevision++;return true;
        }

        /// <summary>可用性与提交共用校验，不允许旧版本、结束局、重复候选或无效权重消耗次数。</summary>
        private int[] RefreshCandidates(int revision,int slot,int[] ids,int[] weights)
        {
            if(Finished||PendingChoices<=0||RemainingRefreshes<=0||revision!=ChoiceRevision||slot<0||slot>=offer.Length||ids==null||weights==null||ids.Length!=weights.Length)return Array.Empty<int>();
            var seen=new HashSet<int>();
            return Enumerable.Range(0,ids.Length).Where(i=>ids[i]>0&&weights[i]>0&&!offer.Contains(ids[i])&&seen.Add(ids[i])).ToArray();
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
