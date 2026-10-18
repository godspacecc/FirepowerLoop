using System;
using System.Reflection;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>用真实网格验证移动触发、移动端预算和地图释放，不依赖粒子或战斗存档。</summary>
    public sealed class ArcadeIslandWadingTests
    {
        private GameObject root;
        private object effect;
        private Type type;
        private readonly ArcadeIslandRegion region = new(19.5f, 19.5f, 1);
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private Vector2 Water
        {
            get { region.EntryFromFourSides(0, .5f, .95f, out var point); return new Vector2(point.X, point.Y); }
        }
        private void Create(bool mobile = true, Camera camera = null)
        {
            type = typeof(ArcadeCombatViewController).Assembly.GetType("Game.ArcadeIslandWadingPresentation");
            Assert.NotNull(type, "正式涉水表现尚未接入");
            root = new GameObject("WadingTestMap");
            var shader = Shader.Find("FirepowerLoop/BombAdditive"); Assert.NotNull(shader);
            effect = Activator.CreateInstance(type, root.transform, region, shader, mobile, camera);
        }
        private void Call(string name, params object[] values) => type.GetMethod(name).Invoke(effect, values);
        private int Count => (int)type.GetProperty("ActivePulseCount").GetValue(effect);
        private void Step(int id, Vector2 from)
        { Call("Set", id, from); Call("Set", id, from + Vector2.up * .25f); Call("Tick", .25f); }
        [TearDown] public void Cleanup() { (effect as IDisposable)?.Dispose(); if (root != null) Object.DestroyImmediate(root); }

        [Test] public void OnlyMovingWetEnemiesEmitAndRingsExpand()
        {
            Create(); Call("Set", 1, Water); Call("Tick", .25f); Assert.Zero(Count);
            Call("Set", 1, Water + Vector2.up * .25f); Call("Tick", .25f); Assert.AreEqual(1, Count);
            var mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
            var before = mesh.vertices[0]; Call("Tick", .1f); Assert.AreNotEqual(before, mesh.vertices[0]);
            Call("Set", 1, Vector2.zero); Call("Tick", 1f); Assert.Zero(Count);
            Call("Set", 2, Vector2.zero); Call("Set", 2, Vector2.up); Call("Tick", .25f); Assert.Zero(Count);
        }
        [TestCase(true, 20)] [TestCase(false, 32)]
        public void MobileAndDesktopUseOneBoundedReusableMesh(bool mobile, int limit)
        {
            Create(mobile);
            for (int round = 0; round < 3; round++)
            {
                for (int id = 1; id <= 48; id++)
                { Call("Set", id, Water); Call("Set", id, Water + Vector2.up * .3f); }
                Call("Tick", .25f); Assert.AreEqual(limit, Count);
            }
            Assert.AreEqual(1, root.transform.childCount);
            Assert.AreEqual(1, root.GetComponentsInChildren<MeshRenderer>().Length);
            Assert.IsEmpty(root.GetComponentsInChildren<Collider>());
            Assert.IsEmpty(root.GetComponentsInChildren<Rigidbody>());
            Assert.IsEmpty(root.GetComponentsInChildren<MonoBehaviour>());
        }
        [Test] public void PauseDuplicatesAndTeleportDoNotEmitExtraSplashes()
        {
            Create(); Step(1, Water); Assert.AreEqual(1, Count);
            var mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
            var vertices = mesh.vertices; Call("Tick", 0f); CollectionAssert.AreEqual(vertices, mesh.vertices);
            for (int i = 0; i < 5; i++) { Call("Set", 1, Water + Vector2.up * .25f); Call("Tick", .02f); }
            Assert.AreEqual(1, Count);
            Call("Set", 1, Water + Vector2.right * 5); Call("Tick", .05f); Assert.AreEqual(1, Count);
        }
        [Test] public void RemovedHiddenOrStaleActorsLeaveNoPermanentWake()
        {
            Create(); Step(1, Water); Call("Remove", 1); Assert.Zero(Count);
            Step(2, Water); root.SetActive(false); Call("Tick", .02f); root.SetActive(true); Assert.Zero(Count);
            Step(3, Water); Call("Tick", 2f); Assert.Zero(Count);
            Call("Dispose"); effect = null; Assert.Zero(root.transform.childCount);
        }
        [Test] public void FormalControllerMovesRemovesAndReleasesWadingEffects()
        {
            Create(); var view = new ArcadeCombatViewController();
            var field = typeof(ArcadeCombatViewController).GetField("islandWading", Private); Assert.NotNull(field);
            field.SetValue(view, effect);
            view.RecordRoundEnemyPosition(1, Water); view.RecordRoundEnemyPosition(1, Water + Vector2.up * .3f);
            view.TickPlayerFeedback(.25f); Assert.AreEqual(1, Count);
            view.RemoveEnemy(1); Assert.Zero(Count);
            view.End(); effect = null; Assert.Zero(root.transform.childCount);
        }
        [Test] public void OffscreenEnemiesDoNotConsumeSplashBudget()
        {
            var go = new GameObject("WadingCullCamera", typeof(Camera));
            try
            {
                var camera = go.GetComponent<Camera>(); camera.transform.position = new Vector3(100, 20, 0);
                camera.transform.rotation = Quaternion.Euler(90, 0, 0);
                Create(true, camera); Step(1, Water); Assert.Zero(Count);
                camera.transform.position = new Vector3(Water.x, 20, Water.y);
                Step(1, Water); Assert.AreEqual(1, Count);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test] public void WarmMovementAndMeshUpdatesDoNotAllocateManagedMemory()
        {
            Create();
            var set = (Action<int, Vector2>)Delegate.CreateDelegate(typeof(Action<int, Vector2>), effect, type.GetMethod("Set"));
            var tick = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), effect, type.GetMethod("Tick"));
            for (int i = 0; i < 20; i++) { set(1, Water + Vector2.up * ((i & 1) * .25f)); tick(.02f); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) { set(1, Water + Vector2.up * ((i & 1) * .25f)); tick(.02f); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), "预热后位置采样、计时与网格上传不应产生托管垃圾");
        }
    }
}
