using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game
{
    /// <summary>幸存者共享距离场；静态阻挡一次栅格化，目标换格才做有界广搜。</summary>
    public sealed class SurvivorNavigation
    {
        /// <summary>导航采样精度；格数只随已校验的场地尺寸变化。</summary>
        private const float Cell=.4f;
        /// <summary>场地边界与敌人碰撞半径，用于膨胀阻挡。</summary>
        private readonly float width,height,radius;
        /// <summary>有界网格尺寸及可行走标记。</summary>
        private readonly int columns,rows;
        private readonly bool[] walkable;
        /// <summary>全体敌人共用距离和广搜队列，不按帧创建寻路对象。</summary>
        private readonly int[] distance,queue;
        /// <summary>固定场地障碍快照与当前目标格。</summary>
        private readonly IReadOnlyList<ArcadeObstacleDefinition> obstacles;
        private int goal=-1;
        /// <summary>一次构建静态可行走网格。</summary>
        public SurvivorNavigation(float halfWidth,float halfHeight,float bodyRadius,IReadOnlyList<ArcadeObstacleDefinition> blocking)
        {
            width=halfWidth;height=halfHeight;radius=bodyRadius;obstacles=blocking;
            columns=Mathf.FloorToInt((width-radius)*2/Cell)+1;rows=Mathf.FloorToInt((height-radius)*2/Cell)+1;
            walkable=new bool[columns*rows];distance=new int[walkable.Length];queue=new int[walkable.Length];
            for(int i=0;i<walkable.Length;i++)walkable[i]=Clear(Point(i));
        }
        private Vector2 Point(int i)=>new Vector2(-width+radius+i%columns*Cell,-height+radius+i/columns*Cell);
        private bool Clear(Vector2 p)
        {
            if(Mathf.Abs(p.x)>width-radius+.001f||Mathf.Abs(p.y)>height-radius+.001f)return false;
            foreach(var o in obstacles)if(Math.Abs((double)p.x-o.Center.x)<o.HalfExtents.x+radius-.00001&&Math.Abs((double)p.y-o.Center.y)<o.HalfExtents.y+radius-.00001)return false;
            return true;
        }
        /// <summary>精确线段与膨胀矩形求交；仅触及边界允许通过，不依赖采样步长。</summary>
        public bool CanTravel(Vector2 a,Vector2 b)
        {
            if(!Clear(a)||!Clear(b))return false;
            foreach(var obstacle in obstacles)
            {
                double enter=0,exit=1;
                // 微小内缩只消除浮点舍入，保持贴边行走和子线段判定一致。
                double hx=obstacle.HalfExtents.x+radius-0.00001,hy=obstacle.HalfExtents.y+radius-0.00001;
                if(Slab(a.x,b.x-a.x,obstacle.Center.x-hx,obstacle.Center.x+hx,ref enter,ref exit)&&
                   Slab(a.y,b.y-a.y,obstacle.Center.y-hy,obstacle.Center.y+hy,ref enter,ref exit)&&enter<exit)return false;
            }
            return true;
        }
        private static bool Slab(double origin,double delta,double minimum,double maximum,ref double enter,ref double exit)
        {
            if(Math.Abs(delta)<1e-12)return origin>minimum&&origin<maximum;
            double first=(minimum-origin)/delta,last=(maximum-origin)/delta;
            if(first>last){double swap=first;first=last;last=swap;}
            enter=Math.Max(enter,first);exit=Math.Min(exit,last);return enter<exit;
        }
        private int Nearest(Vector2 p,bool reachable)
        {
            int found=-1;float best=float.MaxValue;
            int x=Mathf.Clamp(Mathf.RoundToInt((p.x+width-radius)/Cell),0,columns-1),y=Mathf.Clamp(Mathf.RoundToInt((p.y+height-radius)/Cell),0,rows-1);
            // 常规追踪只访问目标周围九格；局部无落点时才扫描有界全图。
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,yy=y+dy;if(xx>=0&&xx<columns&&yy>=0&&yy<rows)Consider(yy*columns+xx,p,reachable,ref found,ref best);}
            if(found>=0)return found;
            for(int i=0;i<walkable.Length;i++)Consider(i,p,reachable,ref found,ref best);
            return found;
        }
        private void Consider(int i,Vector2 p,bool reachable,ref int found,ref float best)
        {
            if(!walkable[i]||reachable&&distance[i]<0)return;
            float d=(Point(i)-p).sqrMagnitude;if(d>=best||reachable&&!CanTravel(p,Point(i)))return;best=d;found=i;
        }
        /// <summary>目标移动不到一个格时复用距离场，全体敌人共用同一份缓存。</summary>
        public void SetTarget(Vector2 target)
        {
            int next=Nearest(target,false);if(next==goal)return;goal=next;Array.Fill(distance,-1);if(goal<0)return;
            int head=0,tail=0;queue[tail++]=goal;distance[goal]=0;
            while(head<tail){int at=queue[head++],x=at%columns,y=at/columns;
                Visit(at,x>0?at-1:-1,ref tail);Visit(at,x+1<columns?at+1:-1,ref tail);Visit(at,y>0?at-columns:-1,ref tail);Visit(at,y+1<rows?at+columns:-1,ref tail);}
        }
        private void Visit(int from,int to,ref int tail){if(to<0||!walkable[to]||distance[to]>=0||!CanTravel(Point(from),Point(to)))return;distance[to]=distance[from]+1;queue[tail++]=to;}
        /// <summary>沿可见的下降格移动，限制本帧步长；直达目标时不再绕格。</summary>
        public Vector2 Move(Vector2 position,Vector2 target,float amount)
        {
            if(amount<=0||goal<0)return position;if(CanTravel(position,target))return Vector2.MoveTowards(position,target,amount);
            int x=Mathf.Clamp(Mathf.RoundToInt((position.x+width-radius)/Cell),0,columns-1),y=Mathf.Clamp(Mathf.RoundToInt((position.y+height-radius)/Cell),0,rows-1);
            int best=-1;float score=float.MaxValue;
            for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++){int xx=x+dx,yy=y+dy;if(xx<0||xx>=columns||yy<0||yy>=rows)continue;int at=yy*columns+xx;if(distance[at]<0||!walkable[at])continue;float candidate=distance[at]*Cell*1.01f+Vector2.Distance(position,Point(at));if(candidate>=score||!CanTravel(position,Point(at)))continue;best=at;score=candidate;}
            if(best<0)best=Nearest(position,true);return best<0?position:Vector2.MoveTowards(position,Point(best),amount);
        }
    }
}
