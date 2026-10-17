using System;
using System.IO;
using System.Linq;
using FirepowerLoop.Arcade;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>真实文件与Unity反序列化路径；只在独立临时根操作。</summary>
    public sealed class SlayerMartCheckpointTests
    {
        [Test] public void SavesCashCardsAndReceiptsTogether()=>WithStore(store=>
        {var data=Make();Assert.That(store.Save(data,out var error),Is.True,error);var read=store.Inspect();Assert.That(read.IsValid,Is.True,read.Error);Assert.That(read.Data.SlayerMart.Players[0].Cash,Is.EqualTo(15000));Assert.That(read.Data.SlayerMart.Receipts.Count,Is.EqualTo(1));Assert.That(read.Data.SlayerMart.Players[0].Offers.Count,Is.EqualTo(16));});
        [Test] public void OldRulesRejectNewShopPayload()
        {var data=Make();data.SlayerMartRulesVersion=0;Assert.That(ArcadeRunFileStore.TryValidate(data,out _),Is.False);}
        [Test] public void UnknownRulesAreRejected()
        {var data=Make();data.SlayerMartRulesVersion=2;Assert.That(ArcadeRunFileStore.TryValidate(data,out _),Is.False);}
        [Test] public void CloneOwnsRootAndRoundCopies()
        {var data=Make();var clone=data.Clone();clone.SlayerMart.Players[0].Cash=0;clone.RoundCheckpoint.SlayerMart.Receipts.Clear();Assert.That(data.SlayerMart.Players[0].Cash,Is.EqualTo(15000));Assert.That(data.RoundCheckpoint.SlayerMart.Receipts.Count,Is.EqualTo(1));}
        [Test] public void UnknownMainFileIsPreserved()=>WithStore(store=>
        {var data=Make();Assert.That(store.Save(data,out _),Is.True);var raw=JObject.FromObject(data);raw["SlayerMartRulesVersion"]=2;string text=raw.ToString();File.WriteAllText(store.RunPath,text);Assert.That(store.Inspect().IsValid,Is.False);Assert.That(File.ReadAllText(store.RunPath),Is.EqualTo(text));});
        [Test] public void OpenShopCannotBeCheckpoint()
        {var data=Make();data.SlayerMart.Closing=false;data.RoundCheckpoint.SlayerMart=data.SlayerMart.Clone();Assert.That(ArcadeRunFileStore.TryValidate(data,out _),Is.False);}
        [Test] public void RootAndRoundMustDescribeSameLedger()
        {var data=Make();data.RoundCheckpoint.SlayerMart.Players[0].Cash++;Assert.That(ArcadeRunFileStore.TryValidate(data,out _),Is.False);}
        [Test] public void ShopCannotPrecedeEleventhRound()
        {var data=Make();data.RoundCheckpoint.RoundNumber=3;data.RoundCheckpoint.MapId="arena.4.round.3";data.RoundCheckpoint.PlayerState.RoundNumber=3;data.RoundCheckpoint.PlayerState.MapId="arena.4.round.3";Assert.That(ArcadeRunFileStore.TryValidate(data,out _),Is.False);}
        private static ArcadeRunSaveData Make()
        {
            string run=new string('a',32);var shop=new ArcadeSlayerMartState{RegionInstanceId=run+":slayermart:1",Visit=1,Generation=1,Revision=2,Ready=true,Closing=true,RemainingSeconds=200,
                Players=new(){new(){PlayerId=1,Cash=15000,Lives=3,Offers=Enumerable.Range(1,16).Select(n=>"slayermart.item."+n.ToString("00")).ToList()}},
                Receipts=new(){new(){PlayerId=1,OperationId="buy",Fingerprint="bomb"}}};
            var data=new ArcadeRunSaveData{RunId=run,IsCasualRoute=true,SlayerMartRulesVersion=1,SlayerMart=shop,CompletedArenaCount=2,NextArenaNumber=3,ArenaConfigNumber=4,
                UpdatedAtUtc=DateTime.UtcNow.ToString("O"),Checkpoint=new(){ArenaNumber=3},RoundCheckpoint=new(){ArenaNumber=3,ArenaConfigNumber=4,RoundNumber=4,LayoutVersion=1,MapId="arena.4.round.4",SlayerMart=shop.Clone(),
                PlayerState=new(){RoundNumber=4,MapId="arena.4.round.4",PlayerHealth=100,PlayerMaxHealth=100,Bombs=1,Dashes=2}}};data.EquipmentBySlot["weapon"]="arcade.weapon.basic";return data;
        }
        private static void WithStore(Action<ArcadeRunFileStore> body)
        {
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"FirepowerLoopSlayerSaveTests",Guid.NewGuid().ToString("N")));
            try{body(new ArcadeRunFileStore(root));}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
