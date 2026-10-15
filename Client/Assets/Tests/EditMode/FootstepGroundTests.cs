using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class FootstepGroundTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private Transform actor;
        private CharacterController controller;
        private FootstepGroundProbe probe;
        private GameObject ground;
        [SetUp] public void SetUp()
        {
            var go = Make("Actor"); actor = go.transform;
            actor.position = new Vector3(5000f, 0f, 5000f);
            controller = go.AddComponent<CharacterController>(); controller.center = Vector3.up; controller.height = 2f;
            controller.enabled = false;
            ground = Make("Ground"); ground.transform.position = actor.position + Vector3.down * 0.5f;
            ground.AddComponent<BoxCollider>().size = new Vector3(10f, 1f, 10f);
            probe = new FootstepGroundProbe();
        }
        [TearDown] public void TearDown() { foreach (var go in objects) Object.DestroyImmediate(go); objects.Clear(); }
        private GameObject Make(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        private bool Query(out FootstepSurfaceKind kind) { Physics.SyncTransforms(); return probe.TryGet(actor, controller, out kind); }
        [Test] public void DisabledRemoteControllerUsesSerializedFootGeometry()
        { ground.AddComponent<FootstepSurface>().Kind = FootstepSurfaceKind.Dirt; Assert.True(Query(out var kind)); Assert.AreEqual(FootstepSurfaceKind.Dirt, kind); }
        [Test] public void UnknownGroundIsSilent() { Assert.False(Query(out _)); }
        [Test] public void DistantGroundDoesNotMakeAirborneActorGrounded()
        { ground.AddComponent<FootstepSurface>(); actor.position += Vector3.up; Assert.False(Query(out _)); }
        [Test] public void NearestUnknownSupportDoesNotBorrowMarkedFloorBelow()
        { ground.AddComponent<FootstepSurface>(); var shelf = Make("Shelf"); shelf.transform.position = actor.position + Vector3.up * 0.05f; shelf.AddComponent<BoxCollider>().size = new Vector3(1f, 0.05f, 1f); Assert.False(Query(out _)); }
        [Test] public void ExplicitRegionOverridesMarkedSupport()
        {
            ground.AddComponent<FootstepSurface>().Kind = FootstepSurfaceKind.Grass;
            var region = Make("Region"); region.transform.position = actor.position;
            region.AddComponent<BoxCollider>().isTrigger = true;
            var marker = region.AddComponent<FootstepSurface>(); marker.IsRegion = true; marker.RegionId = "test-dirt"; marker.Kind = FootstepSurfaceKind.Dirt;
            Assert.True(Query(out var kind)); Assert.AreEqual(FootstepSurfaceKind.Dirt, kind);
        }
        [Test] public void MeshProjectionRejectsBoundingBoxCorner()
        {
            var area = Make("Area"); var filter = area.AddComponent<MeshFilter>(); var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward }; mesh.triangles = new[] { 0, 1, 2 };
            filter.sharedMesh = mesh; var marker = area.AddComponent<FootstepSurface>(); marker.SurfaceMesh = filter;
            try { Assert.True(marker.Contains(new Vector3(0.1f, 0f, 0.1f))); Assert.False(marker.Contains(new Vector3(0.9f, 0f, 0.9f))); }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
