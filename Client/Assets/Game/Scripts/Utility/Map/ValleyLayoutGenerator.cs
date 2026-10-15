using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game
{
    /// <summary>以局部随机源生成河谷拓扑、地表采样和稳定交互身份；不创建 Unity 对象，不写存档。</summary>
    public static partial class ValleyLayoutGenerator
    {
        /// <summary>新远征准备时生成有限次数候选；版本2提供开放缓坡，失败不返回半成品，也不更改活动地图。</summary>
        public static bool TryGenerate(int seed, int attemptIndex, ValleyGenerationProfile profile, CivilisationCatalog templates, out ValleyLayoutSaveData layout, out string error)
        {
            layout = null; error = null;
            if (profile == null || !profile.TryValidate(out error) || templates == null || attemptIndex < 0 || attemptIndex >= profile.MaxAttempts)
            { error ??= "河谷生成依赖或候选序号无效"; return false; }
            if (profile.GenerationVersion == 3) return TryGenerateOpenRiver(seed, attemptIndex, profile, templates, out layout, out error);
            var random = new System.Random(unchecked(seed + attemptIndex * 104729));
            var cells = new List<int>();
            // 开放地形允许自由横切；入口与营地的间隔必须由选址保证，不能靠两侧陡墙绕路。
            for (var selection = 0; selection < 64; selection++)
            {
                cells.Clear();
                if (!Walk(random.Next(16), random, cells)) continue;
                var dx = (cells[0] % 4 - cells[8] % 4) * 256;
                var dz = (cells[0] / 4 - cells[8] / 4) * 256;
                if (profile.GenerationVersion == 1 || dx * dx + dz * dz >= 720 * 720) break;
                cells.Clear();
            }
            if (cells.Count != 9) { error = "无法生成分散的河谷入口与营地"; return false; }
            var l = new ValleyLayoutSaveData { GenerationVersion = profile.GenerationVersion, MapId = profile.MapId, Seed = seed,
                AttemptIndex = attemptIndex, WorldSizeMeters = profile.WorldSizeMeters, HeightSampleSpacingMeters = profile.HeightSampleSpacingMeters,
                ShortcutId = "valley.shortcut.return" };
            foreach (var cell in cells)
            {
                var index = l.Route.Count;
                l.Route.Add(new ValleyPosition { X = -384 + (cell%4)*256 + random.Next(-8,9), Z = -384+(cell/4)*256+random.Next(-8,9), Y = index*2 });
            }
            var startPosition = l.Route[0].ToVector(); var destination = l.Route[8].ToVector();
            var uphill = destination - startPosition; uphill.y = 0;
            float BaseHeight(Vector3 p) => 8 + 12 * Mathf.Clamp01(Vector3.Dot(new Vector3(p.x - startPosition.x, 0, p.z - startPosition.z), uphill) / uphill.sqrMagnitude);
            if (profile.GenerationVersion >= 2)
                foreach (var p in l.Route) p.Y = BaseHeight(p.ToVector());
            var noiseX = random.Next(1024, 8192); var noiseZ = random.Next(1024, 8192);
            for(int i=0;i<l.Route.Count;i++)
            {
                var p=l.Route[i].ToVector();
                var direction=(l.Route[Math.Min(i+1,l.Route.Count-1)].ToVector()-l.Route[Math.Max(0,i-1)].ToVector()); direction.y=0;
                var normal=Vector3.Cross(Vector3.up,direction.normalized);
                l.River.Add(ValleyPosition.From(p+normal*22-Vector3.up*1.5f));
                var alt = i==0 || i==l.Route.Count-1 ? p : p-normal*60+Vector3.up*5;
                l.AlternateRoute.Add(ValleyPosition.From(alt));
                if(i==0 || i==3 || i==6) l.Fords.Add(ValleyPosition.From(p+normal*22));
                if(i>0)
                {
                    l.Edges.Add(new ValleyEdgeData {Id="river.edge."+i,From="region."+(i-1),To="region."+i,SafeRiverRoute=true});
                    l.Edges.Add(new ValleyEdgeData {Id="slope.edge."+i,From="region."+(i-1),To="region."+i,SafeRiverRoute=false});
                }
            }
            l.NominalRouteMeters=Length(l.Route);
            // 路线对应的河段保持原索引，取水点仍绑定原岸线；延伸只改变新局完整河道。
            var routeRiver = l.River.ToArray();
            if (profile.GenerationVersion >= 2)
            {
                l.River.Insert(0, ExtendRiverEnd(routeRiver[0], profile.WorldSizeMeters * .5f));
                l.River.Add(ExtendRiverEnd(routeRiver[routeRiver.Length - 1], profile.WorldSizeMeters * .5f));
            }
            // 最后两段可能共线，向前寻找实际绕行弯道，确保营地发现的连接能缩短返程。
            var shortcutStart=6;
            while(shortcutStart>0)
            {
                float detour=0;for(int i=shortcutStart+1;i<l.Route.Count;i++)detour+=Vector3.Distance(l.Route[i-1].ToVector(),l.Route[i].ToVector());
                if(detour-Vector3.Distance(l.Route[shortcutStart].ToVector(),l.Route[8].ToVector())>100)break;
                shortcutStart--;
            }
            l.Shortcut.Add(ValleyPosition.From(l.Route[shortcutStart].ToVector())); l.Shortcut.Add(ValleyPosition.From(l.Route[8].ToVector()));
            // 捷径穿过河道时保存对应浅滩；否则移除落石后仍会被深水导航阻断。
            var shortcutA=l.Shortcut[0].ToVector();var shortcutB=l.Shortcut[1].ToVector();
            var steps=Mathf.CeilToInt(Vector3.Distance(shortcutA,shortcutB)/4);
            for(int i=1;i<steps;i++)
            {
                var p=Vector3.Lerp(shortcutA,shortcutB,(float)i/steps);
                if(Distance(p,l.River,out _)<5&&!l.Fords.Any(f=>HorizontalDistance(p,f.ToVector())<12))l.Fords.Add(ValleyPosition.From(p));
            }
            var n=profile.WorldSizeMeters/profile.HeightSampleSpacingMeters+1;
            l.Heights=new float[n*n];
            for(int z=0;z<n;z++) for(int x=0;x<n;x++)
            {
                var p=new Vector3(x*4-512,0,z*4-512);
                var riverDistance=Distance(p,l.River,out var riverHeight);
                float h;
                if (profile.GenerationVersion >= 2)
                {
                    // 连续低频起伏覆盖整个内部：路线是方向提示，不再是唯一低地。
                    var hills = 18 * Mathf.PerlinNoise(noiseX + p.x * .004f, noiseZ + p.z * .004f) +
                                5 * Mathf.PerlinNoise(noiseZ + p.x * .011f, noiseX + p.z * .011f);
                    h = BaseHeight(p) + hills * Mathf.SmoothStep(0, 1, riverDistance / 70);
                    var shallow = l.Fords.Any(f => HorizontalDistance(p, f.ToVector()) < 12);
                    if (riverDistance < 5) h = Mathf.Min(h, riverHeight - (shallow ? .15f : 2.5f));
                }
                else
                {
                    h = Mathf.Min(CorridorHeight(p,l.Route,18), Mathf.Min(CorridorHeight(p,l.AlternateRoute,12), CorridorHeight(p,l.Shortcut,10)));
                    if(riverDistance<5 && !l.Fords.Any(f=>HorizontalDistance(p,f.ToVector())<12)) h=Mathf.Min(h,riverHeight-2.5f);
                }
                // 仅最外围保留不可越界陡坡；内部缓坡允许横向探索和自行选择行进方向。
                var edge=Mathf.Max(Mathf.Abs(p.x),Mathf.Abs(p.z));
                if(edge>480)
                    h = profile.GenerationVersion >= 2 ? h + (edge - 480) * 1.4f : Mathf.Max(h,60+(edge-480)*3);
                // 延伸河槽切开边缘坡面，避免水面被抬高陆地吞掉；深水碰撞仍阻止越界。
                if (profile.GenerationVersion >= 2 && edge > 480 && riverDistance < 5)
                    h = Mathf.Min(h, riverHeight - 2.5f);
                l.Heights[z*n+x]=Mathf.Clamp(h,-10,200);
            }
            Vector3 Project(Vector3 v) { v.y=l.HeightAt(v.x,v.z);return v; }
            if (profile.GenerationVersion >= 2)
            {
                // 高度采样完成后统一投影陆地路标；水面仍保留独立水位，不能一起投影。
                foreach (var point in l.Route.Concat(l.AlternateRoute).Concat(l.Shortcut).Concat(l.Fords))
                    point.Y = l.HeightAt(point.X, point.Z);
                l.NominalRouteMeters = Length(l.Route);
            }
            l.Spawn=ValleyPosition.From(Project(l.Route[0].ToVector())); l.Spawn.Y+=.1f;
            l.Camp=ValleyPosition.From(Project(l.Route[8].ToVector()));
            int serial=0;
            void Point(string templateId, Vector3 position, string region)
            {
                l.Points.Add(new ValleyPointData {Id="generated.valley.point."+(serial++).ToString("D3"),TemplateId=templateId,RegionId=region,Pose=ValleyPosition.From(Project(position))});
            }
            var start=l.Spawn.ToVector(); var camp=l.Camp.ToVector();
            Point("camp.axe", start+new Vector3(4,0,0),"region.0"); Point("camp.pickaxe",start+new Vector3(-4,0,0),"region.0");
            Point("camp.cloth",start+new Vector3(0,0,5),"region.0");
            Point("camp.fire",camp,"region.8"); Point("camp.food",camp+new Vector3(4,0,0),"region.8"); Point("camp.wood",camp+new Vector3(-4,0,0),"region.8");
            for(int i=0;i<l.Route.Count;i++)
            {
                var p=l.Route[i].ToVector(); var dir=routeRiver[i].ToVector()-p;dir.y=0;dir=dir.normalized;
                // 弯道顶点的两段河岸可能夹出孤岛；取水点放在直河段朝向主路的一侧。
                var waterSegment=Math.Min(i,l.Route.Count-2);var waterT=i==l.Route.Count-1?.8f:.5f;
                var bank=Vector3.Lerp(l.Route[waterSegment].ToVector(),l.Route[waterSegment+1].ToVector(),waterT);
                var water=Vector3.Lerp(routeRiver[waterSegment].ToVector(),routeRiver[waterSegment+1].ToVector(),waterT);
                var towardWater=water-bank;towardWater.y=0;
                // 开放地形的河槽边缘有坡度，取水交互站位退至稳定岸上，避免落在导航侵蚀区。
                Point("water.valley.ford",water-towardWater.normalized*(profile.GenerationVersion >= 2 ? 12 : 8),"region."+i);
                for(int j=0;j<3;j++) Point(j%2==0?"tree.valley.5":"rock.valley.1",p-dir*(6+j*3)+new Vector3(random.Next(-5,6),0,random.Next(-5,6)),"region."+i);
                if (profile.GenerationVersion >= 2)
                    for (var j = 0; j < 7; j++)
                    {
                        // 采集资源延伸到宽阔支路，避免所有内容仍只排在河岸一条线上。
                        for (var placement = 0; placement < 16; placement++)
                        {
                            var angle = (float)random.NextDouble() * Mathf.PI * 2;
                            var radius = random.Next(75, 121);
                            var candidate = p + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                            candidate.x = Mathf.Clamp(candidate.x, -448, 448); candidate.z = Mathf.Clamp(candidate.z, -448, 448);
                            if (Distance(candidate, l.River, out _) < 16) continue;
                            Point(j % 3 == 0 ? "rock.valley.1" : "tree.valley.5", candidate, "region." + i);
                            break;
                        }
                    }
            }
            for(int chain=0;chain<2;chain++) for(int order=0;order<2;order++)
            {
                var index=chain==0?2+order*3:3+order*3;
                var path=chain==0?l.Route:l.AlternateRoute;
                l.Clues.Add(new ValleyClueData {Id="valley.clue."+chain+"."+order,Chain=chain,Order=order,
                    MessageKey=order==0?"civilisation.clue.upstream":"civilisation.clue.camp",Pose=ValleyPosition.From(Project(path[index].ToVector())),Target=ValleyPosition.From(Project(path[Math.Min(index+2,8)].ToVector()))});
            }
            // 新局把运输队的工艺遗物分布到已有支路；旧局恢复保存清单，不补点或改变身份。
            var chapterPoints = templates.Points.Where(p => p.MapId == profile.MapId && p.Id.StartsWith("han.", StringComparison.Ordinal)).ToArray();
            for (var i = 0; i < chapterPoints.Length; i++)
            {
                var template = chapterPoints[i];
                var region = string.IsNullOrEmpty(template.ChapterId) ? 2 + i % 5 : 8;
                var position = (region == 8 ? camp + new Vector3(0, 0, -5) : l.AlternateRoute[region].ToVector());
                Point(template.Id, position, "region." + region);
                l.Points[l.Points.Count - 1].Id = "generated." + template.Id;
            }
            if (profile.GenerationVersion >= 2)
            {
                var existingResources = l.Points.ToArray();
                var barrierPosition = Vector3.Lerp(l.Shortcut[0].ToVector(), l.Shortcut[1].ToVector(), .8f);
                // 16 米采样格内各轴偏移最多5米，打散整齐行列且保留至少6米轴向间隔；保存真实采集点而非装饰树。
                // 沿用完整清单存档：已开始的稀疏旧局不会在恢复时凭空增加树木。
                for (var z = -472; z <= 472; z += 16)
                    for (var x = -472; x <= 472; x += 16)
                    {
                        var p = new Vector3(x + (float)random.NextDouble() * 10 - 5f, 0,
                            z + (float)random.NextDouble() * 10 - 5f);
                        if (Distance(p, l.River, out _) < 12 || HorizontalDistance(p, start) < 18 ||
                            HorizontalDistance(p, camp) < 18 || HorizontalDistance(p, barrierPosition) < 30) continue;
                        var dx = (l.HeightAt(p.x + 4, p.z) - l.HeightAt(p.x - 4, p.z)) / 8;
                        var dz = (l.HeightAt(p.x, p.z + 4) - l.HeightAt(p.x, p.z - 4)) / 8;
                        if (Mathf.Sqrt(dx * dx + dz * dz) >= Mathf.Tan(30 * Mathf.Deg2Rad)) continue;
                        // 与原有资源留出距离，避免两棵树或石块重叠。
                        if (existingResources.Any(point => HorizontalDistance(p, point.Pose.ToVector()) < 8)) continue;
                        var region = Enumerable.Range(0, l.Route.Count)
                            .OrderBy(i => HorizontalDistance(p, l.Route[i].ToVector())).First();
                        Point("tree.valley.5", p, "region." + region);
                        // 林区身份前缀区分新增植被，恢复旧清单时不会改变原采集物的大小与 LOD。
                        l.Points[l.Points.Count - 1].Id = "generated.valley.forest." + (serial - 1).ToString("D4");
                        l.Points[l.Points.Count - 1].Pose.Yaw = (float)random.NextDouble() * 360;
                    }
                // 覆盖率不足的候选不能进入地图事务；营地空地仍计入陆地分母。
                if (ForestCoverage(l) < .9f) { error = "河谷森林覆盖率不足90%"; return false; }
            }
            if(!ValleyLayoutValidator.TryValidate(l,profile,templates,out error)) return false;
            layout=l; return true;
        }
        /// <summary>32 米陆地小区至少有三棵真实采集树才计为林区；水面和超过30度的坡面不计可探索陆地。</summary>
        private static float ForestCoverage(ValleyLayoutSaveData layout)
        {
            var counts = new int[30,30];
            foreach (var point in layout.Points.Where(p => p.TemplateId.StartsWith("tree.")))
            {
                var x = Mathf.FloorToInt((point.Pose.X + 480) / 32);
                var z = Mathf.FloorToInt((point.Pose.Z + 480) / 32);
                if (x >= 0 && x < 30 && z >= 0 && z < 30) counts[x,z]++;
            }
            var land = 0;var forest = 0;
            for (var z = 0; z < 30; z++) for (var x = 0; x < 30; x++)
            {
                var p = new Vector3(-464 + x * 32,0,-464 + z * 32);
                if (Distance(p,layout.River,out _) < 5) continue;
                var dx = (layout.HeightAt(p.x + 4,p.z) - layout.HeightAt(p.x - 4,p.z)) / 8;
                var dz = (layout.HeightAt(p.x,p.z + 4) - layout.HeightAt(p.x,p.z - 4)) / 8;
                if (Mathf.Sqrt(dx*dx + dz*dz) >= Mathf.Tan(30*Mathf.Deg2Rad)) continue;
                land++;if(counts[x,z]>=3)forest++;
            }
            return land == 0 ? 0 : (float)forest / land;
        }
        /// <summary>将新局河流端点延伸至最近地图边界，保持端点水位；避免横穿内部产生额外长深水屏障。</summary>
        private static ValleyPosition ExtendRiverEnd(ValleyPosition endpoint, float halfSize)
        {
            var p = endpoint.ToVector();
            if (Mathf.Abs(p.x) >= Mathf.Abs(p.z)) p.x = p.x < 0 ? -halfSize : halfSize;
            else p.z = p.z < 0 ? -halfSize : halfSize;
            return ValleyPosition.From(p);
        }
        private static bool Walk(int cell,System.Random random,List<int> path)
        {
            path.Add(cell);if(path.Count==9)return true;
            var neighbors=new List<int>();var x=cell%4;var z=cell/4;
            if(x>0)neighbors.Add(cell-1);if(x<3)neighbors.Add(cell+1);if(z>0)neighbors.Add(cell-4);if(z<3)neighbors.Add(cell+4);
            for(int i=neighbors.Count-1;i>0;i--){var j=random.Next(i+1);(neighbors[i],neighbors[j])=(neighbors[j],neighbors[i]);}
            foreach(var next in neighbors)if(!path.Contains(next)&&Walk(next,random,path))return true;
            path.RemoveAt(path.Count-1);return false;
        }
        public static float Length(IReadOnlyList<ValleyPosition> path)
        {float result=0;for(int i=1;i<path.Count;i++)result+=Vector3.Distance(path[i-1].ToVector(),path[i].ToVector());return result;}
        private static float HorizontalDistance(Vector3 a,Vector3 b){a.y=0;b.y=0;return Vector3.Distance(a,b);}
        internal static float Distance(Vector3 p,IReadOnlyList<ValleyPosition> route,out float height)
        {
            float best=float.MaxValue;height=0;p.y=0;
            for(int i=1;i<route.Count;i++)
            {
                var a=route[i-1].ToVector();var b=route[i].ToVector();var ay=a.y;var by=b.y;a.y=b.y=0;
                var segment=b-a;var t=Mathf.Clamp01(Vector3.Dot(p-a,segment)/Mathf.Max(.001f,segment.sqrMagnitude));var d=Vector3.Distance(p,a+t*segment);
                if(d<best){best=d;height=Mathf.Lerp(ay,by,t);}
            }
            return best;
        }
        private static float CorridorHeight(Vector3 p,IReadOnlyList<ValleyPosition> route,float halfWidth)
        {var d=Distance(p,route,out var h);return h+Mathf.Clamp(d-halfWidth,0,30)*2.5f;}
    }
}
