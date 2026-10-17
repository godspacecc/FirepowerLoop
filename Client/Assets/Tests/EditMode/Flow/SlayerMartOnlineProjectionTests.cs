using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ET;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class SlayerMartOnlineProjectionTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [TearDown] public void TearDown()=>OnlineArcadeRunTests.ResetArchitecture();
        private static object Prop(object value,string name)
        {var p=value.GetType().GetProperty(name);Assert.That(p,Is.Not.Null,"缺少权威投影字段："+name);return p.GetValue(value);}
        private static void Wire(ET.OnlineArcadeRunSnapshot r,bool prepared=true)
        {
            SlayerMartOnlineFlowTests.SetShop(r);r.Battle.RoundGeneration=5;
            typeof(OnlineArcadeBattleInfo).GetProperty("SlayerMartPrepared")?.SetValue(r.Battle,prepared);
            r.Battle.SlayerMartCashBoxIds.AddRange(new[]{1,3});
            var p=OnlineArcadeBattlePlayerInfo.Create();p.PlayerId=10;p.Alive=true;p.CurrentHealth=p.MaxHealth=100;
            p.VestSlots=2;p.Plates=1;p.HasStorm=true;p.StormCharge=99;p.StormSeconds=5;
            for(int i=0;i<2;i++){var b=OnlineSlayerMartSupportInfo.Create();b.ProductId="arcade.powerup.saw";b.InstanceId="saw-instance";b.Kind=2;b.BodyIndex=i;b.PositionX=i==0?3:-3;b.PositionZ=2;b.RemainingSeconds=100;p.SlayerMartSupports.Add(b);}
            r.Battle.Players.Add(p);
        }
        private static OnlineArcadeRunTests.Harness Open(Action<ET.OnlineArcadeRunSnapshot> configure=null)
        {var h=OnlineArcadeRunTests.CreateHarness();h.Network.ConfigureRun=configure??(r=>Wire(r));h.System.StartAsync().GetAwaiter().GetResult();return h;}
        [Test] public void PublicRegionCopiesClockAndPreparedIdentity()
        {var q=Prop(Open().System.Snapshot.Battle,"SlayerMart");Assert.That(Prop(q,"Prepared"),Is.True);Assert.That(Prop(q,"Generation"),Is.EqualTo(4));Assert.That(Prop(q,"RemainingSeconds"),Is.EqualTo(290f));}
        [Test] public void PublicRegionListsAreIndependentOfProtocol()
        {ET.OnlineArcadeRunSnapshot original=null;var h=Open(r=>{Wire(r);original=r;});original.Battle.SlayerMartCashBoxIds.Clear();original.Battle.SlayerMartMembers.Clear();var q=Prop(h.System.Snapshot.Battle,"SlayerMart");Assert.That(((IEnumerable)Prop(q,"CashBoxIds")).Cast<object>().Count(),Is.EqualTo(2));Assert.That(((IEnumerable)Prop(q,"Members")).Cast<object>().Count(),Is.EqualTo(1));}
        [Test] public void PlayerCopiesArmorAndStormWithoutApplyingLocally()
        {var p=Open().System.Snapshot.Battle.Players.Single();Assert.That(Prop(p,"VestSlots"),Is.EqualTo(2));Assert.That(Prop(p,"Plates"),Is.EqualTo(1));Assert.That(Prop(p,"HasStorm"),Is.True);Assert.That(Prop(p,"StormCharge"),Is.EqualTo(99));}
        [Test] public void SupportBodiesCopyIdentityPositionAndList()
        {ET.OnlineArcadeRunSnapshot original=null;var h=Open(r=>{Wire(r);original=r;});var bodies=((IEnumerable)Prop(h.System.Snapshot.Battle.Players.Single(),"SlayerMartSupports")).Cast<object>().ToArray();original.Battle.Players[0].SlayerMartSupports.Clear();Assert.That(bodies.Length,Is.EqualTo(2));Assert.That(Prop(bodies[1],"InstanceId"),Is.EqualTo("saw-instance"));Assert.That(Prop(bodies[1],"BodyIndex"),Is.EqualTo(1));Assert.That(Prop(bodies[1],"PositionX"),Is.EqualTo(-3f));}
        [Test] public void ShopReadinessDoesNotAcknowledgeUnloadedCrypt()
        {var h=Open();var view=new View{Ready=false};typeof(OnlineArcadeRunSystem).GetField("combatView",Private).SetValue(h.System,view);h.System.SubmitInputAsync(0,0,0,1,false).GetAwaiter().GetResult();Assert.That(((C2G_SubmitOnlineArcadeInput)h.Network.LastRequest).RoundReady,Is.False);view.Ready=true;h.System.SubmitInputAsync(0,0,0,1,false).GetAwaiter().GetResult();Assert.That(((C2G_SubmitOnlineArcadeInput)h.Network.LastRequest).RoundReady,Is.True);}
        [Test] public void ShopInputCannotSendCombatConsumables()
        {var h=Open();h.System.SubmitInputAsync(1,0,0,1,true,true,true).GetAwaiter().GetResult();var request=(C2G_SubmitOnlineArcadeInput)h.Network.LastRequest;Assert.That(request.Firing||request.UseBomb||request.UseDash,Is.False);}
        private static (OnlineArcadePresentationSystem,View) Present(OnlineArcadeRunClientSnapshot snapshot)
        {
            var architecture=new Game();var account=new OnlineAccountModel();architecture.RegisterModel(account);
            account.Publish(new OnlineAccountSnapshot(OnlineConnectionState.Authenticated,OnlineOperationState.Idle,OnlineAccountResultCode.Success,"one",1,10,1,false,"one"));
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            var system=new OnlineArcadePresentationSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);var view=new View();
            void Set(string n,object v)=>typeof(OnlineArcadePresentationSystem).GetField(n,Private).SetValue(system,v);
            Set("view",view);Set("catalog",catalog);Set("activeArena",catalog.Arenas.Single(a=>a.ArenaNumber==3));Set("active",true);Set("latest",snapshot);
            Apply(system,snapshot.Battle);return(system,view);
        }
        private static void Apply(OnlineArcadePresentationSystem system,OnlineArcadeBattleSnapshot battle)=>typeof(OnlineArcadePresentationSystem).GetMethod("ApplyAuthoritativeSnapshot",Private).Invoke(system,new object[]{battle});
        [Test] public void PreparedCryptUsesConfiguredBoundsAndNoCombatLayout()
        {var (_,v)=Present(Open().System.Snapshot);Assert.That(v.Crypt,Is.Not.Null);Assert.That(v.Width,Is.EqualTo(v.Crypt.HalfWidth));Assert.That(v.LayoutCalls,Is.Zero);Assert.That(v.BoxIds,Is.EqualTo(new[]{1,3}));}
        [Test] public void SelectionBeforePreparedDoesNotRebuildCombatMap()
        {var (_,v)=Present(Open(r=>Wire(r,false)).System.Snapshot);Assert.That(v.Begins,Is.Zero);Assert.That(v.Crypt,Is.Null);}
        [Test] public void RepeatedCryptFrameDoesNotRebuildAndOldGenerationIsIgnored()
        {
            var h=Open();var(s,v)=Present(h.System.Snapshot);int began=v.Begins;Apply(s,h.System.Snapshot.Battle);
            var old=new OnlineArcadeBattleSnapshot(3,3,4,0,0,2,99,0,h.System.Snapshot.Battle.Players,null,roundGeneration:4,slayerMart:h.System.Snapshot.Battle.SlayerMart);
            Apply(s,old);Assert.That(v.Begins,Is.EqualTo(began));Assert.That(v.Crypt,Is.Not.Null);
            Assert.That(typeof(OnlineArcadePresentationSystem).GetField("lastAppliedBattleRevision",Private).GetValue(s),Is.EqualTo(7L));
        }
        [Test] public void MenuBlocksMovementWhileReadinessPollingContinues()
        {var h=Open(r=>{Wire(r);r.Battle.SlayerMartMembers[0].MenuOpen=true;});typeof(OnlineArcadeRunSystem).GetField("combatView",Private).SetValue(h.System,new View{Ready=true});h.System.SubmitInputAsync(1,1,0,1,true).GetAwaiter().GetResult();var request=(C2G_SubmitOnlineArcadeInput)h.Network.LastRequest;Assert.That(request.RoundReady,Is.True);Assert.That(request.MoveX,Is.Zero);Assert.That(request.MoveZ,Is.Zero);}
        [Test] public void NewCryptGenerationRebuildsThenRepeatedFrameKeepsResources()
        {var h=Open();var(s,v)=Present(h.System.Snapshot);h.Network.ConfigureRun=r=>{Wire(r);r.Battle.RoundGeneration=6;r.Battle.BattleRevision=8;};h.Network.PushConfigured();Apply(s,h.System.Snapshot.Battle);Apply(s,h.System.Snapshot.Battle);Assert.That(v.Begins,Is.EqualTo(2));}
        [Test] public void CombatSupportProjectionUsesActualBodiesAndSkipsLegacyOrbit()
        {
            var h=Open(r=>{Wire(r);r.Battle.SlayerMartActive=false;r.Battle.RoundGeneration=0;
                var p=r.Battle.Players[0];var legacy=OnlineArcadePowerupInfo.Create();legacy.ProductId="arcade.powerup.saw";legacy.RemainingSeconds=100;p.Powerups.Add(legacy);});
            var(_,v)=Present(h.System.Snapshot);Assert.That(v.Bodies.Count,Is.EqualTo(2));Assert.That(v.Bodies[1],Is.EqualTo(new Vector2(-3,2)));Assert.That(v.LegacyPowerups,Is.Zero);
        }
        private sealed class View:IArcadeCombatViewPort
        {
            public bool Ready;public bool IsActive=>true;public bool IsSlayerMartRegionReady=>Ready;
            public int Begins,LayoutCalls,LegacyPowerups;public float Width;public ArcadeSlayerMartRegionLayout Crypt;public int[] BoxIds;
            public readonly List<Vector2> Bodies=new();
            public void SetSlayerMartRegion(ArcadeSlayerMartRegionLayout layout,IReadOnlyList<int> ids){Crypt=layout;BoxIds=ids.ToArray();}
            public void SetRoundLayout(ArcadeRoundLayoutParameters layout){LayoutCalls++;}
            public void SetSlayerMartSupport(long owner,string instance,int body,ArcadePowerupDefinition def,Vector2 position,Vector2 direction){Bodies.Add(position);}
            public void SetPowerup(long owner,ArcadePowerupDefinition def,Vector2 position,Vector2 direction){LegacyPowerups++;}
            public void Begin(int n,float w,float h,IReadOnlyList<ArcadeObstacleDefinition> obstacles){Begins++;Width=w;}
            public void End(){}public bool TryScreenToArena(Vector2 s,out Vector2 p){p=default;return false;}
            public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i){}public void SetPlayerHealth(int a,int b){}
            public void SetRemotePlayer(long id,Vector2 p,Vector2 f,bool v,bool i,int a,int b){}public void RemoveRemotePlayer(long id){}
            public void SetEnemy(int id,string c,Vector2 p,int a,int b,bool t=false,int seq=0,bool health=false){}public void RemoveEnemy(int id,bool death=false){}
            public void SetProjectile(int id,bool h,Vector2 p,Vector2 d){}public void RemoveProjectile(int id){}
            public void SetDrop(int id,ArcadeDropDefinition d,Vector2 p){}public void RemoveDrop(int id){}public void TickPlayerFeedback(float s){}
        }
    }
}
