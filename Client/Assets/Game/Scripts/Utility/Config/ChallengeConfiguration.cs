using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>挑战独立只读武器目录，不访问街机或幸存者规则表。</summary>
    public sealed class ChallengeConfiguration
    {
        /// <summary>复制表数据，防止生成行被外部修改后影响正在进行的局。</summary>
        private readonly Entry[] entries;
        private readonly struct Entry
        {
            public readonly string Id;
            public readonly int Magazine, Reserve;
            public readonly float Reload, Interval;
            public Entry(ChallengeWeaponConfig row)
            {Id=row.Id;Magazine=row.MagazineCapacity;Reserve=row.ReserveCapacity;Reload=row.ReloadSeconds;Interval=row.FireInterval;}
            public ChallengeWeaponState Create() => new ChallengeWeaponState(Id,Magazine,Reserve,Reload,Interval);
        }

        public ChallengeConfiguration(Tables tables)
        {
            if(tables?.TbChallengeWeapon?.DataList == null || tables.TbChallengeWeapon.DataList.Count==0)
                throw new ArgumentException("挑战武器表为空");
            var rows=tables.TbChallengeWeapon.DataList;
            entries=new Entry[rows.Count];
            var ids=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<rows.Count;i++)
            {
                if(rows[i]==null || !ids.Add(rows[i].Id)) throw new ArgumentException("挑战武器身份重复或缺失");
                entries[i]=new Entry(rows[i]);
                entries[i].Create(); // 在提交新局前完整校验容量和计时。
            }
        }

        /// <summary>每局创建全新的可变弹药状态，不共享上一局余弹。</summary>
        internal ChallengeWeaponState[] CreateLoadout()
        {
            var result=new ChallengeWeaponState[entries.Length];
            for(int i=0;i<result.Length;i++) result[i]=entries[i].Create();
            return result;
        }
    }
}
