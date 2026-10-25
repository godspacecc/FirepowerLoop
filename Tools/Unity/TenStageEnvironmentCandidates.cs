using System;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>三关待采用环境的独立几何草稿；只供临时场景预览，不保存或引用正式美术。</summary>
public static class TenStageEnvironmentCandidates
{
    /// <summary>使用真实轮级障碍尺寸建立候选，全部动态网格和材质交给调用方统一释放。</summary>
    public static GameObject Build(Transform parent, ArcadeRoundLayoutParameters layout, List<Object> owned)
    {
        if (layout == null || owned == null) throw new ArgumentNullException();
        if (!((layout.ArenaConfigNumber == 7 && layout.ThemeKey == "fort") ||
              (layout.ArenaConfigNumber == 10 && layout.ThemeKey == "koi") ||
              (layout.ArenaConfigNumber == 20 && layout.ThemeKey == "papaback")))
            throw new ArgumentException("候选只支持配置7/10/20");
        var root = new GameObject("TEMP_UnapprovedEnvironment_" + layout.Id);
        root.transform.SetParent(parent, false);
        var b = new Builder(root.transform, owned);
        b.Ground(layout);
        for (int i = 0; i < layout.Obstacles.Count; i++)
        {
            var o = layout.Obstacles[i];
            var holder = b.Group(root.transform, "ConfiguredObstacle_" + i, new Vector3(o.X, 0, o.Y));
            if (layout.ThemeKey == "fort") b.FortObstacle(holder, o.HalfWidth * 2, o.HalfHeight * 2, i);
            else if (layout.ThemeKey == "koi") b.Pond(holder, o.HalfWidth * 2, o.HalfHeight * 2, i);
            else b.InfectedFacility(holder, o.HalfWidth * 2, o.HalfHeight * 2, i);
        }
        b.Boundary(layout.ThemeKey);
        return root;
    }

    /// <summary>构造器仅持有本次临时根与资源清单，无静态资源缓存、碰撞体和自动更新。</summary>
    private sealed class Builder
    {
        private readonly Transform root;
        private readonly List<Object> owned;
        private readonly Dictionary<uint, Material> palette = new Dictionary<uint, Material>();
        private const uint Sand = 0xB9AC8AFF, SandLight = 0xD5C4A0FF, SandDark = 0x8F8064FF;
        private const uint Rock = 0x87948CFF, RockLight = 0xADB4A0FF, Wood = 0x8F6651FF;
        private const uint Metal = 0x667D88FF, MetalLight = 0x95A6A9FF, MetalDark = 0x3F525EFF;
        private const uint Flesh = 0x9D514FFF, FleshLight = 0xBC7663FF, FleshDark = 0x663D48FF;

        public Builder(Transform root, List<Object> owned) { this.root = root; this.owned = owned; }

        public Transform Group(Transform parent, string name, Vector3 position)
        {
            var group = new GameObject(name).transform; group.SetParent(parent, false); group.localPosition = position; return group;
        }

        /// <summary>39米平坦地面只铺可通行石板或薄木板；不画不存在的水渠、落差或危险区域。</summary>
        public void Ground(ArcadeRoundLayoutParameters p)
        {
            var g = Group(root, "WalkableGround_39x39", Vector3.zero);
            bool fort = p.ThemeKey == "fort", koi = p.ThemeKey == "koi";
            Box(g, "Ground", new Vector3(0, -.14f, 0), new Vector3(39, .28f, 39), fort ? 0xB9B095FFu : koi ? 0xA9B394FFu : 0x7C8B90FFu, .01f);
            if (fort || koi)
            {
                // 浅色石缝保留中心安静区；逐块判断可走面，不在真实障碍下面生成假步道。
                for (int x = -17; x <= 17; x += 2)
                for (int z = -17; z <= 17; z += 2)
                {
                    float w = 1.89f, d = 1.89f;
                    if (Overlaps(p, x, z, w * .5f, d * .5f)) continue;
                    bool central = Mathf.Abs(x) < 4 || Mathf.Abs(z + 11) < 2;
                    if (!central && ((x * 17 + z * 13 + 400) % 7 > 3)) continue;
                    uint c = fort ? (((x + z) % 4 == 0) ? 0xC9C0A5FFu : 0xC2B79AFFu) :
                        (central ? 0xCACBB5FFu : 0xB6BDA2FFu);
                    Box(g, "FlushCourtyardSlab", new Vector3(x, .006f, z), new Vector3(w, .012f, d), c, .06f);
                }
                if (koi)
                {
                    // 木步道沿原有中央可走线铺平，任何板都不得与水池投影相交。
                    for (float z = -16; z < 16; z += .64f)
                    {
                        if (Overlaps(p, 0, z, 1.65f, .28f)) continue;
                        Box(g, "FlushTimberWalk", new Vector3(0, .022f, z), new Vector3(3.3f, .035f, .55f), ((int)(z * 10) % 3 == 0) ? 0xA6845CFFu : 0xB0936BFFu, .02f);
                    }
                    for (int side = -1; side <= 1; side += 2)
                    for (float z = -16; z < 16; z += 1.28f)
                        if (!Overlaps(p, side * 1.45f, z, .025f, .4f))
                            Box(g, "TimberPeg", new Vector3(side * 1.45f, .045f, z), new Vector3(.05f, .012f, .05f), Wood, 0);
                }
            }
            else
            {
                // 冷灰板材与细接缝保留技能预警对比，不以红色涂满整个战场。
                for (int x = -18; x <= 18; x += 3)
                for (int z = -18; z <= 18; z += 3)
                {
                    if (Overlaps(p, x, z, 1.44f, 1.44f)) continue;
                    uint c = ((x / 3 + z / 3 + 20) % 4 == 0) ? 0x89979AFFu : 0x818F94FFu;
                    Box(g, "FlushFacilityPanel", new Vector3(x, .008f, z), new Vector3(2.92f, .016f, 2.92f), c, .025f);
                    if (Mathf.Abs(x) > 14 && z % 6 == 0)
                        Box(g, "PanelDrain", new Vector3(x, .019f, z), new Vector3(1.4f, .014f, .075f), MetalDark, 0);
                }
                foreach (float x in new[] { -16.5f, 16.5f })
                    for (int z = -15; z <= 15; z += 3)
                        if (!Overlaps(p, x, z, .045f, .8f)) Box(g, "PerimeterServiceLine", new Vector3(x, .023f, z), new Vector3(.09f, .018f, 1.6f), 0xC4C7ACFFu, 0);
            }
        }

        private static bool Overlaps(ArcadeRoundLayoutParameters p, float x, float z, float hw, float hd)
        {
            foreach (var o in p.Obstacles)
                if (Mathf.Abs(o.X - x) < o.HalfWidth + hw + .08f && Mathf.Abs(o.Y - z) < o.HalfHeight + hd + .08f) return true;
            return false;
        }

        /// <summary>砂岩壁垒沿矩形长轴分段砌筑；底座精确占满足迹，断口和城垛不越过配置边。</summary>
        public void FortObstacle(Transform holder, float w, float d, int seed)
        {
            bool longZ = d > w; float length = Mathf.Max(w, d), width = Mathf.Min(w, d);
            var t = Group(holder, "SandstoneCurtainWall", Vector3.zero);
            if (longZ) t.localRotation = Quaternion.Euler(0, 90, 0);
            Box(t, "ExactMasonryFootprint", new Vector3(0, .13f, 0), new Vector3(length, .26f, width), SandDark, .07f);
            int n = Mathf.Max(3, Mathf.CeilToInt(length / 1.8f)); float step = length / n;
            for (int i = 0; i < n; i++)
            {
                float x = -length * .5f + step * (i + .5f);
                float h = 1.55f + .18f * ((i + seed) % 3);
                if (i == n / 2 && seed % 2 == 1) h = .82f;
                Box(t, "WeatheredMasonry", new Vector3(x, .26f + h * .5f, 0), new Vector3(step - .055f, h, width * .88f), i % 2 == 0 ? Sand : SandLight, .14f);
                Box(t, "LowerCourse", new Vector3(x, .48f, -width * .445f), new Vector3(step * .84f, .24f, .055f), SandDark, .018f);
                Box(t, "CapStone", new Vector3(x, h + .3f, 0), new Vector3(step * .98f, .19f, width * .96f), SandLight, .055f);
                if (i % 3 != 1)
                    Box(t, "BrokenMerlon", new Vector3(x, h + .68f, 0), new Vector3(step * .52f, .64f + .12f * (i % 2), width * .8f), Sand, .11f);
                // 砌缝是附着在立面的浅色短线；无另加通路阻挡。
                Box(t, "VerticalMortarJoint", new Vector3(x - step * .24f, .9f, -width * .448f), new Vector3(.035f, .4f, .028f), SandDark, 0);
            }
            for (int end = -1; end <= 1; end += 2)
            {
                float x = end * (length * .5f - Mathf.Min(width * .31f, .48f));
                Box(t, "EndButtress", new Vector3(x, .8f, 0), new Vector3(Mathf.Min(width * .6f, .85f), 1.6f, width * .98f), SandDark, .10f);
            }
            if (width > 2.5f)
                Facet(t, "BrokenStoneFace", new Vector3(length * .12f, .78f, width * .24f), new Vector3(length * .16f, .72f, width * .2f), RockLight, 6, seed);
        }

        /// <summary>水面只存在于原配置阻挡矩形内；石岸沿足迹边围合，锦鲤为静态装饰采样。</summary>
        public void Pond(Transform t, float w, float d, int seed)
        {
            Box(t, "ExactPondFootprint", new Vector3(0, .055f, 0), new Vector3(w, .11f, d), 0x5A7167FFu, .055f);
            float bank = Mathf.Min(.43f, Mathf.Min(w, d) * .15f);
            Box(t, "StillPondWater", new Vector3(0, .125f, 0), new Vector3(w - bank * 1.65f, .025f, d - bank * 1.65f), 0x477E80FFu, .03f);
            // 底部浅色块与细波线形成深浅变化，不使用全屏蓝条或额外透明Shader。
            for (int j = 0; j < 4; j++)
            {
                float z = (j - 1.5f) * (d - bank * 3) * .23f;
                Box(t, "QuietWaterFacet", new Vector3((j % 2 == 0 ? -.16f : .17f) * w, .143f, z), new Vector3(w * .25f, .009f, Mathf.Min(.16f, d * .05f)), j % 2 == 0 ? 0x609593FFu : 0x578C8CFFu, .02f);
            }
            int nx = Mathf.Max(2, Mathf.CeilToInt(w / .95f)), nz = Mathf.Max(2, Mathf.CeilToInt(d / .95f));
            for (int s = -1; s <= 1; s += 2)
            {
                for (int i = 0; i < nx; i++)
                    Box(t, "NorthSouthBankStone", new Vector3(-w * .5f + w / nx * (i + .5f), .23f, s * (d - bank) * .5f), new Vector3(w / nx - .035f, .30f + .04f * (i % 2), bank), i % 3 == 0 ? RockLight : Rock, .09f);
                for (int i = 0; i < nz; i++)
                    Box(t, "EastWestBankStone", new Vector3(s * (w - bank) * .5f, .23f, -d * .5f + d / nz * (i + .5f)), new Vector3(bank, .30f + .03f * (i % 2), d / nz - .035f), i % 3 == 1 ? RockLight : Rock, .09f);
            }
            float rockX = -w * .5f + bank + Mathf.Min(.65f, w * .2f);
            float rockZ = d * .5f - bank - Mathf.Min(.65f, d * .2f);
            Facet(t, "PondViewingStone", new Vector3(rockX, .5f, rockZ), new Vector3(Mathf.Min(.7f, w * .2f), .65f, Mathf.Min(.7f, d * .2f)), Rock, 7, seed);
            float lanternScale = Mathf.Min(1, Mathf.Min(w, d) / 3.2f);
            Lantern(t, new Vector3(w * .5f - bank * .62f, .34f, -d * .5f + bank * .62f), lanternScale);
            // 鱼长小于主角腿部，在原水面内部交错；只展示静态候选，不暗示新玩法。
            int fishCount = w * d > 30 ? 3 : 2;
            for (int i = 0; i < fishCount; i++)
            {
                float x = (i % 2 == 0 ? -.16f : .19f) * (w - bank * 3);
                float z = (i - (fishCount - 1) * .5f) * Mathf.Min(.7f, d * .20f);
                var f = Group(t, "StaticKoi_" + i, new Vector3(x, .158f, z)); f.localRotation = Quaternion.Euler(0, 32 + 105 * i + seed * 17, 0);
                Facet(f, "KoiBody", Vector3.zero, new Vector3(.16f, .035f, .38f), i % 2 == 0 ? 0xDDD9BBFFu : 0xC6764FFFu, 6, i);
                Facet(f, "KoiRedPatch", new Vector3(0, .022f, .07f), new Vector3(.10f, .014f, .13f), 0xB96048FFu, 5, i + 1);
                Triangle(f, "KoiTail", new Vector3(0, .004f, -.29f), new Vector3(-.13f, .004f, -.43f), new Vector3(.13f, .004f, -.43f), 0xC6BDA4FFu, true);
            }
        }

        private void Lantern(Transform t, Vector3 p, float s)
        {
            var l = Group(t, "StoneLantern", p);
            // 足部向水池内部收拢，连同灯盖始终处于矩形内。
            l.localPosition += new Vector3(-.18f * s, 0, .18f * s);
            Box(l, "LanternFoot", new Vector3(0, .08f * s, 0), new Vector3(.43f, .16f, .43f) * s, RockLight, .045f * s);
            Prism(l, "LanternColumn", new Vector3(0, .37f * s, 0), .11f * s, .5f * s, 6, RockLight);
            Box(l, "LanternLight", new Vector3(0, .70f * s, 0), new Vector3(.27f, .27f, .27f) * s, 0xD0B57DFFu, .035f * s);
            Cone(l, "LanternRoof", new Vector3(0, .92f * s, 0), .31f * s, .16f * s, 4, 0x717F75FFu);
        }

        /// <summary>既有设施足迹改为被组织攀附的机组；金属围边与暗红组织保持可分辨。</summary>
        public void InfectedFacility(Transform t, float w, float d, int seed)
        {
            bool longZ = d > w; float length = Mathf.Max(w, d), width = Mathf.Min(w, d);
            var h = Group(t, "InfestedServiceModule", Vector3.zero); if (longZ) h.localRotation = Quaternion.Euler(0, 90, 0);
            Box(h, "ExactFacilityFootprint", new Vector3(0, .12f, 0), new Vector3(length, .24f, width), MetalDark, .055f);
            int count = Mathf.Max(2, Mathf.CeilToInt(length / 2.3f)); float step = length / count;
            for (int i = 0; i < count; i++)
            {
                float x = -length * .5f + step * (i + .5f);
                float height = 1.1f + .24f * ((seed + i) % 3);
                Box(h, "BevelledMachineHousing", new Vector3(x, .24f + height * .5f, 0), new Vector3(step * .93f, height, width * .84f), (i + seed) % 2 == 0 ? Metal : MetalLight, .14f);
                Box(h, "UpperServiceRim", new Vector3(x, height + .3f, 0), new Vector3(step * .96f, .12f, width * .9f), MetalDark, .04f);
                for (int n = 0; n < 3; n++)
                    Box(h, "VentSlit", new Vector3(x + (n - 1) * step * .20f, .82f, -width * .424f), new Vector3(step * .1f, .38f, .028f), MetalDark, .01f);
                Box(h, "ServiceLabel", new Vector3(x - step * .3f, .42f, -width * .435f), new Vector3(step * .17f, .16f, .025f), 0xC1B47BFFu, .01f);
                // 感染块使用真正分面几何；中心收束，不向矩形外蔓延。
                float skinX = x + step * .1f;
                Facet(h, "InfectionRootMass", new Vector3(skinX, height + .37f, width * .12f), new Vector3(step * .35f, .52f + .13f * (i % 2), width * .30f), FleshDark, 7, seed + i);
                Facet(h, "RedTissueFold", new Vector3(skinX - step * .06f, height + .64f, width * .05f), new Vector3(step * .23f, .40f, width * .21f), Flesh, 6, seed + i + 7);
                Facet(h, "TissueHighlight", new Vector3(skinX + step * .09f, height + .72f, width * .11f), new Vector3(step * .11f, .21f, width * .13f), FleshLight, 5, seed + i + 3);
                var a = new Vector3(x - step * .25f, height + .32f, -width * .30f);
                var b = new Vector3(x + step * .2f, height + .46f, width * .20f);
                Beam(h, "SurfaceTendon", a, b, Mathf.Min(.14f, width * .08f), Flesh, 5);
            }
            Box(h, "FrontArmorLip", new Vector3(0, .28f, -width * .46f), new Vector3(length * .97f, .2f, width * .08f), MetalLight, .02f);
            Box(h, "RearArmorLip", new Vector3(0, .28f, width * .46f), new Vector3(length * .97f, .2f, width * .08f), MetalLight, .02f);
        }

        /// <summary>所有高地标只放场地边外，四个入口正前方留空；前沿只用低矮围边。</summary>
        public void Boundary(string theme)
        {
            var outside = Group(root, "OutsidePlayableArea_Landmarks", Vector3.zero);
            if (theme == "fort")
            {
                foreach (float x in new[] { -14f, 14f })
                {
                    Tower(outside, x, 23, 1);
                    var wing = Group(outside, "RearCurtainWing", new Vector3(x > 0 ? 8 : -8, 0, 23));
                    FortObstacle(wing, 6, 1.6f, x > 0 ? 2 : 1);
                }
                foreach (float x in new[] { -21f, 21f })
                foreach (float z in new[] { -12f, 11f })
                    Box(outside, "LowBrokenBoundary", new Vector3(x, .55f, z), new Vector3(1.3f, 1.1f, 9), SandDark, .15f);
                foreach (float x in new[] { -16f, 16f })
                    Box(outside, "ForegroundLowWall", new Vector3(x, .4f, -21), new Vector3(5, .8f, 1.3f), Sand, .12f);
            }
            else if (theme == "koi")
            {
                foreach (float x in new[] { -12f, 12f })
                {
                    var gate = Group(outside, "OuterTorii", new Vector3(x, 0, 23));
                    foreach (float side in new[] { -2.4f, 2.4f })
                    {
                        Box(gate, "ToriiStoneFoot", new Vector3(side, .15f, 0), new Vector3(.8f, .3f, .8f), Rock, .07f);
                        Prism(gate, "RedCedarPillar", new Vector3(side, 2.15f, 0), .24f, 4, 8, 0x995F4CFFu);
                    }
                    Box(gate, "ToriiLowerBeam", new Vector3(0, 3.45f, 0), new Vector3(5.6f, .25f, .36f), 0x995F4CFFu, .03f);
                    Box(gate, "ToriiUpperBeam", new Vector3(0, 4.3f, 0), new Vector3(6.4f, .36f, .6f), 0x505F56FFu, .08f);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var tip = Box(gate, "ToriiRaisedTip", new Vector3(side * 3.15f, 4.41f, 0), new Vector3(1, .27f, .62f), 0x505F56FFu, .05f);
                        tip.localRotation = Quaternion.Euler(0, 0, side * 12);
                    }
                }
                foreach (float x in new[] { -21.5f, 21.5f })
                foreach (float z in new[] { -13f, -7f, 7f, 13f })
                {
                    Box(outside, "BambooPlantingBed", new Vector3(x, .15f, z), new Vector3(2.3f, .3f, 3), 0x677A5CFFu, .10f);
                    for (int i = 0; i < 4; i++)
                    {
                        float bx = x + (i % 2 - .5f) * .8f, bz = z + (i / 2 - .5f) * 1.3f;
                        float bh = 1.6f + i * .23f;
                        Prism(outside, "BambooCulm", new Vector3(bx, bh * .5f, bz), .065f, bh, 5, 0x7C925FFFu);
                        Facet(outside, "BambooLeafSpray", new Vector3(bx, bh * .83f, bz), new Vector3(.35f, .22f, .65f), i % 2 == 0 ? 0x718653FFu : 0x85975FFFu, 5, i);
                    }
                }
                foreach (float x in new[] { -12f, 12f })
                    Box(outside, "ForegroundGardenEdge", new Vector3(x, .2f, -21), new Vector3(10, .4f, 1.2f), Rock, .09f);
            }
            else
            {
                foreach (float x in new[] { -13f, 13f }) LaunchFacility(outside, x, 24);
                foreach (float x in new[] { -21f, 21f })
                foreach (float z in new[] { -11f, 11f })
                {
                    Box(outside, "QuarantineBoundary", new Vector3(x, .58f, z), new Vector3(1.3f, 1.16f, 11), MetalDark, .14f);
                    for (int i = 0; i < 4; i++)
                        Box(outside, "BoundaryBrace", new Vector3(x, 1.0f, z - 4 + i * 2.5f), new Vector3(1.5f, .3f, .48f), MetalLight, .045f);
                }
                foreach (float x in new[] { -12f, 12f })
                    Box(outside, "ForegroundBlastWall", new Vector3(x, .35f, -21), new Vector3(10, .7f, 1.4f), Metal, .12f);
            }
        }

        private void Tower(Transform t, float x, float z, float s)
        {
            var tower = Group(t, "WeatheredFortTower", new Vector3(x, 0, z));
            Box(tower, "TowerBatteredFoot", new Vector3(0, .4f, 0), new Vector3(4.7f, .8f, 4.7f), SandDark, .22f);
            Box(tower, "TowerShaft", new Vector3(0, 2.7f, 0), new Vector3(3.9f, 4.8f, 3.9f), Sand, .30f);
            Box(tower, "TowerUpperCourse", new Vector3(0, 5.2f, 0), new Vector3(4.5f, .45f, 4.5f), SandLight, .12f);
            for (int i = 0; i < 3; i++)
            foreach (int side in new[] { -1, 1 })
            {
                Box(tower, "TowerCrenellation", new Vector3((i - 1) * 1.55f, 5.8f, side * 1.72f), new Vector3(.9f, .95f, 1.0f), Sand, .11f);
                if (i == 1) continue;
                Box(tower, "TowerSideCrenellation", new Vector3(side * 1.72f, 5.8f, (i - 1) * 1.55f), new Vector3(1, .95f, .9f), Sand, .11f);
            }
            foreach (float xx in new[] { -.82f, .82f }) Box(tower, "ArrowSlit", new Vector3(xx, 3.45f, -1.958f), new Vector3(.18f, .95f, .03f), 0x5E5B50FFu, .015f);
            Beam(tower, "FlagPole", new Vector3(0, 5.5f, 0), new Vector3(0, 7.5f, 0), .055f, 0x5C635BFFu, 6);
            Triangle(tower, "MutedBurgundyBanner", new Vector3(.04f, 7.35f, 0), new Vector3(1.45f, 7.15f, .12f), new Vector3(.04f, 6.55f, 0), 0x955A54FFu, true);
        }

        private void LaunchFacility(Transform t, float x, float z)
        {
            var f = Group(t, "SealedInfectedLaunchStation", new Vector3(x, 0, z));
            Box(f, "StationBlastBase", new Vector3(0, .55f, 0), new Vector3(8, 1.1f, 5), MetalDark, .18f);
            Box(f, "StationHousing", new Vector3(0, 1.8f, .65f), new Vector3(7.3f, 2.6f, 3.2f), Metal, .18f);
            Box(f, "SealedSiloDoor", new Vector3(0, 1.6f, -1.01f), new Vector3(2.6f, 2.0f, .10f), MetalDark, .06f);
            foreach (float side in new[] { -1f, 1f })
            {
                Box(f, "DoorBrace", new Vector3(side * .72f, 1.6f, -1.08f), new Vector3(.19f, 2.15f, .13f), MetalLight, .02f);
                Box(f, "LaunchGantryLeg", new Vector3(side * 2.35f, 4.1f, 1), new Vector3(.32f, 7.4f, .48f), MetalDark, .04f);
                Beam(f, "GantryDiagonal", new Vector3(side * 2.35f, 3, .9f), new Vector3(side * .8f, 6.7f, .9f), .10f, MetalLight, 4);
            }
            Box(f, "LaunchGantryBeam", new Vector3(0, 7.2f, 1), new Vector3(5.4f, .45f, .70f), MetalLight, .07f);
            Prism(f, "OldRocketBody", new Vector3(0, 4.2f, 1), .85f, 5.3f, 10, MetalLight);
            Cone(f, "RocketNose", new Vector3(0, 7.45f, 1), .86f, 1.2f, 10, 0xA4685BFFu);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2;
                Facet(f, "InfestationAtSilo", new Vector3(Mathf.Cos(a) * 2.5f, 1.3f, Mathf.Sin(a) * 1.4f), new Vector3(1.3f, .85f, .8f), i % 2 == 0 ? FleshDark : Flesh, 7, i + 11);
            }
            Box(f, "StationIdentifier", new Vector3(2.3f, 2.5f, -1.02f), new Vector3(1.3f, .44f, .045f), 0xB9B591FFu, .03f);
        }

        private Material Mat(uint color)
        {
            if (palette.TryGetValue(color, out var cached)) return cached;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("缺少通用Lit Shader");
            var c = new Color((color >> 24 & 255) / 255f, (color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f, 1);
            var material = new Material(shader) { name = "TEMP_TenStageMat_" + color.ToString("X8"), color = c };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .08f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .08f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
            palette.Add(color, material); owned.Add(material); return material;
        }

        /// <summary>逐三角形独立顶点与法线，避免程序岩石变成光滑球；不创建原始体碰撞器。</summary>
        private Transform MeshPart(Transform t, string name, List<Vector3> v, List<int> triangles, uint color)
        {
            var mesh = new Mesh { name = "TEMP_" + name }; mesh.SetVertices(v); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); owned.Add(mesh);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(t, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = Mat(color); return go.transform;
        }

        private static void Tri(List<Vector3> v, List<int> ids, Vector3 a, Vector3 b, Vector3 c)
        { int n = v.Count; v.Add(a); v.Add(b); v.Add(c); ids.Add(n); ids.Add(n + 1); ids.Add(n + 2); }
        private static void Quad(List<Vector3> v, List<int> ids, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        { Tri(v, ids, a, b, c); Tri(v, ids, a, c, d); }

        /// <summary>倒角长方体保留精确最大外轮廓；每条边实际分面而非光滑法线。</summary>
        private Transform Box(Transform t, string name, Vector3 center, Vector3 size, uint color, float bevel)
        {
            var v = new List<Vector3>(); var ids = new List<int>();
            float hx = size.x * .5f, hy = size.y * .5f, hz = size.z * .5f;
            float b = Mathf.Max(0, Mathf.Min(bevel, Mathf.Min(hx, Mathf.Min(hy, hz)) * .8f));
            Vector3[] bottom = Ring(hx, hz, b, -hy + b), top = Ring(hx, hz, b, hy - b);
            for (int i = 0; i < 8; i++)
            {
                int j = (i + 1) % 8;
                Quad(v, ids, bottom[i], top[i], top[j], bottom[j]);
                Vector3 a = new Vector3(Mathf.Clamp(top[i].x, -hx + b, hx - b), hy, Mathf.Clamp(top[i].z, -hz + b, hz - b));
                Vector3 c = new Vector3(Mathf.Clamp(top[j].x, -hx + b, hx - b), hy, Mathf.Clamp(top[j].z, -hz + b, hz - b));
                Quad(v, ids, top[i], a, c, top[j]); Tri(v, ids, new Vector3(0, hy, 0), c, a);
                Vector3 ba = new Vector3(a.x, -hy, a.z), bc = new Vector3(c.x, -hy, c.z);
                Quad(v, ids, bottom[j], bc, ba, bottom[i]); Tri(v, ids, new Vector3(0, -hy, 0), ba, bc);
            }
            var part = MeshPart(t, name, v, ids, color); part.localPosition = center; return part;
        }

        private static Vector3[] Ring(float x, float z, float b, float y) => new[]
        {
            new Vector3(-x+b,y,-z),new Vector3(x-b,y,-z),new Vector3(x,y,-z+b),new Vector3(x,y,z-b),
            new Vector3(x-b,y,z),new Vector3(-x+b,y,z),new Vector3(-x,y,z-b),new Vector3(-x,y,-z+b)
        };

        private void Prism(Transform t, string name, Vector3 center, float radius, float height, int sides, uint color)
        { Frustum(t, name, center, radius, radius, height, sides, color); }
        private void Cone(Transform t, string name, Vector3 center, float radius, float height, int sides, uint color)
        { Frustum(t, name, center, radius, 0, height, sides, color); }
        private Transform Frustum(Transform t, string name, Vector3 center, float bottom, float top, float height, int sides, uint color)
        {
            var v = new List<Vector3>(); var ids = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                var p = center + new Vector3(Mathf.Cos(a) * bottom, -height * .5f, Mathf.Sin(a) * bottom);
                var q = center + new Vector3(Mathf.Cos(b) * bottom, -height * .5f, Mathf.Sin(b) * bottom);
                var r = center + new Vector3(Mathf.Cos(a) * top, height * .5f, Mathf.Sin(a) * top);
                var s = center + new Vector3(Mathf.Cos(b) * top, height * .5f, Mathf.Sin(b) * top);
                Quad(v, ids, p, r, s, q);
                Tri(v, ids, center + Vector3.up * height * .5f, s, r); Tri(v, ids, center - Vector3.up * height * .5f, p, q);
            }
            return MeshPart(t, name, v, ids, color);
        }

        /// <summary>六至八边不规则切面体；size是半尺寸，所有顶点严格位于该局部包围盒内。</summary>
        private void Facet(Transform t, string name, Vector3 center, Vector3 size, uint color, int sides, int seed)
        {
            var v = new List<Vector3>(); var ids = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides; float a = i * Mathf.PI * 2 / sides, b = j * Mathf.PI * 2 / sides;
                float ra = .77f + .2f * Mathf.Abs(Mathf.Sin(seed * 7 + i * 13));
                float rb = .77f + .2f * Mathf.Abs(Mathf.Sin(seed * 7 + j * 13));
                var loA = center + Vector3.Scale(new Vector3(Mathf.Cos(a) * ra, -.6f, Mathf.Sin(a) * ra), size);
                var loB = center + Vector3.Scale(new Vector3(Mathf.Cos(b) * rb, -.6f, Mathf.Sin(b) * rb), size);
                var hiA = center + Vector3.Scale(new Vector3(Mathf.Cos(a) * ra * .72f, .48f + .16f * (i % 2), Mathf.Sin(a) * ra * .72f), size);
                var hiB = center + Vector3.Scale(new Vector3(Mathf.Cos(b) * rb * .72f, .48f + .16f * (j % 2), Mathf.Sin(b) * rb * .72f), size);
                Quad(v, ids, loA, hiA, hiB, loB); Tri(v, ids, hiA, center + Vector3.up * size.y, hiB);
                Tri(v, ids, center - Vector3.up * size.y * .6f, loA, loB);
            }
            MeshPart(t, name, v, ids, color);
        }

        private void Beam(Transform t, string name, Vector3 a, Vector3 b, float radius, uint color, int sides)
        {
            var part = Frustum(t, name, Vector3.zero, radius, radius * .86f, (b - a).magnitude, sides, color);
            part.localPosition = (a + b) * .5f; part.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
        }
        private void Triangle(Transform t, string name, Vector3 a, Vector3 b, Vector3 c, uint color, bool doubleSided)
        {
            var v = new List<Vector3>(); var ids = new List<int>(); Tri(v, ids, a, b, c); if (doubleSided) Tri(v, ids, c, b, a); MeshPart(t, name, v, ids, color);
        }
    }
}
