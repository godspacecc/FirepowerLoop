using System;
using System.Collections.Generic;
namespace Game
{
    /// <summary>查询地图本地几何的连续首接触，不负责伤害、弹药或网络状态。</summary>
    #if DOTNET
    [ET.EnableClass]
    #endif
    public sealed class WorldProjectileCollisionQuery
    {
        private const float ContactTolerance=.00002f;
        private readonly WorldProjectileShape[] shapes;
        private readonly Node root;
        #if DOTNET
        [ET.EnableClass]
        #endif
        private sealed class Node
        {
            public WorldCollisionVector Min,Max;
            public Node Left,Right;
            public int Start,Count;
        }
        public WorldProjectileCollisionQuery(WorldProjectileShape[] shapes)
        {
            this.shapes=(WorldProjectileShape[])(shapes??Array.Empty<WorldProjectileShape>()).Clone();
            root=this.shapes.Length==0?null:Build(0,this.shapes.Length);
        }
        /// <summary>沿完整位移查询首接触，包围盒仅用于筛选，精确结果来自凸体距离；并列以稳定身份排序。</summary>
        public bool TrySweep(WorldCollisionVector from,WorldCollisionVector to,WorldProjectileShape projectile,out WorldProjectileContact contact)
        {
            contact=default;if(root==null||projectile==null||!from.IsFinite||!to.IsFinite)return false;
            var moving=projectile.At(from+projectile.Center);moving.Bounds(out var min,out var max);var delta=to-from;
            var endMin=min+delta;var endMax=max+delta;
            min=Min(min,endMin);max=Max(max,endMax);
            float nearest=float.PositiveInfinity;long id=long.MaxValue;
            Visit(root,min,max,moving,delta,ref nearest,ref id);
            if(float.IsPositiveInfinity(nearest))return false;
            contact=new WorldProjectileContact(id,nearest*delta.Length,from+delta*nearest);return true;
        }
        private void Visit(Node node,WorldCollisionVector min,WorldCollisionVector max,WorldProjectileShape moving,WorldCollisionVector delta,ref float nearest,ref long id)
        {
            if(!Overlaps(min,max,node.Min,node.Max))return;
            if(node.Left!=null){Visit(node.Left,min,max,moving,delta,ref nearest,ref id);Visit(node.Right,min,max,moving,delta,ref nearest,ref id);return;}
            for(int i=node.Start;i<node.Start+node.Count;i++)
            {
                var shape=shapes[i];shape.Bounds(out var a,out var b);
                if(!Overlaps(min,max,a,b))continue;
                float fraction;
                bool hit=shape.Kind==WorldCollisionShapeKind.Heightfield?SweepTerrain(moving,shape,delta,min,max,out fraction):Sweep(moving,shape,delta,out fraction);
                if(!hit)continue;
                if(fraction<nearest-1e-6f||(Math.Abs(fraction-nearest)<=1e-6f&&shape.Id<id)){nearest=fraction;id=shape.Id;}
            }
        }
        private Node Build(int start,int count)
        {
            shapes[start].Bounds(out var min,out var max);
            for(int i=start+1;i<start+count;i++){shapes[i].Bounds(out var a,out var b);min=Min(min,a);max=Max(max,b);}
            var n=new Node{Min=min,Max=max,Start=start,Count=count};if(count<=8)return n;
            var extent=max-min;int axis=extent.X>=extent.Y&&extent.X>=extent.Z?0:extent.Y>=extent.Z?1:2;
            Array.Sort(shapes,start,count,Comparer<WorldProjectileShape>.Create((a,b)=>
            {a.Bounds(out var amin,out var amax);b.Bounds(out var bmin,out var bmax);return Component(amin+amax,axis).CompareTo(Component(bmin+bmax,axis));}));
            n.Left=Build(start,count/2);n.Right=Build(start+count/2,count-count/2);return n;
        }
        private static float Component(WorldCollisionVector v,int axis)=>axis==0?v.X:axis==1?v.Y:v.Z;
        /// <summary>从终止点向下投影到场景表面；没有地面时失败，不生成悬空回收物。</summary>
        public bool TryGround(WorldCollisionVector position,out WorldCollisionVector ground)
        {
            ground=default;if(root==null||!position.IsFinite)return false;
            var from=new WorldCollisionVector(position.X,position.Y+.01f,position.Z);
            var to=new WorldCollisionVector(position.X,root.Min.Y-1,position.Z);
            if(from.Y<to.Y)return false;
            var point=new WorldProjectileShape{Kind=WorldCollisionShapeKind.Sphere,Radius=0};
            if(!TrySweep(from,to,point,out var contact))return false;ground=contact.Position;return true;
        }
        private static WorldCollisionVector Min(WorldCollisionVector a,WorldCollisionVector b)=>new WorldCollisionVector(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Min(a.Z,b.Z));
        private static WorldCollisionVector Max(WorldCollisionVector a,WorldCollisionVector b)=>new WorldCollisionVector(Math.Max(a.X,b.X),Math.Max(a.Y,b.Y),Math.Max(a.Z,b.Z));
        private static bool Overlaps(WorldCollisionVector a,WorldCollisionVector b,WorldCollisionVector c,WorldCollisionVector d)=>a.X<=d.X+ContactTolerance&&b.X>=c.X-ContactTolerance&&a.Y<=d.Y+ContactTolerance&&b.Y>=c.Y-ContactTolerance&&a.Z<=d.Z+ContactTolerance&&b.Z>=c.Z-ContactTolerance;

        private static bool SweepTerrain(WorldProjectileShape moving,WorldProjectileShape terrain,WorldCollisionVector delta,WorldCollisionVector min,WorldCollisionVector max,out float fraction)
        {
            fraction=float.PositiveInfinity;int n=terrain.HeightmapResolution;
            if(n<2||terrain.Heights==null||terrain.Heights.Length!=n*n)return false;
            float determinant=WorldCollisionVector.Dot(terrain.AxisX,WorldCollisionVector.Cross(terrain.AxisY,terrain.AxisZ));if(Math.Abs(determinant)<1e-8f)return false;
            float xmin=float.PositiveInfinity,zmin=float.PositiveInfinity,xmax=float.NegativeInfinity,zmax=float.NegativeInfinity;
            for(int i=0;i<8;i++)
            {
                var p=new WorldCollisionVector((i&1)==0?min.X:max.X,(i&2)==0?min.Y:max.Y,(i&4)==0?min.Z:max.Z)-terrain.A;
                float x=WorldCollisionVector.Dot(p,WorldCollisionVector.Cross(terrain.AxisY,terrain.AxisZ))/determinant;
                float z=WorldCollisionVector.Dot(p,WorldCollisionVector.Cross(terrain.AxisX,terrain.AxisY))/determinant;
                xmin=Math.Min(xmin,x);xmax=Math.Max(xmax,x);zmin=Math.Min(zmin,z);zmax=Math.Max(zmax,z);
            }
            int firstX=Math.Max(0,(int)Math.Floor(xmin*(n-1))-1),lastX=Math.Min(n-2,(int)Math.Floor(xmax*(n-1))+1);
            int firstZ=Math.Max(0,(int)Math.Floor(zmin*(n-1))-1),lastZ=Math.Min(n-2,(int)Math.Floor(zmax*(n-1))+1);
            WorldCollisionVector Point(int x,int z)=>terrain.A+terrain.AxisX*((float)x/(n-1))+terrain.AxisY*terrain.Heights[z*n+x]+terrain.AxisZ*((float)z/(n-1));
            for(int z=firstZ;z<=lastZ;z++)for(int x=firstX;x<=lastX;x++)
            {
                if(terrain.HoleCells!=null&&Array.BinarySearch(terrain.HoleCells,z*(n-1)+x)>=0)continue;
                var a=Point(x,z);var b=Point(x,z+1);var c=Point(x+1,z);var d=Point(x+1,z+1);
                // Unity Terrain 使用格子 a→d 对角线，必须与实际 Heightfield Collider 一致。
                var triangle=new WorldProjectileShape{Kind=WorldCollisionShapeKind.Triangle,A=a,B=b,C=d};
                if(Sweep(moving,triangle,delta,out var t))fraction=Math.Min(fraction,t);
                triangle.A=a;triangle.B=d;triangle.C=c;
                if(Sweep(moving,triangle,delta,out t))fraction=Math.Min(fraction,t);
            }
            return !float.IsPositiveInfinity(fraction);
        }

        // 保守推进每次只走到当前分离面的最早接触，不以采样步长跳过薄障碍物。
        private static bool Sweep(WorldProjectileShape moving,WorldProjectileShape target,WorldCollisionVector delta,out float fraction)
        {
            fraction=0;var initial=moving.Center;
            for(int step=0;step<96;step++)
            {
                var separation=Distance(moving.At(initial+delta*fraction),target);float length=separation.Length;
                if(length<=ContactTolerance)return true;
                float closing=-WorldCollisionVector.Dot(separation*(1f/length),delta);
                if(closing<=1e-8f)return false;
                float next=fraction+length/closing;if(next>1+1e-6f)return false;
                if(next<=fraction)return length<.0002f;
                fraction=Math.Min(1,next);
            }
            return false;
        }
        private static WorldCollisionVector Distance(WorldProjectileShape a,WorldProjectileShape b)
        {
            var v=a.Center-b.Center;if(v.Length<1e-5f)v=new WorldCollisionVector(1,0,0);
            var simplex=new List<WorldCollisionVector>(4);
            for(int step=0;step<64;step++)
            {
                var w=a.Support(-v)-b.Support(v);
                if(simplex.Count>0&&WorldCollisionVector.Dot(v,v)-WorldCollisionVector.Dot(v,w)<=1e-7f*Math.Max(1,WorldCollisionVector.Dot(v,v)))return v;
                bool duplicate=false;foreach(var p in simplex)if((p-w).Length<1e-7f){duplicate=true;break;}if(duplicate)return v;
                simplex.Add(w);v=Closest(simplex);
                if(v.Length<=ContactTolerance)return default;
            }
            return v;
        }
        // 最多四点的凸包中枚举面，解重心坐标后保留最近面；退化三角形/共线端点也有合法低维候选。
        private static WorldCollisionVector Closest(List<WorldCollisionVector> simplex)
        {
            double best=double.PositiveInfinity;int bestMask=0;WorldCollisionVector closest=default;
            int count=simplex.Count;
            for(int mask=1;mask<(1<<count);mask++)
            {
                var ids=new List<int>(4);for(int i=0;i<count;i++)if((mask&(1<<i))!=0)ids.Add(i);
                int dim=ids.Count-1;var origin=simplex[ids[0]];var point=origin;bool valid=true;
                if(dim>0)
                {
                    var edges=new WorldCollisionVector[dim];for(int i=0;i<dim;i++)edges[i]=simplex[ids[i+1]]-origin;
                    var matrix=new double[dim,dim+1];
                    for(int i=0;i<dim;i++){for(int j=0;j<dim;j++)matrix[i,j]=WorldCollisionVector.Dot(edges[i],edges[j]);matrix[i,dim]=-WorldCollisionVector.Dot(edges[i],origin);}
                    if(!Solve(matrix,dim))continue;double total=0;
                    for(int i=0;i<dim;i++){double weight=matrix[i,dim];if(weight< -1e-7){valid=false;break;}total+=weight;point+=edges[i]*(float)weight;}
                    if(total>1+1e-7)valid=false;
                }
                if(!valid)continue;double sq=WorldCollisionVector.Dot(point,point);
                if(sq<best){best=sq;bestMask=mask;closest=point;}
            }
            for(int i=count-1;i>=0;i--)if((bestMask&(1<<i))==0)simplex.RemoveAt(i);
            return closest;
        }
        private static bool Solve(double[,] m,int n)
        {
            for(int col=0;col<n;col++)
            {
                int pivot=col;for(int row=col+1;row<n;row++)if(Math.Abs(m[row,col])>Math.Abs(m[pivot,col]))pivot=row;
                if(Math.Abs(m[pivot,col])<1e-12)return false;
                if(pivot!=col)for(int j=col;j<=n;j++){double tmp=m[col,j];m[col,j]=m[pivot,j];m[pivot,j]=tmp;}
                double d=m[col,col];for(int j=col;j<=n;j++)m[col,j]/=d;
                for(int row=0;row<n;row++){if(row==col)continue;double factor=m[row,col];for(int j=col;j<=n;j++)m[row,j]-=factor*m[col,j];}
            }
            return true;
        }
    }
}
