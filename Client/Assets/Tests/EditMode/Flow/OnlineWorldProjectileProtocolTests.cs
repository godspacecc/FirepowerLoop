using System;
using MemoryPack;
using NUnit.Framework;
namespace Game.Tests
{
    public sealed class OnlineWorldProjectileProtocolTests
    {
        [Test] public void AttackRoundTripPreservesVerticalAimAndMuzzle()
        {
            using var request=ET.C2G_WorldMeleeAttack.Create();var type=request.GetType();
            var aim=type.GetProperty("AimY");Assert.That(aim,Is.Not.Null,"攻击协议丢失垂直方向");aim.SetValue(request,.6f);
            type.GetProperty("OriginY").SetValue(request,1.3f);type.GetProperty("HasProjectileOrigin").SetValue(request,true);
            using var decoded=MemoryPackSerializer.Deserialize<ET.C2G_WorldMeleeAttack>(MemoryPackSerializer.Serialize(request));
            Assert.That(aim.GetValue(decoded),Is.EqualTo(.6f));Assert.That(type.GetProperty("OriginY").GetValue(decoded),Is.EqualTo(1.3f));
        }
        [Test] public void FullWorldRoundTripIncludesFlyingProjectiles()
        {
            using var world=ET.OnlineWorldSnapshot.Create();var property=world.GetType().GetProperty("Projectiles");
            Assert.That(property,Is.Not.Null,"重连快照缺少飞行弹体");
            var list=(System.Collections.IList)property.GetValue(world);var projectileType=typeof(ET.OnlineWorldSnapshot).Assembly.GetType("ET.WorldProjectileInfo");
            var p=Activator.CreateInstance(projectileType);projectileType.GetProperty("RuntimeId").SetValue(p,73L);projectileType.GetProperty("DirectionY").SetValue(p,-.5f);list.Add(p);
            using var copy=MemoryPackSerializer.Deserialize<ET.OnlineWorldSnapshot>(MemoryPackSerializer.Serialize(world));
            var decoded=(System.Collections.IList)property.GetValue(copy);Assert.That(decoded.Count,Is.EqualTo(1));Assert.That(projectileType.GetProperty("DirectionY").GetValue(decoded[0]),Is.EqualTo(-.5f));
        }
    }
}
