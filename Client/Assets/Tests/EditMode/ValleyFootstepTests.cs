using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Tests
{
    public sealed class ValleyFootstepTests
    {
        private GameObject root, actor;
        private GeneratedValleyContentHandle handle;
        private List<Mesh> meshes;
        private FieldInfo Field(string name) => typeof(GeneratedValleyContentHandle).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        [SetUp] public void SetUp()
        {
            root = new GameObject("ValleyFootstepTest"); root.SetActive(false);
            var layout = new ValleyLayoutSaveData { WorldSizeMeters = 1024, HeightSampleSpacingMeters = 4, Heights = new float[257 * 257] };
            layout.River.Add(ValleyPosition.From(new Vector3(0, .2f, -10)));
            layout.River.Add(ValleyPosition.From(new Vector3(0, .2f, 10)));
            layout.Fords.Add(ValleyPosition.From(new Vector3(0, .2f, 0)));
            handle = new GeneratedValleyContentHandle(null, null, layout);
            Field("GeneratedRoot").SetValue(handle, root);
            meshes = (List<Mesh>)Field("Meshes").GetValue(handle);
            var kit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab").GetComponent<ValleyGenerationKit>();
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            typeof(ValleyMapBuilder).GetMethod("BuildTerrain", flags).Invoke(null, new object[] { handle, kit, layout });
            typeof(ValleyMapBuilder).GetMethod("BuildRiver", flags).Invoke(null, new object[] { handle, kit, layout });
        }
        [TearDown] public void TearDown()
        {
            if (actor != null) Object.DestroyImmediate(actor);
            if (root != null) Object.DestroyImmediate(root);
            if (meshes != null) foreach (var mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
        }
        [Test] public void EveryGeneratedTerrainChunkProvidesMarkedSupport()
        {
            var terrain = root.GetComponentsInChildren<MeshCollider>(true);
            Assert.AreEqual(256, terrain.Length);
            foreach (var collider in terrain) Assert.AreEqual(FootstepSurfaceKind.Dirt, collider.GetComponent<FootstepSurface>().Kind);
        }
        [Test] public void SurfaceRegionsDoNotBecomeNavigationOrSolidObstacles()
        {
            Assert.AreEqual(256, ((List<NavMeshBuildSource>)Field("Sources").GetValue(handle)).Count);
            var regions = root.GetComponentsInChildren<FootstepSurface>(true);
            foreach (var marker in regions) if (marker.IsRegion) Assert.True(marker.GetComponent<Collider>().isTrigger);
        }
        [Test] public void GroundProbeDistinguishesBankMudAndShallowWater()
        {
            root.SetActive(true);
            actor = new GameObject("Actor"); var cc = actor.AddComponent<CharacterController>();
            cc.center = Vector3.up; cc.height = 2; cc.enabled = false;
            var probe = new FootstepGroundProbe();
            foreach (var pair in new[] {
                (position: new Vector3(10, 0, 0), kind: FootstepSurfaceKind.Dirt),
                (position: new Vector3(6, 0, 0), kind: FootstepSurfaceKind.Mud),
                (position: Vector3.zero, kind: FootstepSurfaceKind.Water) })
            {
                actor.transform.position = pair.position; Physics.SyncTransforms();
                Assert.True(probe.TryGet(actor.transform, cc, out var kind));
                Assert.AreEqual(pair.kind, kind, pair.position.ToString());
            }
        }
        [Test] public void DestroyingCandidateRootReleasesAllSurfaceRegions()
        {
            var markers = root.GetComponentsInChildren<FootstepSurface>(true);
            Object.DestroyImmediate(root); root = null;
            foreach (var marker in markers) Assert.True(marker == null);
        }
    }
}
