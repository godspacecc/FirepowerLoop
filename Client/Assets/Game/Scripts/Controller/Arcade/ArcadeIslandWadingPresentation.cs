using System;
using FirepowerLoop.Arcade;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>地图独占涉水水花与波纹；固定数组循环复用，单一网格绘制，不创建逐敌人粒子或Update。</summary>
    internal sealed class ArcadeIslandWadingPresentation : IDisposable
    {
        private const int Segments = 12, VerticesPerPulse = Segments * 2 + 9;
        private const float Lifetime = .7f, StepSeconds = .25f;
        /// <summary>只读权威位置的短期采样；离场、缺失和传送不补放历史水花。</summary>
        private struct Actor
        {
            public int Id, Step;
            public Vector2 Position, Direction;
            public float Distance, SeenAt, NextAt;
        }
        /// <summary>视觉槽位是固定池；超预算覆盖最早的波纹，不扩大内存或创建实体。</summary>
        private struct Pulse
        {
            public int Id;
            public Vector3 Position, Forward;
            public float Age;
        }
        private readonly Actor[] actors = new Actor[64];
        private readonly Pulse[] pulses;
        private readonly Vector3[] vertices, unitCircle;
        private readonly Color32[] colors;
        private readonly Transform parent;
        private readonly Camera camera;
        private readonly ArcadeIslandRegion region;
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly MeshRenderer renderer;
        private float clock;
        private int nextPulse;
        private bool disposed;
        public int ActivePulseCount
        {
            get { int count = 0; for (int i = 0; i < pulses.Length; i++) if (pulses[i].Id != 0) count++; return count; }
        }

        public ArcadeIslandWadingPresentation(Transform parent, ArcadeIslandRegion region, Shader shader, bool mobile, Camera camera)
        {
            this.parent = parent; this.region = region; this.camera = camera;
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            pulses = new Pulse[mobile ? 20 : 32];
            vertices = new Vector3[pulses.Length * VerticesPerPulse]; colors = new Color32[vertices.Length];
            var uv = new Vector2[vertices.Length]; var triangles = new int[pulses.Length * (Segments * 6 + 9)];
            unitCircle = new Vector3[Segments];
            for (int i = 0; i < Segments; i++) unitCircle[i] = new Vector3(Mathf.Cos(i * Mathf.PI * 2 / Segments), 0, Mathf.Sin(i * Mathf.PI * 2 / Segments));
            for (int slot = 0; slot < pulses.Length; slot++)
            {
                int first = slot * VerticesPerPulse, t = slot * (Segments * 6 + 9);
                for (int i = 0; i < Segments; i++)
                {
                    int a = first + i * 2, b = first + (i + 1) % Segments * 2;
                    uv[a] = new Vector2(0, 0); uv[a + 1] = new Vector2(0, 1);
                    triangles[t++] = a; triangles[t++] = a + 1; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = a + 1; triangles[t++] = b + 1;
                }
                for (int i = 0; i < 9; i++) { int v = first + Segments * 2 + i; uv[v] = new Vector2(.5f, .5f); triangles[t++] = v; }
            }
            mesh = new Mesh { name = "IslandWadingBoundedMesh", bounds = new Bounds(Vector3.zero, new Vector3(200, 8, 200)) };
            mesh.MarkDynamic(); mesh.vertices = vertices; mesh.colors32 = colors; mesh.uv = uv; mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(200, 8, 200));
            material = new Material(shader) { name = "IslandWadingFoam", renderQueue = 3002 };
            material.SetTexture("_BaseMap", Texture2D.whiteTexture); material.SetColor("_BaseColor", new Color(.62f, .85f, .9f, .85f));
            material.SetFloat("_SoftEdge", 1f);
            root = new GameObject("IslandWadingEffects", typeof(MeshFilter), typeof(MeshRenderer)); root.transform.SetParent(parent, false);
            root.GetComponent<MeshFilter>().sharedMesh = mesh; renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off; renderer.enabled = false;
        }

        /// <summary>重复快照只更新采样时间；水外或镜头外停止采样，超长位移作为新基线。</summary>
        public void Set(int id, Vector2 position)
        {
            if (disposed || id <= 0) return;
            if (!region.IsShallow(position.x, position.y) || !Visible(position)) { ForgetActor(id); return; }
            int index = -1, free = -1;
            for (int i = 0; i < actors.Length; i++)
            { if (actors[i].Id == id) { index = i; break; } if (actors[i].Id == 0 && free < 0) free = i; }
            if (index < 0)
            {
                if (free < 0) return;
                actors[free] = new Actor { Id = id, Position = position, SeenAt = clock, NextAt = clock }; return;
            }
            var actor = actors[index]; var delta = position - actor.Position;
            if (delta.sqrMagnitude > 6.25f || clock - actor.SeenAt > .5f) actor.Distance = 0;
            else if (delta.sqrMagnitude > .00001f) { actor.Distance += delta.magnitude; actor.Direction = delta.normalized; }
            actor.Position = position; actor.SeenAt = clock; actors[index] = actor;
        }
        private bool Visible(Vector2 position)
        {
            if (camera == null) return true;
            var viewport = camera.WorldToViewportPoint(parent.TransformPoint(new Vector3(position.x, .8f, position.y)));
            return viewport.z > 0 && viewport.x >= -.05f && viewport.x <= 1.05f && viewport.y >= -.05f && viewport.y <= 1.05f;
        }

        /// <summary>复用现有帧循环；暂停不推进，隐藏地图清空，缺失状态自动停止发射且不追补积压。</summary>
        public void Tick(float seconds)
        {
            if (disposed) return;
            if (parent == null || !parent.gameObject.activeInHierarchy) { Clear(); return; }
            if (seconds <= 0) return;
            clock += seconds;
            for (int i = 0; i < pulses.Length; i++)
                if (pulses[i].Id != 0) { pulses[i].Age += seconds; if (pulses[i].Age >= Lifetime) pulses[i] = default; }
            for (int i = 0; i < actors.Length; i++)
            {
                var actor = actors[i]; if (actor.Id == 0) continue;
                if (clock - actor.SeenAt > .5f) { actors[i] = default; continue; }
                if (actor.Distance >= .18f && clock >= actor.NextAt)
                {
                    var forward = new Vector3(actor.Direction.x, 0, actor.Direction.y);
                    var side = Vector3.Cross(Vector3.up, forward);
                    pulses[nextPulse] = new Pulse { Id = actor.Id, Forward = forward,
                        Position = new Vector3(actor.Position.x, .065f, actor.Position.y) + side * ((actor.Step++ & 1) == 0 ? .14f : -.14f) };
                    nextPulse = (nextPulse + 1) % pulses.Length; actor.Distance = 0; actor.NextAt = clock + StepSeconds;
                    actors[i] = actor;
                }
            }
            Render();
        }

        /// <summary>预计算圆周只缩放，三片水花沿小抛物线落下；每帧复用顶点及颜色数组。</summary>
        private void Render()
        {
            int active = 0;
            for (int slot = 0; slot < pulses.Length; slot++)
            {
                var pulse = pulses[slot]; int first = slot * VerticesPerPulse;
                if (pulse.Id == 0)
                { for (int i = 0; i < VerticesPerPulse; i++) colors[first + i] = default; continue; }
                active++; float progress = pulse.Age / Lifetime, radius = .12f + progress * .72f;
                byte alpha = (byte)(195 * (1 - progress) * (1 - progress)); var color = new Color32(185, 232, 242, alpha);
                for (int i = 0; i < Segments; i++)
                {
                    int v = first + i * 2;
                    vertices[v] = pulse.Position + unitCircle[i] * radius; vertices[v + 1] = pulse.Position + unitCircle[i] * (radius + .075f * (1 - progress));
                    colors[v] = colors[v + 1] = color;
                }
                var side = Vector3.Cross(Vector3.up, pulse.Forward);
                float spray = Mathf.Clamp01(pulse.Age / .38f), height = 4 * spray * (1 - spray) * .32f;
                for (int i = 0; i < 3; i++)
                {
                    var direction = side * (i - 1) - pulse.Forward * .6f;
                    var center = pulse.Position + direction * (.08f + spray * .4f) + Vector3.up * height;
                    float size = .065f * (1 - spray); int v = first + Segments * 2 + i * 3;
                    vertices[v] = center - side * size; vertices[v + 1] = center + side * size; vertices[v + 2] = center + Vector3.up * size * 2;
                    var drop = new Color32(205, 243, 250, (byte)(210 * (1 - spray)));
                    colors[v] = colors[v + 1] = colors[v + 2] = drop;
                }
            }
            renderer.enabled = active > 0;
            if (active > 0)
            { mesh.vertices = vertices; mesh.colors32 = colors; mesh.bounds = new Bounds(Vector3.zero, new Vector3(200, 8, 200)); }
        }
        private void ForgetActor(int id)
        { for (int i = 0; i < actors.Length; i++) if (actors[i].Id == id) { actors[i] = default; return; } }
        public void Remove(int id)
        { if (disposed) return; ForgetActor(id); for (int i = 0; i < pulses.Length; i++) if (pulses[i].Id == id) pulses[i] = default; Render(); }
        public void Clear()
        { Array.Clear(actors, 0, actors.Length); Array.Clear(pulses, 0, pulses.Length); nextPulse = 0; renderer.enabled = false; }
        private static void Destroy(Object value)
        { if (value == null) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
        public void Dispose()
        { if (disposed) return; disposed = true; root.SetActive(false); Destroy(root); Destroy(mesh); Destroy(material); }
    }
}
