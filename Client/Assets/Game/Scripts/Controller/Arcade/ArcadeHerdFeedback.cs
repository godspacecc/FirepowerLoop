using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using UnityEngine;
using Object = UnityEngine.Object;
namespace Game
{
    /// <summary>地图独占的牛群短反馈：消费只读相位，持有有限尘团和临时网格，不计算移动、命中或伤害。</summary>
    public sealed class ArcadeHerdFeedback : IDisposable
    {
        private readonly Transform owner;
        private readonly Action<string, Vector2, float> sound;
        private readonly Action stopSounds;
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly Dictionary<long, ArcadeHerdSnapshot> previous = new Dictionary<long, ArcadeHerdSnapshot>();
        private readonly Dictionary<long, float> impacts = new Dictionary<long, float>();
        private readonly List<Puff> puffs = new List<Puff>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();
        private bool enabled = true, disposed;
        private float clock, lastImpact = float.NegativeInfinity;
        /// <summary>每粒寿命不超过0.65秒，总数硬上限256，不随长局累计。</summary>
        private struct Puff { public Vector3 Position; public float Age, Life, X, Z, Dx, Dz; public bool Impact; }

        public ArcadeHerdFeedback(Transform owner, Material material, Action<string, Vector2, float> sound, Action stopSounds)
        {
            this.owner = owner; this.sound = sound; this.stopSounds = stopSounds;
            root = new GameObject("HerdDustFeedback", typeof(MeshFilter), typeof(MeshRenderer));
            root.transform.SetParent(owner, false); root.layer = owner.gameObject.layer;
            mesh = new Mesh { name = "HerdDustRuntime" }; mesh.MarkDynamic();
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.enabled = material != null;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        private bool Active => !disposed && enabled && owner != null && owner.gameObject.activeInHierarchy;

        /// <summary>暂停／隐藏清掉短反馈并取消在途音频；再次出现时只建立当前相位基线。</summary>
        public void SetEnabled(bool value)
        {
            if (enabled == value) return;
            enabled = value; Clear();
        }
        private void Clear()
        {
            puffs.Clear(); previous.Clear(); impacts.Clear(); lastImpact=float.NegativeInfinity;
            if (mesh != null) mesh.Clear(); stopSounds?.Invoke();
        }

        /// <summary>同快照、倒退、跨越0.25秒及新接入的中途姿态都不补播旧落蹄。</summary>
        public void Sync(IReadOnlyList<ArcadeHerdSnapshot> cows)
        {
            if (!Active) { Clear(); return; }
            var present = new HashSet<long>();
            foreach (var cow in cows)
            {
                if (cow.Id <= 0 || cow.Phase != ArcadeHerdPhase.Windup && cow.Phase != ArcadeHerdPhase.Charging ||
                    float.IsNaN(cow.X) || float.IsInfinity(cow.X) || float.IsNaN(cow.Z) || float.IsInfinity(cow.Z) ||
                    double.IsNaN(cow.PhaseSeconds) || double.IsInfinity(cow.PhaseSeconds) || cow.PhaseSeconds < 0 || !present.Add(cow.Id)) continue;
                if (previous.TryGetValue(cow.Id, out var old))
                {
                    double before = old.PhaseSeconds, after = cow.PhaseSeconds;
                    if (old.Phase == cow.Phase && after > before && after - before <= .25)
                    {
                        var position = new Vector2(cow.X, cow.Z);
                        if (cow.Phase == ArcadeHerdPhase.Windup && (cow.Id - 1) % 5 == 0)
                        {
                            foreach (float mark in new[] { .25f, .6f })
                                if (before < mark && after >= mark) { Emit(position, cow.DirectionX, cow.DirectionZ, 1, 0, cow.Id); sound?.Invoke("ArcadeHerdScrape", position, .7f); }
                            if (before < .29 && after >= .29) sound?.Invoke("ArcadeHerdSnort", position, .8f);
                        }
                        else if (cow.Phase == ArcadeHerdPhase.Charging)
                        {
                            double offset = (cow.Id - 1) % 5 * .07;
                            int a = (int)Math.Floor(((before + offset) * 15 - Math.PI * .5) / Math.PI);
                            int b = (int)Math.Floor(((after + offset) * 15 - Math.PI * .5) / Math.PI);
                            if (b > a)
                            {
                                int pair = (b % 2 + 2) % 2, tone = (int)((cow.Id - 1 + pair) % 4);
                                Emit(position, cow.DirectionX, cow.DirectionZ, 0, pair, cow.Id);
                                sound?.Invoke(tone == 0 ? "ArcadeHerdHoof" : "ArcadeHerdHoof" + (tone + 1), position, 1);
                            }
                        }
                    }
                }
                previous[cow.Id] = cow;
            }
            foreach (long id in previous.Keys.Where(id => !present.Contains(id)).ToArray()) previous.Remove(id);
        }

        /// <summary>存活受击只比较已经观察过的权威状态；新加入的受击中目标不补播历史撞击。</summary>
        public void ObserveImpact(long target, ArcadeHerdImpactState state, Vector2 position)
        {
            bool known = impacts.TryGetValue(target, out float old); impacts[target] = state.Remaining;
            if (known && state.Remaining > ArcadeHerdImpactRules.TotalSeconds - .25f &&
                (old <= 0 || state.Remaining > old + .1f)) Impact(position);
        }
        public void ForgetImpact(long target) { impacts.Remove(target); }

        /// <summary>只由已确认死亡类别／受击上升沿调用；同一时刻密集接触最多25次每秒，保持短促。</summary>
        public void Impact(Vector2 position)
        {
            if (!Active || clock - lastImpact < .04f) return;
            lastImpact = clock; Emit(position, 1, 0, 2, 0, 1); sound?.Invoke("ArcadeHerdImpact", position, .95f);
        }

        private void Emit(Vector2 position, float dx, float dz, int kind, int pair, long id)
        {
            var direction = new Vector2(dx, dz).normalized; if (direction.sqrMagnitude < .01f) direction = Vector2.right;
            var side = new Vector2(-direction.y, direction.x);
            var random = new System.Random(unchecked((int)(clock * 10000) + (int)id * 31));
            int count = kind == 2 ? 14 : kind == 1 ? 4 : 8;
            for (int i = 0; i < count; i++)
            {
                if (puffs.Count >= 256) puffs.RemoveAt(0);
                bool front = i < 4;
                var foot = position + (kind == 0 ? direction * (front ? .63f : -.63f) + side * (front == (pair == 0) ? .35f : -.35f) : Vector2.zero);
                puffs.Add(new Puff { Position = new Vector3(foot.x, 0, foot.y), X = (float)random.NextDouble() - .5f,
                    Z = (float)random.NextDouble() - .5f, Dx = direction.x, Dz = direction.y, Life = kind == 2 ? .52f : .65f, Impact = kind == 2 });
            }
        }

        /// <summary>复用战场表现帧推进有限寿命，地面薄尘与采用候选同源；没有独立MonoBehaviour.Update。</summary>
        public void Tick(float seconds, Camera camera = null)
        {
            if (!Active) { if (!disposed) Clear(); return; }
            float dt = float.IsNaN(seconds) || float.IsInfinity(seconds) ? 0 : Mathf.Max(0, seconds);
            clock += dt;
            for (int i = puffs.Count - 1; i >= 0; i--)
            { var p = puffs[i]; p.Age += dt; if (p.Age >= p.Life) puffs.RemoveAt(i); else puffs[i] = p; }
            camera = camera != null ? camera : Camera.main;
            var right = camera != null ? owner.InverseTransformDirection(camera.transform.right) : Vector3.right;
            var up = camera != null ? owner.InverseTransformDirection(camera.transform.up) : Vector3.up;
            vertices.Clear(); colors.Clear(); uv.Clear(); triangles.Clear();
            foreach (var p in puffs)
            {
                float u = p.Age / p.Life;
                var position = p.Position + new Vector3(p.X * (.35f + p.Age) - p.Dx * p.Age * .4f,
                    .09f + p.Age * .27f, p.Z * (.25f + p.Age) - p.Dz * p.Age * .4f);
                float size = (p.Impact ? .27f : .21f) + u * (p.Impact ? .65f : .55f);
                var color = new Color(.55f, .39f, .18f, (p.Impact ? .48f : .45f) * Mathf.Pow(1 - u, 1.2f));
                var r = right * size * .5f; var v = up * size * .5f; int first = vertices.Count;
                vertices.Add(position - r - v); vertices.Add(position + r - v); vertices.Add(position + r + v); vertices.Add(position - r + v);
                for (int i = 0; i < 4; i++) colors.Add(color);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
        }
        /// <summary>网格由当前地图唯一持有；释放后无回调或材质销毁权。</summary>
        public void Dispose()
        {
            if (disposed) return; Clear(); impacts.Clear(); disposed = true; Destroy(root); Destroy(mesh);
        }
        private static void Destroy(Object value)
        { if (value == null) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
