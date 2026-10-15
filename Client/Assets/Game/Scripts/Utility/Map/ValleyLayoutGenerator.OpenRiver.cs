using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game
{
    public static partial class ValleyLayoutGenerator
    {
        /// <summary>版本3先确定无自交河道与双岸道路，再采样连续缓坡，最后选择内容和安全出生；不修改旧局。</summary>
        private static bool TryGenerateOpenRiver(int seed, int attempt, ValleyGenerationProfile profile, CivilisationCatalog templates,
            out ValleyLayoutSaveData layout, out string error)
        {
            layout = null; error = null;
            var r = new System.Random(unchecked(seed + attempt * 104729));
            float R(float a, float b) => a + (b-a)*(float)r.NextDouble();
            var l = new ValleyLayoutSaveData {GenerationVersion=3,MapId=profile.MapId,Seed=seed,AttemptIndex=attempt,
                WorldSizeMeters=1024,HeightSampleSpacingMeters=4,ShortcutId="valley.shortcut.return"};
            // 使用独立抽样源，不改变地形序列，也不以玩家装备反馈难度。
            l.EnemyCount=new System.Random(unchecked(seed*31+attempt*104729)).Next(profile.MinEnemies,profile.MaxEnemies+1);
            var phase=R(0,6.28f);var amplitude=R(40,100);var bias=R(-90,90);var frequency=R(.0035f,.006f);
            float RiverX(float z) => bias + amplitude*Mathf.Sin(z*frequency+phase);
            // z单调保证主河不分叉、不回绕形成封闭孤岛，折线坐标与水位均进入存档。
            for(int z=-512;z<=512;z+=16) l.River.Add(new ValleyPosition{X=RiverX(z),Y=2,Z=z});
            var fordCount=r.Next(2,4);
            for(int i=0;i<fordCount;i++)
            {
                var z=-340+i*680f/(fordCount-1)+R(-38,38);
                l.Fords.Add(new ValleyPosition{X=RiverX(z),Y=1.8f,Z=z});
            }
            var nx=R(100,900);var nz=R(100,900);
            l.Heights=new float[257*257];
            for(int z=0;z<257;z++) for(int x=0;x<257;x++)
            {
                var p=new Vector3(x*4-512,0,z*4-512);
                var d=Distance(p,l.River,out var water);
                var fordDistance=l.Fords.Min(f=>Mathf.Abs(p.z-f.Z));
                var ground=3.8f+3*Mathf.PerlinNoise(nx+p.x*.002f,nz+p.z*.002f)+Mathf.PerlinNoise(nz+p.x*.005f,nx+p.z*.005f);
                // 河床到陆地用36米连续过渡，深水也能沿两岸缓坡上岸；浅滩横贯河宽。
                var bed=water-Mathf.Lerp(.2f,3f,Mathf.SmoothStep(0,1,Mathf.Clamp01((fordDistance-16)/24)));
                l.Heights[z*257+x]=Mathf.Lerp(bed,ground,Mathf.SmoothStep(0,1,Mathf.Clamp01((d-3)/36)));
            }
            ValleyPosition Land(float x,float z) => new ValleyPosition{X=x,Z=z,Y=l.HeightAt(x,z)};
            for(int i=0;i<9;i++)
            {
                var z=-416+i*104;
                l.Route.Add(Land(RiverX(z)-R(42,58),z));
                l.AlternateRoute.Add(Land(RiverX(z)+R(42,58),z));
                if(i>0)
                {
                    l.Edges.Add(new ValleyEdgeData{Id="bank.left."+i,From="region."+(i-1),To="region."+i,SafeRiverRoute=true});
                    l.Edges.Add(new ValleyEdgeData{Id="bank.right."+i,From="region."+(i-1),To="region."+i});
                }
            }
            foreach(var f in l.Fords)
                l.BranchRoads.Add(new List<ValleyPosition>{Land(f.X-58,f.Z),Land(f.X,f.Z),Land(f.X+58,f.Z)});
            // 同岸分支回接另一个河段，提供两个方向进入及换路返程，而非仅保存抽象图边。
            var loopStart=r.Next(1,4);var side=r.Next(2)==0?-1:1;
            var bank=side<0?l.Route:l.AlternateRoute;
            var outerX=RiverX(bank[loopStart].Z)+side*R(130,185);
            l.BranchRoads.Add(new List<ValleyPosition>{bank[loopStart],Land(outerX,bank[loopStart].Z+R(-20,20)),
                Land(outerX+R(-20,20),bank[loopStart+3].Z+R(-20,20)),bank[loopStart+3]});
            l.Shortcut=l.BranchRoads[0].Select(p=>ValleyPosition.From(p.ToVector())).ToList();
            l.NominalRouteMeters=Length(l.Route);
            var campSide=r.Next(2)==0?l.Route:l.AlternateRoute;
            var campIndex=r.Next(2,7);var c=campSide[campIndex];
            l.Camp=Land(c.X+(campSide==l.Route?-12:12),c.Z);
            var exitPath=r.Next(2)==0?l.Route:l.AlternateRoute;var exitIndex=r.Next(1,8);
            var exit=exitPath[exitIndex];l.Extraction=Land(exit.X+(exitPath==l.Route?-7:7),exit.Z+R(-12,12));
            int serial=0;
            void Point(string template,ValleyPosition p,int region,string id=null)
            {l.Points.Add(new ValleyPointData{Id=id??"generated.valley.point."+(serial++).ToString("D4"),TemplateId=template,RegionId="region."+region,Pose=p});}
            foreach(var template in new[]{"camp.fire","camp.food","camp.wood","camp.axe","camp.pickaxe","camp.cloth"})
                Point(template,Land(l.Camp.X+R(-7,7),l.Camp.Z+R(-7,7)),campIndex);
            var chapterTemplates=templates.Points.Where(p=>p.MapId==l.MapId&&p.Id.StartsWith("han.",StringComparison.Ordinal)).ToArray();
            var optional=chapterTemplates.Where(p=>string.IsNullOrEmpty(p.ChapterId)).OrderBy(_=>r.Next()).ToArray();
            var optionalCount=optional.Length==0?0:r.Next(Math.Min(2,optional.Length),optional.Length+1);
            var chapter=chapterTemplates.Where(p=>!string.IsNullOrEmpty(p.ChapterId)).Concat(optional.Take(optionalCount)).ToArray();
            // 必需凭证全部保留，可选工艺只抽已有模板；稳定身份不随装饰或抽样顺序改变。
            for(int i=0;i<chapter.Length;i++)
            {
                var index=r.Next(1,8);var path=i%2==0?l.Route:l.AlternateRoute;var p=path[index];
                var outward=path==l.Route?-1:1;
                Point(chapter[i].Id,Land(p.X+outward*R(15,30),p.Z+R(-25,25)),index,"generated."+chapter[i].Id);
            }
            for(int i=0;i<9;i++)
            {
                var z=l.Route[i].Z;
                Point("water.valley.ford",Land(RiverX(z)+(i%2==0?-26:26),z),i);
            }
            for(int chain=0;chain<2;chain++) for(int order=0;order<2;order++)
            {
                var path=chain==0?l.Route:l.AlternateRoute;var index=2+order*3;
                l.Clues.Add(new ValleyClueData{Id="valley.clue."+chain+"."+order,Chain=chain,Order=order,
                    MessageKey=order==0?"civilisation.clue.upstream":"civilisation.clue.camp",Pose=Land(path[index].X,path[index].Z),Target=l.Camp});
            }
            // 出生与道路保持可见距离；远离营地、遗物敌人和撤离，任一岸均有候选。
            for(int candidate=0;candidate<128;candidate++)
            {
                var path=r.Next(2)==0?l.Route:l.AlternateRoute;var i=r.Next(1,8);var p=path[i];
                var spawn=Land(p.X+(path==l.Route?-1:1)*R(18,28),p.Z+R(-30,30));
                if(l.Points.Any(q=>HorizontalDistance(q.Pose.ToVector(),spawn.ToVector())<80)||HorizontalDistance(l.Extraction.ToVector(),spawn.ToVector())<30)continue;
                spawn.Y+=.1f;spawn.Yaw=R(0,360);l.Spawn=spawn;break;
            }
            if(l.Spawn==null){error="未找到安全陆地出生点";return false;}
            var approach=SpawnApproach(l);
            var clearings=l.Points.Where(p=>!p.TemplateId.StartsWith("water.")).Select(p=>p.Pose).Concat(l.Clues.Select(c=>c.Pose)).ToArray();
            for(int z=-500;z<=500;z+=16) for(int x=-500;x<=500;x+=16)
            {
                var p=Land(x+R(-4,4),z+R(-4,4));
                if(Distance(p.ToVector(),l.River,out _)<34||RoadDistance(l,p.ToVector())<8||
                    HorizontalDistance(p.ToVector(),l.Spawn.ToVector())<18||HorizontalDistance(p.ToVector(),l.Extraction.ToVector())<14||
                    Distance(p.ToVector(),approach,out _)<7 ||
                    clearings.Any(q=>HorizontalDistance(q.ToVector(),p.ToVector())<12))continue;
                Point(r.Next(12)==0?"rock.valley.1":"tree.valley.5",p,r.Next(9),"generated.valley.forest."+(serial++).ToString("D4"));
                p.Yaw=R(0,360);
            }
            RotateCompleteLayout(l,new System.Random(unchecked(seed*17+attempt)).Next(4));
            if(!ValleyLayoutValidator.TryValidate(l,profile,templates,out error))return false;
            layout=l;return true;
        }
        /// <summary>道路留空、导航及地被共用的折线距离，单位米；不包含水域本身。</summary>
        internal static float RoadDistance(ValleyLayoutSaveData l,Vector3 p)
        {
            var d=Mathf.Min(Distance(p,l.Route,out _),Distance(p,l.AlternateRoute,out _));
            if(l.GenerationVersion>=3)foreach(var branch in l.BranchRoads)d=Mathf.Min(d,Distance(p,branch,out _));
            return Mathf.Min(d,Distance(p,l.Shortcut,out _));
        }
        /// <summary>从保存道路投影出生的可视接近带；只作林木/布景留白，不追加工具门槛或免费物资。</summary>
        internal static List<ValleyPosition> SpawnApproach(ValleyLayoutSaveData l)
        {
            var position=l.Spawn.ToVector();position.y=0;var closest=Vector3.zero;var best=float.PositiveInfinity;
            var paths=new List<List<ValleyPosition>>{l.Route,l.AlternateRoute};paths.AddRange(l.BranchRoads);
            foreach(var path in paths)for(int i=1;i<path.Count;i++)
            {
                var a=path[i-1].ToVector();var b=path[i].ToVector();a.y=b.y=0;var delta=b-a;
                var p=a+delta*Mathf.Clamp01(Vector3.Dot(position-a,delta)/Mathf.Max(.001f,delta.sqrMagnitude));
                var distance=(p-position).sqrMagnitude;if(distance>=best)continue;best=distance;closest=p;
            }
            closest.y=l.HeightAt(closest.x,closest.z);
            return new List<ValleyPosition>{l.Spawn,ValleyPosition.From(closest)};
        }
        /// <summary>按90度整格旋转整张已完成清单，河流可贯穿任意一对边界；网格及共享坐标只旋转一次。</summary>
        private static void RotateCompleteLayout(ValleyLayoutSaveData l,int turns)
        {
            if(turns==0)return;
            var positions=l.Route.Concat(l.AlternateRoute).Concat(l.River).Concat(l.Fords).Concat(l.Shortcut)
                .Concat(l.BranchRoads.SelectMany(p=>p)).Concat(l.Points.Select(p=>p.Pose))
                .Concat(l.Clues.SelectMany(c=>new[]{c.Pose,c.Target})).Concat(new[]{l.Spawn,l.Camp,l.Extraction}).Distinct();
            foreach(var p in positions)
            {
                var x=p.X;var z=p.Z;
                if(turns==1){p.X=z;p.Z=-x;}else if(turns==2){p.X=-x;p.Z=-z;}else{p.X=-z;p.Z=x;}
                p.Yaw=(p.Yaw+turns*90)%360;
            }
            var heights=new float[l.Heights.Length];
            for(int z=0;z<257;z++)for(int x=0;x<257;x++)
            {
                var nx=turns==1?z:turns==2?256-x:256-z;
                var nz=turns==1?256-x:turns==2?256-z:x;
                heights[nz*257+nx]=l.Heights[z*257+x];
            }
            l.Heights=heights;
        }
    }
}
