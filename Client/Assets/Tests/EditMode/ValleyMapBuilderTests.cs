using System;
using System.Linq;
using System.Collections;
using UnityEngine.TestTools;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ValleyMapBuilderTests
    {
        private GameObject root;
        private TestHandle baseHandle;
        private FakeLoader loader;
        private MapContentLoadResult built;
        private static Type BuilderType()
        { var type=typeof(ExpeditionModel).Assembly.GetType("Game.ValleyMapBuilder");Assert.That(type,Is.Not.Null,"候选构建器尚未实现");return type; }
        [SetUp] public void SetUp()
        {
            root=new GameObject("ValleyCandidateTest");var context=root.AddComponent<MapContext>();var dynamicRoot=new GameObject("RuntimeUnitRoot");dynamicRoot.transform.SetParent(root.transform);
            var fields=new SerializedObject(context);fields.FindProperty("mapContentRoot").objectReferenceValue=root;fields.FindProperty("dynamicObjectRoot").objectReferenceValue=dynamicRoot.transform;fields.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(false);baseHandle=new TestHandle(context);loader=new FakeLoader();
        }
        [TearDown] public void TearDown()
        {
            if(built?.Handle!=null) built.Handle.GetType().GetMethod("ReleaseGeneratedAsync").Invoke(built.Handle,null);
            if(root!=null)UnityEngine.Object.DestroyImmediate(root);
        }
        private async Task<MapContentLoadResult> Build(CancellationToken token=default,int seed=37,ValleyLayoutSaveData savedLayout=null,Action<float> onProgress=null,Action<MapLoadingStage> onStage=null)
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/"+n+".bytes").bytes)));
            Assert.That(provider.TryGetCivilisationCatalog(out var templates,out _),Is.True);
            Assert.That(provider.TryGetValleyGenerationProfile(MapIds.CivilisationValley,out var profile,out _),Is.True);
            var builder=Activator.CreateInstance(BuilderType(),new object[]{loader});
            for(int attempt=0;attempt<8;attempt++)
            {
                var layout = savedLayout;
                if (layout == null) { Assert.That(ValleyLayoutGenerator.TryGenerate(seed,attempt,profile,templates,out layout,out var error),Is.True,error); }
                built=await (onStage!=null
                    ? (UniTask<MapContentLoadResult>)BuilderType().GetMethod("BuildWithStagesAsync").Invoke(builder,new object[]{baseHandle,layout,profile,token,onProgress,onStage})
                    : onProgress==null
                    ? (UniTask<MapContentLoadResult>)BuilderType().GetMethod("BuildAsync").Invoke(builder,new object[]{baseHandle,layout,profile,token})
                    : (UniTask<MapContentLoadResult>)BuilderType().GetMethod("BuildWithProgressAsync").Invoke(builder,new object[]{baseHandle,layout,profile,token,onProgress})).AsTask();
                if(built.Success||token.IsCancellationRequested||loader.Missing)return built;
                if (savedLayout != null) return built;
            }
            return built;
        }
        [UnityTest] public IEnumerator LoadingStages_MatchActualTerrainAndTreePreparation() => Run_LoadingStages().ToCoroutine();
        private async UniTask Run_LoadingStages()
        {
            var stages=new System.Collections.Generic.List<MapLoadingStage>();
            var result=await Build(onStage:stage=>
            {
                stages.Add(stage);
                if(stage==MapLoadingStage.Navigation)
                    Assert.That(root.GetComponentsInChildren<Transform>(true).Count(t=>t.name.StartsWith("TerrainChunk_")),Is.EqualTo(256));
                if(stage==MapLoadingStage.GroundCover)
                    Assert.That(root.GetComponentsInChildren<CivilisationPointView>(true).Count(v=>v.PointId?.StartsWith("generated.valley.forest.")==true),Is.GreaterThan(3000));
            });
            Assert.That(result.Success,Is.True,result.Error);
            Assert.That(stages,Is.EqualTo(new[]{MapLoadingStage.Resources,MapLoadingStage.Terrain,MapLoadingStage.Navigation,
                MapLoadingStage.ValidateRoute,MapLoadingStage.Shortcut,MapLoadingStage.Points,MapLoadingStage.GroundCover}));
        }
        [UnityTest] public IEnumerator LoadingProgress_ReportsTerrainNavigationAndVegetationBatches() => Run_LoadingProgress().ToCoroutine();
        private async UniTask Run_LoadingProgress()
        {
            var progress=new System.Collections.Generic.List<float>();
            var frames=new System.Collections.Generic.HashSet<int>();
            var result=await Build(onProgress:p=>{progress.Add(p);frames.Add(Time.frameCount);});
            Assert.That(result.Success,Is.True,result.Error);
            Assert.That(progress.Exists(p=>p>.05f&&p<.25f),Is.True,"地形应按批次推进");
            Assert.That(progress.Exists(p=>p>=.25f&&p<=.65f),Is.True,"导航准备应纳入进度");
            Assert.That(progress.Exists(p=>p>.65f&&p<.9f),Is.True,"实际采集点创建应按批次推进");
            Assert.That(progress.Exists(p=>p>.9f&&p<.99f),Is.True,"草灌准备应按批次推进");
            Assert.That(progress.Last(),Is.EqualTo(1f));
            Assert.That(frames.Count,Is.GreaterThan(10),"生成准备期间必须允许界面刷新");
        }
        private static float Number(IMapContentHandle handle,string name)=>(float)handle.GetType().GetProperty(name).GetValue(handle);
        [UnityTest] public IEnumerator SceneryRocks_RequireNavigationDetourAfterShortcutRebake() => Run_RockNavigation().ToCoroutine();
        private async UniTask Run_RockNavigation()
        {
            var result=await Build();Assert.That(result.Success,Is.True,result.Error);
            var handle=(GeneratedValleyContentHandle)result.Handle;
            var rocks=root.GetComponentsInChildren<MeshCollider>(true).Where(c=>c.name=="SceneryRockBlocker").ToArray();
            Assert.That(rocks.Length,Is.GreaterThan(0));
            var checkedCenters=new System.Collections.Generic.List<Vector3>();
            var riverDistance=typeof(ValleyLayoutGenerator).GetMethod("Distance",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(var rock in rocks)
            {
                var center=rock.transform.TransformPoint(rock.sharedMesh.bounds.center);
                // 只选可从两侧接近的岸上石头，排除原本不可行走的深水。
                if((float)riverDistance.Invoke(null,new object[]{center,handle.Layout.River,0f})<8)continue;
                var extent=rock.sharedMesh.bounds.extents.x+2;
                var a=center+Vector3.left*extent;var b=center+Vector3.right*extent;
                a.y=handle.Layout.HeightAt(a.x,a.z);b.y=handle.Layout.HeightAt(b.x,b.z);
                if(!UnityEngine.AI.NavMesh.SamplePosition(a,out var start,.5f,UnityEngine.AI.NavMesh.AllAreas)||
                   !UnityEngine.AI.NavMesh.SamplePosition(b,out var end,.5f,UnityEngine.AI.NavMesh.AllAreas))continue;
                center.y=handle.Layout.HeightAt(center.x,center.z);
                Assert.That(UnityEngine.AI.NavMesh.SamplePosition(center,out _,.1f,UnityEngine.AI.NavMesh.AllAreas),Is.False,"石头中心不能保留可走导航");
                Assert.That(UnityEngine.AI.NavMesh.Raycast(start.position,end.position,out _,UnityEngine.AI.NavMesh.AllAreas),Is.True,"导航直线必须被石头截断");
                var path=new UnityEngine.AI.NavMeshPath();
                Assert.That(UnityEngine.AI.NavMesh.CalculatePath(start.position,end.position,UnityEngine.AI.NavMesh.AllAreas,path),Is.True);
                Assert.That(path.status,Is.EqualTo(UnityEngine.AI.NavMeshPathStatus.PathComplete),"两侧仍应能绕行：stone="+center+", start="+start.position+", end="+end.position);
                Assert.That(path.corners.Length,Is.GreaterThan(2));
                checkedCenters.Add(center);
            }
            Assert.That(checkedCenters.Count,Is.GreaterThan(3),"至少验证四块岸上石头");
            Assert.That((await new ValleyMapBuilder(loader).SetShortcutOpenAsync(handle,handle.Layout.ShortcutId,true,CancellationToken.None)).Success,Is.True);
            foreach(var center in checkedCenters)
            {
                Assert.That(UnityEngine.AI.NavMesh.SamplePosition(center,out _,.1f,UnityEngine.AI.NavMesh.AllAreas),Is.False,"捷径重烘焙不能清除石头阻挡");
            }
        }
        [UnityTest] public IEnumerator HanChapter_SearchPointsAndGuardsAreReachable() => Run_HanChapter().ToCoroutine();
        private async UniTask Run_HanChapter()
        {
            var result = await Build();
            Assert.That(result.Success, Is.True, result.Error);
            var handle = (GeneratedValleyContentHandle)result.Handle;
            var provider = new LubanRuntimeConfigProvider(() => new Tables(n => new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + n + ".bytes").bytes)));
            Assert.That(provider.TryGetMapSpawns(MapIds.CivilisationValley, out var spawns, out var error), Is.True, error);
            var guards = ValleyChapterSpawns.Resolve(handle.Layout, spawns).Where(s => s.ObjectType == MapSpawnObjectType.Enemy).ToArray();
            Assert.That(guards.Length, Is.EqualTo(4));
            var points = handle.Layout.Points.Where(p => p.TemplateId.StartsWith("han.")).ToArray();
            Assert.That(points.Length, Is.EqualTo(6));
            var views = root.GetComponentsInChildren<CivilisationPointView>(true);
            foreach (var point in points)
            {
                var view=views.Single(v=>v.PointId==point.Id);
                Assert.That(view.GetComponent<Collider>(),Is.Not.Null,point.Id);
                var scenery=view.transform.Find("StoryScene");Assert.That(scenery,Is.Not.Null,point.Id);
                foreach(Transform prop in scenery)
                    Assert.That(prop.position.y,Is.EqualTo(handle.Layout.HeightAt(prop.position.x,prop.position.z)).Within(.01f),"布景组底座应逐组贴地："+prop.name);
                var evidence=view.transform.Find("EvidenceRoot");Assert.That(evidence,Is.Not.Null);
                Assert.That(evidence.position.y,Is.EqualTo(handle.Layout.HeightAt(evidence.position.x,evidence.position.z)+.055f).Within(.01f));
            }
            foreach(var clue in handle.Layout.Clues)
            {
                var view=views.Single(v=>v.PointId==clue.Id);
                Assert.That(view.transform.Find("StoryScene"),Is.Not.Null,clue.Id);
                Assert.That(view.GetComponentsInChildren<Collider>(true).All(c=>c.isTrigger),Is.True,"现场装饰不应阻断已验收导航");
                if(clue.Order==0)
                {
                    var ground=view.transform.Find("GroundSurface");
                    Assert.That(ground,Is.Not.Null,"运输现场需要连续的地面痕迹");
                    foreach(var filter in ground.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Assert.That(AssetDatabase.Contains(filter.sharedMesh),Is.False,"贴地只修改地图拥有的网格副本");
                        foreach(var vertex in filter.sharedMesh.vertices)
                        {
                            var world=filter.transform.TransformPoint(vertex);
                            Assert.That(world.y-handle.Layout.HeightAt(world.x,world.z),Is.InRange(.015f,.065f),"车辙和裸土必须随保存地形贴地");
                        }
                    }
                }
                foreach(Transform prop in view.transform.Find("StoryScene"))
                {
                    var renderers=prop.GetComponentsInChildren<Renderer>(true);var bounds=renderers[0].bounds;
                    foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                    foreach(var tree in handle.Layout.Points.Where(p=>p.TemplateId.StartsWith("tree.")))
                    {
                        var position=tree.Pose.ToVector();position.y=bounds.center.y;
                        Assert.That(Vector3.Distance(position,bounds.ClosestPoint(position)),Is.GreaterThan(.35f),"沿路线索布景不能穿过树干："+clue.Id+"/"+prop.name);
                    }
                }
            }
            Assert.That(UnityEngine.AI.NavMesh.SamplePosition(handle.Layout.Spawn.ToVector(), out var start, 2, UnityEngine.AI.NavMesh.AllAreas), Is.True);
            foreach (var position in points.Select(p => p.Pose.ToVector()).Concat(guards.Select(s => s.LocalPosition)))
            {
                Assert.That(UnityEngine.AI.NavMesh.SamplePosition(position, out var end, 2.25f, UnityEngine.AI.NavMesh.AllAreas), Is.True, position.ToString());
                var path = new UnityEngine.AI.NavMeshPath();
                Assert.That(UnityEngine.AI.NavMesh.CalculatePath(start.position, end.position, UnityEngine.AI.NavMesh.AllAreas, path), Is.True);
                Assert.That(path.status, Is.EqualTo(UnityEngine.AI.NavMeshPathStatus.PathComplete));
            }
            var surfaceMeshes=views.Where(v=>handle.Layout.Clues.Any(c=>c.Id==v.PointId&&c.Order==0))
                .SelectMany(v=>v.transform.Find("GroundSurface").GetComponentsInChildren<MeshFilter>(true)).Select(f=>f.sharedMesh).ToArray();
            await handle.ReleaseGeneratedAsync();built=null;
            Assert.That(surfaceMeshes.All(m=>m==null),Is.True,"地面痕迹网格必须随远征地图释放");
        }
        [UnityTest] public IEnumerator Vegetation_SaveRestoreKeepsAppearanceAndReleaseDestroysCombinedMeshes() => Run_VegetationRestore().ToCoroutine();
        private async UniTask Run_VegetationRestore()
        {
            var first = await Build(); Assert.That(first.Success, Is.True, first.Error);
            var handle = (GeneratedValleyContentHandle)first.Handle;
            var saved = Newtonsoft.Json.JsonConvert.DeserializeObject<ValleyLayoutSaveData>(Newtonsoft.Json.JsonConvert.SerializeObject(handle.Layout));
            var before = VegetationSnapshot(root, saved);
            var cover = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "ValleyGroundCover");
            var ownedMeshes = cover.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).ToArray();
            await handle.ReleaseGeneratedAsync(); built = null;
            Assert.That(ownedMeshes.All(m => m == null), Is.True, "草灌合批网格应随地图释放，不能跨远征泄漏");
            var restored = await Build(savedLayout: saved); Assert.That(restored.Success, Is.True, restored.Error);
            Assert.That(VegetationSnapshot(root, saved), Is.EqualTo(before), "读档不能重抽树形或地面植被");
        }
        private static string VegetationSnapshot(GameObject root, ValleyLayoutSaveData layout)
        {
            using var stream = new System.IO.MemoryStream();
            using (var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                var ids = layout.Points.Where(p => p.TemplateId.StartsWith("tree.")).Select(p => p.Id).ToHashSet();
                foreach (var view in root.GetComponentsInChildren<CivilisationPointView>(true).Where(v => ids.Contains(v.PointId)).OrderBy(v => v.PointId))
                {
                    writer.Write(view.PointId);
                    foreach (var filter in view.GetComponentsInChildren<MeshFilter>(true))
                    { writer.Write(filter.sharedMesh.name); writer.Write(filter.transform.lossyScale.x); }
                }
                var cover = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "ValleyGroundCover");
                foreach (var filter in cover.GetComponentsInChildren<MeshFilter>(true).OrderBy(f => f.name))
                {
                    writer.Write(filter.name);
                    foreach (var v in filter.sharedMesh.vertices) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                    foreach (var index in filter.sharedMesh.triangles) writer.Write(index);
                }
            }
            using var hash = System.Security.Cryptography.SHA256.Create();
            return System.Convert.ToBase64String(hash.ComputeHash(stream.ToArray()));
        }
        [UnityTest] public IEnumerator Vegetation_UsesMixedHarvestableTreesAndNonBlockingGroundCover() => Run_Vegetation().ToCoroutine();
        private async UniTask Run_Vegetation()
        {
            var result = await Build(); Assert.That(result.Success, Is.True, result.Error);
            var layout = ((GeneratedValleyContentHandle)result.Handle).Layout;
            var trees = layout.Points.Where(p => p.TemplateId.StartsWith("tree.")).ToArray();
            var identities = trees.Select(p => p.Id).ToHashSet();
            var views = root.GetComponentsInChildren<CivilisationPointView>(true).Where(v => identities.Contains(v.PointId)).ToDictionary(v => v.PointId);
            var meshes = new System.Collections.Generic.HashSet<string>();
            foreach (var point in trees)
            {
                Assert.That(views.ContainsKey(point.Id), Is.True, point.Id);
                var view = views[point.Id];
                Assert.That(Vector3.Distance(view.transform.position, point.Pose.ToVector()), Is.LessThan(.001f));
                var visual = (GameObject)typeof(CivilisationPointView).GetField("harvestedVisualRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
                foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true)) meshes.Add(filter.sharedMesh.name);
                Assert.That(view.GetComponent<Collider>().enabled, Is.True);
                Assert.That(visual.GetComponent<CameraOccluderView>().BodyRenderers.All(r => r != null && r.transform.IsChildOf(visual.transform)), Is.True,
                    "换树形必须重新绑定遮挡淡化渲染器");
            }
            Assert.That(meshes.Count, Is.GreaterThanOrEqualTo(5), "河谷不能继续只使用一个树形及其LOD");
            var cover = root.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == "ValleyGroundCover");
            Assert.That(cover, Is.Not.Null, "应实际生成地面植被");
            Assert.That(cover.GetComponentsInChildren<Collider>(true), Is.Empty, "装饰草灌不能阻塞采集与通行");
            Assert.That(cover.GetComponentsInChildren<CivilisationPointView>(true), Is.Empty);
            var coverMeshes = cover.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(coverMeshes.Any(m => m.name.StartsWith("ShrubChunk")), Is.True);
            Assert.That(coverMeshes.Any(m => m.name.StartsWith("GrassChunk")), Is.True);
            Assert.That(coverMeshes.All(m => m.sharedMesh.vertexCount <= 65535), Is.True);
            Assert.That(coverMeshes.Sum(m => m.sharedMesh.vertexCount), Is.LessThanOrEqualTo(1200000), "地被合批不能增加数百万顶点的整图内存负担");
            Assert.That(coverMeshes.Length, Is.LessThanOrEqualTo(512), "地面植被应合批，不能每簇成为独立渲染物件");
        }
        [UnityTest] public IEnumerator RiverWater_FacesUpwardForVisibleSurface() => Run_WaterNormals().ToCoroutine();
        private async UniTask Run_WaterNormals()
        {var r=await Build();Assert.That(r.Success,Is.True,r.Error);var rivers=root.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.name.StartsWith("RiverWater_")).ToArray();Assert.That(rivers.Length,Is.GreaterThan(0));Assert.That(rivers.All(m=>m.sharedMesh.normals.All(n=>n.y>0)),Is.True);}
        [UnityTest] public IEnumerator CancelShortcutUpdate_RestoresClosedNavigation() => Run_CancelShortcut().ToCoroutine();
        private async UniTask Run_CancelShortcut()
        {
            var r=await Build();Assert.That(r.Success,Is.True,r.Error);var h=(GeneratedValleyContentHandle)r.Handle;
            using var cancellation=new CancellationTokenSource();var pending=new ValleyMapBuilder(loader).SetShortcutOpenAsync(h,h.Layout.ShortcutId,true,cancellation.Token).AsTask();cancellation.Cancel();
            Assert.That((await pending).Success,Is.False);
            var path=new UnityEngine.AI.NavMeshPath();Assert.That(UnityEngine.AI.NavMesh.CalculatePath(h.Layout.Spawn.ToVector(),h.Layout.Camp.ToVector(),UnityEngine.AI.NavMesh.AllAreas,path),Is.True);
            var length=0f;for(int i=1;i<path.corners.Length;i++)length+=Vector3.Distance(path.corners[i-1],path.corners[i]);Assert.That(length,Is.GreaterThan(h.RouteLength-10));
        }
        [UnityTest] public IEnumerator ReleaseDuringShortcutUpdate_WaitsAndReleasesOnce() => Run_ReleaseShortcut().ToCoroutine();
        private async UniTask Run_ReleaseShortcut()
        {
            var r=await Build();Assert.That(r.Success,Is.True,r.Error);var h=(GeneratedValleyContentHandle)r.Handle;var before=loader.Releases;
            var pending=new ValleyMapBuilder(loader).SetShortcutOpenAsync(h,h.Layout.ShortcutId,true,CancellationToken.None).AsTask();
            var first=h.ReleaseGeneratedAsync().AsTask();var second=h.ReleaseGeneratedAsync().AsTask();
            await Task.WhenAll(pending,first,second);Assert.That(h.IsValid,Is.False);Assert.That(loader.Releases,Is.EqualTo(before+1));Assert.That((await pending).Success,Is.False);built=null;
        }
        [UnityTest] public IEnumerator MultipleSeeds_HaveReachableCluesAndReturnShortcut() => Run_MultipleSeeds().ToCoroutine();
        private async UniTask Run_MultipleSeeds()
        {
            var records=new System.Collections.Generic.List<object>();
            foreach(var seed in new[]{11,37,101,509})
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();var r=await Build(default,seed);Assert.That(r.Success,Is.True,"seed="+seed+": "+r.Error);
                var h=(GeneratedValleyContentHandle)r.Handle;
                Assert.That(root.GetComponentsInChildren<CivilisationPointView>(true).Count(v=>v.Kind==CivilisationPointKind.Clue),Is.EqualTo(h.Layout.Clues.Count));
                foreach(var c in h.Layout.Clues)Assert.That(UnityEngine.AI.NavMesh.SamplePosition(c.Pose.ToVector(),out _,2.25f,UnityEngine.AI.NavMesh.AllAreas),Is.True,c.Id);
                var builder=new ValleyMapBuilder(loader);Assert.That((await builder.SetShortcutOpenAsync(h,h.Layout.ShortcutId,true,CancellationToken.None)).Success,Is.True);
                var path=new UnityEngine.AI.NavMeshPath();Assert.That(UnityEngine.AI.NavMesh.CalculatePath(h.Layout.Camp.ToVector(),h.Layout.Spawn.ToVector(),UnityEngine.AI.NavMesh.AllAreas,path),Is.True);Assert.That(path.status,Is.EqualTo(UnityEngine.AI.NavMeshPathStatus.PathComplete));
                var length=0f;for(int i=1;i<path.corners.Length;i++)length+=Vector3.Distance(path.corners[i-1],path.corners[i]);
                Assert.That(length,Is.LessThanOrEqualTo(h.RouteLength+5),"seed="+seed+"：开放返程连接不能破坏通行，也不强制玩家绕路");
                records.Add(new{seed,attempt=h.Layout.AttemptIndex,routeMeters=h.RouteLength,alternateMeters=h.AlternateRouteLength,returnMeters=length,loadAndOpenMs=watch.ElapsedMilliseconds});
                await h.ReleaseGeneratedAsync();built=null;
            }
            System.IO.Directory.CreateDirectory("Assets/Tests/Results~/OpenValley");System.IO.File.WriteAllText("Assets/Tests/Results~/OpenValley/navigation-seeds.json",Newtonsoft.Json.JsonConvert.SerializeObject(records,Newtonsoft.Json.Formatting.Indented));
        }
        [UnityTest] public IEnumerator Shortcut_OpenThenCloseRestoresNavigationBarrier() => Run_Shortcut().ToCoroutine();
        private async UniTask Run_Shortcut()
        {
            var r=await Build();Assert.That(r.Success,Is.True,r.Error);
            var h=(GeneratedValleyContentHandle)r.Handle;var builder=new ValleyMapBuilder(loader);
            Assert.That((await builder.SetShortcutOpenAsync(h,h.Layout.ShortcutId,true,CancellationToken.None)).Success,Is.True);
            Assert.That((await builder.SetShortcutOpenAsync(h,h.Layout.ShortcutId,false,CancellationToken.None)).Success,Is.True);
            var sources=(System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildSource>)typeof(GeneratedValleyContentHandle).GetField("Sources",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(h);
            var index=(int)typeof(GeneratedValleyContentHandle).GetField("ShortcutSourceIndex",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(h);
            Assert.That(sources[index].size.sqrMagnitude,Is.GreaterThan(0),"关闭捷径必须恢复实际导航阻挡尺寸");
        }
        [UnityTest] public IEnumerator SharedEdges_UseIdenticalSavedHeights() => Run_SharedEdges_UseIdenticalSavedHeights().ToCoroutine();
        // 移除河床泥沙绑定时失败；验收实际地形子网格，不新增重叠碰撞面。
        [UnityTest] public IEnumerator Riverbed_UsesSandOnExistingTerrainMesh() => Run_RiverbedSand().ToCoroutine();
        private async UniTask Run_RiverbedSand()
        {
            var result = await Build(); Assert.That(result.Success, Is.True, result.Error);
            var sand = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Environment/CivilisationExpedition/River/RiverSand.mat");
            var beds = root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.name.StartsWith("TerrainChunk") && r.sharedMaterials.Contains(sand)).ToArray();
            Assert.That(beds.Length, Is.GreaterThan(0), "河床必须能和绿色地表区分");
            Assert.That(beds.Any(r => r.GetComponent<MeshFilter>().sharedMesh.GetTriangles(1).Length > 0), Is.True);
            foreach (var bed in beds)
                Assert.That(bed.GetComponent<MeshCollider>().sharedMesh, Is.SameAs(bed.GetComponent<MeshFilter>().sharedMesh));
        }
        private async UniTask Run_SharedEdges_UseIdenticalSavedHeights()
        {
            var result=await Build();Assert.That(result.Success,Is.True,result.Error);
            var meshes=root.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.name.StartsWith("TerrainChunk")).ToArray();Assert.That(meshes.Length,Is.EqualTo(256));
            var vertices=meshes.SelectMany(m=>m.sharedMesh.vertices.Select(v=>m.transform.TransformPoint(v))).GroupBy(v=>new Vector2(v.x,v.z));
            Assert.That(vertices.All(g=>g.Select(v=>v.y).Distinct().Count()==1),Is.True);
        }
        [UnityTest] public IEnumerator Chunks_StayWithinSixteenBitIndices() => Run_Chunks_StayWithinSixteenBitIndices().ToCoroutine();
        private async UniTask Run_Chunks_StayWithinSixteenBitIndices()
        {var r=await Build();Assert.That(r.Success,Is.True,r.Error);Assert.That(root.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.name.StartsWith("TerrainChunk")).All(m=>m.sharedMesh.vertexCount==289),Is.True);}
        [UnityTest] public IEnumerator River_HasExplicitNonWalkableSourcesAndFords() => Run_River_HasExplicitNonWalkableSourcesAndFords().ToCoroutine();
        private async UniTask Run_River_HasExplicitNonWalkableSourcesAndFords()
        {var r=await Build();Assert.That(r.Success,Is.True,r.Error);var count=Number(r.Handle,"DeepWaterSourceCount");Assert.That(count,Is.GreaterThan(0));Assert.That(root.GetComponentsInChildren<BoxCollider>(true).Count(c=>c.name.StartsWith("DeepWaterCollision_")&&!c.isTrigger),Is.EqualTo((int)count));}
        [UnityTest] public IEnumerator Navigation_ConnectsSpawnToCampWithinRouteBudget() => Run_Navigation_ConnectsSpawnToCampWithinRouteBudget().ToCoroutine();
        private async UniTask Run_Navigation_ConnectsSpawnToCampWithinRouteBudget()
        {
            var r=await Build();Assert.That(r.Success,Is.True,r.Error);Assert.That(Number(r.Handle,"RouteLength"),Is.InRange(600,2400));
            var h=(GeneratedValleyContentHandle)r.Handle;
            var trees=h.Layout.Points.Where(p=>p.TemplateId.StartsWith("tree.")).Select(p=>p.Id).ToHashSet();
            var views=root.GetComponentsInChildren<CivilisationPointView>(true).Where(v=>trees.Contains(v.PointId)).ToArray();
            Assert.That(views.Length,Is.EqualTo(trees.Count),"森林不能仅保存位置而没有实际交互树木");
            Assert.That(views.All(v=>v.GetComponent<Collider>()!=null&&v.GetComponentsInChildren<MeshRenderer>(true).Any(m=>m.enabled)),Is.True);
            var forest=views.First(v=>v.PointId.StartsWith("generated.valley.forest."));
            var lod=forest.GetComponentInChildren<LODGroup>(true);
            // 当前静态树形主动移除 LODGroup；保留 LOD 的来源仍不得提前隐去远处树木。
            if (lod != null) Assert.That(lod.GetLODs().Last().screenRelativeTransitionHeight,Is.LessThanOrEqualTo(.001f),"成片林区不能沿用小场景中提前隐去远处树木的 LOD");
        }
        [UnityTest] public IEnumerator Navigation_ProvidesAlternativeRoute() => Run_Navigation_ProvidesAlternativeRoute().ToCoroutine();
        private async UniTask Run_Navigation_ProvidesAlternativeRoute()
        {var r=await Build();Assert.That(r.Success,Is.True,r.Error);Assert.That(Number(r.Handle,"AlternateRouteLength"),Is.GreaterThan(0));}

        [UnityTest] public IEnumerator InteriorNavigation_ReachesBroadAreaOutsideRiverRoute() => Run_OpenArea().ToCoroutine();
        private async UniTask Run_OpenArea()
        {
            var records = new System.Collections.Generic.List<object>();
            foreach (var seed in new[] { 11, 37, 101, 509 })
            {
                var result = await Build(default, seed); Assert.That(result.Success, Is.True, result.Error);
                var h = (GeneratedValleyContentHandle)result.Handle;
                var reached = 0; var total = 0;
                Assert.That(UnityEngine.AI.NavMesh.SamplePosition(h.Layout.Spawn.ToVector(), out var start, 3, UnityEngine.AI.NavMesh.AllAreas), Is.True);
                for (var z = -448; z <= 448; z += 64)
                    for (var x = -448; x <= 448; x += 64)
                    {
                        total++;
                        var p = new Vector3(x, h.Layout.HeightAt(x, z), z);
                        if (!UnityEngine.AI.NavMesh.SamplePosition(p, out var end, 3, UnityEngine.AI.NavMesh.AllAreas)) continue;
                        var path = new UnityEngine.AI.NavMeshPath();
                        if (UnityEngine.AI.NavMesh.CalculatePath(start.position, end.position, UnityEngine.AI.NavMesh.AllAreas, path) && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) reached++;
                    }
                Assert.That((float)reached / total, Is.GreaterThan(.65f), "seed=" + seed + "：必须能从入口到达大部分内部区域");
                records.Add(new { seed, attempt = h.Layout.AttemptIndex, reached, total, reachableFraction = (float)reached / total, routeMeters = h.RouteLength });
                await h.ReleaseGeneratedAsync(); built = null;
            }
            System.IO.Directory.CreateDirectory("Assets/Tests/Results~/OpenValley");
            System.IO.File.WriteAllText("Assets/Tests/Results~/OpenValley/reachable-area.json", Newtonsoft.Json.JsonConvert.SerializeObject(records, Newtonsoft.Json.Formatting.Indented));
        }
        [UnityTest] public IEnumerator NavigationValidation_IsolatedFromOtherMapData() => Run_NavigationValidation_IsolatedFromOtherMapData().ToCoroutine();
        private async UniTask Run_NavigationValidation_IsolatedFromOtherMapData()
        {
            var settings=UnityEngine.AI.NavMesh.GetSettingsByIndex(0);
            var foreign=UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings,new[]{new UnityEngine.AI.NavMeshBuildSource {shape=UnityEngine.AI.NavMeshBuildSourceShape.Box,transform=Matrix4x4.TRS(new Vector3(0,7.5f,0),Quaternion.identity,Vector3.one),size=new Vector3(1024,1,1024),area=0}}.ToList(),new Bounds(Vector3.zero,new Vector3(1024,500,1024)),Vector3.zero,Quaternion.identity);
            var instance=UnityEngine.AI.NavMesh.AddNavMeshData(foreign);
            try
            {
                var foreignPath=new UnityEngine.AI.NavMeshPath();Assert.That(UnityEngine.AI.NavMesh.CalculatePath(new Vector3(-380,8,-380),new Vector3(380,8,380),UnityEngine.AI.NavMesh.AllAreas,foreignPath),Is.True);
                Assert.That(foreignPath.status,Is.EqualTo(UnityEngine.AI.NavMeshPathStatus.PathComplete));
                var r=await Build();Assert.That(r.Success,Is.True,r.Error);Assert.That(Number(r.Handle,"RouteLength"),Is.InRange(600,2400));Assert.That(Number(r.Handle,"ValidationOffsetX"),Is.EqualTo(16384));
            }
            finally{instance.Remove();UnityEngine.Object.DestroyImmediate(foreign);}
        }
        [UnityTest] public IEnumerator CancelBeforeLoad_CreatesNoGeneratedObjects() => Run_CancelBeforeLoad_CreatesNoGeneratedObjects().ToCoroutine();
        [UnityTest] public IEnumerator CancelDuringForestPreparation_ReleasesPartialCandidate() => Run_CancelForest().ToCoroutine();
        private async UniTask Run_CancelForest()
        {
            using var cancellation=new CancellationTokenSource();
            var cancelledAfterPartialCreation=false;
            var result=await Build(cancellation.Token,onProgress:p=>
            {
                if(p>.65f&&p<.9f){cancelledAfterPartialCreation=true;cancellation.Cancel();}
            });
            Assert.That(cancelledAfterPartialCreation,Is.True,"必须进入真实采集点创建后再取消");
            Assert.That(result.Success,Is.False);
            Assert.That(root.transform.childCount,Is.EqualTo(1),"分批准备取消后不得遗留生成根或模板");
            Assert.That(loader.Releases,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator CancelDuringNavigation_ReleasesCandidateAfterBake() => Run_CancelNavigation().ToCoroutine();
        private async UniTask Run_CancelNavigation()
        {
            using var cancellation=new CancellationTokenSource();
            var navigationReports=0;
            var result=await Build(cancellation.Token,onProgress:p=>
            {
                // 地形结束和烘焙开始各报告25%；第三次来自已经启动的原生操作。
                if(p>=.25f&&p<.4f&&++navigationReports==3)cancellation.Cancel();
            });
            Assert.That(navigationReports,Is.GreaterThanOrEqualTo(3));
            Assert.That(result.Success,Is.False);
            Assert.That(root.transform.childCount,Is.EqualTo(1));
            Assert.That(loader.Releases,Is.EqualTo(1));
        }
        private async UniTask Run_CancelBeforeLoad_CreatesNoGeneratedObjects()
        {using var cts=new CancellationTokenSource();cts.Cancel();var r=await Build(cts.Token);Assert.That(r.Success,Is.False);Assert.That(root.transform.childCount,Is.EqualTo(1));}
        [UnityTest] public IEnumerator CancelAtResourceCallback_ReleasesHandle() => Run_CancelAtResourceCallback_ReleasesHandle().ToCoroutine();
        private async UniTask Run_CancelAtResourceCallback_ReleasesHandle()
        {using var cts=new CancellationTokenSource();loader.BeforeCallback=()=>cts.Cancel();var r=await Build(cts.Token);Assert.That(r.Success,Is.False);Assert.That(loader.Releases,Is.EqualTo(1));}
        [UnityTest] public IEnumerator MissingKit_FailsWithoutLeakingGeometry() => Run_MissingKit_FailsWithoutLeakingGeometry().ToCoroutine();
        private async UniTask Run_MissingKit_FailsWithoutLeakingGeometry()
        {loader.Missing=true;var r=await Build();Assert.That(r.Success,Is.False);Assert.That(root.transform.childCount,Is.EqualTo(1));Assert.That(loader.Releases,Is.EqualTo(1));}
        [UnityTest] public IEnumerator RepeatedRelease_IsIdempotent() => Run_RepeatedRelease_IsIdempotent().ToCoroutine();
        private async UniTask Run_RepeatedRelease_IsIdempotent()
        {var r=await Build();Assert.That(r.Success,Is.True,r.Error);var releasesBefore=loader.Releases;var method=r.Handle.GetType().GetMethod("ReleaseGeneratedAsync");await ((UniTask<MapContentUnloadResult>)method.Invoke(r.Handle,null)).AsTask();await ((UniTask<MapContentUnloadResult>)method.Invoke(r.Handle,null)).AsTask();Assert.That(loader.Releases,Is.EqualTo(releasesBefore+1));Assert.That(r.Handle.IsValid,Is.False);built=null;}
        private sealed class TestHandle:IMapContentHandle
        {public TestHandle(MapContext context){Context=context;}public string MapId=>MapIds.CivilisationValley;public MapContext Context{get;}public bool IsValid=>Context!=null;}
        private sealed class FakeLoader:IResourceLoader
        {
            public bool Missing;public int Releases;public Action BeforeCallback;
            public T LoadAsset<T>(string path)where T:UnityEngine.Object=>Missing?null:AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> loaded)where T:UnityEngine.Object{BeforeCallback?.Invoke();loaded(LoadAsset<T>(path));return new ResourceLoadHandle(new object());}
            public void Release(ResourceLoadHandle handle){if(handle.IsValid)Releases++;}
        }
    }
}
