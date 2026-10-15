using System;
using System.Linq;
using System.Reflection;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    public sealed class ValleySaveIntegrityTests
    {
        private ValleyLayoutSaveData layout;
        [SetUp] public void SetUp()
        {
            var config=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/"+n+".bytes").bytes)));
            config.TryGetCivilisationCatalog(out var catalog,out _);config.TryGetValleyGenerationProfile(MapIds.CivilisationValley,out var profile,out _);
            Assert.That(ValleyLayoutGenerator.TryGenerate(11,0,profile,catalog,out layout,out var e),Is.True,e);
        }
        private bool Validate(JToken state)
        {
            var method=typeof(ValleyLayoutValidator).GetMethod("TryValidateExploration",BindingFlags.Public|BindingFlags.Static);
            Assert.That(method,Is.Not.Null);var args=new object[]{layout,state,null};return (bool)method.Invoke(null,args);
        }
        [Test] public void SavedExploration_AcceptsOnlyOwnStableIds()
        {
            var state=CivilisationExpeditionModel.Empty();((JArray)state["visited"]).Add(layout.Points[0].Id);((JArray)state["clues"]).Add(layout.Clues[0].Id);((JArray)state["shortcuts"]).Add(layout.ShortcutId);
            Assert.That(Validate(state),Is.True);
            foreach(var key in new[]{"visited","clues","shortcuts"})
            {var corrupt=(JObject)state.DeepClone();corrupt[key]=new JArray("foreign.id");Assert.That(Validate(corrupt),Is.False,key);}
        }
        [Test] public void Layout_RejectsUnknownRegionsAndIdentityCollisions()
        {
            layout.Edges[0].To="unknown.region";Assert.That(ValleyLayoutValidator.TryValidateData(layout,out _),Is.False);
            SetUp();layout.Clues[0].Id=layout.Points[0].Id;Assert.That(ValleyLayoutValidator.TryValidateData(layout,out _),Is.False);
        }
        [Test] public void DenseForest_RestoresHarvestRecordsAndRejectsForeignTrees()
        {
            var trees=layout.Points.Where(p=>p.TemplateId.StartsWith("tree.")).ToArray();
            Assert.That(trees.Length, Is.GreaterThan(3000));
            var state=CivilisationExpeditionModel.Empty();
            state["visited"]=new JArray(trees.Select(p=>p.Id));
            Assert.That(Validate(state),Is.True);
            var model=new CivilisationExpeditionModel();
            // 恢复会发布采集状态事件，使用独立架构承接事件，不触发正式 Game 初始化。
            ((QFramework.ICanSetArchitecture)model).SetArchitecture(new Game());
            model.Restore(state);
            Assert.That(model.HasVisited(trees[trees.Length-1].Id),Is.True);
            model.Reset();Assert.That(model.HasVisited(trees[trees.Length-1].Id),Is.False);
            ((JArray)state["visited"]).Add("generated.valley.point.foreign");
            Assert.That(Validate(state),Is.False);
        }
    }
}
