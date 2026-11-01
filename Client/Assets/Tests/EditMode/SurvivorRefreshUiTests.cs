using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using QFramework;
namespace Game.Tests
{
    /// <summary>真实配置与正式卡片回归，临时架构由既有夹具释放，不写玩家数据。</summary>
    public sealed class SurvivorRefreshUiTests
    {
        private SurvivorRuntimeTests fixture;private SurvivorModel model;private SurvivorSystem system;private FirepowerLoop.Arcade.SurvivorConfiguration config;private GameObject card;
        private object oldArchitecture;private IResourceLoader oldLoader;private object oldLanguages;
        private static readonly FieldInfo LanguageCache=typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic);
        private static readonly FieldInfo ArchitectureField=typeof(Architecture<Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            oldLoader=ResourceLoader.Current;oldLanguages=LanguageCache.GetValue(null);ResourceLoader.Use(new LocalizationAssetResourceLoader());LocalizationLanguages.ResetForTests();
            fixture=new SurvivorRuntimeTests();fixture.Setup();
            T Read<T>(string n)=>(T)typeof(SurvivorRuntimeTests).GetField(n,Flags).GetValue(fixture);
            var architecture=Read<Game>("architecture");oldArchitecture=ArchitectureField.GetValue(null);ArchitectureField.SetValue(null,architecture);architecture.RegisterModel(new LocalizationModel());var loc=new LocalizationUtility(false);loc.LoadFromJson("zh-CN",System.IO.File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));architecture.RegisterUtility(loc);
            model=Read<SurvivorModel>("model");system=Read<SurvivorSystem>("system");config=Read<FirepowerLoop.Arcade.SurvivorConfiguration>("config");
            model.State=new SurvivorRunState(10,5,3);model.State.AddExperience(10);Offer();
            card=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Survivor/Box/SurvivorUpgrade.prefab"));
        }
        [TearDown] public void Cleanup(){if(card!=null)UnityEngine.Object.DestroyImmediate(card);fixture?.Cleanup();ArchitectureField.SetValue(null,oldArchitecture);ResourceLoader.Use(oldLoader);LanguageCache.SetValue(null,oldLanguages);}
        private void Offer()=>typeof(SurvivorSystem).GetMethod("Offer",Flags).Invoke(system,null);
        private void Bind(Action<int,int> select,Action<int,int> refresh)
        {
            var bind=typeof(SurvivorUpgradeBoxCtrl).GetMethods().SingleOrDefault(x=>x.Name=="Bind"&&x.GetParameters().Length==9);
            Assert.That(bind,Is.Not.Null,"卡片刷新绑定尚未接入");
            bind.Invoke(null,new object[]{card.GetComponent<SurvivorUpgradeBoxUI>(),model.UpgradeChoices[0],model.State.ChoiceRevision,system,select,0,model.State.RemainingRefreshes,system.CanRefreshUpgrade(model.State.ChoiceRevision,0),refresh});
        }
        [Test] public void RefreshClickDoesNotSelectAndRebindDoesNotDuplicateListeners()
        {
            int choices=0,refreshes=0;Action<int,int> select=(r,id)=>choices++;Action<int,int> refresh=(r,slot)=>refreshes++;
            Bind(select,refresh);Bind(select,refresh);var ui=card.GetComponent<SurvivorUpgradeBoxUI>();ui.BtnRefresh.onClick.Invoke();Assert.That(refreshes,Is.EqualTo(1));Assert.That(choices,Is.Zero);ui.BtnSelect.onClick.Invoke();Assert.That(choices,Is.EqualTo(1));
            Assert.That(ui.BtnRefresh.transform.IsChildOf(ui.BtnSelect.transform),Is.False);
            Assert.That(((RectTransform)ui.BtnSelect.transform).rect.size,Is.EqualTo(new Vector2(404,476)));
        }
        [Test] public void SystemRefreshPreservesOtherCardsPausingAndSnapshotBudget()
        {
            var before=model.UpgradeChoices;int rev=model.State.ChoiceRevision;system.SetPaused(true);
            Assert.That(system.RefreshUpgrade(rev,1),Is.True);Assert.That(model.UpgradeChoices[0],Is.EqualTo(before[0]));Assert.That(model.UpgradeChoices[2],Is.EqualTo(before[2]));
            Assert.That(model.Snapshot.RemainingRefreshes,Is.EqualTo(2));Assert.That(model.Paused,Is.True);Assert.That(system.Choose(rev,before[0]),Is.False);
            Assert.That(system.RefreshUpgrade(rev,0),Is.False);Assert.That(model.Snapshot.RemainingRefreshes,Is.EqualTo(2));
            Bind((r,id)=>{},(r,slot)=>{});Assert.That(card.GetComponent<SurvivorUpgradeBoxUI>().RefreshCountText.text,Is.EqualTo("2"));
        }
        [Test] public void UsedBudgetDisablesOnlyRefreshAndPreservesCardChoice()
        {
            for(int i=0;i<3;i++)Assert.That(system.RefreshUpgrade(model.State.ChoiceRevision,i),Is.True);
            Bind((r,id)=>{},(r,slot)=>{});var ui=card.GetComponent<SurvivorUpgradeBoxUI>();Assert.That(ui.BtnRefresh.interactable,Is.False);Assert.That(ui.RefreshCountText.text,Is.EqualTo("0"));Assert.That(ui.BtnSelect.interactable,Is.True);
        }
        /// <summary>实际绑定所有启用卡片，检查双语正文边界并完整复用元素装饰。</summary>
        [Test] public void AllEnabledCardsFitBothLanguagesAndResetDecoration()
        {
            var arch=(Game)ArchitectureField.GetValue(null);var language=arch.GetModel<LocalizationModel>();var loc=arch.GetUtility<LocalizationUtility>();
            var ui=card.GetComponent<SurvivorUpgradeBoxUI>();
            foreach(string code in new[]{"zh-CN","en-US"})
            {
                loc.LoadFromJson(code,System.IO.File.ReadAllText("Assets/Game/Config/Localization/"+code+".json"));language.SetLanguageCode(code);
                for(int id=1;id<=config.Upgrades.Count;id++)
                {
                    var definition=config.Upgrades[id-1];if(definition.Weight<=0)continue;
                    foreach(int level in new[]{0,Math.Max(0,definition.MaxLevel-1)})
                    {
                        var levels=(System.Collections.Generic.Dictionary<int,int>)typeof(SurvivorRunState).GetField("upgrades",Flags).GetValue(model.State);levels[id]=level;
                        SurvivorUpgradeBoxCtrl.Bind(ui,id,model.State.ChoiceRevision,system,(r,n)=>{},0,3,true,(r,n)=>{});
                        Assert.That(ui.TitleText.GetPreferredValues(ui.TitleText.text,350,float.PositiveInfinity).y,Is.LessThanOrEqualTo(60),code+" title "+id);
                        Assert.That(ui.DetailText.GetPreferredValues(ui.DetailText.text,350,float.PositiveInfinity).y,Is.LessThanOrEqualTo(180),code+" detail "+id+" "+level+" "+ui.DetailText.text);
                        Assert.That(ui.FireBadge.activeSelf,Is.EqualTo(definition.Kind=="RifleFire"));Assert.That(ui.FrostBadge.activeSelf,Is.EqualTo(definition.Kind=="RifleFrost"));Assert.That(ui.PierceBadge.activeSelf,Is.EqualTo(definition.Kind=="RiflePierce"));levels.Remove(id);
                    }
                }
            }
        }
        [Test] public void ActualConfigProvidesRunBudgetAndStableStringKey()
        {Assert.That(config.UpgradeRefreshes,Is.EqualTo(3));Assert.That(config.Id,Is.EqualTo("survivor.default"));}
    }
}
