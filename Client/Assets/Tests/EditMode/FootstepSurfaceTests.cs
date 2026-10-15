using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class FootstepSurfaceTests
    {
        private GameObject area;
        private Mesh mesh;
        private FootstepSurface marker;
        [SetUp] public void SetUp()
        {
            area = new GameObject("SurfaceProjectionTest");
            var filter = area.AddComponent<MeshFilter>();
            mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward,
                new Vector3(2, 0, 0), new Vector3(3, 0, 0), new Vector3(2, 0, 1) };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
            filter.sharedMesh = mesh;
            marker = area.AddComponent<FootstepSurface>();
            marker.SurfaceMesh = filter;
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(area); Object.DestroyImmediate(mesh); }
        private void Set(string name, object value)
        {
            var field = typeof(FootstepSurface).GetField(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field, name + " 尚未实现");
            field.SetValue(marker, value);
        }
        [Test] public void MudRegionUsesOnlySelectedSubmesh()
        {
            Set("SurfaceSubmesh", 1);
            Assert.False(marker.Contains(new Vector3(.1f, 0, .1f)));
            Assert.True(marker.Contains(new Vector3(2.1f, 0, .1f)));
        }
        [Test] public void ChangingSubmeshRefreshesProjectionCache()
        {
            Set("SurfaceSubmesh", 0); Assert.True(marker.Contains(new Vector3(.1f, 0, .1f)));
            Set("SurfaceSubmesh", 1); Assert.False(marker.Contains(new Vector3(.1f, 0, .1f)));
        }
        [Test] public void WaterRejectsDryFeetAboveSurface()
        {
            Set("CheckSurfaceHeight", true);
            Assert.False(marker.Contains(new Vector3(.1f, .3f, .1f)));
        }
        [Test] public void WaterRejectsFeetTooFarBelowSurface()
        {
            Set("CheckSurfaceHeight", true);
            Assert.False(marker.Contains(new Vector3(.1f, -.8f, .1f)));
        }
        [Test] public void WaterHeightFollowsSlopedTriangle()
        {
            mesh.vertices = new[] { Vector3.zero, new Vector3(1, 1, 0), Vector3.forward,
                new Vector3(2, 0, 0), new Vector3(3, 0, 0), new Vector3(2, 0, 1) };
            Set("CheckSurfaceHeight", true);
            Assert.True(marker.Contains(new Vector3(.5f, .3f, .1f)));
            Assert.False(marker.Contains(new Vector3(.5f, 0f, .1f)));
        }
    }
}
