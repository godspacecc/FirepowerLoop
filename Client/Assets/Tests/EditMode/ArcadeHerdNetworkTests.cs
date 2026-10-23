using System.Linq;
using System.Reflection;
using System.IO;
using ET;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using Luban;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>真正网络转换必须保留牛的地图身份和动作年龄，不持有可变协议消息。</summary>
    public sealed class ArcadeHerdNetworkTests
    {
        [Test] public void ImpactCopiesCurrentPhaseForPlayersAndEnemiesAndStopsPrediction()
        {
            using var source=OnlineArcadeBattleInfo.Create();var player=OnlineArcadeBattlePlayerInfo.Create();player.PlayerId=4;player.HerdImpactRemaining=.6f;player.HerdImpactDirectionX=1;source.Players.Add(player);
            var enemy=OnlineArcadeEnemyInfo.Create();enemy.RuntimeId=8;enemy.HerdImpactRemaining=1.2f;enemy.HerdImpactDirectionX=-1;source.Enemies.Add(enemy);
            var method=typeof(OnlineArcadeRunSystem).GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.ReturnType==typeof(OnlineArcadeBattleSnapshot)&&m.GetParameters().Length==1);
            var copy=(OnlineArcadeBattleSnapshot)method.Invoke(null,new object[]{source});player.HerdImpactRemaining=0;enemy.HerdImpactRemaining=0;
            Assert.That(copy.Players[0].HerdImpact.Remaining,Is.EqualTo(.6f));Assert.That(copy.Enemies[0].HerdImpact.Remaining,Is.EqualTo(1.2f));Assert.That(copy.Enemies[0].HerdImpact.DirectionX,Is.EqualTo(-1));
            Assert.That(OnlineArcadePresentationSystem.PredictedMovementDirection(Vector2.up,copy.Players[0]),Is.EqualTo(Vector2.zero));
        }
        [Test] public void ProtocolToPresentationCopiesHerdPose()
        {
            var source=OnlineArcadeBattleInfo.Create();source.RoundGeneration=7;var cow=OnlineArcadeHerdInfo.Create();cow.MapInstanceId=7;cow.Id=12;cow.Phase=1;cow.X=-20;cow.Z=-14;cow.DirectionX=1;cow.PhaseSeconds=.4;source.Herds.Add(cow);
            var method=typeof(OnlineArcadeRunSystem).GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.ReturnType==typeof(OnlineArcadeBattleSnapshot)&&m.GetParameters().Length==1);
            var target=(OnlineArcadeBattleSnapshot)method.Invoke(null,new object[]{source});var property=target.GetType().GetProperty("Herds");Assert.That(property,Is.Not.Null,"联机表现尚未转换牛群");
            var poses=((System.Collections.Generic.IEnumerable<ArcadeHerdSnapshot>)property.GetValue(target)).ToArray();cow.X=90;Assert.That(poses.Length,Is.EqualTo(1));Assert.That(poses[0].X,Is.EqualTo(-20));Assert.That(poses[0].MapInstanceId,Is.EqualTo(7));Assert.That(poses[0].PhaseSeconds,Is.EqualTo(.4));source.Dispose();
        }
        [TestCase(21,null,false)] [TestCase(3,"123:4:arcade.armory.second",true)] public void PresentationUsesAuthorityRoomIdentity(int map,string roomId,bool armory)
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            var system=new OnlineArcadePresentationSystem();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(OnlineArcadePresentationSystem).GetField("catalog",flags).SetValue(system,catalog);typeof(OnlineArcadePresentationSystem).GetField("activeArena",flags).SetValue(system,catalog.Arenas.Single(p=>p.ArenaNumber==map));
            var battle=new OnlineArcadeBattleSnapshot(2,3,4,1,1,2,1,0,null,null,arenaConfigNumber:map,treasureRoomId:roomId);
            var latest=new OnlineArcadeRunClientSnapshot(true,1,2,1,OnlineArcadeRunPhase.Arena,1,2,"test",null,OnlineArcadeOperationState.Idle,OnlineArcadeResultCode.Success,battle);
            typeof(OnlineArcadePresentationSystem).GetField("latest",flags).SetValue(system,latest);
            var room=(ArcadeTreasureRoomParameters)typeof(OnlineArcadePresentationSystem).GetMethod("TreasureParameters",flags).Invoke(system,null);
            Assert.That(room?.IsArmory??false,Is.EqualTo(armory));if(!armory)Assert.That(room,Is.Null);
        }
    }
}
