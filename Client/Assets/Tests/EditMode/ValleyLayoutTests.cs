using System;
using System.Linq;
using System.Reflection;
using Luban;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ValleyLayoutTests
    {
        private static Type TypeOf(string name)
        { var type = typeof(ExpeditionModel).Assembly.GetType("Game." + name); Assert.That(type, Is.Not.Null, name + " 尚未实现"); return type; }
        private static object Profile() => JsonConvert.DeserializeObject("{\"MapId\":\"" + MapIds.CivilisationValley + "\",\"GenerationVersion\":2,\"WorldSizeMeters\":1024,\"ChunkSizeMeters\":64,\"HeightSampleSpacingMeters\":4,\"MinRouteMeters\":600,\"MaxRouteMeters\":2400,\"MaxAttempts\":8,\"KitAddress\":\"CivilisationValleyGenerationKit\"}", TypeOf("ValleyGenerationProfile"));
        private static CivilisationCatalog Templates()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + name + ".bytes").bytes)));
            Assert.That(provider.TryGetCivilisationCatalog(out var catalog, out var error), Is.True, error); return catalog;
        }
        private static object Generate(int seed = 37, int attempt = 0)
        {
            var args = new object[] { seed, attempt, Profile(), Templates(), null, null };
            Assert.That((bool)TypeOf("ValleyLayoutGenerator").GetMethod("TryGenerate").Invoke(null, args), Is.True, args[5]?.ToString()); return args[4];
        }
        private static JObject Json(object layout) => JObject.FromObject(layout);
        private static bool Validate(object layout)
        { var args = new object[] { layout, Profile(), Templates(), null }; return (bool)TypeOf("ValleyLayoutValidator").GetMethod("TryValidate").Invoke(null, args); }
        [Test] public void SameSeed_ProducesIdenticalCompleteLayout()
        { Assert.That(JsonConvert.SerializeObject(Generate()), Is.EqualTo(JsonConvert.SerializeObject(Generate()))); }
        // 河流两端重新缩回营地路线时失败；同时检查延伸处确实有水下地形。
        [Test] public void River_ExtendsToMapBoundaryWithSubmergedBed()
        {
            foreach (var seed in new[] { 11, 37, 101, 509 })
            {
                var layout = (ValleyLayoutSaveData)Generate(seed);
                foreach (var end in new[] { layout.River.First(), layout.River.Last() })
                    Assert.That(Mathf.Max(Mathf.Abs(end.X), Mathf.Abs(end.Z)), Is.EqualTo(512).Within(.01f));
                Assert.That(layout.River.Count, Is.GreaterThan(layout.Route.Count));
                foreach (var segment in new[] { 1, layout.River.Count - 1 })
                {
                    var p = Vector3.Lerp(layout.River[segment - 1].ToVector(), layout.River[segment].ToVector(), .5f);
                    if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) < 480)
                        Assert.That(layout.HeightAt(p.x, p.z), Is.LessThan(p.y), "延伸河段不能盖在陆地上");
                }
                Assert.That(Validate(layout), Is.True);
            }
        }
        [Test] public void DifferentSeeds_ChangeRouteTopology()
        { Assert.That(new[] { 11, 37, 101, 509 }.Select(s => Json(Generate(s))["Route"].ToString()).Distinct().Count(), Is.GreaterThan(1)); }
        [Test] public void ForestPoints_KeepAccessSpaceAroundShortcutBarrier()
        {
            var layout=(ValleyLayoutSaveData)Generate();
            var a=layout.Shortcut[0].ToVector();var b=layout.Shortcut[1].ToVector();
            var center=Vector3.Lerp(a,b,.8f);center.y=layout.HeightAt(center.x,center.z)+4;
            foreach(var tree in layout.Points.Where(p=>p.Id.StartsWith("generated.valley.forest.")))
            {
                var local=Quaternion.Inverse(Quaternion.LookRotation(b-a))*(tree.Pose.ToVector()-center);
                Assert.That(Mathf.Abs(local.x)>=23 || Mathf.Abs(local.z)>=7,Is.True,tree.Id+" 不得种进40×8米封堵及周围操作范围");
            }
        }
        // 删除全域植树或退回沿路线零散摆树时必须失败；林区以真实树木数量验收。
        [Test] public void ExplorableLand_AtLeastNinetyPercentContainsForest()
        {
            var records = new System.Collections.Generic.List<object>();
            foreach (var seed in new[] { 11, 37, 101, 509 })
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var layout = (ValleyLayoutSaveData)Generate(seed, attempt);
                var trees = layout.Points.Where(p => p.TemplateId.StartsWith("tree.")).ToArray();
                int land = 0, forest = 0;
                for (int z = -464; z <= 464; z += 32)
                    for (int x = -464; x <= 464; x += 32)
                    {
                        var position = new Vector2(x, z);
                        var water = false;
                        for (int i = 1; i < layout.River.Count; i++)
                        {
                            var a = new Vector2(layout.River[i - 1].X, layout.River[i - 1].Z);
                            var b = new Vector2(layout.River[i].X, layout.River[i].Z);
                            var ab = b - a;
                            if (Vector2.Distance(position, a + ab * Mathf.Clamp01(Vector2.Dot(position - a, ab) / ab.sqrMagnitude)) < 5) water = true;
                        }
                        var dx = (layout.HeightAt(x + 4, z) - layout.HeightAt(x - 4, z)) / 8;
                        var dz = (layout.HeightAt(x, z + 4) - layout.HeightAt(x, z - 4)) / 8;
                        if (water || Mathf.Sqrt(dx * dx + dz * dz) >= Mathf.Tan(30 * Mathf.Deg2Rad)) continue;
                        land++;
                        if (trees.Count(p => Mathf.Abs(p.Pose.X - x) < 16 && Mathf.Abs(p.Pose.Z - z) < 16) >= 3) forest++;
                    }
                Assert.That((float)forest / land, Is.GreaterThanOrEqualTo(.9f), "seed=" + seed + " forest=" + forest + "/" + land);
                Assert.That(Validate(layout), Is.True, "完整森林必须能通过存档校验");
                records.Add(new {seed,attempt,trees=trees.Length,forest,land,coverage=(float)forest/land});
            }
            System.IO.Directory.CreateDirectory("Assets/Tests/Results~/ForestValley");
            System.IO.File.WriteAllText("Assets/Tests/Results~/ForestValley/coverage.json",JsonConvert.SerializeObject(records,Formatting.Indented));
        }
        [Test] public void Terrain_ContainsCompleteHeightSamples()
        { var j = Json(Generate()); Assert.That((float)j["WorldSizeMeters"], Is.EqualTo(1024)); Assert.That(j["Heights"].Count(), Is.EqualTo(257 * 257)); }
        [Test] public void Route_IsConnectedAndWithinConfiguredNominalDistance()
        { var j = Json(Generate()); Assert.That(j["Route"].Count(), Is.GreaterThan(2)); Assert.That((float)j["NominalRouteMeters"], Is.InRange(1800,2400)); Assert.That(Validate(Generate()), Is.True); }
        [Test] public void Routes_IncludeIndependentRiverAndSlopePaths()
        { var j = Json(Generate()); Assert.That(j["AlternateRoute"].Count(), Is.GreaterThan(2)); Assert.That(JToken.DeepEquals(j["Route"], j["AlternateRoute"]), Is.False); }
        [Test] public void Clues_ContainTwoIndependentChains()
        { Assert.That(Json(Generate())["Clues"].Select(c => (int)c["Chain"]).Distinct().Count(), Is.EqualTo(2)); }
        [Test] public void Points_IncludeWaterToolsAndCampTechnology()
        { var ids = Json(Generate())["Points"].Select(p => (string)p["TemplateId"]).ToArray(); CollectionAssert.Contains(ids,"camp.fire"); CollectionAssert.Contains(ids,"camp.axe"); CollectionAssert.Contains(ids,"camp.pickaxe"); Assert.That(ids.Any(i => i.StartsWith("water.valley.")), Is.True); }
        [Test] public void Points_HaveUniqueStableIds()
        { var ids = Json(Generate())["Points"].Select(p => (string)p["Id"]).ToArray(); Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length)); }
        [Test] public void Clone_DoesNotShareTerrainArrays()
        { var source = Generate(); var clone = source.GetType().GetMethod("Clone").Invoke(source,null); var heights = source.GetType().GetProperty("Heights"); ((float[])heights.GetValue(source))[0] = -100; Assert.That(((float[])heights.GetValue(clone))[0], Is.Not.EqualTo(-100)); }
        [Test] public void Serialization_RetainsCompleteLayout()
        { var source = Generate(); var copy = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(source), source.GetType()); Assert.That(JToken.DeepEquals(Json(source),Json(copy)), Is.True); Assert.That(Validate(copy), Is.True); }
        [Test] public void Validator_RejectsCorruptHeightsPointsAndTemplates()
        {
            foreach(var mutation in new Action<JObject>[] { j => j["Heights"] = new JArray(0), j => j["Points"][0]["TemplateId"] = "missing", j => j["Points"][1]["Id"] = j["Points"][0]["Id"].DeepClone() })
            { var j=Json(Generate()); mutation(j); Assert.That(Validate(j.ToObject(TypeOf("ValleyLayoutSaveData"))),Is.False); }
        }
        [Test] public void Validator_RejectsUnsupportedGenerationVersion()
        { var j=Json(Generate()); j["GenerationVersion"] = 999; Assert.That(Validate(j.ToObject(TypeOf("ValleyLayoutSaveData"))),Is.False); }

        // 捕获重新把路线外地表抬成不可走陡坡的退化，不以路线点数量代替探索面积。
        [Test] public void InteriorTerrain_HasBroadWalkableSlopes()
        {
            foreach (var seed in new[] { 11, 37, 101, 509 })
            {
                var layout = (ValleyLayoutSaveData)Generate(seed);
                var gentle = 0; var total = 0;
                for (var z = -448; z <= 448; z += 16)
                    for (var x = -448; x <= 448; x += 16)
                    {
                        var dx = (layout.HeightAt(x + 4, z) - layout.HeightAt(x - 4, z)) / 8;
                        var dz = (layout.HeightAt(x, z + 4) - layout.HeightAt(x, z - 4)) / 8;
                        // 高原顶部本身平坦，但旧版陡坡把它与探索区隔离，不能计作开放低地。
                        if (Mathf.Sqrt(dx * dx + dz * dz) < Mathf.Tan(30 * Mathf.Deg2Rad) &&
                            layout.HeightAt(x, z) < Mathf.Max(layout.Spawn.Y, layout.Camp.Y) + 30) gentle++;
                        total++;
                    }
                Assert.That((float)gentle / total, Is.GreaterThan(.75f), "seed=" + seed);
            }
        }

        [Test] public void NewLayouts_KeepEntranceAndCampFarApart()
        {
            foreach (var seed in Enumerable.Range(0, 24))
            {
                var layout = (ValleyLayoutSaveData)Generate(seed);
                var a = layout.Spawn.ToVector(); var b = layout.Camp.ToVector(); a.y = b.y = 0;
                Assert.That(Vector3.Distance(a, b), Is.GreaterThanOrEqualTo(650), "seed=" + seed);
            }
        }

        [Test] public void HarvestPoints_ExtendIntoAreasAwayFromSuggestedRoutes()
        {
            var layout = (ValleyLayoutSaveData)Generate();
            var remote = layout.Points.Count(p =>
                (p.TemplateId.StartsWith("tree.") || p.TemplateId.StartsWith("rock.")) &&
                layout.Route.All(r => Vector2.Distance(new Vector2(p.Pose.X, p.Pose.Z), new Vector2(r.X, r.Z)) > 70));
            Assert.That(remote, Is.GreaterThanOrEqualTo(12));
        }

        [Test] public void NavigationWaypoints_AreAnchoredToSavedTerrain()
        {
            var layout = (ValleyLayoutSaveData)Generate();
            foreach (var point in layout.Route.Concat(layout.AlternateRoute).Concat(layout.Shortcut))
                Assert.That(point.Y, Is.EqualTo(layout.HeightAt(point.X, point.Z)).Within(.01f), "导航路径必须贴合实际地面");
        }

        [Test] public void OuterRim_RisesWithoutAnAbruptHighWall()
        {
            var layout = (ValleyLayoutSaveData)Generate();
            foreach (var sign in new[] { -1, 1 })
                for (var coordinate = -448; coordinate <= 448; coordinate += 64)
                    for (var edge = 480; edge < 512; edge += 4)
                    {
                        Assert.That(Mathf.Abs(layout.HeightAt(sign * (edge + 4), coordinate) - layout.HeightAt(sign * edge, coordinate)), Is.LessThan(10));
                        Assert.That(Mathf.Abs(layout.HeightAt(coordinate, sign * (edge + 4)) - layout.HeightAt(coordinate, sign * edge)), Is.LessThan(10));
                    }
        }
    }
}
