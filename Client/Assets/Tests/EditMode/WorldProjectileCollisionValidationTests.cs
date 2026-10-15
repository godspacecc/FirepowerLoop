using System;
using NUnit.Framework;
namespace Game.Tests
{
    public sealed class WorldProjectileCollisionValidationTests
    {
        [Test] public void RejectsMissingMapCollisionInsteadOfAllowingShots()
        {var t=typeof(Game).Assembly.GetType("Game.WorldProjectileCollisionValidation");Assert.That(t,Is.Not.Null);object[] args={new WorldProjectileCollisionData(),null};Assert.That(t.GetMethod("TryValidate").Invoke(null,args),Is.False);Assert.That(args[1],Is.Not.Null.And.Not.Empty);}
        private static WorldProjectileCollisionData ValidData()=>new WorldProjectileCollisionData{MapId="map",ContentHash=new string('a',64),MaxOriginDistance=2,Shapes=new[]{Box(1)},Profiles=new[]{new WorldProjectileCollisionProfile{ConfigId="arrow",Shapes=new[]{Box(0)}}}};
        private static WorldProjectileShape Box(long id)=>new WorldProjectileShape{Id=id,Kind=WorldCollisionShapeKind.Box,AxisX=new WorldCollisionVector(1,0,0),AxisY=new WorldCollisionVector(0,1,0),AxisZ=new WorldCollisionVector(0,0,1)};
        [Test] public void ValidGeometryAndHashAreAccepted(){Assert.That(WorldProjectileCollisionValidation.TryValidate(ValidData(),out _),Is.True);}
        [Test] public void NonHexHashIsRejected(){var d=ValidData();d.ContentHash=new string('z',64);Assert.That(WorldProjectileCollisionValidation.TryValidate(d,out _),Is.False);}
        [Test] public void DuplicateStaticIdentityIsRejected(){var d=ValidData();d.Shapes=new[]{Box(1),Box(1)};Assert.That(WorldProjectileCollisionValidation.TryValidate(d,out _),Is.False);}
        [Test] public void FlattenedBoxIsRejected(){var d=ValidData();d.Shapes[0].AxisY=d.Shapes[0].AxisX;Assert.That(WorldProjectileCollisionValidation.TryValidate(d,out _),Is.False);}
        [Test] public void InvalidClientIndexDoesNotEnableBow(){WorldProjectileCollisionCatalog.Release("map");Assert.That(WorldProjectileCollisionCatalog.Prepare("map","{\"Version\":1,\"MapId\":\"map\",\"ContentHash\":\""+new string('z',64)+"\"}",out _),Is.False);WorldProjectileCollisionCatalog.Release("map");}
    }
}
