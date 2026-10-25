using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>十关补齐用五种待采用演员；只供隔离场景绝对采样，不拥有规则、平面移动或Unity更新。</summary>
public static class TenStageCreatureCandidates
{
    /// <summary>八秒预览循环只展示既有追击、蓄力、攻击和恢复；不代表正式技能冷却。</summary>
    public static float Duration(int special, int boss) { Validate(special, boss); return 8; }

    /// <summary>所有材质和自建分面网格交给调用方释放，根保持原尺寸且面向+Z。</summary>
    public static GameObject Create(int special, int boss, Transform parent, List<Object> owned)
    {
        Validate(special, boss); if (owned == null) throw new ArgumentNullException(nameof(owned));
        string name = boss == 2 ? "ZRex" : boss == 4 ? "Papaback" : special == 6 ? "Spider" : special == 10 ? "Ghoul" : "RedMangler";
        var actor = new GameObject("TEMP_Unapproved_" + name); actor.transform.SetParent(parent, false);
        var b = new Builder(actor.transform, owned); var body = b.Group(actor.transform, "Body", Vector3.zero);
        if (boss == 2) b.Rex(body); else if (boss == 4) b.Ape(body);
        else if (special == 6) b.Spider(body); else if (special == 10) b.Ghoul(body); else b.Mangler(body);
        Sample(actor, special, boss, 0); return actor;
    }

    private static void Validate(int special, int boss)
    {
        if (!((boss == 0 && (special == 6 || special == 10 || special == 17)) || (special == 0 && (boss == 2 || boss == 4))))
            throw new ArgumentException("只支持special6/10/17或boss2/4，Boss与特殊编号不能同时启用");
    }

    /// <summary>绝对时间重算所有可动节点；不会累积根位移、修改材质、实例化对象或积分状态。</summary>
    public static void Sample(GameObject actor, int special, int boss, float t)
    {
        Validate(special, boss); if (actor == null) throw new ArgumentNullException(nameof(actor));
        if (float.IsNaN(t) || float.IsInfinity(t)) throw new ArgumentException("采样时间必须有限");
        t = Mathf.Repeat(t, 8); var body = actor.transform.Find("Body");
        body.localPosition = Vector3.zero; body.localRotation = Quaternion.identity;
        if (boss == 2) PoseRex(body, t); else if (boss == 4) PoseApe(body, t);
        else if (special == 6) PoseSpider(body, t); else if (special == 10) PoseGhoul(body, t); else PoseMangler(body, t);
    }

    /// <summary>平滑窗口只表达姿态，不产生独立技能、危险区域或伤害。</summary>
    private static float Window(float t, float a, float b, float c, float d)
    { return Smooth(a, b, t) * (1 - Smooth(c, d, t)); }
    private static float Smooth(float a, float b, float t) { return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, t)); }
    private static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
    private static Transform Find(Transform t, string path)
    { var p = t.Find(path); if (p == null) throw new InvalidOperationException("演员节点缺失：" + path); return p; }

    /// <summary>独立骨段面向真实关节连线，缩放只沿局部Y，不用一根直杆替代弯曲肢体。</summary>
    private static void Bone(Transform node, Vector3 a, Vector3 b)
    {
        node.localPosition = (a + b) * .5f; node.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
        node.localScale = V(1, Mathf.Max(.001f, (b - a).magnitude), 1);
    }
    private static void Limb(Transform body, string name, Vector3 a, Vector3 b, Vector3 c, Vector3 handForward)
    {
        var t = Find(body, name); SolveJoint(a, ref b, ref c, Find(t, "Lengths").localScale);
        Bone(Find(t, "Upper"), a, b); Bone(Find(t, "Lower"), b, c);
        Find(t, "Elbow").localPosition = b; var hand = Find(t, "Hand"); hand.localPosition = c;
        hand.localRotation = Quaternion.LookRotation(handForward.sqrMagnitude > .001f ? handForward : Vector3.forward, Vector3.up);
    }
    private static void Leg(Transform body, string name, Vector3 hip, Vector3 knee, Vector3 ankle, float toeAngle = 0)
    {
        var t = Find(body, name); SolveJoint(hip, ref knee, ref ankle, Find(t, "Lengths").localScale);
        Bone(Find(t, "Upper"), hip, knee); Bone(Find(t, "Lower"), knee, ankle);
        Find(t, "Knee").localPosition = knee; var foot = Find(t, "Foot"); foot.localPosition = ankle;
        foot.localRotation = Quaternion.Euler(0, toeAngle, 0);
    }

    /// <summary>固定上下肢骨长的解析关节求解；目标过远时只收回末端，绝不拉伸骨段。</summary>
    private static void SolveJoint(Vector3 origin, ref Vector3 joint, ref Vector3 end, Vector3 lengths)
    {
        var direction = end - origin; float distance = direction.magnitude;
        direction = distance > .0001f ? direction / distance : Vector3.down;
        distance = Mathf.Clamp(distance, Mathf.Abs(lengths.x - lengths.y) + .001f, lengths.x + lengths.y - .001f);
        var bend = joint - origin; bend -= direction * Vector3.Dot(bend, direction);
        if (bend.sqrMagnitude < .0001f) { bend = Vector3.forward - direction * Vector3.Dot(Vector3.forward, direction); }
        if (bend.sqrMagnitude < .0001f) bend = Vector3.right;
        float along = (lengths.x * lengths.x + distance * distance - lengths.y * lengths.y) / (2 * distance);
        float away = Mathf.Sqrt(Mathf.Max(0, lengths.x * lengths.x - along * along));
        joint = origin + direction * along + bend.normalized * away; end = origin + direction * distance;
    }

    private static void PoseSpider(Transform body, float t)
    {
        float attack = Window(t, 4.4f, 4.75f, 5.05f, 5.55f);
        float walk = 1 - Window(t, 3.8f, 4.15f, 5.55f, 6.1f);
        float bob = .018f * Mathf.Sin(t * Mathf.PI * 7) * walk;
        body.localPosition = V(0, bob, 0);
        Find(body, "Thorax").localRotation = Quaternion.Euler(-attack * 13, 0, 0);
        Find(body, "Abdomen").localRotation = Quaternion.Euler(attack * 8, Mathf.Sin(t * Mathf.PI * .5f) * 3, 0);
        Find(body, "Head").localRotation = Quaternion.Euler(attack * 18, 0, 0);
        for (int side = -1; side <= 1; side += 2)
        for (int i = 0; i < 4; i++)
        {
            float phase = t * Mathf.PI * 4.5f + i * Mathf.PI * .8f + (side > 0 ? Mathf.PI : 0);
            float stride = Mathf.Sin(phase) * .16f * walk;
            float lift = Mathf.Max(0, Mathf.Cos(phase)) * .13f * walk;
            float baseZ = .58f - i * .38f;
            var a = V(side * .25f, .44f, baseZ * .72f);
            var b = V(side * (.72f + .08f * (i % 2)), .74f - .1f * i + lift * .25f, baseZ + stride);
            var c = V(side * (1.0f + .06f * (i % 2)), .24f + lift, baseZ * 1.35f + stride);
            var d = V(side * (1.20f + .05f * (i % 2)), .035f - bob + lift, baseZ * 1.55f + stride);
            if (i == 0)
            {
                b += V(side * -.12f, attack * .24f, attack * .15f);
                c += V(side * -.15f, attack * .35f, attack * .32f);
                d += V(side * -.12f, attack * .20f, attack * .38f);
            }
            var leg = Find(body, "Leg" + side + "_" + i); Bone(Find(leg, "A"), a, b); Bone(Find(leg, "B"), b, c); Bone(Find(leg, "C"), c, d);
            Find(leg, "Joint").localPosition = b;
        }
        Find(body, "Head/FangL").localRotation = Quaternion.Euler(attack * 27, -attack * 18, 0);
        Find(body, "Head/FangR").localRotation = Quaternion.Euler(attack * 27, attack * 18, 0);
    }

    private static void PoseGhoul(Transform body, float t)
    {
        float warning = Window(t, 2.0f, 2.45f, 2.9f, 3.15f);
        float rush = Window(t, 2.9f, 3.12f, 3.85f, 4.25f);
        float slash = Window(t, 5.0f, 5.3f, 5.53f, 5.9f);
        float recover = Window(t, 5.7f, 6.0f, 6.35f, 6.8f);
        float run = 1 - Mathf.Max(warning, slash * .9f, recover * .7f);
        var hip = V(0, .92f - warning * .10f - slash * .07f, -.08f);
        var core = Find(body, "Core"); core.localPosition = hip; core.localRotation = Quaternion.Euler(11 + warning * 14 + rush * 22 - slash * 12, 0, 0);
        Find(core, "Head").localRotation = Quaternion.Euler(-11 + warning * 6 + slash * 16, Mathf.Sin(t * Mathf.PI * .5f) * 4, 0);
        foreach (int side in new[] { -1, 1 })
        {
            float phase = side < 0 ? Mathf.PI : 0;
            float s = Mathf.Lerp(Mathf.Sin(t * Mathf.PI * 3 + phase), Mathf.Sin(t * Mathf.PI * 5 + phase), rush) * run;
            float lift = Mathf.Max(0, s) * (.11f + rush * .08f);
            var knee = V(side * .17f, .51f + lift, .02f + s * .19f);
            var ankle = V(side * .16f, .105f + lift, .03f + s * (.25f + .13f * rush));
            Leg(body, side < 0 ? "LegL" : "LegR", hip + V(side * .15f, 0, 0), knee, ankle, side * -5);
            var shoulder = core.localPosition + core.localRotation * V(side * .28f, .43f, .03f);
            var elbow = shoulder + V(side * .19f, -.41f + warning * .34f + slash * .41f, .09f - s * .18f - warning * .14f);
            var hand = elbow + V(side * .02f - side * slash * .22f, -.35f + slash * .18f, .11f + warning * .10f + slash * .58f + rush * .18f);
            Limb(body, side < 0 ? "ArmL" : "ArmR", shoulder, elbow, hand, V(side * .08f, -.25f + slash * .5f, .7f));
        }
    }

    private static void PoseMangler(Transform body, float t)
    {
        float warning = Window(t, 2.0f, 2.4f, 3.0f, 3.12f);
        float fire = Window(t, 3.08f, 3.13f, 3.25f, 3.55f);
        float rush = Window(t, 3.05f, 3.3f, 4.25f, 4.8f);
        float brace = Mathf.Max(warning, fire);
        float walk = 1 - brace * .9f;
        var hip = V(0, 1.0f + Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2.5f)) * .025f * walk, -.02f);
        var core = Find(body, "Core"); core.localPosition = hip; core.localRotation = Quaternion.Euler(5 + rush * 10 - fire * 7, 0, warning * -5);
        Find(core, "Head").localRotation = Quaternion.Euler(-4, 0, 0);
        foreach (int side in new[] { -1, 1 })
        {
            float s = Mathf.Sin(t * Mathf.PI * 2.5f + (side < 0 ? Mathf.PI : 0)) * walk;
            Leg(body, side < 0 ? "LegL" : "LegR", hip + V(side * .23f, 0, 0), V(side * .26f, .57f + Mathf.Max(0, s) * .09f, .10f + s * .17f),
                V(side * .27f, .135f + Mathf.Max(0, s) * .08f, s * .22f), side * -3);
        }
        var ls = hip + core.localRotation * V(-.47f, .50f, 0);
        Limb(body, "ArmL", ls, ls + V(-.14f, -.33f + warning * .31f, .03f + warning * .08f),
            ls + V(-.12f + warning * .36f, -.77f + warning * .93f, .15f + warning * .32f), V(.1f, -.2f, 1));
        var rs = hip + core.localRotation * V(.47f, .50f, 0);
        var elbow = rs + V(.16f, -.28f + brace * .24f, .05f + brace * .10f);
        var wrist = rs + V(.20f, -.60f + brace * .56f, .18f + brace * .42f - fire * .12f);
        Limb(body, "ArmR", rs, elbow, wrist, V(0, -.7f + brace * .7f, .6f + brace * .4f));
        Find(body, "ArmR/Hand/BarrelRecoil").localPosition = V(0, 0, -.07f * fire);
    }

    private static void PoseRex(Transform body, float t)
    {
        float wind = Window(t, 2.0f, 2.65f, 3.1f, 3.3f);
        float charge = Window(t, 3.1f, 3.35f, 4.25f, 4.7f);
        float expel = Window(t, 5.3f, 5.9f, 6.5f, 7.0f);
        float walk = 1 - Mathf.Max(wind, expel * .92f);
        var hip = V(0, 1.55f + .07f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2.5f)) * walk - wind * .14f, -.55f);
        var core = Find(body, "Core"); core.localPosition = hip; core.localRotation = Quaternion.Euler(wind * 6 + charge * 11 - expel * 12, 0, 0);
        var head = Find(core, "Head"); head.localRotation = Quaternion.Euler(-wind * 7 + expel * -12, 0, 0);
        Find(head, "Jaw").localRotation = Quaternion.Euler(wind * 9 + expel * 31 + charge * 16, 0, 0);
        foreach (int side in new[] { -1, 1 })
        {
            float phase = side < 0 ? Mathf.PI : 0;
            float s = Mathf.Lerp(Mathf.Sin(t * Mathf.PI * 2.5f + phase), Mathf.Sin(t * Mathf.PI * 3.5f + phase), charge) * walk;
            var knee = V(side * .62f, .89f + Mathf.Max(0, s) * .22f, -.18f + s * .26f);
            var ankle = V(side * .59f, .17f + Mathf.Max(0, s) * .22f, -.48f + s * .46f);
            Leg(body, side < 0 ? "LegL" : "LegR", hip + V(side * .50f, 0, .05f), knee, ankle, side * -7);
            var shoulder = hip + core.localRotation * V(side * .59f, .34f, .73f);
            Limb(body, side < 0 ? "ArmL" : "ArmR", shoulder, shoulder + V(side * .10f, -.30f, .10f), shoulder + V(side * .03f, -.25f, .40f), V(0, -.2f, 1));
        }
        for (int i = 0; i < 5; i++)
        {
            var tail = Find(core, i == 0 ? "Tail0" : "Tail0/Tail1" + (i > 1 ? "/Tail2" : "") + (i > 2 ? "/Tail3" : "") + (i > 3 ? "/Tail4" : ""));
            tail.localRotation = Quaternion.Euler(-3 + wind * 2, Mathf.Sin(t * Mathf.PI - i * .6f) * (5 + charge * 2), 0);
        }
    }

    private static void PoseApe(Transform body, float t)
    {
        float wind = Window(t, 2.0f, 2.7f, 2.9f, 3.1f);
        float charge = Window(t, 2.95f, 3.2f, 4.2f, 4.6f);
        float strikeUp = Window(t, 5.0f, 5.35f, 5.52f, 5.72f);
        float strikeDown = Window(t, 5.55f, 5.75f, 5.91f, 6.2f);
        float ray = Window(t, 6.45f, 6.9f, 7.3f, 7.7f);
        float walk = 1 - Mathf.Max(wind, strikeUp, strikeDown, ray);
        var hip = V(0, 1.35f - wind * .22f + strikeUp * .16f - strikeDown * .14f, -.28f);
        var core = Find(body, "Core"); core.localPosition = hip;
        core.localRotation = Quaternion.Euler(14 + charge * 14 + wind * 10 - strikeUp * 20 + strikeDown * 17 - ray * 8, 0, strikeUp * -8 + strikeDown * 6);
        Find(core, "Head").localRotation = Quaternion.Euler(-12 - ray * 8, strikeUp * -8, 0);
        Find(core, "Head/Jaw").localRotation = Quaternion.Euler(3 + wind * 9 + strikeUp * 16 + ray * 18, 0, 0);
        foreach (int side in new[] { -1, 1 })
        {
            float phase = side < 0 ? Mathf.PI : 0;
            float s = Mathf.Lerp(Mathf.Sin(t * Mathf.PI * 2.5f + phase), Mathf.Sin(t * Mathf.PI * 4 + phase), charge) * walk;
            var knee = V(side * .55f, .66f + Mathf.Max(0, s) * .16f, -.02f + s * .18f);
            var ankle = V(side * .58f, .17f + Mathf.Max(0, s) * .14f, -.32f + s * .33f);
            Leg(body, side < 0 ? "LegL" : "LegR", hip + V(side * .47f, 0, -.08f), knee, ankle, side * -7);
            var shoulder = hip + core.localRotation * V(side * 1.02f, .87f, .22f);
            var elbow = V(side * (1.34f - wind * .12f), 1.17f + wind * .12f, .28f - s * .23f);
            var hand = V(side * (1.28f - wind * .15f), .25f + Mathf.Max(0, -s) * .17f + wind * .10f, .60f - s * .34f);
            if (side > 0)
            {
                elbow += V(-strikeUp * .28f, strikeUp * 1.75f - strikeDown * .08f, -strikeUp * .25f + strikeDown * .26f);
                hand += V(-strikeUp * .75f - strikeDown * .55f, strikeUp * 3.08f, -strikeUp * .08f + strikeDown * .91f);
            }
            else
            {
                elbow += V(wind * .12f, ray * .18f, ray * .08f);
                hand += V(wind * .10f, ray * .08f, ray * .15f);
            }
            Limb(body, side < 0 ? "ArmL" : "ArmR", shoulder, elbow, hand, V(side * .04f, -.28f + strikeUp * (side > 0 ? 1 : 0), .82f));
        }
        var vent = Find(core, "BackVent"); vent.localRotation = Quaternion.Euler(-ray * 17, 0, 0);
    }

    /// <summary>自建切面体和骨段工具；同一演员按色复用材质，所有所有权显式交给预览调用者。</summary>
    private sealed class Builder
    {
        private readonly Transform root;
        private readonly List<Object> owned;
        private readonly Dictionary<uint, Material> palette = new Dictionary<uint, Material>();
        private const uint Skin = 0x8A9C87FF, Pale = 0xADB39AFF, Dark = 0x4F645FFF, Wound = 0x9E6057FF;
        private const uint BoneColor = 0xC5B99BFF, Eye = 0xD3AA68FF, Armor = 0x965F58FF, ArmorDark = 0x634A4EFF, Steel = 0x64747AFF;
        public Builder(Transform root, List<Object> owned) { this.root = root; this.owned = owned; }
        public Transform Group(Transform t, string name, Vector3 p)
        { var go = new GameObject(name).transform; go.SetParent(t, false); go.localPosition = p; return go; }

        public void Spider(Transform b)
        {
            var abdomen = Group(b, "Abdomen", V(0, .49f, -.36f));
            Shape(abdomen, "AngularAbdomen", V(0, 0, 0), V(.41f, .29f, .57f), 0x6C7062FFu, 8, 1);
            Shape(abdomen, "MutedRedDorsalMark", V(0, .23f, -.05f), V(.17f, .045f, .32f), 0x986A57FFu, 6, 2);
            var thorax = Group(b, "Thorax", V(0, .48f, .27f));
            Shape(thorax, "ArmoredThorax", Vector3.zero, V(.29f, .21f, .34f), 0x65756AFFu, 8, 3);
            var head = Group(b, "Head", V(0, .41f, .65f));
            Shape(head, "LowHead", Vector3.zero, V(.24f, .16f, .20f), Dark, 7, 1);
            foreach (int side in new[] { -1, 1 })
            {
                Shape(head, "Eye" + side, V(side * .12f, .07f, .16f), V(.036f, .032f, .026f), Eye, 5, side);
                Shape(head, "SecondaryEye" + side, V(side * .20f, .04f, .12f), V(.022f, .021f, .024f), Eye, 5, 1);
                var fang = Group(head, side < 0 ? "FangL" : "FangR", V(side * .12f, -.06f, .12f));
                TaperBetween(fang, "FangRoot", Vector3.zero, V(side * .035f, -.045f, .15f), .055f, .035f, Dark, 6);
                TaperBetween(fang, "CurvedFangTip", V(side * .035f, -.045f, .15f), V(0, -.14f, .20f), .035f, .005f, BoneColor, 5);
                for (int i = 0; i < 4; i++)
                {
                    var leg = Group(b, "Leg" + side + "_" + i, Vector3.zero);
                    UnitBone(leg, "A", .073f, .052f, 0x68736AFFu, 6); UnitBone(leg, "B", .052f, .028f, Dark, 5); UnitBone(leg, "C", .028f, .007f, 0x6E6B5BFFu, 5);
                    Shape(leg, "Joint", Vector3.zero, V(.084f, .075f, .08f), 0x817E69FFu, 6, i);
                }
            }
        }

        public void Ghoul(Transform b)
        {
            var core = Group(b, "Core", V(0, .92f, -.08f));
            Shape(core, "NarrowPelvis", V(0, .02f, 0), V(.22f, .20f, .16f), 0x656C61FFu, 7, 3);
            Shape(core, "SunkenRibcage", V(0, .36f, .05f), V(.275f, .36f, .20f), Skin, 8, 4);
            Shape(core, "HunchedupBack", V(0, .46f, -.13f), V(.24f, .25f, .17f), Dark, 7, 3);
            TaperBetween(core, "Neck", V(0, .57f, .09f), V(0, .70f, .22f), .12f, .11f, Pale, 7);
            var head = Group(core, "Head", V(0, .74f, .24f));
            Shape(head, "AngularSkull", V(0, .05f, 0), V(.17f, .22f, .18f), Pale, 7, 1);
            Shape(head, "GauntJaw", V(0, -.11f, .07f), V(.13f, .09f, .13f), Skin, 6, 1);
            Shape(head, "Mouth", V(0, -.075f, .169f), V(.10f, .033f, .015f), 0x564E47FFu, 6, 0);
            Eyes(head, .105f, .08f, .162f, .038f);
            for (int i = 0; i < 4; i++)
                Shape(core, "RibFold" + i, V(0, .18f + i * .10f, .209f), V(.19f - i * .012f, .025f, .025f), 0xB0AE91FFu, 5, i);
            Shape(core, "MutedChestInfection", V(-.13f, .44f, .18f), V(.095f, .13f, .043f), Wound, 6, 2);
            HumanLeg(b, "LegL", .12f, .085f, Dark, .30f, .45f, .43f); HumanLeg(b, "LegR", .12f, .085f, Dark, .30f, .45f, .43f);
            Arm(b, "ArmL", .095f, .065f, Skin, .12f, true, .46f, .43f); Arm(b, "ArmR", .095f, .065f, Skin, .12f, true, .46f, .43f);
        }

        public void Mangler(Transform b)
        {
            var core = Group(b, "Core", V(0, 1, 0));
            Shape(core, "HeavyPelvis", V(0, 0, 0), V(.34f, .25f, .25f), ArmorDark, 8, 1);
            Shape(core, "AngularBreastplate", V(0, .40f, .05f), V(.49f, .43f, .31f), Armor, 8, 2);
            Shape(core, "RaisedChestCenter", V(0, .46f, .315f), V(.16f, .25f, .045f), 0xB5816BFFu, 6, 2);
            Shape(core, "DarkAbdominalSeal", V(0, .06f, .24f), V(.25f, .13f, .055f), Steel, 6, 3);
            foreach (int side in new[] { -1, 1 })
            {
                Shape(core, "ShoulderArmor" + side, V(side * .49f, .54f, 0), V(.25f, .23f, .31f), side < 0 ? ArmorDark : Armor, 7, side);
                Shape(core, "BackCanister" + side, V(side * .22f, .42f, -.33f), V(.14f, .35f, .13f), Steel, 7, 1);
                Shape(core, "CanisterCap" + side, V(side * .22f, .75f, -.33f), V(.13f, .07f, .12f), 0xB2AA8BFFu, 6, 2);
            }
            var head = Group(core, "Head", V(0, .85f, .03f));
            Shape(head, "RedArmoredHelmet", V(0, .10f, 0), V(.24f, .26f, .23f), ArmorDark, 8, 1);
            Shape(head, "HelmetCrown", V(0, .27f, -.015f), V(.24f, .10f, .22f), Armor, 6, 2);
            Shape(head, "Visor", V(0, .12f, .215f), V(.17f, .045f, .022f), 0xC7AA70FFu, 6, 0);
            Shape(head, "FaceMask", V(0, -.035f, .17f), V(.17f, .10f, .09f), Steel, 6, 3);
            HumanLeg(b, "LegL", .20f, .155f, ArmorDark, .39f, .51f, .49f); HumanLeg(b, "LegR", .20f, .155f, ArmorDark, .39f, .51f, .49f);
            Arm(b, "ArmL", .19f, .15f, Armor, .18f, false, .43f, .48f); Arm(b, "ArmR", .20f, .20f, ArmorDark, .23f, false, .43f, .48f);
            var hand = Find(b, "ArmR/Hand"); var barrel = Group(hand, "BarrelRecoil", Vector3.zero);
            TaperBetween(barrel, "CannonHousing", V(0, 0, -.10f), V(0, 0, .54f), .25f, .225f, Steel, 8);
            TaperBetween(barrel, "CannonMuzzleRim", V(0, 0, .48f), V(0, 0, .65f), .245f, .245f, ArmorDark, 8);
            TaperBetween(barrel, "DarkCannonBore", V(0, 0, .65f), V(0, 0, .656f), .145f, .145f, 0x303F42FFu, 8);
            Shape(barrel, "CannonSignal", V(0, .245f, .16f), V(.065f, .025f, .10f), Eye, 5, 1);
        }

        public void Rex(Transform b)
        {
            var core = Group(b, "Core", V(0, 1.55f, -.55f));
            Shape(core, "MassiveRearHaunch", V(0, .10f, -.09f), V(.72f, .69f, .80f), 0x7B8975FFu, 9, 1);
            Shape(core, "ForwardSlopingTorso", V(0, .37f, .69f), V(.66f, .67f, 1.12f), 0x849783FFu, 9, 2);
            Shape(core, "DarkDorsalRidge", V(0, .95f, .32f), V(.35f, .15f, 1.05f), Dark, 7, 1);
            TaperBetween(core, "PowerfulNeck", V(0, .51f, 1.1f), V(0, .92f, 1.73f), .44f, .40f, Skin, 8);
            var head = Group(core, "Head", V(0, .99f, 1.75f));
            Shape(head, "BroadDinosaurSkull", V(0, .16f, .22f), V(.52f, .43f, .69f), 0x97A38AFFu, 8, 1);
            Shape(head, "BluntSnout", V(0, -.025f, .72f), V(.45f, .22f, .50f), 0xA8AF94FFu, 8, 2);
            Shape(head, "ForeheadInfection", V(-.24f, .43f, -.01f), V(.22f, .10f, .33f), Wound, 6, 2);
            foreach (int side in new[] { -1, 1 })
            {
                Shape(head, "EyeSocket" + side, V(side * .46f, .24f, .25f), V(.075f, .13f, .22f), Dark, 6, 1);
                Shape(head, "DinosaurEye" + side, V(side * .509f, .27f, .34f), V(.022f, .047f, .065f), Eye, 6, 1);
                Shape(head, "Nostril" + side, V(side * .28f, .13f, 1.14f), V(.075f, .04f, .025f), Dark, 6, 0);
            }
            var jaw = Group(head, "Jaw", V(0, -.13f, -.02f));
            Shape(jaw, "DeepLowerJaw", V(0, -.18f, .62f), V(.40f, .19f, .63f), 0x778B77FFu, 8, 2);
            Shape(jaw, "DarkMouthInterior", V(0, .01f, .63f), V(.34f, .025f, .48f), 0x664A47FFu, 6, 1);
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 7; i++)
            {
                float z = .22f + i * .145f; float x = side * (.34f - .018f * Mathf.Abs(i - 3));
                TaperBetween(head, "UpperTooth" + side + "_" + i, V(x, -.03f, z), V(x * .95f, -.20f - (i % 2) * .035f, z + .015f), .048f, .004f, BoneColor, 5);
                TaperBetween(jaw, "LowerTooth" + side + "_" + i, V(x * .96f, -.015f, z + .02f), V(x * .94f, .11f, z + .025f), .042f, .004f, BoneColor, 5);
            }
            HumanLeg(b, "LegL", .35f, .24f, Dark, .72f, .90f, .85f); HumanLeg(b, "LegR", .35f, .24f, Dark, .72f, .90f, .85f);
            Arm(b, "ArmL", .13f, .09f, Skin, .12f, true, .34f, .34f); Arm(b, "ArmR", .13f, .09f, Skin, .12f, true, .34f, .34f);
            var tail = core;
            for (int i = 0; i < 5; i++)
            {
                tail = Group(tail, "Tail" + i, i == 0 ? V(0, .20f, -.73f) : V(0, 0, -.62f));
                float radius = .39f - i * .067f;
                TaperBetween(tail, "TaperedTailSegment", Vector3.zero, V(0, -.045f, -.68f), radius, Mathf.Max(.025f, radius - .09f), i % 2 == 0 ? Dark : Skin, 7);
            }
            for (int i = 0; i < 5; i++) Shape(core, "SpinalScute" + i, V(0, 1.02f, -.54f + i * .40f), V(.13f, .14f, .14f), 0xA5A98CFFu, 5, i);
        }

        public void Ape(Transform b)
        {
            var core = Group(b, "Core", V(0, 1.35f, -.28f));
            Shape(core, "GreatApePelvis", V(0, 0, -.10f), V(.65f, .48f, .48f), 0x596B63FFu, 8, 1);
            Shape(core, "BarrelRibcage", V(0, .65f, .10f), V(1.04f, .82f, .65f), 0x607469FFu, 9, 2);
            Shape(core, "GreatHunchedBack", V(0, 1.01f, -.35f), V(.88f, .65f, .59f), Dark, 8, 1);
            Shape(core, "BareChestPlate", V(0, .64f, .62f), V(.66f, .56f, .10f), 0x8E9B85FFu, 8, 2);
            foreach (int side in new[] { -1, 1 })
            {
                Shape(core, "MassiveShoulder" + side, V(side * .99f, .89f, .15f), V(.52f, .49f, .48f), 0x52695EFFu, 8, side);
                Shape(core, "BackInfection" + side, V(side * .62f, 1.15f, -.72f), V(.35f, .29f, .25f), side < 0 ? Wound : ArmorDark, 7, side);
            }
            var head = Group(core, "Head", V(0, 1.24f, .55f));
            Shape(head, "ApeCranium", V(0, .20f, -.05f), V(.49f, .52f, .42f), Dark, 8, 1);
            Shape(head, "HeavyBrow", V(0, .30f, .33f), V(.43f, .13f, .16f), 0x697D6FFFu, 7, 2);
            Shape(head, "ApeMuzzle", V(0, -.015f, .41f), V(.30f, .22f, .26f), 0x9AA48DFFu, 7, 3);
            Shape(head, "FlatApeNose", V(0, .06f, .635f), V(.14f, .085f, .025f), Dark, 6, 2);
            Eyes(head, .25f, .22f, .425f, .05f);
            var jaw = Group(head, "Jaw", V(0, -.08f, .14f));
            Shape(jaw, "HeavyApeJaw", V(0, -.17f, .28f), V(.31f, .17f, .28f), 0x7F8F7BFFu, 7, 1);
            foreach (int side in new[] { -1, 1 })
                TaperBetween(head, "ApeCanine" + side, V(side * .20f, -.05f, .56f), V(side * .18f, -.28f, .57f), .068f, .009f, BoneColor, 6);
            HumanLeg(b, "LegL", .34f, .24f, Dark, .68f, .72f, .68f); HumanLeg(b, "LegR", .34f, .24f, Dark, .68f, .72f, .68f);
            Arm(b, "ArmL", .35f, .30f, 0x5A7063FFu, .35f, false, 1.06f, 1.05f); Arm(b, "ArmR", .38f, .32f, 0x5A7063FFu, .38f, false, 1.06f, 1.05f);
            var vent = Group(core, "BackVent", V(0, 1.20f, -.75f));
            Shape(vent, "BackOrganRoot", Vector3.zero, V(.39f, .29f, .28f), ArmorDark, 7, 2);
            TaperBetween(vent, "BackOrganChimney", V(0, .02f, 0), V(0, .78f, -.15f), .22f, .16f, Wound, 7);
            TaperBetween(vent, "BackOrganMouth", V(0, .77f, -.15f), V(0, .80f, -.156f), .13f, .13f, 0xB49A72FFu, 7);
            // 后背辨识沿既有Papaback轮廓保留，射线/增援由主会话只读演示既有权威内容。
        }

        private void Eyes(Transform t, float x, float y, float z, float size)
        {
            foreach (int side in new[] { -1, 1 })
            {
                Shape(t, "SunkenEyeSocket" + side, V(side * x, y, z), V(size * 1.6f, size * 1.1f, .025f), Dark, 6, 0);
                Shape(t, "SmallWarmEye" + side, V(side * x, y, z + .023f), V(size * .66f, size * .48f, .012f), Eye, 5, 1);
            }
        }

        private void HumanLeg(Transform b, string name, float upper, float lower, uint color, float footLength, float upperLength, float lowerLength)
        {
            var t = Group(b, name, Vector3.zero); UnitBone(t, "Upper", upper, upper * .78f, color, 7); UnitBone(t, "Lower", lower, lower * .70f, color, 7);
            Group(t, "Lengths", Vector3.zero).localScale = V(upperLength, lowerLength, 1);
            Shape(t, "Knee", Vector3.zero, V(lower * 1.07f, lower * .90f, lower * 1.05f), color, 6, 2);
            var foot = Group(t, "Foot", Vector3.zero);
            Shape(foot, "FootMass", V(0, -.04f, footLength * .18f), V(lower * 1.0f, lower * .48f, footLength * .48f), color, 7, 1);
            for (int i = 0; i < 3; i++)
                TaperBetween(foot, "Toe" + i, V((i - 1) * lower * .65f, -.05f, footLength * .48f), V((i - 1) * lower * .75f, -.08f, footLength * .74f), lower * .24f, .006f, BoneColor, 5);
        }

        private void Arm(Transform b, string name, float upper, float lower, uint color, float hand, bool claws, float upperLength, float lowerLength)
        {
            var t = Group(b, name, Vector3.zero); UnitBone(t, "Upper", upper, upper * .82f, color, 7); UnitBone(t, "Lower", lower, lower * .72f, color, 7);
            Group(t, "Lengths", Vector3.zero).localScale = V(upperLength, lowerLength, 1);
            Shape(t, "Elbow", Vector3.zero, V(lower * 1.15f, lower, lower * 1.05f), color, 6, 2);
            var palm = Group(t, "Hand", Vector3.zero);
            Shape(palm, "Palm", V(0, 0, .06f), V(hand, hand * .70f, hand * 1.18f), color, 7, 1);
            for (int i = 0; i < 4; i++)
            {
                float x = (i - 1.5f) * hand * .46f; float length = hand * (claws ? 2.2f : 1.25f);
                var a = V(x, -.015f, hand * .8f); var c = V(x * 1.12f, -.03f, hand * .8f + length * .65f);
                var d = V(x * 1.02f, -.10f - hand * .16f, hand * .8f + length);
                TaperBetween(palm, "FingerBase" + i, a, c, hand * .19f, hand * .15f, color, 5);
                TaperBetween(palm, "FingerTip" + i, c, d, hand * .15f, .005f, claws ? BoneColor : color, 5);
            }
            TaperBetween(palm, "Thumb", V(-hand * .9f, 0, 0), V(-hand * 1.35f, -.08f, hand * .7f), hand * .24f, hand * .12f, color, 5);
        }

        private void UnitBone(Transform t, string name, float r1, float r2, uint color, int sides)
        { TaperBetween(t, name, V(0, -.5f, 0), V(0, .5f, 0), r1, r2, color, sides); }

        /// <summary>骨段是四圈收束的切面棱体，关节中部有明确体积，非Unity胶囊原始体。</summary>
        private Transform TaperBetween(Transform t, string name, Vector3 a, Vector3 b, float r1, float r2, uint color, int sides)
        {
            var vertices = new List<Vector3>(); var ids = new List<int>();
            var rings = new Vector3[4][];
            float length = (b - a).magnitude;
            float[] y = { -.5f, -.27f, .25f, .5f }; float[] radius = { r1 * .73f, r1, r2, r2 * .72f };
            for (int j = 0; j < 4; j++)
            {
                rings[j] = new Vector3[sides];
                for (int i = 0; i < sides; i++) { float angle = i * Mathf.PI * 2 / sides; rings[j][i] = V(Mathf.Cos(angle) * radius[j], y[j] * length, Mathf.Sin(angle) * radius[j]); }
            }
            for (int j = 0; j < 3; j++) for (int i = 0; i < sides; i++)
            { int k = (i + 1) % sides; Quad(vertices, ids, rings[j][i], rings[j + 1][i], rings[j + 1][k], rings[j][k]); }
            for (int i = 0; i < sides; i++)
            {
                int k = (i + 1) % sides; Tri(vertices, ids, V(0, length * .5f, 0), rings[3][k], rings[3][i]);
                Tri(vertices, ids, V(0, -length * .5f, 0), rings[0][i], rings[0][k]);
            }
            var part = MeshPart(t, name, vertices, ids, color); part.localPosition = (a + b) * .5f; part.localRotation = Quaternion.FromToRotation(Vector3.up, b - a); return part;
        }

        /// <summary>三圈切面形体采用扁平法线和稳定细微不对称，半尺寸为明确米制包围范围。</summary>
        private Transform Shape(Transform t, string name, Vector3 center, Vector3 size, uint color, int sides, int seed)
        {
            var vertices = new List<Vector3>(); var ids = new List<int>(); var rings = new Vector3[3][];
            float[] levels = { -.65f, .03f, .66f }; float[] radii = { .65f, 1, .65f };
            for (int j = 0; j < 3; j++)
            {
                rings[j] = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float angle = (i + .5f) * Mathf.PI * 2 / sides;
                    float r = radii[j] * (.94f + .045f * Mathf.Sin(seed * 3.3f + i * 7.2f + j));
                    rings[j][i] = Vector3.Scale(V(Mathf.Cos(angle) * r, levels[j], Mathf.Sin(angle) * r), size);
                }
            }
            for (int j = 0; j < 2; j++) for (int i = 0; i < sides; i++)
            { int k = (i + 1) % sides; Quad(vertices, ids, rings[j][i], rings[j + 1][i], rings[j + 1][k], rings[j][k]); }
            for (int i = 0; i < sides; i++)
            {
                int k = (i + 1) % sides; Tri(vertices, ids, V(0, size.y, 0), rings[2][k], rings[2][i]);
                Tri(vertices, ids, V(0, -size.y, 0), rings[0][i], rings[0][k]);
            }
            var part = MeshPart(t, name, vertices, ids, color); part.localPosition = center; return part;
        }

        private Material Material(uint rgba)
        {
            if (palette.TryGetValue(rgba, out var existing)) return existing;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("缺少通用Lit Shader");
            var color = new Color((rgba >> 24 & 255) / 255f, (rgba >> 16 & 255) / 255f, (rgba >> 8 & 255) / 255f, 1);
            var m = new Material(shader) { name = "TEMP_Creature_" + rgba.ToString("X8"), color = color };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color); if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", .08f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", .08f); if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0);
            palette.Add(rgba, m); owned.Add(m); return m;
        }
        private Transform MeshPart(Transform t, string name, List<Vector3> v, List<int> ids, uint color)
        {
            var mesh = new Mesh { name = "TEMP_" + name }; mesh.SetVertices(v); mesh.SetTriangles(ids, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); owned.Add(mesh);
            var node = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); node.transform.SetParent(t, false);
            node.GetComponent<MeshFilter>().sharedMesh = mesh; node.GetComponent<MeshRenderer>().sharedMaterial = Material(color); return node.transform;
        }
        private static void Tri(List<Vector3> v, List<int> ids, Vector3 a, Vector3 b, Vector3 c)
        { int start = v.Count; v.Add(a); v.Add(b); v.Add(c); ids.Add(start); ids.Add(start + 1); ids.Add(start + 2); }
        private static void Quad(List<Vector3> v, List<int> ids, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        { Tri(v, ids, a, b, c); Tri(v, ids, a, c, d); }
    }
}
