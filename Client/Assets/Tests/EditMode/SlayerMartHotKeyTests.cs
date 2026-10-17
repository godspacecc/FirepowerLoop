using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;

namespace Game.Tests
{
    /// <summary>复用现有设置热键事务，不新增第二套键盘F监听或玩家设置文件。</summary>
    public sealed class SlayerMartHotKeyTests
    {
        private const string Action="arcade.field-upgrade";
        private SlayerMartUiFlowTests h;private HotKeySystem system;private int intents;
        [SetUp]public void Setup()
        {
            intents=0;h=new SlayerMartUiFlowTests();h.Setup();h.Architecture.RegisterModel(new HotKeyModel());h.Architecture.RegisterModel(new AppStateModel());h.Architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;
            h.Architecture.RegisterUtility(new HotKeyActionRegistry());system=new HotKeySystem();h.Architecture.RegisterSystem(system);
            h.Architecture.RegisterEvent<ArcadeHudActionRequestedEvent>(e=>{if(e.Action.ToString()=="FieldUpgrade")intents++;});Combat();
        }
        private void Combat(bool loading=false)
        {h.Configure(r=>{r.Battle.SlayerMartActive=false;r.Battle.SlayerMartRulesVersion=1;r.Battle.RoundLoading=loading;r.SelfSlayerMart=null;var p=ET.OnlineArcadeBattlePlayerInfo.Create();p.PlayerId=10;p.Alive=true;p.HasStorm=true;r.Battle.Players.Add(p);});h.Network.PushConfigured();}
        [TearDown]public void Cleanup()=>h.Cleanup();
        [Test]public void DefaultsAddRebindableGameplayF()
        {var binding=HotKeyDefaults.Create().SingleOrDefault(b=>b.ActionId==Action);Assert.That(binding,Is.Not.Null);Assert.That(binding.Context,Is.EqualTo(HotKeyContext.Gameplay));Assert.That(binding.BindingPath,Is.EqualTo("<Keyboard>/f"));}
        [Test]public void OldSettingsMergeNewDefaultWithoutLosingCustomPause()
        {var bindings=SettingsData.SanitizeHotKeys(new[]{new HotKeyBinding{ActionId=HotKeyActionIds.TogglePause,Context=HotKeyContext.Gameplay,BindingPath="<Keyboard>/p"}});Assert.That(bindings.Any(b=>b.ActionId==Action),Is.True);Assert.That(bindings.Single(b=>b.ActionId==HotKeyActionIds.TogglePause).BindingPath,Is.EqualTo("<Keyboard>/p"));}
        [Test]public void FDispatchesOneIntentThroughExistingRegistry()
        {Assert.That(system.ExecuteBinding("<Keyboard>/f"),Is.True);Assert.That(intents,Is.EqualTo(1));}
        [Test]public void ReboundKeyUsesNewPathAndStopsUsingF()
        {h.Architecture.GetModel<HotKeyModel>().TryReplace(new HotKeyBinding{ActionId=Action,Context=HotKeyContext.Gameplay,BindingPath="<Keyboard>/g"});system.RebuildActions();Assert.That(system.ExecuteBinding("<Keyboard>/f"),Is.False);Assert.That(system.ExecuteBinding("<Keyboard>/g"),Is.True);Assert.That(intents,Is.EqualTo(1));}
        [Test]public void PauseAndLoadingNeverDispatchSkillIntent()
        {h.Architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Paused;Assert.That(system.ExecuteBinding("<Keyboard>/f"),Is.False);h.Architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;Combat(true);Assert.That(system.ExecuteBinding("<Keyboard>/f"),Is.False);Assert.That(intents,Is.Zero);}
        [Test]public void SettingsPageHasLocalizedActionName()
        {var type=typeof(ArcadeHudUICtrl).Assembly.GetType("Game.SettingControlsPageCtrl");Assert.That(type,Is.Not.Null);var text=(string)type.GetMethod("FormatAction",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Action});Assert.That(text,Does.Contain("风暴"));}
    }
}
