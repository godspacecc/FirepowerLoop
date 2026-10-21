using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using TMPro;

namespace Game.Tests
{
    /// <summary>正式HUD只读本人能力，操作仍经领域输入事件。</summary>
    public sealed class SlayerMartFieldHudTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private SlayerMartHudEntryTests h;
        [SetUp]public void Setup(){h=new SlayerMartHudEntryTests();h.Setup();h.h.Architecture.RegisterModel(new HotKeyModel());Combat();}
        [TearDown]public void Cleanup()=>h.Cleanup();
        private void Combat(int charge=100,float seconds=0,bool alive=true,bool loading=false,bool owned=true,int version=1)
        {h.Configure(r=>{r.Battle.SlayerMartActive=false;r.Battle.SlayerMartRulesVersion=version;r.Battle.RoundLoading=loading;r.SelfSlayerMart=null;var p=r.Battle.Players[0];p.HasStorm=owned;p.StormCharge=charge;p.StormSeconds=seconds;p.VestSlots=2;p.Plates=1;p.Alive=alive;});}
        private void Sync()=>h.Sync();
        [Test]public void OwnedStormShowsActualChargeAndArmor(){Combat(42);Sync();Assert.That(h.ui.TextStormCharge.gameObject.activeSelf,Is.True);Assert.That(h.ui.TextStormCharge.text,Does.Contain("42%"));Assert.That(h.ui.DesktopArmor.text,Does.Contain("1/2"));Assert.That(h.ui.BtnFieldUpgrade.interactable,Is.False);}
        [Test]public void FullChargeAllowsIntent(){Sync();Assert.That(h.ui.BtnFieldUpgrade.gameObject.activeSelf,Is.True);Assert.That(h.ui.BtnFieldUpgrade.interactable,Is.True);}
        [Test]public void ActiveStormDisplaysRemainingSeconds(){Combat(0,2.2f);Sync();Assert.That(h.ui.TextStormCharge.text,Does.Contain("3"));Assert.That(h.ui.TextStormCharge.text,Does.Not.Contain("/100"));Assert.That(h.ui.BtnFieldUpgrade.interactable,Is.False);}
        [Test]public void DeadAndLoadingDisableAbility(){Combat(alive:false);Sync();Assert.That(h.ui.BtnFieldUpgrade.interactable,Is.False);Combat(loading:true);Sync();Assert.That(h.ui.BtnFieldUpgrade.interactable,Is.False);}
        [Test]public void ShopHidesCombatAbility(){h.Configure();Sync();Assert.That(h.ui.BtnFieldUpgrade.gameObject.activeSelf,Is.False);}
        [Test]public void UnownedStormHidesButtonAndCharge(){Combat(owned:false);Sync();Assert.That(h.ui.BtnFieldUpgrade.gameObject.activeSelf,Is.False);Assert.That(h.ui.TextStormCharge.gameObject.activeSelf,Is.False);Assert.That(h.ui.DesktopArmor.gameObject.activeSelf,Is.True);}
        [Test]public void LegacyRulesHideAllNewStatus(){Combat(version:0);Sync();Assert.That(h.ui.BtnFieldUpgrade.gameObject.activeSelf,Is.False);Assert.That(h.ui.TextStormCharge.gameObject.activeSelf,Is.False);Assert.That(h.ui.DesktopArmor.gameObject.activeSelf,Is.False);}
        [Test]public void ButtonSendsOnlySkillIntent(){int count=0;h.h.Architecture.RegisterEvent<ArcadeHudActionRequestedEvent>(e=>{Assert.That(e.Action,Is.EqualTo(ArcadeHudActionKind.FieldUpgrade));count++;});Sync();h.ui.BtnFieldUpgrade.onClick.Invoke();Assert.That(count,Is.EqualTo(1));Assert.That(h.h.Online.Snapshot.Battle.Players.First(p=>p.PlayerId==10).StormCharge,Is.EqualTo(100));}
        [Test]public void DesktopLabelFollowsReboundKey(){h.h.Architecture.GetModel<HotKeyModel>().TryReplace(new HotKeyBinding{ActionId=HotKeyActionIds.UseFieldUpgrade,Context=HotKeyContext.Gameplay,BindingPath="<Keyboard>/g"});Sync();Assert.That(h.ui.BtnFieldUpgrade.GetComponentInChildren<TextMeshProUGUI>(true).text.ToUpperInvariant(),Does.Contain("[G]"));}
        [Test]public void SingleSnapshotCarriesAbilityWithoutNetwork(){typeof(ArcadeHudUICtrl).GetField("onlineMode",Private).SetValue(h.controller,false);var snapshot=new ArcadeCombatHudSnapshot();foreach(var pair in new[]{("SlayerMartRulesVersion",(object)1),("HasStorm",true),("StormCharge",73),("VestSlots",2),("Plates",1),("PlayerHealth",100)}){var p=typeof(ArcadeCombatHudSnapshot).GetProperty(pair.Item1);Assert.That(p,Is.Not.Null,pair.Item1);p.SetValue(snapshot,pair.Item2);}typeof(ArcadeHudUICtrl).GetField("combat",Private).SetValue(h.controller,snapshot);var sync=typeof(ArcadeHudUICtrl).GetMethod("SyncFieldUpgradeUi",Private);Assert.That(sync,Is.Not.Null);sync.Invoke(h.controller,null);Assert.That(h.ui.TextStormCharge.text,Does.Contain("73%"));Assert.That(h.ui.DesktopArmor.text,Does.Contain("1/2"));}
    }
}
