using System.Linq;
using Luban;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ValleyRandomSwimmingTests
    {
        internal static LubanRuntimeConfigProvider Provider() => new LubanRuntimeConfigProvider(() => new Tables(n => new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + n + ".bytes").bytes)));
        internal static ValleyLayoutSaveData Generate(int seed)
        {
            var provider = Provider();
            Assert.That(provider.TryGetValleyGenerationProfile(MapIds.CivilisationValley, out var p, out var error), Is.True, error);
            p.GenerationVersion = 3;
            Assert.That(provider.TryGetCivilisationCatalog(out var catalog, out error), Is.True, error);
            Assert.That(ValleyLayoutGenerator.TryGenerate(seed, 0, p, catalog, out var layout, out error), Is.True, error);
            return layout;
        }
        // 捕获仅改变掉落而不改变地形、道路及浅滩的退化。
        [Test] public void NewTripsChangeRiverRoadsAndFords()
        {
            var a = Generate(37); var b = Generate(101);
            Assert.That(JsonConvert.SerializeObject(a.River), Is.Not.EqualTo(JsonConvert.SerializeObject(b.River)));
            Assert.That(JsonConvert.SerializeObject(a.Route), Is.Not.EqualTo(JsonConvert.SerializeObject(b.Route)));
            Assert.That(JsonConvert.SerializeObject(a.Fords), Is.Not.EqualTo(JsonConvert.SerializeObject(b.Fords)));
            Assert.That(a.Fords.Count, Is.InRange(2, 3));
            Assert.That(Mathf.Max(Mathf.Abs(a.River.First().X),Mathf.Abs(a.River.First().Z)),Is.EqualTo(512));
            Assert.That(Mathf.Max(Mathf.Abs(a.River.Last().X),Mathf.Abs(a.River.Last().Z)),Is.EqualTo(512));
            Assert.That(Enumerable.Range(0,6).Select(Generate).Select(l=>Mathf.Abs(l.River.First().X)>511?"x":"z").Distinct().Count(),Is.EqualTo(2),"整体走向也应变化");
        }
        [Test] public void SpawnIsLandAwayFromStartAndFreeSupplies()
        {
            foreach (var seed in Enumerable.Range(0, 12))
            {
                var l = Generate(seed);
                Assert.That(Vector3.Distance(l.Spawn.ToVector(), l.Route[0].ToVector()), Is.GreaterThan(10));
                Assert.That(l.Points.Where(p => p.TemplateId.StartsWith("camp.")).All(p => Vector3.Distance(p.Pose.ToVector(), l.Spawn.ToVector()) > 30), Is.True);
            }
        }
        [Test] public void InteriorTerrainHasNoCliffsOrTrappedDepressions()
        {
            var l = Generate(37);
            for (int z = -476; z <= 476; z += 4) for (int x = -476; x <= 476; x += 4)
            {
                Assert.That(Mathf.Abs(l.HeightAt(x + 4, z) - l.HeightAt(x, z)), Is.LessThan(1.8f));
                Assert.That(Mathf.Abs(l.HeightAt(x, z + 4) - l.HeightAt(x, z)), Is.LessThan(1.8f));
            }
        }
        [Test] public void SavedLayoutRestoresActualSamplesAndStableIdentities()
        {
            var l = Generate(101); var json = JsonConvert.SerializeObject(l);
            var copy = JsonConvert.DeserializeObject<ValleyLayoutSaveData>(json);
            Assert.That(JsonConvert.SerializeObject(copy), Is.EqualTo(json));
            Assert.That(ValleyLayoutValidator.TryValidateData(copy, out var error), Is.True, error);
            copy.Heights[100] += 1; Assert.That(l.Heights[100], Is.Not.EqualTo(copy.Heights[100]));
        }
        [Test] public void SwimmingUsesHysteresisAndRestoresAtShore()
        {
            var type=typeof(PlayerMoveSystem).Assembly.GetType("Game.ValleySwimmingRules");
            Assert.That(type,Is.Not.Null,"缺少水陆切换规则");
            var method=type.GetMethod("ShouldSwim");
            bool Swim(float depth,bool previous)=>(bool)method.Invoke(null,new object[]{depth,previous});
            Assert.That(Swim(1.2f,false),Is.True);Assert.That(Swim(.85f,true),Is.True);
            Assert.That(Swim(.85f,false),Is.False);Assert.That(Swim(.4f,true),Is.False);
        }
        [Test] public void WaterPlayerSnapshotKeepsPositionAndSwimmingState()
        {
            var property=typeof(ExpeditionPlayerSaveData).GetProperty("IsSwimming");Assert.That(property,Is.Not.Null);
            var player=new ExpeditionPlayerSaveData{RuntimeId=1,Pose=new ExpeditionPoseData{X=10,Y=.7f,Z=20}};
            property.SetValue(player,true);
            var copy=JsonConvert.DeserializeObject<ExpeditionPlayerSaveData>(JsonConvert.SerializeObject(player)).Clone();
            Assert.That((bool)property.GetValue(copy),Is.True);Assert.That(copy.Pose.Y,Is.EqualTo(.7f));
        }
        [Test] public void InvalidAttemptNeverReturnsHalfLayout()
        {
            var p=new ValleyGenerationProfile{MapId=MapIds.CivilisationValley,GenerationVersion=3,WorldSizeMeters=1024,ChunkSizeMeters=64,HeightSampleSpacingMeters=4,MinRouteMeters=100,MaxRouteMeters=2400,MaxAttempts=8,KitAddress="kit"};
            Provider().TryGetCivilisationCatalog(out var catalog,out _);
            Assert.That(ValleyLayoutGenerator.TryGenerate(1,8,p,catalog,out var layout,out var error),Is.False);
            Assert.That(layout,Is.Null);Assert.That(error,Is.Not.Empty);
        }
        [Test] public void EnemyNumbersAndCombinationsVaryWithinChapterRange()
        {
            var counts=new System.Collections.Generic.HashSet<int>();var combinations=new System.Collections.Generic.HashSet<string>();
            var provider=Provider();provider.TryGetMapSpawns(MapIds.CivilisationValley,out var configured,out _);
            for(int seed=0;seed<8;seed++)
            {
                var l=Generate(seed);var spawns=ValleyChapterSpawns.Resolve(l,configured).Where(s=>s.ObjectType==MapSpawnObjectType.Enemy).ToArray();
                Assert.That(spawns.Length,Is.InRange(2,4));counts.Add(spawns.Length);combinations.Add(string.Join(",",spawns.Select(s=>s.Id)));
                var saved=l.Clone();ValleyChapterSpawns.Resolve(saved,configured);
                Assert.That(JsonConvert.SerializeObject(saved.Enemies),Is.EqualTo(JsonConvert.SerializeObject(l.Enemies)));
            }
            Assert.That(counts.Count,Is.GreaterThan(1));Assert.That(combinations.Count,Is.GreaterThan(1));
        }
        [Test] public void OptionalLocationsVaryWhileRequiredProofRemains()
        {
            var combinations=Enumerable.Range(0,6).Select(Generate).Select(l=>
            {
                Assert.That(l.Points.Any(p=>p.TemplateId=="han.proof"),Is.True);
                return string.Join(",",l.Points.Where(p=>p.TemplateId.StartsWith("han.")).Select(p=>p.TemplateId).OrderBy(p=>p));
            }).Distinct().Count();
            Assert.That(combinations,Is.GreaterThan(1));
        }
    }
}
