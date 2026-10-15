using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests
{
    public sealed class OnlineWorldProjectilePresentationTests
    {
        private static ET.G2C_WorldProjectilesChanged Message(long revision, bool terminal=false)
        {
            var m=ET.G2C_WorldProjectilesChanged.Create();m.WorldId=1;m.MapId="map";m.WorldRevision=revision;
            if(terminal){var e=ET.WorldProjectileTermination.Create();e.RuntimeId=1;e.StateRevision=3;m.Terminations.Add(e);}
            else {var p=ET.WorldProjectileInfo.Create();p.RuntimeId=1;p.OwnerUnitId=10;p.OwnerLifecycleRevision=1;p.ProjectileConfigId="projectile.arrow.basic";p.StateRevision=1;p.Speed=14;p.RemainingDistance=14;p.DirectionY=1;m.Projectiles.Add(p);}return m;
        }
        private static OnlineWorldProjectileState State(){var s=new OnlineWorldProjectileState();s.ObserveWorld(1,"map");return s;}
        [Test] public void BirthAndDuplicateConverge(){var s=State();s.Apply(Message(4));s.Apply(Message(4));Assert.That(s.Projectiles.Count,Is.EqualTo(1));Assert.That(s.Projectiles[0].Direction,Is.EqualTo(Vector3.up));}
        [Test] public void TerminationBeforeBirthDoesNotReviveArrow(){var s=State();s.Apply(Message(5,true));s.Apply(Message(4));Assert.That(s.Projectiles,Is.Empty);}
        [Test] public void OlderSnapshotCannotEraseTermination(){var s=State();s.Apply(Message(5,true));s.ReplaceSnapshot(null,4);s.Apply(Message(6));Assert.That(s.Projectiles,Is.Empty);}
        [Test] public void FullSnapshotRejectsOldBirth(){var s=State();s.ReplaceSnapshot(null,5);s.Apply(Message(4));Assert.That(s.Projectiles,Is.Empty);}
        [Test] public void NewWorldClearsOldArrows(){var s=State();s.Apply(Message(4));s.ObserveWorld(2,"map");s.Apply(Message(6));Assert.That(s.Projectiles,Is.Empty);}
        [Test] public void PredictionPreservesVerticalAndClampsRange(){var s=State();s.Apply(Message(4));Assert.That(s.Projectiles.Single().PositionAt(2000),Is.EqualTo(Vector3.up*14));}
    }
}
