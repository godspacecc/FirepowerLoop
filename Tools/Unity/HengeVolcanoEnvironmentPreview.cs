using System;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>巨石环阵、熔火巢穴的隔离候选；只建立临时视觉，不保存资源、不改变玩法足迹。</summary>
public static class HengeVolcanoEnvironmentPreview
{
    /// <summary>正式战斗场地的半宽；外围景物不得侵入该矩形。</summary>
    private const float ArenaHalf = 19.5f;

    /// <summary>构建完整候选环境；自建 Mesh/Material 均交由调用方统一释放。</summary>
    public static GameObject Build(Transform parent, ArcadeRoundLayoutParameters layout, List<Object> owned)
    {
        if (parent == null || layout == null || owned == null) throw new ArgumentNullException("预览父节点、布局、资源清单不能为空");
        if (layout.ThemeKey != "henge" && layout.ThemeKey != "volcano") throw new ArgumentException("仅支持 henge/volcano 候选");
        var root = Group(parent, "EnvironmentCandidate_" + layout.ThemeKey);
        var c = new Context(root.transform, owned, layout.ThemeKey == "volcano");
        Ground(c);
        int index = 0;
        foreach (var obstacle in layout.Obstacles)
        {
            if (c.Volcano) VolcanicObstacle(c, obstacle, index++);
            else HengeObstacle(c, obstacle, index++);
        }
        GroundDetail(c, layout);
        if (c.Volcano) VolcanoOutside(c);
        else HengeOutside(c);
        return root;
    }

    /// <summary>可选预览采样；只调外圈和障碍凹口的共用辉光，不添加组件或改变物体位置。</summary>
    public static void Sample(GameObject root, float t)
    {
        if (root == null) return;
        var seen = new HashSet<Material>();
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null || !material.name.StartsWith("HV_Lava") || !seen.Add(material)) continue;
            float pulse = .74f + .09f * Mathf.Sin(t * 1.6f);
            material.SetColor("_EmissionColor", material.color * pulse);
        }
    }

    /// <summary>一次预览的局部资源上下文；不在静态字段保留场景或自建资源引用。</summary>
    private sealed class Context
    {
        public readonly Transform Root;
        public readonly List<Object> Owned;
        public readonly bool Volcano;
        public readonly Material[] Stone, Moss, Ground, Dust, Lava, Grass;

        public Context(Transform root, List<Object> owned, bool volcano)
        {
            Root = root; Owned = owned; Volcano = volcano;
            Stone = Palette(this, volcano ? "Basalt" : "WeatheredStone", volcano
                ? new[] { 0x343B42u, 0x41464Cu, 0x50515Au, 0x5C5556u, 0x454851u, 0x6A605Fu }
                : new[] { 0x68767Au, 0x7C8987u, 0x8B9590u, 0x9FA59Au, 0x5B6D71u, 0xB0AE9Cu });
            Moss = Palette(this, "Moss", new[] { 0x687C43u, 0x81924Cu, 0x536B3Fu, 0x93A15Cu });
            Ground = Palette(this, "Ground", volcano
                ? new[] { 0x8C8982u, 0x96928Au, 0x84847Fu, 0x9C958Au, 0x8B8C85u, 0xA19A8Fu }
                : new[] { 0x9EAB7Cu, 0xA9B484u, 0xA1AD79u, 0xADB68Au, 0x98A475u, 0xAFB68Au });
            Dust = Palette(this, "Dust", volcano
                ? new[] { 0x676A67u, 0x777A75u, 0xA29B8Cu, 0x898B82u }
                : new[] { 0xB2AA88u, 0xC0B696u, 0x929D70u, 0x829465u });
            Lava = Palette(this, "Lava", new[] { 0xE37035u, 0xF4A24Bu, 0xC7522Eu, 0xFFBC64u }, true);
            Grass = Palette(this, "Grass", new[] { 0x71844Bu, 0x8A9D59u, 0xA2AA67u });
        }
    }

    /// <summary>以实际 URP Lit 平色哑光材质提供面色变化；不依赖贴图、顶点色或自定义 Shader。</summary>
    private static Material[] Palette(Context c, string label, uint[] colors, bool emission = false)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("候选预览需要项目现有 URP/Lit Shader");
        var result = new Material[colors.Length];
        for (int i = 0; i < result.Length; i++)
        {
            uint rgb = colors[i];
            Color color = new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
            var m = new Material(shader) { name = "HV_" + label + "_" + i, hideFlags = HideFlags.DontSave };
            c.Owned.Add(m); result[i] = m;
            m.SetColor("_BaseColor", color); m.color = color;
            m.SetFloat("_Smoothness", emission ? .16f : .025f);
            m.SetFloat("_Metallic", 0);
            if (emission) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * .75f); }
            m.enableInstancing = true;
        }
        return result;
    }

    /// <summary>每个三角形独立顶点和法线，保持近景切面；子网格面色避免默认圆球外观。</summary>
    private sealed class Facets
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int>[] triangles;
        public Facets(int materials) { triangles = new List<int>[materials]; for (int i = 0; i < materials; i++) triangles[i] = new List<int>(); }
        public void Tri(Vector3 a, Vector3 b, Vector3 c, int color)
        {
            int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
            var list = triangles[Math.Abs(color) % triangles.Length]; list.Add(n); list.Add(n + 1); list.Add(n + 2);
        }
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int color)
        { Tri(a, b, c, color); Tri(a, c, d, color); }
        public GameObject Emit(Context c, Transform parent, string name, Material[] materials)
        {
            var mesh = new Mesh { name = "HV_" + name, hideFlags = HideFlags.DontSave };
            c.Owned.Add(mesh);
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = Group(parent, name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            return go;
        }
    }

    private static GameObject Group(Transform parent, string name)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(parent, false); go.layer = parent.gameObject.layer; return go;
    }

    /// <summary>固定哈希保证同一配置多次拍摄完全一致，不改变 Unity 全局随机状态。</summary>
    private static float Noise(int seed)
    {
        unchecked { uint x = (uint)seed; x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16; return (x & 65535) / 65535f; }
    }

    /// <summary>39×39 米地面沿用平坦可读的走道；低对比色块与碎面不会制造假高差。</summary>
    private static void Ground(Context c)
    {
        const int cells = 18;
        var grid = new Vector3[cells + 1, cells + 1];
        for (int x = 0; x <= cells; x++) for (int z = 0; z <= cells; z++)
        {
            float xx = -ArenaHalf + x * 39f / cells, zz = -ArenaHalf + z * 39f / cells;
            if (x > 0 && x < cells) xx += (Noise(x * 173 + z * 39) - .5f) * .9f;
            if (z > 0 && z < cells) zz += (Noise(x * 131 + z * 193) - .5f) * .9f;
            grid[x, z] = new Vector3(xx, -.032f, zz);
        }
        var f = new Facets(c.Ground.Length);
        for (int x = 0; x < cells; x++) for (int z = 0; z < cells; z++)
        {
            int tint = (int)(Noise(x * 379 + z * 59) * c.Ground.Length) % c.Ground.Length;
            var a = grid[x, z]; var b = grid[x + 1, z]; var d = grid[x, z + 1]; var e = grid[x + 1, z + 1];
            f.Tri(a, d, b, tint); f.Tri(b, d, e, tint);
        }
        f.Emit(c, c.Root, "FlatReadableGround_39m", c.Ground);
        var patches = new Facets(c.Dust.Length);
        for (int i = 0; i < 58; i++)
        {
            float x = Mathf.Lerp(-18.3f, 18.3f, Noise(753 + i * 31));
            float z = Mathf.Lerp(-18.3f, 18.3f, Noise(177 + i * 113));
            Patch(patches, new Vector3(x, -.024f + i * .00001f, z), .45f + Noise(i * 79 + 13) * 1.7f, .35f + Noise(i * 109 + 29), i * 17 + 5, i % c.Dust.Length);
        }
        patches.Emit(c, c.Root, "SubtleSoilAndAshPatches", c.Dust).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    /// <summary>八角不规则平面色块，最大半径受给定尺寸约束。</summary>
    private static void Patch(Facets f, Vector3 center, float halfX, float halfZ, int seed, int color)
    {
        const int count = 9;
        for (int i = 0; i < count; i++)
        {
            float a = i * Mathf.PI * 2 / count, b = (i + 1) * Mathf.PI * 2 / count;
            float ra = .72f + Noise(seed + i * 89) * .28f, rb = .72f + Noise(seed + ((i + 1) % count) * 89) * .28f;
            f.Tri(center, center + new Vector3(Mathf.Sin(a) * halfX * ra, 0, Mathf.Cos(a) * halfZ * ra),
                center + new Vector3(Mathf.Sin(b) * halfX * rb, 0, Mathf.Cos(b) * halfZ * rb), color);
        }
    }

    /// <summary>削角矩形岩块的三层收分网格；任何顶点均不超过指定宽深，无额外碰撞体。</summary>
    private static GameObject Rock(Context c, Transform parent, string name, Vector3 position, Vector3 size, int seed, bool moss = false)
    {
        var f = new Facets(c.Stone.Length);
        Vector2[] oct = { new Vector2(-.5f,-.31f), new Vector2(-.31f,-.5f), new Vector2(.31f,-.5f), new Vector2(.5f,-.31f),
            new Vector2(.5f,.31f), new Vector2(.31f,.5f), new Vector2(-.31f,.5f), new Vector2(-.5f,.31f) };
        var rings = new Vector3[3, oct.Length];
        for (int r = 0; r < 3; r++) for (int i = 0; i < oct.Length; i++)
        {
            float scale = r == 0 ? 1 : r == 1 ? .87f + Noise(seed + i * 13) * .10f : .68f + Noise(seed + i * 29) * .21f;
            float y = r == 0 ? 0 : r == 1 ? size.y * (.31f + Noise(seed + i * 43) * .15f) : size.y * (.86f + Noise(seed + i * 59) * .14f);
            rings[r, i] = position + new Vector3(oct[i].x * size.x * scale, y, oct[i].y * size.z * scale);
        }
        for (int r = 0; r < 2; r++) for (int i = 0; i < oct.Length; i++)
        {
            int j = (i + 1) % oct.Length;
            int tint = (int)(Noise(seed + i * 73 + r * 501) * c.Stone.Length) % c.Stone.Length;
            f.Tri(rings[r, i], rings[r + 1, i], rings[r, j], tint);
            f.Tri(rings[r, j], rings[r + 1, i], rings[r + 1, j], (tint + (i % 3 == 0 ? 1 : 0)) % c.Stone.Length);
        }
        Vector3 top = position + new Vector3(size.x * .025f, size.y * .91f, -size.z * .018f);
        for (int i = 0; i < oct.Length; i++) f.Tri(top, rings[2, (i + 1) % oct.Length], rings[2, i], (i + seed) % c.Stone.Length);
        var go = f.Emit(c, parent, name, c.Stone);
        if (moss)
        {
            // 贴着真实顶面制作薄苔面；不以悬浮色片遮住石块切面。
            var lichen = new Facets(c.Moss.Length);
            for (int i = 0; i < oct.Length; i += 2)
            {
                int j = (i + 1) % oct.Length;
                Vector3 a = Vector3.Lerp(top, rings[2, i], .83f), b = Vector3.Lerp(top, rings[2, j], .78f);
                Vector3 center = Vector3.Lerp(top, (a + b) * .5f, .34f);
                lichen.Tri(center + Vector3.up * .008f, b + Vector3.up * .008f, a + Vector3.up * .008f, i / 2);
            }
            lichen.Emit(c, go.transform, "StoneCreviceMoss", c.Moss);
        }
        return go;
    }

    /// <summary>长条足迹化为完整的低矮碎石基座和破损石门；石门顶部仍严格位于该足迹内。</summary>
    private static void HengeObstacle(Context c, ArcadeLayoutObstacle o, int index)
    {
        var holder = Group(c.Root, "ConfiguredHengeObstacle_" + index).transform;
        holder.localPosition = new Vector3(o.X, 0, o.Y);
        bool alongZ = o.HalfHeight > o.HalfWidth;
        if (alongZ) holder.localRotation = Quaternion.Euler(0, 90, 0);
        float length = Mathf.Max(o.HalfWidth, o.HalfHeight) * 2, depth = Mathf.Min(o.HalfWidth, o.HalfHeight) * 2;
        Rock(c, holder, "ContinuousRuinFootprint", Vector3.zero, new Vector3(length, .46f, depth), 290 + index, true);
        float tall = 2.25f + Noise(index * 59 + 73) * .65f;
        float pillar = Mathf.Min(length * .25f, 1.6f);
        Rock(c, holder, "WeatheredUprightA", new Vector3(-length * .30f, .24f, -.04f), new Vector3(pillar, tall, depth * .83f), index * 43 + 112, true);
        Rock(c, holder, "WeatheredUprightB", new Vector3(length * .30f, .24f, .06f), new Vector3(pillar * .93f, tall * .91f, depth * .79f), index * 97 + 118, true);
        if (index % 3 != 1)
        {
            Rock(c, holder, "ChippedAncientLintel", new Vector3(0, tall * .89f + .22f, 0), new Vector3(length * .91f, .56f, depth * .86f), index * 41 + 203, true);
        }
        else
        {
            Rock(c, holder, "BrokenLintelRemnant", new Vector3(-length * .22f, tall * .92f + .20f, 0), new Vector3(length * .48f, .43f, depth * .8f), index * 71 + 261, true);
            Rock(c, holder, "FallenSlabWithinFootprint", new Vector3(length * .13f, .36f, 0), new Vector3(length * .35f, .55f, depth * .78f), index * 71 + 262, true);
        }
        for (int i = 0; i < 5; i++)
        {
            float x = Mathf.Lerp(-.42f, .42f, Noise(index * 73 + i * 31 + 2)) * length;
            float z = (i % 2 == 0 ? -.40f : .40f) * depth;
            Rock(c, holder, "SmallFootprintRubble_" + i, new Vector3(x, .08f, z), new Vector3(.22f, .16f, .22f), index * 91 + i * 37);
        }
    }

    /// <summary>不规则玄武岩障碍保留矩形投影；熔岩只位于顶部真实凹口，普通地面保持安全灰色。</summary>
    private static void VolcanicObstacle(Context c, ArcadeLayoutObstacle o, int index)
    {
        var holder = Group(c.Root, "ConfiguredBasaltObstacle_" + index).transform;
        holder.localPosition = new Vector3(o.X, 0, o.Y);
        float width = o.HalfWidth * 2, depth = o.HalfHeight * 2;
        Rock(c, holder, "BasaltContinuousFootprint", Vector3.zero, new Vector3(width, .73f, depth), 197 + index * 31);
        float radiusX = Mathf.Min(width * .37f, 1.7f), radiusZ = Mathf.Min(depth * .37f, 1.7f);
        Crater(c, holder, "RecessedLavaRock", new Vector3(0, .48f, 0), radiusX, radiusZ, 1.35f + Noise(index + 503) * .4f, index * 61 + 411, false);
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0 ? -.38f : .38f) * width, z = (i < 2 ? -.35f : .35f) * depth;
            Rock(c, holder, "JaggedBasaltShard_" + i, new Vector3(x, .46f, z), new Vector3(width * .20f, .7f + Noise(index * 91 + i * 7) * .7f, depth * .23f), index * 71 + i * 193);
        }
    }

    /// <summary>四圈开口火山，凹入的内壁与低于火山口的熔岩面形成真实深度。</summary>
    private static void Crater(Context c, Transform parent, string name, Vector3 center, float radiusX, float radiusZ, float height, int seed, bool landmark)
    {
        const int count = 14;
        var f = new Facets(c.Stone.Length);
        var rings = new Vector3[4, count];
        for (int i = 0; i < count; i++)
        {
            float a = i * Mathf.PI * 2 / count;
            float irregular = .86f + Noise(seed + i * 31) * .14f;
            float crest = height * (.89f + Noise(seed + i * 71) * .11f);
            rings[0, i] = center + new Vector3(Mathf.Sin(a) * radiusX * irregular, 0, Mathf.Cos(a) * radiusZ * irregular);
            rings[1, i] = center + new Vector3(Mathf.Sin(a) * radiusX * .49f, crest, Mathf.Cos(a) * radiusZ * .49f);
            rings[2, i] = center + new Vector3(Mathf.Sin(a) * radiusX * .34f, crest - height * .055f, Mathf.Cos(a) * radiusZ * .34f);
            rings[3, i] = center + new Vector3(Mathf.Sin(a) * radiusX * .21f, height * .62f, Mathf.Cos(a) * radiusZ * .21f);
        }
        for (int i = 0; i < count; i++)
        {
            int j = (i + 1) % count;
            int tint = (i * 3 + seed) % c.Stone.Length;
            f.Quad(rings[0, j], rings[1, j], rings[1, i], rings[0, i], tint);
            f.Quad(rings[1, j], rings[2, j], rings[2, i], rings[1, i], (tint + 2) % c.Stone.Length);
            f.Quad(rings[2, j], rings[3, j], rings[3, i], rings[2, i], i % 2);
        }
        var go = f.Emit(c, parent, name, c.Stone);
        var lava = new Facets(c.Lava.Length);
        Patch(lava, center + Vector3.up * (height * .625f), radiusX * .215f, radiusZ * .215f, seed + 91, 1);
        lava.Emit(c, go.transform, landmark ? "OuterCraterLava" : "ObstacleTopLavaRecess", c.Lava);
        if (!landmark) return;
        // 火山坡面下半段是静态黑色熔岩壳；暖色裂口仅发生在不可活动的山体表面。
        var scars = new Facets(c.Lava.Length);
        foreach (int i in new[] { 2, 5, 9, 12 })
        {
            Vector3 high = Vector3.Lerp(rings[0, i], rings[1, i], .88f);
            Vector3 low = Vector3.Lerp(rings[0, i], rings[1, i], .42f);
            Vector3 tangent = (rings[0, (i + 1) % count] - rings[0, i]).normalized * .055f;
            Vector3 outward = new Vector3(high.x - center.x, 0, high.z - center.z).normalized * .025f;
            scars.Quad(low + tangent + outward, high + tangent * .5f + outward, high - tangent * .5f + outward, low - tangent + outward, 2);
        }
        scars.Emit(c, go.transform, "OuterSlopeEmberFissures", c.Lava);
    }

    /// <summary>通路只放低于脚踝的碎石和少量草；入口、出生点及中心保留安静可读的空间。</summary>
    private static void GroundDetail(Context c, ArcadeRoundLayoutParameters layout)
    {
        var pebbles = new Facets(c.Stone.Length);
        var grass = new Facets(c.Grass.Length);
        for (int i = 0; i < 125; i++)
        {
            float x = Mathf.Lerp(-18.4f, 18.4f, Noise(i * 97 + 31)), z = Mathf.Lerp(-18.4f, 18.4f, Noise(i * 41 + 79));
            if (x * x + z * z < 14 || NearEntry(layout, x, z) || Mathf.Abs(x - layout.SpawnX) < 1.5f && Mathf.Abs(z - layout.SpawnY) < 1.5f) continue;
            bool within = false;
            foreach (var o in layout.Obstacles) if (Mathf.Abs(x - o.X) < o.HalfWidth + .28f && Mathf.Abs(z - o.Y) < o.HalfHeight + .28f) { within = true; break; }
            if (within) continue;
            float r = .055f + Noise(i * 23 + 4) * .10f;
            Vector3 p = new Vector3(x, -.015f, z), tip = p + new Vector3(.015f, .07f + r * .2f, 0);
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 2 / 5, b = (k + 1) * Mathf.PI * 2 / 5;
                pebbles.Tri(p + new Vector3(Mathf.Sin(b) * r, 0, Mathf.Cos(b) * r), tip,
                    p + new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r), (i + k) % c.Stone.Length);
            }
            if (c.Volcano || i % 4 != 0) continue;
            for (int k = 0; k < 5; k++)
            {
                float a = k * 2.4f + i, h = .10f + Noise(k * 131 + i) * .15f;
                Vector3 side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .035f;
                Vector3 crown = p + new Vector3(Mathf.Sin(a) * .09f, h, Mathf.Cos(a) * .09f);
                grass.Tri(p - side, crown, p + side, k % c.Grass.Length);
                grass.Tri(p + side, crown, p - side, k % c.Grass.Length);
            }
        }
        pebbles.Emit(c, c.Root, "AnkleLowScatteredPebbles", c.Stone);
        if (!c.Volcano) grass.Emit(c, c.Root, "SparseLowGrassTufts", c.Grass);
        else
        {
            // 安全地面裂纹采用冷灰色，不画会暗示伤害的橙色熔岩。
            var cracks = new Facets(c.Stone.Length);
            for (int i = 0; i < 22; i++)
            {
                float x = Mathf.Lerp(-17.5f, 17.5f, Noise(i * 139 + 16)), z = Mathf.Lerp(-17.5f, 17.5f, Noise(i * 79 + 13));
                if (NearEntry(layout, x, z)) continue;
                var p = new Vector3(x, -.017f, z);
                var q = p + new Vector3(.3f + Noise(i * 173) * .6f, 0, .24f);
                var r = q + new Vector3(.4f, 0, -.16f);
                cracks.Quad(p, p + Vector3.forward * .025f, q + Vector3.forward * .025f, q, 1);
                cracks.Quad(q, q + Vector3.forward * .025f, r + Vector3.forward * .016f, r, 1);
            }
            cracks.Emit(c, c.Root, "ColdAshHairlineCracks", c.Stone).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    private static bool NearEntry(ArcadeRoundLayoutParameters layout, float x, float z)
    {
        foreach (var entry in layout.Entries) if (Mathf.Abs(x - entry.X) < 2.5f && Mathf.Abs(z - entry.Y) < 2.5f) return true;
        return Mathf.Abs(x) < 2.2f && Mathf.Abs(z) > 15 || Mathf.Abs(z) < 2.2f && Mathf.Abs(x) > 15;
    }

    /// <summary>外围不封墙、不设门；独立巨石只占四角景区，北侧中轴入口完整保留。</summary>
    private static void HengeOutside(Context c)
    {
        var outside = Group(c.Root, "OutsidePlayableArea_MegalithLandmarks").transform;
        Rock(c, outside, "NorthernAncientMonolithA", new Vector3(-12.7f, -.05f, 22.2f), new Vector3(2.9f, 5.1f, 2.6f), 981, true);
        Rock(c, outside, "NorthernAncientMonolithB", new Vector3(-7.7f, -.05f, 22.5f), new Vector3(2.7f, 4.7f, 2.5f), 1057, true);
        Rock(c, outside, "BrokenGiantLintel", new Vector3(-10.2f, 4.25f, 22.4f), new Vector3(7.9f, .9f, 2.5f), 1093, true);
        Rock(c, outside, "SolitaryWeatheredMenhir", new Vector3(11, -.05f, 22.4f), new Vector3(3.1f, 4.6f, 2.5f), 1201, true);
        Rock(c, outside, "SplitMenhirShard", new Vector3(15.4f, -.05f, 22.7f), new Vector3(1.5f, 2.6f, 1.8f), 1271, true);
        Rock(c, outside, "WesternFallenStone", new Vector3(-22.1f, -.05f, -10.3f), new Vector3(3.9f, 1.45f, 5.1f), 1313, true);
        Rock(c, outside, "EasternCrag", new Vector3(22.1f, -.05f, 9.5f), new Vector3(3.8f, 2.65f, 4.7f), 1439, true);
        var patches = new Facets(c.Moss.Length);
        foreach (var p in new[] { new Vector3(-11, -.033f, 23), new Vector3(12, -.033f, 23), new Vector3(-23, -.033f, -10), new Vector3(23, -.033f, 10) })
            Patch(patches, p, p.z > 20 ? 7.5f : 3.1f, p.z > 20 ? 2.8f : 5.2f, (int)(p.x * 17), 2);
        patches.Emit(c, outside, "OuterMossAndEarthIslands", c.Moss);
    }

    /// <summary>真实开口火山位于北边界外；外圈孤立熔岩池也完全处于不可活动区。</summary>
    private static void VolcanoOutside(Context c)
    {
        var outside = Group(c.Root, "OutsidePlayableArea_VolcanoLandmarks").transform;
        Crater(c, outside, "NorthernFacetedVolcanoWithOpenCrater", new Vector3(-9.5f, -.04f, 26.4f), 8.2f, 6.4f, 6.1f, 1559, true);
        Crater(c, outside, "DistantSecondaryCrater", new Vector3(13.4f, -.04f, 24), 4.3f, 3.5f, 3.8f, 1657, true);
        Rock(c, outside, "WesternBasaltOutcrop", new Vector3(-22.4f, -.04f, 11), new Vector3(4.6f, 2.3f, 6), 1777);
        Rock(c, outside, "EasternBasaltOutcrop", new Vector3(22.5f, -.04f, -9), new Vector3(4.8f, 1.8f, 6), 1871);
        var dark = new Facets(c.Stone.Length);
        var lava = new Facets(c.Lava.Length);
        foreach (var p in new[] { new Vector3(-23.4f, -.04f, -12.5f), new Vector3(23.4f, -.04f, 10.5f), new Vector3(-13, -.04f, -22.7f), new Vector3(13, -.04f, -22.7f) })
        {
            Patch(dark, p, 3, 2.5f, (int)(p.x * 29), 0);
            Patch(lava, p + Vector3.up * .012f, 2.1f, 1.7f, (int)(p.x * 29), 0);
            Patch(lava, p + new Vector3(.2f, .014f, .1f), 1.3f, .8f, (int)(p.x * 39), 1);
        }
        dark.Emit(c, outside, "OuterDarkLavaBanks", c.Stone);
        lava.Emit(c, outside, "OuterSeparatedLavaPools", c.Lava).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }
}
