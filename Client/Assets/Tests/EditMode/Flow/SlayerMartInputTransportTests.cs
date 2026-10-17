using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ET;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Game.Tests
{
    /// <summary>真实输入System的代次边界与技能意图，不以本地状态替代服务端伤害裁决。</summary>
    public sealed class SlayerMartInputTransportTests
    {
        private OnlineArcadeRunTests.Harness h;private ET.OnlineArcadeRunSnapshot wire;
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp]public void Setup()
        {h=OnlineArcadeRunTests.CreateHarness();Configure();h.System.StartAsync().GetAwaiter().GetResult();}
        [TearDown]public void Cleanup()=>OnlineArcadeRunTests.ResetArchitecture();
        private void Configure(int generation=1,int arena=1,Action<OnlineArcadeBattleInfo> customize=null)
        {
            h.Network.ConfigureRun=r=>{r.Phase=2;r.NextArena=arena;r.CompletedArena=arena-1;
                var battle=OnlineArcadeBattleInfo.Create();battle.ArenaNumber=arena;battle.RoundGeneration=generation;battle.BattleRevision=generation*10;
                var player=OnlineArcadeBattlePlayerInfo.Create();player.PlayerId=10;player.Alive=true;player.CurrentHealth=player.MaxHealth=100;battle.Players.Add(player);
                customize?.Invoke(battle);r.Battle=battle;wire=r;};
        }
        private OnlineArcadeInputController Input=>(OnlineArcadeInputController)typeof(OnlineArcadeRunSystem).GetField("input",Private).GetValue(h.System);
        private Task<OnlineArcadeInputResult> SubmitUpgrade(bool bomb=true,bool dash=true)
        {var method=typeof(OnlineArcadeRunSystem).GetMethod("SubmitInputAsync");Assert.That(method.GetParameters().Length,Is.EqualTo(8),"客户端缺少独立技能意图");return (Task<OnlineArcadeInputResult>)method.Invoke(h.System,new object[]{1f,0f,0f,1f,true,bomb,dash,true});}
        [Test]public void UpgradeIntentIsExclusiveWithBombAndDash()
        {SubmitUpgrade().GetAwaiter().GetResult();var request=(C2G_SubmitOnlineArcadeInput)h.Network.LastRequest;Assert.That(request.UseFieldUpgrade,Is.True);Assert.That(request.UseBomb||request.UseDash,Is.False);Assert.That(h.System.Snapshot.Players[0].Bombs,Is.EqualTo(1));}
        [Test]public void LoadingBlocksConsumablesButKeepsInputPolling()
        {Configure(customize:b=>b.RoundLoading=true);h.Network.PushConfigured();SubmitUpgrade().GetAwaiter().GetResult();var r=(C2G_SubmitOnlineArcadeInput)h.Network.LastRequest;Assert.That(r.UseFieldUpgrade||r.UseBomb||r.UseDash||r.Firing,Is.False);Assert.That(r.MoveX,Is.Zero);}
        [Test]public void RespawnBlocksUpgradeAndConsumables()
        {Configure(customize:b=>{b.Players[0].Alive=false;b.Players[0].RespawnSeconds=1;});h.Network.PushConfigured();SubmitUpgrade().GetAwaiter().GetResult();var r=(C2G_SubmitOnlineArcadeInput)h.Network.LastRequest;Assert.That(r.UseFieldUpgrade||r.UseBomb||r.UseDash||r.Firing,Is.False);}
        [Test]public void TreasureRegionRejectsUpgradeWithoutFallingBackToConsumables()
        {Configure(customize:b=>b.Players[0].InTreasureRoom=true);h.Network.PushConfigured();SubmitUpgrade().GetAwaiter().GetResult();var r=(C2G_SubmitOnlineArcadeInput)h.Network.LastRequest;Assert.That(r.UseFieldUpgrade||r.UseBomb||r.UseDash,Is.False);}
        [Test]public void ShopBlocksUpgradeEvenBeforePersonalQuoteArrives()
        {Configure(customize:b=>{b.SlayerMartRulesVersion=1;b.SlayerMartActive=true;b.SlayerMartReady=true;});h.Network.PushConfigured();SubmitUpgrade().GetAwaiter().GetResult();Assert.That(((C2G_SubmitOnlineArcadeInput)h.Network.LastRequest).UseFieldUpgrade,Is.False);}
        [UnityTest]public IEnumerator LateInputCannotResurrectEndedRun()
        {
            var old=wire;var response=new TaskCompletionSource<IResponse>();h.Network.OverrideAsyncResponse=_=>response.Task;
            var pending=h.System.SubmitInputAsync(0,0,0,1,false);h.Network.PushRunEnded();response.SetResult(new G2C_SubmitOnlineArcadeInput{Accepted=true,LastAcceptedInputSequence=1,Run=old});
            for(int frame=0;frame<20&&!pending.IsCompleted;frame++)yield return null;Assert.That(pending.IsCompleted,Is.True);
            var result=pending.GetAwaiter().GetResult();Assert.That(h.System.Snapshot.HasRun,Is.False);Assert.That(result.Accepted,Is.False);
        }
        [UnityTest]public IEnumerator NewRoundCancelsOldRequestAndClearsQueuedPulse()
        {
            Input.QueueBomb();var old=wire;var response=new TaskCompletionSource<IResponse>();h.Network.OverrideAsyncResponse=_=>response.Task;
            var pending=h.System.SubmitInputAsync(0,0,0,1,false);var cancellation=(CancellationTokenSource)typeof(OnlineArcadeRunSystem).GetField("inputCancellation",Private).GetValue(h.System);
            Configure(2);h.Network.PushConfigured();bool canceled=cancellation.IsCancellationRequested;int queued=Input.QueuedBombs;
            response.SetResult(new G2C_SubmitOnlineArcadeInput{Accepted=true,LastAcceptedInputSequence=1,Run=old});for(int frame=0;frame<20&&!pending.IsCompleted;frame++)yield return null;
            Assert.That(pending.IsCompleted,Is.True);pending.GetAwaiter().GetResult();Assert.That(canceled,Is.True);Assert.That(queued,Is.Zero);Assert.That(h.System.Snapshot.Battle.RoundGeneration,Is.EqualTo(2));
        }
        [Test]public void CrossArenaBoundaryClearsQueuedConsumable()
        {Input.QueueBomb();Configure(1,2);h.Network.PushConfigured();Assert.That(Input.QueuedBombs,Is.Zero);}
        [Test]public void SameRoundBroadcastKeepsUnacknowledgedPulse()
        {Input.QueueBomb();Configure(customize:b=>b.BattleRevision=11);h.Network.PushConfigured();Assert.That(Input.QueuedBombs,Is.EqualTo(1));}
        [UnityTest]public IEnumerator LateRestoreCannotResurrectEndedRun()
        {
            var old=wire;var response=new TaskCompletionSource<IResponse>();h.Network.OverrideAsyncResponse=_=>response.Task;
            var pending=h.System.RestoreAsync();h.Network.PushRunEnded();response.SetResult(new G2C_GetCurrentOnlineArcadeRun{HasRun=true,Run=old});
            for(int frame=0;frame<20&&!pending.IsCompleted;frame++)yield return null;Assert.That(pending.IsCompleted,Is.True);pending.GetAwaiter().GetResult();
            Assert.That(h.System.Snapshot.HasRun,Is.False,"迟到的恢复响应不能重开已结束的对局");
        }
    }
}
