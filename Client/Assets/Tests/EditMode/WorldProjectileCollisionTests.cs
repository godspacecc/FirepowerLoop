using NUnit.Framework;

namespace Game.Tests
{
    public sealed class WorldProjectileCollisionTests
    {
        private static WorldCollisionVector V(float x,float y,float z)=>new WorldCollisionVector(x,y,z);
        private static WorldProjectileShape Arrow()=>new WorldProjectileShape{Kind=WorldCollisionShapeKind.Box,AxisX=V(.05f,0,0),AxisY=V(0,.05f,0),AxisZ=V(0,0,.25f)};
        private static WorldProjectileShape Box(long id,float x,float y,float z)=>new WorldProjectileShape{Id=id,Kind=WorldCollisionShapeKind.Box,Center=V(x,y,z),AxisX=V(.5f,0,0),AxisY=V(0,.5f,0),AxisZ=V(0,0,.5f)};
        [Test] public void SweepFindsFirstContactBeforeThinWall()
        {var q=new WorldProjectileCollisionQuery(new[]{Box(7,0,0,5)});Assert.That(q.TrySweep(V(0,0,0),V(0,0,10),Arrow(),out var hit),Is.True);Assert.That(hit.Distance,Is.EqualTo(4.25f).Within(.001f));Assert.That(hit.ShapeId,Is.EqualTo(7));}
        [Test] public void SweepKeepsVerticalSeparation()
        {var q=new WorldProjectileCollisionQuery(new[]{Box(1,0,4,5)});Assert.That(q.TrySweep(V(0,0,0),V(0,0,10),Arrow(),out _),Is.False);}
        [TestCase(1)] [TestCase(-1)] public void SweepSupportsVerticalTravel(int sign)
        {var q=new WorldProjectileCollisionQuery(new[]{Box(1,0,sign*5,0)});Assert.That(q.TrySweep(V(0,0,0),V(0,sign*10,0),Arrow(),out var hit),Is.True);Assert.That(hit.Distance,Is.EqualTo(4.45f).Within(.001f));}
        [Test] public void SweepChoosesNearestAndStableIdentity()
        {var q=new WorldProjectileCollisionQuery(new[]{Box(9,0,0,7),Box(3,0,0,3),Box(2,0,0,3)});Assert.That(q.TrySweep(V(0,0,0),V(0,0,10),Arrow(),out var hit),Is.True);Assert.That(hit.ShapeId,Is.EqualTo(2));}
        [Test] public void SweepDetectsInitialOverlap()
        {var q=new WorldProjectileCollisionQuery(new[]{Box(1,0,0,0)});Assert.That(q.TrySweep(V(0,0,0),V(0,0,10),Arrow(),out var hit),Is.True);Assert.That(hit.Distance,Is.EqualTo(0).Within(.001f));}
        [Test] public void SweepHitsTriangleInterior()
        {var t=new WorldProjectileShape{Id=4,Kind=WorldCollisionShapeKind.Triangle,A=V(-2,-2,5),B=V(2,-2,5),C=V(0,2,5)};var q=new WorldProjectileCollisionQuery(new[]{t});Assert.That(q.TrySweep(V(0,0,0),V(0,0,10),Arrow(),out var hit),Is.True);Assert.That(hit.Distance,Is.EqualTo(4.75f).Within(.001f));}
        [Test] public void SweepDoesNotTreatTriangleBoundsAsSolid()
        {var t=new WorldProjectileShape{Id=4,Kind=WorldCollisionShapeKind.Triangle,A=V(-2,-2,5),B=V(2,-2,5),C=V(0,2,5)};var q=new WorldProjectileCollisionQuery(new[]{t});Assert.That(q.TrySweep(V(1.8f,1.8f,0),V(1.8f,1.8f,10),Arrow(),out _),Is.False);}
        [Test] public void SweepHitsSphereSurface()
        {var q=new WorldProjectileCollisionQuery(new[]{new WorldProjectileShape{Id=1,Kind=WorldCollisionShapeKind.Sphere,Center=V(0,0,5),Radius=.5f}});Assert.That(q.TrySweep(V(0,0,0),V(0,0,10),Arrow(),out var hit),Is.True);Assert.That(hit.Distance,Is.EqualTo(4.25f).Within(.002f));}
        [Test] public void SweepHitsCapsuleAtRaisedHeight()
        {var q=new WorldProjectileCollisionQuery(new[]{new WorldProjectileShape{Id=1,Kind=WorldCollisionShapeKind.Capsule,A=V(0,0,5),B=V(0,2,5),Radius=.5f}});Assert.That(q.TrySweep(V(0,1,0),V(0,1,10),Arrow(),out var hit),Is.True);Assert.That(hit.Distance,Is.EqualTo(4.25f).Within(.002f));}
        [Test] public void SweepRejectsInvalidCoordinates()
        {var q=new WorldProjectileCollisionQuery(new[]{Box(1,0,0,5)});Assert.That(q.TrySweep(V(float.NaN,0,0),V(0,0,10),Arrow(),out _),Is.False);Assert.That(q.TrySweep(V(0,0,0),V(0,float.PositiveInfinity,10),Arrow(),out _),Is.False);}
        [Test] public void SweepDetectsShapeAtSpatialPartitionBoundary()
        {var shapes=new WorldProjectileShape[65];for(int i=0;i<65;i++)shapes[i]=Box(i+1,i*10,0,5);var q=new WorldProjectileCollisionQuery(shapes);Assert.That(q.TrySweep(V(320,0,0),V(320,0,10),Arrow(),out var hit),Is.True);Assert.That(hit.ShapeId,Is.EqualTo(33));}
        [Test] public void HeightfieldRetainsSurfaceAndGroundProjection()
        {var terrain=new WorldProjectileShape{Id=5,Kind=WorldCollisionShapeKind.Heightfield,A=V(0,0,0),AxisX=V(10,0,0),AxisY=V(0,10,0),AxisZ=V(0,0,10),HeightmapResolution=2,Heights=new[]{.2f,.2f,.2f,.2f},HoleCells=new int[0]};var q=new WorldProjectileCollisionQuery(new[]{terrain});Assert.That(q.TrySweep(V(5,6,5),V(5,-1,5),Arrow(),out var hit),Is.True);Assert.That(hit.Distance,Is.EqualTo(3.95f).Within(.002));Assert.That(q.TryGround(V(5,6,5),out var ground),Is.True);Assert.That(ground.Y,Is.EqualTo(2).Within(.002));}
        [Test] public void HeightfieldHoleDoesNotBlockArrow()
        {var terrain=new WorldProjectileShape{Id=5,Kind=WorldCollisionShapeKind.Heightfield,A=V(0,0,0),AxisX=V(10,0,0),AxisY=V(0,10,0),AxisZ=V(0,0,10),HeightmapResolution=2,Heights=new[]{.2f,.2f,.2f,.2f},HoleCells=new[]{0}};var q=new WorldProjectileCollisionQuery(new[]{terrain});Assert.That(q.TrySweep(V(5,6,5),V(5,-1,5),Arrow(),out _),Is.False);}
        [Test] public void ProjectileCenterOffsetIsPreserved()
        {var arrow=Arrow();arrow.Center=V(0,2,0);var q=new WorldProjectileCollisionQuery(new[]{Box(1,0,2,5)});Assert.That(q.TrySweep(V(0,0,0),V(0,0,10),arrow,out var hit),Is.True);Assert.That(hit.Distance,Is.EqualTo(4.25f).Within(.001f));}
        [Test] public void LargeMapCoordinatesKeepThinSurfaceContact()
        {var q=new WorldProjectileCollisionQuery(new[]{Box(1,230,125,220)});Assert.That(q.TrySweep(V(230,125,215),V(230,125,225),Arrow(),out var hit),Is.True);Assert.That(hit.Distance,Is.EqualTo(4.25f).Within(.002f));}
    }
}
