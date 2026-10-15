using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

namespace Game
{
    /// <summary>创建隐藏河谷候选并验收实际导航；不改变活动远征、访问状态或背包。</summary>
    public sealed partial class ValleyMapBuilder:IValleyMapBuilder,IValleyMapProgressBuilder
    {
        private readonly IResourceLoader configuredLoader;
        public ValleyMapBuilder(){ }
        public ValleyMapBuilder(IResourceLoader loader){configuredLoader=loader;}
        public UniTask<MapContentLoadResult> BuildAsync(IMapContentHandle original,ValleyLayoutSaveData layout,ValleyGenerationProfile profile,CancellationToken token)
            => BuildWithProgressAsync(original,layout,profile,token,null);

        /// <summary>按实际完成的地形、导航和植被批次报告进度；失败由候选句柄统一回收。</summary>
        public UniTask<MapContentLoadResult> BuildWithProgressAsync(IMapContentHandle original,ValleyLayoutSaveData layout,ValleyGenerationProfile profile,CancellationToken token,Action<float> onProgress)
            => BuildWithStagesAsync(original,layout,profile,token,onProgress,null);

        /// <summary>进入真实构建步骤前报告阶段，即使进度不变也允许界面显示卡住的位置。</summary>
        public async UniTask<MapContentLoadResult> BuildWithStagesAsync(IMapContentHandle original,ValleyLayoutSaveData layout,ValleyGenerationProfile profile,CancellationToken token,Action<float> onProgress,Action<MapLoadingStage> onStage)
        {
            if(token.IsCancellationRequested)return MapContentLoadResult.Fail("河谷生成已取消");
            if(original==null||!original.IsValid||original.Context==null||!original.Context.Validate(out _)||!ValleyLayoutValidator.TryValidateData(layout,out var dataError))
                return MapContentLoadResult.Fail("河谷候选上下文或清单无效");
            var loader=configuredLoader??ResourceLoader.Current;
            var handle=new GeneratedValleyContentHandle(original,loader,layout);
            try
            {
                onStage?.Invoke(MapLoadingStage.Resources);
                await UniTask.Yield(); token.ThrowIfCancellationRequested();
                GameObject prefab=null;bool completed=false;bool accepting=true;
                handle.KitHandle=loader.LoadAssetAsync<GameObject>(profile.KitAddress,result=>{if(accepting){prefab=result;completed=true;}});
                while(!completed&&!token.IsCancellationRequested)await UniTask.Yield();
                accepting=false;token.ThrowIfCancellationRequested();
                if(prefab==null)throw new InvalidOperationException("河谷生成资源加载失败");
                // 先在禁用根下实例化资源，避免交互模板的 OnEnable 接触活动领域。
                handle.GeneratedRoot=new GameObject("GeneratedValley");handle.GeneratedRoot.SetActive(false);
                handle.GeneratedRoot.transform.SetParent(original.Context.MapContentRoot.transform,false);
                handle.KitInstance=UnityEngine.Object.Instantiate(prefab,handle.GeneratedRoot.transform);handle.KitInstance.SetActive(false);
                var kit=handle.KitInstance.GetComponent<ValleyGenerationKit>();
                if(kit==null||!kit.Validate(out var kitError))throw new InvalidOperationException("河谷生成 Kit 引用无效");
                foreach(var p in layout.Points)if(kit.FindTemplate(p.TemplateId)==null)throw new InvalidOperationException("河谷缺少交互模板："+p.TemplateId);
                foreach(Transform child in original.Context.MapContentRoot.transform)
                    if(child!=handle.GeneratedRoot.transform&&child!=original.Context.DynamicObjectRoot&&child.gameObject.activeSelf)
                    {handle.LegacyRoots.Add(child.gameObject);child.gameObject.SetActive(false);}
                onStage?.Invoke(MapLoadingStage.Terrain);
                onProgress?.Invoke(.05f);
                await BuildTerrainAsync(handle,kit,layout,token,onProgress);
                BuildRiver(handle,kit,layout);
                if(layout.GenerationVersion==3) BuildOpenRoadsAndBoundary(handle,kit,layout);
                handle.GeneratedRoot.AddComponent<MapAmbientSoundView>().Configure(
                    "AmbienceRiver", null, 0.16f, 18f, 48f, layout.River.Select(p => p.ToVector()).ToArray());
                BuildShortcut(handle,kit,layout);
                token.ThrowIfCancellationRequested();
                // 大石块必须参与候选验收及后续捷径重烘焙，不能在导航完成后才创建。
                ValleySceneryBuilder.Build(handle,kit);
                onStage?.Invoke(MapLoadingStage.Navigation);
                onProgress?.Invoke(.25f);
                handle.Navigation=await BuildNavigationAsync(handle.Sources,token,p=>onProgress?.Invoke(.25f+p*.15f));
                if(handle.Navigation==null)throw new InvalidOperationException("河谷候选导航为空");
                onStage?.Invoke(MapLoadingStage.ValidateRoute);
                var validation=NavMesh.AddNavMeshData(handle.Navigation,new Vector3(16384,0,0),Quaternion.identity);
                try
                {
                    var offset=new Vector3(16384,0,0);
                    handle.RouteLength=PathLength(layout.Spawn.ToVector()+offset,layout.Camp.ToVector()+offset);
                    if(layout.GenerationVersion<3 && (handle.RouteLength<profile.MinRouteMeters||handle.RouteLength>profile.MaxRouteMeters))throw new InvalidOperationException("河谷实际路线长度不合格："+handle.RouteLength);
                    handle.AlternateRouteLength=0;
                    for(int i=1;i<layout.AlternateRoute.Count;i++)handle.AlternateRouteLength+=PathLength(layout.AlternateRoute[i-1].ToVector()+offset,layout.AlternateRoute[i].ToVector()+offset);
                    var validatedPoints = 0;
                    if (!NavMesh.SamplePosition(layout.Spawn.ToVector()+offset,out var resourceStart,8,NavMesh.AllAreas))
                        throw new InvalidOperationException("采集路线起点缺少导航");
                    var resourcePath = new NavMeshPath();
                    foreach(var p in layout.Points)
                    {
                        // 大量林区采集点分批验收，让取消和加载界面在主线程获得执行机会。
                        if (++validatedPoints % 64 == 0) { onProgress?.Invoke(.4f+.15f*validatedPoints/layout.Points.Count); await UniTask.Yield(); token.ThrowIfCancellationRequested(); }
                        if(!NavMesh.SamplePosition(p.Pose.ToVector()+offset,out var resourceEnd,2.25f,NavMesh.AllAreas))throw new InvalidOperationException("交互点不在可通行岸边："+p.Id);
                        // 全量采集点仍须可达，但共用导航查询对象，避免每棵树重复采样起点并分配路径数组。
                        if(!NavMesh.CalculatePath(resourceStart.position,resourceEnd.position,NavMesh.AllAreas,resourcePath) || resourcePath.status!=NavMeshPathStatus.PathComplete)
                            throw new InvalidOperationException("采集点不可达："+p.Id+" ("+p.TemplateId+")");
                    }
                    foreach(var clue in layout.Clues)
                    {
                        if(!NavMesh.SamplePosition(clue.Pose.ToVector()+offset,out _,2.25f,NavMesh.AllAreas))throw new InvalidOperationException("线索不可达："+clue.Id);
                        try{PathLength(layout.Spawn.ToVector()+offset,clue.Pose.ToVector()+offset);}
                        catch(Exception e){throw new InvalidOperationException("线索 "+clue.Id+"："+e.Message);}
                    }
                }
                finally{if(validation.valid)validation.Remove();}
                // 开放捷径也必须验收真实路径，使用隔离坐标避免当前地图导航污染候选结果。
                onStage?.Invoke(MapLoadingStage.Shortcut);
                if(layout.GenerationVersion<3)
                {
                var openSources=handle.Sources.ToList();var openSource=openSources[handle.ShortcutSourceIndex];openSource.size=Vector3.zero;openSources[handle.ShortcutSourceIndex]=openSource;
                var openData=await BuildNavigationAsync(openSources,token,p=>onProgress?.Invoke(.55f+p*.1f));
                var openInstance=default(NavMeshDataInstance);
                try
                {
                    if(openData==null)throw new InvalidOperationException("捷径导航为空");
                    var offset=new Vector3(16384,0,0);openInstance=NavMesh.AddNavMeshData(openData,offset,Quaternion.identity);
                    var returnLength=PathLength(layout.Camp.ToVector()+offset,layout.Spawn.ToVector()+offset);
                    // 开放河谷允许玩家自行横切，不能为了保证捷径收益再把整张地图封成走廊。
                    if (layout.GenerationVersion == 1 ? returnLength >= handle.RouteLength - 50 : returnLength > handle.RouteLength + 5)
                        throw new InvalidOperationException("捷径返程导航不合格");
                    PathLength(layout.Shortcut[0].ToVector()+offset,layout.Shortcut[1].ToVector()+offset);
                }
                finally{if(openInstance.valid)openInstance.Remove();GeneratedValleyContentHandle.DestroyOwned(openData);}
                }
                token.ThrowIfCancellationRequested();
                handle.NavigationInstance=NavMesh.AddNavMeshData(handle.Navigation);
                onStage?.Invoke(MapLoadingStage.Points);
                await BuildPointsAsync(handle,kit,layout,token,p=>onProgress?.Invoke(.65f+p*.25f));
                if(layout.GenerationVersion==3)
                {
                    // 内容与实体碰撞齐备后重新烘焙，验收不能只检查空地形。
                    AddContentCollisionSources(handle);
                    if(handle.NavigationInstance.valid)handle.NavigationInstance.Remove();
                    GeneratedValleyContentHandle.DestroyOwned(handle.Navigation);
                    handle.Navigation=await BuildNavigationAsync(handle.Sources,token,null);
                    var instance=NavMesh.AddNavMeshData(handle.Navigation,new Vector3(16384,0,0),Quaternion.identity);
                    try { await ValidateOpenPassageAsync(handle,token); }
                    finally { if(instance.valid)instance.Remove(); }
                    handle.NavigationInstance=NavMesh.AddNavMeshData(handle.Navigation);
                }
                onStage?.Invoke(MapLoadingStage.GroundCover);
                onProgress?.Invoke(.9f);
                await ValleyVegetationBuilder.BuildGroundCoverAsync(handle, kit, token,p=>onProgress?.Invoke(.9f+p*.09f));
                token.ThrowIfCancellationRequested();
                var renderers = handle.GeneratedRoot.GetComponentsInChildren<MeshRenderer>(true);
                handle.Visibility=new ValleyVisibilityCtrl(renderers.Where(r=>r.name.StartsWith("TerrainChunk")).Cast<Renderer>().ToArray(),
                    renderers.Where(r => r.name.StartsWith("SceneryRocks_") || (!r.transform.IsChildOf(handle.KitInstance.transform) &&
                        r.GetComponentInParent<CivilisationPointView>()?.PointId.StartsWith("generated.valley.") == true)).Cast<Renderer>().ToArray(),
                    renderers.Where(r => r.name.StartsWith("ShrubChunk_") || r.name.StartsWith("GrassChunk_") || r.name.StartsWith("SceneryGrass_")).Cast<Renderer>().ToArray());
                handle.GeneratedRoot.SetActive(true); // 外层 MapContentRoot 仍隐藏，只有地图事务可以正式显示。
                onProgress?.Invoke(1f);
                return MapContentLoadResult.Ok(handle);
            }
            catch(Exception e)
            {await handle.ReleaseGeneratedAsync();return MapContentLoadResult.Fail(e is OperationCanceledException?"河谷生成已取消":e.Message);}
        }
        /// <summary>每八个地形块让出一帧，避免网格与碰撞创建占满主线程。</summary>
        private static async UniTask BuildTerrainAsync(GeneratedValleyContentHandle handle,ValleyGenerationKit kit,ValleyLayoutSaveData layout,CancellationToken token,Action<float> onProgress)
        {
            for(int cz=0;cz<16;cz++)for(int cx=0;cx<16;cx++)
            {
                var vertices=new Vector3[289];var uv=new Vector2[289];var triangles=new int[16*16*6];
                for(int z=0;z<17;z++)for(int x=0;x<17;x++){int i=z*17+x;vertices[i]=new Vector3(x*4,layout.Heights[(cz*16+z)*257+cx*16+x],z*4);uv[i]=new Vector2((cx*64+x*4)/16f,(cz*64+z*4)/16f);}
                int cursor=0;for(int z=0;z<16;z++)for(int x=0;x<16;x++){int a=z*17+x;triangles[cursor++]=a;triangles[cursor++]=a+17;triangles[cursor++]=a+1;triangles[cursor++]=a+1;triangles[cursor++]=a+17;triangles[cursor++]=a+18;}
                var mesh=new Mesh{name="ValleyChunkMesh"};mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();handle.Meshes.Add(mesh);
                ApplyRiverbedMaterial(mesh, vertices, triangles, new Vector3(cx*64-512,0,cz*64-512), layout);
                var go=new GameObject("TerrainChunk_"+cx+"_"+cz);go.transform.SetParent(handle.GeneratedRoot.transform,false);go.transform.localPosition=new Vector3(cx*64-512,0,cz*64-512);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=new[]{kit.TerrainMaterial,kit.RiverbedMaterial};go.AddComponent<MeshCollider>().sharedMesh=mesh;
                // 地形自身提供支撑音色，河岸与浅水仅覆盖表现；不改变导航和移动碰撞。
                go.AddComponent<FootstepSurface>().Kind=FootstepSurfaceKind.Dirt;
                handle.Sources.Add(new NavMeshBuildSource {shape=NavMeshBuildSourceShape.Mesh,sourceObject=mesh,transform=go.transform.localToWorldMatrix,area=0});
                if ((cz*16+cx+1)%8==0)
                {onProgress?.Invoke(.05f+.2f*(cz*16+cx+1)/256f);await UniTask.Yield();token.ThrowIfCancellationRequested();}
            }
        }
        /// <summary>地形构建时按三角形中心划分河槽与两岸泥沙；只分配材质，不移动顶点或新增碰撞面。</summary>
        private static void ApplyRiverbedMaterial(Mesh mesh, Vector3[] vertices, int[] triangles, Vector3 origin, ValleyLayoutSaveData layout)
        {
            var land = new List<int>(triangles.Length);
            var bed = new List<int>();
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var center = origin + (vertices[triangles[i]] + vertices[triangles[i+1]] + vertices[triangles[i+2]]) / 3;
                var target = ValleyLayoutGenerator.Distance(center, layout.River, out _) < 8 ? bed : land;
                target.Add(triangles[i]); target.Add(triangles[i+1]); target.Add(triangles[i+2]);
            }
            mesh.subMeshCount = 2;
            mesh.SetTriangles(land, 0); mesh.SetTriangles(bed, 1);
        }
        /// <summary>按保存河段创建匹配水面Shader协议的网格；沿用原水域与浅滩阻挡，网格随候选句柄释放。</summary>
        private static void BuildRiver(GeneratedValleyContentHandle handle,ValleyGenerationKit kit,ValleyLayoutSaveData l)
        {
            for(int i=1;i<l.River.Count;i++)
            {
                var a=l.River[i-1].ToVector();var b=l.River[i].ToVector();var segment=b-a;segment.y=0;var normal=Vector3.Cross(Vector3.up,segment.normalized)*(l.GenerationVersion==3?ValleySwimmingRules.WaterHalfWidth:5);
                // 正式河流Shader使用UV0/颜色中的三角形邻点计算法线，UV1保存流向；普通UV会令水面光照错误。
                var vertices=new[]{a-normal,b-normal,a+normal,b-normal,b+normal,a+normal};
                var neighbours=new List<Vector4>();var colours=new List<Color>();var flows=new List<Vector4>();var direction=segment.normalized;
                for(int j=0;j<6;j++)
                {
                    var q=vertices[j/3*3+(j+1)%3];var r=vertices[j/3*3+(j+2)%3];
                    neighbours.Add(new Vector4(q.x,q.y,q.z,direction.x));colours.Add(new Color(r.x,r.y,r.z,direction.z));
                    flows.Add(new Vector4(direction.x,direction.z,direction.x,direction.z));
                }
                var mesh=new Mesh{name="GeneratedRiverMesh",vertices=vertices,triangles=new[]{0,1,2,3,4,5}};
                mesh.SetUVs(0,neighbours);mesh.SetUVs(1,flows);mesh.SetColors(colours);mesh.RecalculateNormals();mesh.RecalculateBounds();handle.Meshes.Add(mesh);
                var go=new GameObject("RiverWater_"+i);go.transform.SetParent(handle.GeneratedRoot.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=kit.WaterMaterial;
                AddRiverFootsteps(handle.GeneratedRoot.transform,go.GetComponent<MeshFilter>(),a,b,i);
                var count=Mathf.CeilToInt(segment.magnitude/4);
                for(int j=0;j<count;j++)
                {
                    var p=Vector3.Lerp(a,b,(j+.5f)/count);
                    if(l.GenerationVersion==3)
                    {
                        if(l.HeightAt(p.x,p.z)>=p.y-.7f)continue;
                        var n=normal.normalized;float width=0;
                        for(float distance=0;distance<=ValleySwimmingRules.WaterHalfWidth;distance+=.5f)
                            if(l.HeightAt(p.x+n.x*distance,p.z+n.z*distance)<p.y-.7f || l.HeightAt(p.x-n.x*distance,p.z-n.z*distance)<p.y-.7f)width=distance;
                        // 仅导航不可行走区：本地CharacterController可游过，敌人不得进入深水。
                        handle.Sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.ModifierBox,
                            transform=Matrix4x4.TRS(p,Quaternion.LookRotation(segment),Vector3.one),size=new Vector3(width*2+1,12,segment.magnitude/count+1),area=1});
                        handle.DeepWaterSourceCount++;continue;
                    }
                    if(l.Fords.Any(f=>Vector2.Distance(new Vector2(f.X,f.Z),new Vector2(p.x,p.z))<12))continue;
                    var collision=new GameObject("DeepWaterCollision_"+i+"_"+j);collision.transform.SetParent(handle.GeneratedRoot.transform,false);collision.transform.position=p;collision.transform.rotation=Quaternion.LookRotation(segment);
                    var collider=collision.AddComponent<BoxCollider>();collider.size=new Vector3(10,12,segment.magnitude/count+1);
                    // 玩家使用 CharacterController，深水必须同时拥有实体与导航阻挡；浅滩没有这些边界。
                    handle.Sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.Box,transform=collision.transform.localToWorldMatrix,size=collider.size,area=1});handle.DeepWaterSourceCount++;
                }
            }
        }
        /// <summary>与实际河段一起生成并由候选根释放；岸边两米泥沙带、浅水投影和脚底高度独立于导航。</summary>
        private static void AddRiverFootsteps(Transform root,MeshFilter water,Vector3 a,Vector3 b,int index)
        {
            var segment=b-a;segment.y=0;
            if(segment.sqrMagnitude<0.0001f)return;
            var bank=new GameObject("FootstepMud_"+index);bank.transform.SetParent(root,false);
            bank.transform.position=(a+b)*.5f;bank.transform.rotation=Quaternion.LookRotation(segment);
            var bankBox=bank.AddComponent<BoxCollider>();bankBox.isTrigger=true;
            // 现有水面宽十米，向两岸各延伸两米；高盒只粗筛，必须先命中已标记的真实地面。
            bankBox.size=new Vector3(14,128,segment.magnitude);
            var mud=bank.AddComponent<FootstepSurface>();mud.Kind=FootstepSurfaceKind.Mud;mud.IsRegion=true;
            mud.Priority=10;mud.RegionId="valley/mud/"+index;
            var area=new GameObject("FootstepWater_"+index);area.transform.SetParent(water.transform,false);
            var bounds=water.sharedMesh.bounds;
            var waterBox=area.AddComponent<BoxCollider>();waterBox.isTrigger=true;waterBox.center=bounds.center;
            waterBox.size=new Vector3(bounds.size.x+.02f,bounds.size.y+1.2f,bounds.size.z+.02f);
            var wet=area.AddComponent<FootstepSurface>();wet.Kind=FootstepSurfaceKind.Water;wet.IsRegion=true;
            wet.Priority=20;wet.RegionId="valley/water/"+index;wet.SurfaceMesh=water;wet.CheckSurfaceHeight=true;
        }
        private static void BuildShortcut(GeneratedValleyContentHandle handle,ValleyGenerationKit kit,ValleyLayoutSaveData l)
        {
            if(l.GenerationVersion==3){handle.ShortcutOpen=true;handle.ShortcutSourceIndex=-1;return;}
            var a=l.Shortcut[0].ToVector();var b=l.Shortcut[1].ToVector();var p=Vector3.Lerp(a,b,.8f);p.y=l.HeightAt(p.x,p.z)+4;
            var go=new GameObject("ReturnShortcutBarrier");go.transform.SetParent(handle.GeneratedRoot.transform,false);go.transform.position=p;go.transform.rotation=Quaternion.LookRotation(b-a);
            var collider=go.AddComponent<BoxCollider>();collider.size=new Vector3(40,10,8);handle.ShortcutBlocker=go;
            handle.ShortcutSourceIndex=handle.Sources.Count;handle.Sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.Box,transform=go.transform.localToWorldMatrix,size=collider.size,area=1});
            handle.ClosedShortcutSource=handle.Sources[handle.ShortcutSourceIndex];
            // 使用项目石块表现标明封堵，避免只有碰撞而看不到原因。
            var rock=kit.FindTemplate("rock.valley.1")?.HarvestedVisualRoot;
            if(rock!=null)for(int i=0;i<8;i++)
            {var visual=UnityEngine.Object.Instantiate(rock,go.transform);visual.transform.localPosition=new Vector3(-17.5f+i*5,-4,0);visual.transform.localScale*=3;visual.SetActive(true);}
        }
        /// <summary>隐藏候选内分批创建真实交互点；取消时由句柄回收已创建部分，尚未正式绑定领域。</summary>
        private static async UniTask BuildPointsAsync(GeneratedValleyContentHandle handle,ValleyGenerationKit kit,ValleyLayoutSaveData l,CancellationToken token,Action<float> onProgress)
        {
            var created = 0;
            foreach(var p in l.Points)
            {
                var view=UnityEngine.Object.Instantiate(kit.FindTemplate(p.TemplateId),handle.GeneratedRoot.transform);view.name=p.Id;view.transform.position=p.Pose.ToVector();view.transform.rotation=Quaternion.Euler(0,p.Pose.Yaw,0);view.ConfigureGenerated(CivilisationPointKind.Search,p.Id);view.gameObject.SetActive(true);
                if (p.TemplateId.StartsWith("tree.")) ValleyVegetationBuilder.ApplyTree(view, p, l, kit);
                GroundStoryVisuals(view, l, handle);
                if (++created % 32 == 0) { onProgress?.Invoke((float)created/l.Points.Count); await UniTask.Yield(); token.ThrowIfCancellationRequested(); }
            }
            if(l.GenerationVersion<3)
            {
                var prep=UnityEngine.Object.Instantiate(kit.PreparationTemplate,handle.GeneratedRoot.transform);prep.transform.position=l.Spawn.ToVector()+new Vector3(-2,0,-3);prep.gameObject.SetActive(true);
                var back=UnityEngine.Object.Instantiate(kit.ReturnTemplate,handle.GeneratedRoot.transform);back.transform.position=l.Spawn.ToVector()+new Vector3(2,0,-3);back.gameObject.SetActive(true);
            }
            UnityEngine.Object.Instantiate(kit.CampfirePrefab,l.Camp.ToVector(),Quaternion.identity,handle.GeneratedRoot.transform);
            var shelterPosition=l.Camp.ToVector()+new Vector3(-5,0,4);shelterPosition.y=l.HeightAt(shelterPosition.x,shelterPosition.z);
            UnityEngine.Object.Instantiate(kit.SceneryShelterPrefab!=null?kit.SceneryShelterPrefab:kit.AwningPrefab,shelterPosition,Quaternion.identity,handle.GeneratedRoot.transform);
            foreach(var clue in l.Clues)
            {
                var view=UnityEngine.Object.Instantiate(kit.FindClueTemplate(clue.Order),handle.GeneratedRoot.transform);
                view.transform.position=clue.Pose.ToVector();view.ConfigureGenerated(CivilisationPointKind.Clue,clue.Id);
                GroundStoryVisuals(view, l, handle);
                view.ConfigureGeneratedPrompt("civilisation.point.clue");view.gameObject.SetActive(true);
            }
            if(l.GenerationVersion==3)return;
            var shortcut=UnityEngine.Object.Instantiate(kit.PreparationTemplate,handle.GeneratedRoot.transform);
            shortcut.transform.position=l.Camp.ToVector()+new Vector3(2,0,2);shortcut.ConfigureGenerated(CivilisationPointKind.Shortcut,l.ShortcutId);
            shortcut.ConfigureGeneratedPrompt("civilisation.point.shortcut");shortcut.gameObject.SetActive(true);
        }
        /// <summary>新实例定位后调用一次；按保存地形调整每组布景底座，保留台上证物高度，不改碰撞和领域状态。</summary>
        private static void GroundStoryVisuals(CivilisationPointView view, ValleyLayoutSaveData layout, GeneratedValleyContentHandle handle)
        {
            var scenery=view.transform.Find("StoryScene");
            if(scenery==null)return;
            var surface=view.transform.Find("GroundSurface");
            // 完整运输现场允许装饰平移三米避树；交互身份、采集树和导航均保持保存位置。
            var nearbyTrees=layout.Points.Where(p=>p.TemplateId.StartsWith("tree.") &&
                (new Vector2(p.Pose.X-view.transform.position.x,p.Pose.Z-view.transform.position.z)).sqrMagnitude<100)
                .Select(p=>p.Pose.ToVector()).ToArray();
            if(nearbyTrees.Length>0)
            {
                var propRenderers=scenery.Cast<Transform>().Select(p=>p.GetComponentsInChildren<Renderer>(true)).Where(r=>r.Length>0).ToArray();
                var original=scenery.localRotation;var originalPosition=scenery.localPosition;
                var best=original;var bestPosition=originalPosition;var bestPenalty=float.PositiveInfinity;
                for(int shift=0;shift<(surface!=null?9:1);shift++)
                for(int turn=0;turn<8;turn++)
                {
                    var offset=shift==0?Vector3.zero:Quaternion.Euler(0,(shift-1)*45,0)*Vector3.forward*3;
                    scenery.localPosition=originalPosition+offset;
                    scenery.localRotation=original*Quaternion.Euler(0,turn*45,0);float penalty=0;
                    foreach(var renderers in propRenderers)
                    {
                        var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                        foreach(var tree in nearbyTrees)
                        {
                            var position=tree;position.y=bounds.center.y;
                            var overlap=Mathf.Max(0,(surface!=null?1.25f:.8f)-Vector3.Distance(position,bounds.ClosestPoint(position)));
                            penalty+=overlap*overlap;
                        }
                    }
                    if(surface!=null)foreach(var tree in nearbyTrees)
                    {
                        var center=scenery.TransformPoint(new Vector3(-.3f,0,.2f));center.y=tree.y;
                        var overlap=Mathf.Max(0,3.5f-Vector3.Distance(tree,center));penalty+=overlap*overlap*2;
                        // 入口车辙同样需要避开树干，否则道具虽不穿树，接近现场的视觉路线仍被堵住。
                        var local=scenery.InverseTransformPoint(tree);var point=new Vector2(local.x,local.z);
                        var begin=new Vector2(.9f,-4.6f);var end=new Vector2(-.2f,-.2f);var direction=end-begin;
                        var closest=begin+direction*Mathf.Clamp01(Vector2.Dot(point-begin,direction)/direction.sqrMagnitude);
                        var entryOverlap=Mathf.Max(0,1.85f-Vector2.Distance(point,closest));penalty+=entryOverlap*entryOverlap*5;
                    }
                    if(penalty<bestPenalty){bestPenalty=penalty;best=scenery.localRotation;bestPosition=scenery.localPosition;}
                    if(penalty==0)break;
                }
                scenery.localRotation=best;scenery.localPosition=bestPosition;
            }
            foreach(Transform prop in scenery)Project(prop);
            var evidence=view.transform.Find("EvidenceRoot");
            if(evidence!=null)Project(evidence);
            // 裸土、车辙共享现场朝向，逐顶点贴地；绝不修改 Prefab 共享网格。
            if(surface!=null)
            {
                surface.localRotation=scenery.localRotation;surface.localPosition=scenery.localPosition;
                foreach(var filter in surface.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);
                    handle.Meshes.Add(mesh); // 先交给候选句柄；后续失败也能走统一回滚。
                    var vertices=mesh.vertices;
                    for(int i=0;i<vertices.Length;i++)
                    {
                        var world=filter.transform.TransformPoint(vertices[i]);
                        var lift=world.y-view.transform.position.y;
                        world.y=layout.HeightAt(world.x,world.z)+lift;
                        vertices[i]=filter.transform.InverseTransformPoint(world);
                    }
                    mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();filter.sharedMesh=mesh;
                }
            }
            void Project(Transform prop)
            {
                var position=prop.position;
                position.y+=layout.HeightAt(position.x,position.z)-view.transform.position.y;
                prop.position=position;
            }
        }
        /// <summary>异步烘焙期间允许UI刷新；取消也必须等待原生操作结束后才能销毁数据。</summary>
        private static async UniTask<NavMeshData> BuildNavigationAsync(List<NavMeshBuildSource> sources,CancellationToken token,Action<float> onProgress)
        {
            var settings=NavMesh.GetSettingsByIndex(0);
            var data=new NavMeshData(settings.agentTypeID);
            AsyncOperation operation=null;
            try
            {
                operation=NavMeshBuilder.UpdateNavMeshDataAsync(data,settings,sources,new Bounds(Vector3.zero,new Vector3(1024,500,1024)));
                while(!operation.isDone)
                {
                    if(!token.IsCancellationRequested)onProgress?.Invoke(Mathf.Min(operation.progress,.99f));
                    await UniTask.Yield();
                }
                token.ThrowIfCancellationRequested();onProgress?.Invoke(1f);return data;
            }
            catch
            {
                // 进度订阅者抛异常时也不能释放仍被原生烘焙使用的数据。
                while(operation!=null&&!operation.isDone)await UniTask.Yield();
                GeneratedValleyContentHandle.DestroyOwned(data);throw;
            }
        }
        private static float PathLength(Vector3 a,Vector3 b)
        {
            if(!NavMesh.SamplePosition(a,out var start,8,NavMesh.AllAreas)||!NavMesh.SamplePosition(b,out var end,8,NavMesh.AllAreas))throw new InvalidOperationException("路线端点缺少导航");
            var path=new NavMeshPath();if(!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)throw new InvalidOperationException("河谷路线不连通");
            float length=0;for(int i=1;i<path.corners.Length;i++)length+=Vector3.Distance(path.corners[i-1],path.corners[i]);return length;
        }
        /// <summary>导航更新只作用于指定仍有效句柄；失败恢复旧阻挡，不修改探索状态。</summary>
        public async UniTask<MapControllerResult> SetShortcutOpenAsync(GeneratedValleyContentHandle handle,string shortcutId,bool isOpen,CancellationToken token)
        {
            if(handle==null||!handle.IsValid||handle.NavigationUpdating||handle.Layout.ShortcutId!=shortcutId||token.IsCancellationRequested)return MapControllerResult.Fail("捷径地图身份无效");
            if(handle.Layout.GenerationVersion==3)return MapControllerResult.Ok();
            if(handle.ShortcutOpen==isOpen)return MapControllerResult.Ok();
            var old=handle.Sources[handle.ShortcutSourceIndex];var replacement=handle.ClosedShortcutSource;if(isOpen)replacement.size=Vector3.zero;
            handle.NavigationUpdating=true;
            handle.Sources[handle.ShortcutSourceIndex]=replacement;
            try
            {
                var operation=NavMeshBuilder.UpdateNavMeshDataAsync(handle.Navigation,NavMesh.GetSettingsByIndex(0),handle.Sources,new Bounds(Vector3.zero,new Vector3(1024,500,1024)));
                while(!operation.isDone)await UniTask.Yield();
                if(!handle.IsValid||token.IsCancellationRequested)throw new OperationCanceledException();
                handle.ShortcutBlocker.SetActive(!isOpen);handle.ShortcutOpen=isOpen;return MapControllerResult.Ok();
            }
            catch(Exception e)
            {
                handle.Sources[handle.ShortcutSourceIndex]=old;
                if(handle.IsValid)
                {
                    var rollback=NavMeshBuilder.UpdateNavMeshDataAsync(handle.Navigation,NavMesh.GetSettingsByIndex(0),handle.Sources,new Bounds(Vector3.zero,new Vector3(1024,500,1024)));
                    while(!rollback.isDone)await UniTask.Yield();
                }
                return MapControllerResult.Fail(e.Message);
            }
            finally{handle.NavigationUpdating=false;}
        }
    }
}
