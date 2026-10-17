using System;
using System.Linq;
using System.Reflection;
using ET;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>合作救援的正式协议投影、输入脉冲与 HUD 资源合同。</summary>
    public sealed class CoopRescueClientTests
    {
        private static object Copy(float seconds,float rate)
        {
            using var info=OnlineArcadeBattleInfo.Create();var player=OnlineArcadeBattlePlayerInfo.Create();player.PlayerId=1;
            typeof(OnlineArcadeBattlePlayerInfo).GetProperty("DownedSeconds").SetValue(player,seconds);
            typeof(OnlineArcadeBattlePlayerInfo).GetProperty("RescueRate").SetValue(player,rate);info.Players.Add(player);
            var snapshot=(OnlineArcadeBattleSnapshot)typeof(OnlineArcadeRunSystem).GetMethod("CopyBattle",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{info});
            return snapshot.Players[0];
        }
        private static void Queue(OnlineArcadeInputController input,long target)
        {
            var method=typeof(OnlineArcadeInputController).GetMethod("QueueDonateLife");Assert.That(method,Is.Not.Null,"missing donation pulse");method.Invoke(input,new object[]{target});
        }
        private static long Target(OnlineArcadeInputController input)
        {
            var frame=input.Read(null,Vector2.zero);var property=frame.GetType().GetProperty("DonateLifeTargetId");Assert.That(property,Is.Not.Null);return (long)property.GetValue(frame);
        }
        [Test] public void OfficialProjectionPreservesDownedTime(){var value=Copy(43,1);var p=value.GetType().GetProperty("DownedSeconds");Assert.That(p,Is.Not.Null);Assert.That(p.GetValue(value),Is.EqualTo(43f));}
        [Test] public void OfficialProjectionPreservesRescueRate(){var value=Copy(20,2);var p=value.GetType().GetProperty("RescueRate");Assert.That(p,Is.Not.Null);Assert.That(p.GetValue(value),Is.EqualTo(2f));}
        [Test] public void DonationPulsePersistsUntilAcknowledged(){using var input=new OnlineArcadeInputController();Queue(input,10);Assert.That(Target(input),Is.EqualTo(10));Assert.That(Target(input),Is.EqualTo(10));}
        [Test] public void DisableClearsDonationAtBoundary(){using var input=new OnlineArcadeInputController();Queue(input,10);input.Disable(true);Assert.That(Target(input),Is.Zero);}
        [Test] public void AcknowledgedDonationDoesNotRepeat(){using var input=new OnlineArcadeInputController();Queue(input,10);var method=typeof(OnlineArcadeInputController).GetMethod("AcknowledgeDonation");Assert.That(method,Is.Not.Null);method.Invoke(input,new object[]{10L});Assert.That(Target(input),Is.Zero);}
        [Test] public void RepeatedClicksCoalesceOneTarget(){using var input=new OnlineArcadeInputController();Queue(input,10);Queue(input,10);var method=typeof(OnlineArcadeInputController).GetMethod("AcknowledgeDonation");Assert.That(method,Is.Not.Null);method.Invoke(input,new object[]{10L});Assert.That(Target(input),Is.Zero);}
        [Test] public void DonationHasRebindableDefault(){Assert.That(HotKeyDefaults.Create().Any(b=>b.ActionId=="arcade.donate-life"&&b.BindingPath=="<Keyboard>/r"),Is.True);}
        [Test] public void SafeNodeReadinessUsesServerIdentityForZeroLifeMember()
        {
            foreach(bool eligible in new[]{false,true})
            {
                using var source=ET.OnlineArcadeRunSnapshot.Create();var player=OnlineArcadePlayerInfo.Create();player.PlayerId=1;player.Lives=0;
                var authority=player.GetType().GetProperty("CanReadyForArena");Assert.That(authority,Is.Not.Null);authority.SetValue(player,eligible);source.Players.Add(player);
                var snapshot=(OnlineArcadeRunClientSnapshot)typeof(OnlineArcadeRunSystem).GetMethod("CopySnapshot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(new OnlineArcadeRunSystem(),new object[]{source,OnlineArcadeResultCode.Success});
                var can=typeof(ArcadeResumeUICtrl).GetMethod("CanReadyOnline",BindingFlags.Static|BindingFlags.NonPublic);Assert.That(can,Is.Not.Null);
                Assert.That(can.Invoke(null,new object[]{snapshot.Players[0],true}),Is.EqualTo(eligible));
                Assert.That(can.Invoke(null,new object[]{snapshot.Players[0],false}),Is.False);
            }
        }
        [Test] public void HudPrefabHasSerializedDonationButton(){var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab");var ui=root.GetComponent<ArcadeHudUI>();var field=ui.GetType().GetField("BtnDonateLife");Assert.That(field,Is.Not.Null);Assert.That(field.GetValue(ui),Is.Not.Null);}
        [Test] public void RescueMarkerHasDistinctCrossAndReleasesWithoutColliders()
        {
            var root=new GameObject("CoopMarkerTest");var view=new ArcadeCombatViewController();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(ArcadeCombatViewController).GetField("root",flags).SetValue(view,root);
            try
            {
                var tables=new Tables(n=>new Luban.ByteBuf(System.IO.File.ReadAllBytes(System.IO.Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
                view.ConfigureRoundBounds(19.5f,19.5f,.55f);
                view.SetRoundLayout(FirepowerLoop.Arcade.ArcadeRoundLayoutParameters.FromConfig(tables.TbArcadeRoundLayout.DataList.First(p=>p.ArenaConfigNumber==3)));
                view.SetRoundContent(0,new[]{new FirepowerLoop.Arcade.ArcadeContentAreaSnapshot(-20000,12,3,4,2,true)});
                Assert.That(root.GetComponentsInChildren<Transform>().Any(t=>t.name=="RescueCrossHorizontal"),Is.True);
                Assert.That(root.GetComponentsInChildren<Collider>(true),Is.Empty);
                view.SetRoundContent(0,Array.Empty<FirepowerLoop.Arcade.ArcadeContentAreaSnapshot>());
                Assert.That(root.GetComponentsInChildren<Transform>().Any(t=>t.name=="RescueCrossHorizontal"),Is.False);
            }
            finally{typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",flags).Invoke(view,null);UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
