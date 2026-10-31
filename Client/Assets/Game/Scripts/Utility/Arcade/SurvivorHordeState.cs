using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>远场敌群的生成候选；保留令牌，表现提交成功之前不扣除敌群人数。</summary>
    public readonly struct SurvivorActivationRequest
    {
        /// <summary>单调递增的展开令牌，清空局状态后不复用，拒绝旧局迟到提交。</summary>
        public readonly long Id;
        /// <summary>地图分区、敌种和当前位置；不携带Unity对象或资源句柄。</summary>
        public readonly int Region;
        public readonly string EnemyId;
        public readonly float X, Y, Speed;
        internal SurvivorActivationRequest(long id, int region, string enemyId, float x, float y, float speed)
        { Id=id; Region=region; EnemyId=enemyId; X=x; Y=y; Speed=speed; }
    }

    /// <summary>尚未展开的同种敌群账本；只占群级记录，不按远场敌人数创建角色或对象。</summary>
    public sealed class SurvivorHordeState
    {
        /// <summary>只有同分区、同位置和同速度的初始敌人可合并；已受伤个体不进入此账本。</summary>
        private sealed class Group
        {
            public int Region;
            public string EnemyId;
            public float X,Y,Speed;
            public long Count;
            public readonly List<SurvivorActivationRequest> Pending=new List<SurvivorActivationRequest>();
        }
        /// <summary>固定遍历顺序让相同输入的展开次序稳定，群人数不参与遍历次数。</summary>
        private readonly List<Group> groups=new List<Group>();
        private readonly Dictionary<long,Group> reservations=new Dictionary<long,Group>();
        private long nextId;
        /// <summary>包含已预留但尚未成功展开的数量，不受旧存活上限影响。</summary>
        public long TotalCount { get; private set; }
        public int GroupCount=>groups.Count;

        /// <summary>原子加入远场人数；验证和溢出检查在任何修改之前完成。</summary>
        public void Add(int region,string enemyId,float x,float y,float speed,long count)
        {
            if(string.IsNullOrWhiteSpace(enemyId)||!Finite(x)||!Finite(y)||!Finite(speed)||speed<0||count<=0)
                throw new ArgumentException("远场敌群参数无效");
            long total=checked(TotalCount+count);
            foreach(var g in groups)
                if(g.Region==region&&g.EnemyId==enemyId&&g.X==x&&g.Y==y&&g.Speed==speed)
                {g.Count=checked(g.Count+count);TotalCount=total;return;}
            groups.Add(new Group{Region=region,EnemyId=enemyId,X=x,Y=y,Speed=speed,Count=count});
            TotalCount=total;
        }

        /// <summary>诊断及独立检查入口；运行态使用复用List重载避免每帧数组。</summary>
        public SurvivorActivationRequest[] Reserve(float minX,float minY,float maxX,float maxY,int budget)
        {
            var output=new List<SurvivorActivationRequest>();
            Reserve(minX,minY,maxX,maxY,budget,output);
            return output.ToArray();
        }

        /// <summary>仅准备带内生成候选；重复请求重用令牌，未提交不会丢怪或重复预留。</summary>
        public void Reserve(float minX,float minY,float maxX,float maxY,int budget,List<SurvivorActivationRequest> output)
        {
            if(output==null)throw new ArgumentNullException(nameof(output));
            if(!Finite(minX)||!Finite(minY)||!Finite(maxX)||!Finite(maxY)||maxX<minX||maxY<minY||budget<0)
                throw new ArgumentException("远场展开范围或预算无效");
            output.Clear();
            foreach(var g in groups)
            {
                if(output.Count>=budget)break;
                if(g.X<minX||g.X>maxX||g.Y<minY||g.Y>maxY)continue;
                int take=(int)Math.Min(g.Count,budget-output.Count);
                // 先检查令牌余量，避免溢出留下半次分配。
                checked { _=nextId+Math.Max(0,take-g.Pending.Count); }
                for(int i=0;i<take;i++)
                {
                    if(i==g.Pending.Count)
                    {
                        var request=new SurvivorActivationRequest(++nextId,g.Region,g.EnemyId,g.X,g.Y,g.Speed);
                        g.Pending.Add(request);reservations.Add(request.Id,g);
                    }
                    output.Add(g.Pending[i]);
                }
            }
        }

        /// <summary>由完整实体创建事务成功后调用；重复、旧局和未知令牌不会重复扣减。</summary>
        public bool Commit(long id)
        {
            if(!reservations.TryGetValue(id,out var group))return false;
            reservations.Remove(id);
            for(int i=0;i<group.Pending.Count;i++)if(group.Pending[i].Id==id){group.Pending.RemoveAt(i);break;}
            group.Count--;TotalCount--;
            if(group.Count==0)groups.Remove(group);
            return true;
        }

        /// <summary>失败候选释放预留并把该群排到末尾，人数不变，避免坏落点永久饿死其他群。</summary>
        public void Defer(long id)
        {
            if(!reservations.TryGetValue(id,out var group))return;
            reservations.Remove(id);
            for(int i=0;i<group.Pending.Count;i++)if(group.Pending[i].Id==id){group.Pending.RemoveAt(i);break;}
            groups.Remove(group);groups.Add(group);
        }

        /// <summary>相机突变时可见群需优先恢复；计数不逐只展开或分配对象。</summary>
        public long CountInBounds(float minX,float minY,float maxX,float maxY)
        {
            long count=0;foreach(var group in groups)
                if(group.X>=minX&&group.X<=maxX&&group.Y>=minY&&group.Y<=maxY)count=checked(count+group.Count);
            return count;
        }

        /// <summary>按群推进远场，通路由对应体型导航决定；暂停不调用导航，不产生逐敌人工作。</summary>
        public void Advance(float seconds,Func<int,string,float,float,float,(float X,float Y)> move)
        {
            if(!Finite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
            if(move==null)throw new ArgumentNullException(nameof(move));
            if(seconds==0)return;
            foreach(var g in groups)
            {
                float distance=g.Speed*seconds;
                if(!Finite(distance))throw new ArgumentOutOfRangeException(nameof(seconds));
                var next=move(g.Region,g.EnemyId,g.X,g.Y,distance);
                double dx=(double)next.X-g.X,dy=(double)next.Y-g.Y;
                if(!Finite(next.X)||!Finite(next.Y)||dx*dx+dy*dy>(double)distance*distance+0.0001)
                    throw new InvalidOperationException("远场导航返回非法位置或超出步长");
                if(g.X==next.X&&g.Y==next.Y)continue;
                // 位置变化撤销旧预留；令牌永不复用，迟到提交拒绝而不扣数量。
                foreach(var ticket in g.Pending)reservations.Remove(ticket.Id);
                g.Pending.Clear();g.X=next.X;g.Y=next.Y;
            }
        }

        /// <summary>释放所有远场状态，令牌序列不归零，隔离旧局回调。</summary>
        public void Clear(){groups.Clear();reservations.Clear();TotalCount=0;}
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
