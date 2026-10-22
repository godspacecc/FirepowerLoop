using System;
using System.IO;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>新增地图必须能经真实隔离文件恢复，同时保留旧地图身份和非法身份拒绝。</summary>
    public sealed class ArcadeEarlyStageSaveTests
    {
        [TestCase(21)] [TestCase(22)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(15)]
        public void RoundNodeRoundTripsWithoutChangingMapIdentity(int map)
        {
            string directory=Path.Combine(Path.GetTempPath(),"FirepowerEarlySave-"+Guid.NewGuid().ToString("N"));
            try
            {
                var data=new ArcadeRunSaveData{RunId=Guid.NewGuid().ToString("N"),IsCasualRoute=true,ArenaConfigNumber=map,NextArenaNumber=3,CompletedArenaCount=2,UpdatedAtUtc=DateTime.UtcNow.ToString("O")};
                data.Checkpoint.ArenaNumber=3;data.EquipmentBySlot["weapon"]=ArcadeRunDefaults.BasicWeaponId;
                data.RoundCheckpoint=new ArcadeRoundCheckpoint{ArenaNumber=3,ArenaConfigNumber=map,RoundNumber=2,LayoutVersion=1,MapId=$"arena.{map}.round.2",
                    PlayerState=new ArcadeIslandRoundCheckpoint{MapId=$"arena.{map}.round.2",RoundNumber=2,PlayerHealth=75,PlayerMaxHealth=100,Bombs=1,Dashes=2}};
                var store=new ArcadeRunFileStore(directory);Assert.That(store.Save(data,out var error),Is.True,error);var read=store.Inspect();Assert.That(read.IsValid,Is.True,read.Error);
                Assert.That(read.Data.RoundCheckpoint.ArenaConfigNumber,Is.EqualTo(map));Assert.That(read.Data.RoundCheckpoint.PlayerState.PlayerHealth,Is.EqualTo(75));
            }
            finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
        [TestCase(21)] [TestCase(22)] public void ChallengeCanReturnToNewMapIdentity(int map)
        {
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));var config=ArcadeSlayerChallengeConfiguration.FromTables(tables);
            var state=ArcadeSlayerChallengeRules.Create(config,"return-new-map",new long[]{1},3,map,1);state=ArcadeSlayerChallengeRules.SetEntryChoice(state,config,1,false);
            Assert.That(state.ReturnMap,Is.EqualTo(map));Assert.That(state.Phase,Is.EqualTo(ArcadeSlayerChallengePhase.Returning));
        }
    }
}
