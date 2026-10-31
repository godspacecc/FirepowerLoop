using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>近场敌人空间索引；只存稳定ID和坐标，最终身体命中仍由战斗规则判定。</summary>
    public sealed class SurvivorEnemySpatialIndex
    {
        /// <summary>索引点及所在格，用于同一ID移动时移除旧候选。</summary>
        private readonly struct Point
        {
            public readonly float X,Y;
            public readonly long Cell;
            public Point(float x,float y,long cell){X=x;Y=y;Cell=cell;}
        }
        private readonly float cellSize;
        private readonly Dictionary<long,List<int>> cells=new Dictionary<long,List<int>>();
        private readonly Dictionary<int,Point> points=new Dictionary<int,Point>();
        /// <summary>只允许有限正格宽；格宽影响查询成本，不改变碰撞半径。</summary>
        public SurvivorEnemySpatialIndex(float cellSize)
        {if(!Finite(cellSize)||cellSize<=0)throw new ArgumentOutOfRangeException(nameof(cellSize));this.cellSize=cellSize;}
        /// <summary>重建时复用已分配格列表，死亡实体不残留在新快照。</summary>
        public void Clear(){foreach(var list in cells.Values)list.Clear();points.Clear();}
        /// <summary>加入或移动稳定ID；重复加入不会产生重复命中候选。</summary>
        public void Add(int id,float x,float y)
        {
            if(id<=0||!Finite(x)||!Finite(y))throw new ArgumentException("空间索引输入无效");
            long key=Key(Cell(x),Cell(y));
            if(points.TryGetValue(id,out var old))
            {
                if(old.Cell==key){points[id]=new Point(x,y,key);return;}
                cells[old.Cell].Remove(id);
            }
            if(!cells.TryGetValue(key,out var list)){list=new List<int>();cells.Add(key,list);}
            list.Add(id);points[id]=new Point(x,y,key);
        }
        /// <summary>圆形候选按稳定ID输出，用于最近目标选择的确定性同距处理。</summary>
        public void QueryCircle(float x,float y,float radius,List<int> output)=>QuerySegment(x,y,x,y,radius,output);
        /// <summary>返回线段胶囊范围内的中心候选；padding须覆盖所查询敌种最大身体半径。</summary>
        public void QuerySegment(float fromX,float fromY,float toX,float toY,float padding,List<int> output)
        {
            if(output==null)throw new ArgumentNullException(nameof(output));
            if(!Finite(fromX)||!Finite(fromY)||!Finite(toX)||!Finite(toY)||!Finite(padding)||padding<0)
                throw new ArgumentException("空间查询参数无效");
            output.Clear();if(points.Count==0)return;
            int minX=Cell(Math.Min(fromX,toX)-padding),maxX=Cell(Math.Max(fromX,toX)+padding);
            int minY=Cell(Math.Min(fromY,toY)-padding),maxY=Cell(Math.Max(fromY,toY)+padding);
            double area=((double)maxX-minX+1)*((double)maxY-minY+1);
            if(area>cells.Count)
            {
                // 极长弹道不遍历海量空格；只访问当前地图曾使用的格。
                foreach(var pair in cells)
                {
                    int x=(int)(pair.Key>>32),y=unchecked((int)pair.Key);
                    if(x>=minX&&x<=maxX&&y>=minY&&y<=maxY)Collect(pair.Value,fromX,fromY,toX,toY,padding,output);
                }
            }
            else for(long x=minX;x<=maxX;x++)for(long y=minY;y<=maxY;y++)
                if(cells.TryGetValue(Key((int)x,(int)y),out var list))Collect(list,fromX,fromY,toX,toY,padding,output);
            output.Sort();
        }
        /// <summary>中心距离精确过滤候选，不在此处处理血量、遮挡或爬出身体姿态。</summary>
        private void Collect(List<int> list,float ax,float ay,float bx,float by,float radius,List<int> output)
        {
            double dx=(double)bx-ax,dy=(double)by-ay,length=dx*dx+dy*dy;
            foreach(int id in list)
            {
                var p=points[id];double px=(double)p.X-ax,py=(double)p.Y-ay;
                double t=length==0?0:Math.Max(0,Math.Min(1,(px*dx+py*dy)/length));
                double x=px-t*dx,y=py-t*dy;
                if(x*x+y*y<=(double)radius*radius)output.Add(id);
            }
        }
        private int Cell(float value)
        {double cell=Math.Floor((double)value/cellSize);if(double.IsNaN(cell)||cell<int.MinValue||cell>int.MaxValue)throw new ArgumentOutOfRangeException(nameof(value));return (int)cell;}
        private static long Key(int x,int y)=>((long)x<<32)|(uint)y;
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
