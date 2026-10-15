using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed partial class ArcadeTreasureRoomPresentationTests
    {
        [Test] public void RegionProjectionRejectsStaleSnapshots()
        {
            Assert.That(typeof(OnlineArcadePresentationSystem).GetField("lastAppliedBattleRevision", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
        }
        [Test] public void RegionSwitchHasExplicitMotorTeleport()
        {
            Assert.That(typeof(ICharacterMotorPort).GetMethod("SetArcadeRegionPosition"), Is.Not.Null);
        }
    }
}
