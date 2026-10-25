using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>潜地怪与恶犬的隔离候选外观；纯时间采样，不进入正式战斗或资源。</summary>
public static class BurrowerHoundPreview
{
    /// <summary>潜地、土面扰动、撑地破土、爬行及复位的完整预览秒数。</summary>
    public const float BurrowerSeconds = 9.6f;
    /// <summary>奔跑、蓄势、扑咬、落地及恢复的完整预览秒数。</summary>
    public const float HoundSeconds = 8f;

    /// <summary>建立临时对象；全部材质与网格交由调用方统一释放。</summary>
    public static GameObject Create(int kind, Transform parent, List<Object> owned)
    {
        if (kind != 4 && kind != 5) throw new ArgumentOutOfRangeException("kind");
        if (owned == null) throw new ArgumentNullException("owned");
        var actor = new GameObject(kind == 4 ? "Burrower_Candidate" : "Hound_Candidate");
        actor.transform.SetParent(parent, false);
        var visual = Node("Visual", actor.transform);
        var body = Node("Body", visual);
        var hide = Material("感染皮肤", kind == 4 ? new Color(.26f, .27f, .20f) : new Color(.19f, .14f, .13f), owned);
        var lightHide = Material("肩部皮肤", kind == 4 ? new Color(.40f, .40f, .28f) : new Color(.30f, .22f, .18f), owned);
        var dark = Material("口腔与鼻端", new Color(.055f, .045f, .04f), owned);
        var horn = Material("黄骨爪牙", new Color(.77f, .72f, .51f), owned);
        var red = Material("感染裂隙", new Color(.69f, .12f, .075f), owned);
        var eye = Material("感染眼光", new Color(1f, .35f, .06f), owned);
        eye.EnableKeyword("_EMISSION"); eye.SetColor("_EmissionColor", new Color(.7f, .16f, .02f));
        var flesh = Material("牙龈", new Color(.39f, .105f, .075f), owned);
        // 关键部位均使用独立顶点的切面网格，避免光滑球体拼装的轮廓。
        Mesh shape = FacetedShape(8, 4, owned);
        Mesh wedge = Wedge(owned);
        Mesh limb = FacetedShape(6, 2, owned);
        bool burrower = kind == 4;
        Part("Torso", body, shape, hide, Vector3.zero,
            burrower ? new Vector3(.53f, .34f, .76f) : new Vector3(.33f, .30f, .74f));
        Part("Shoulders", body, shape, lightHide, new Vector3(0, .025f, .24f),
            burrower ? new Vector3(.56f, .31f, .36f) : new Vector3(.39f, .36f, .35f));
        Part("Haunches", body, shape, hide, new Vector3(0, -.015f, -.28f),
            burrower ? new Vector3(.42f, .26f, .31f) : new Vector3(.32f, .32f, .30f));
        var neck = Node("Neck", body);
        Part("NeckFlesh", neck, shape, lightHide, Vector3.zero,
            burrower ? new Vector3(.30f, .22f, .30f) : new Vector3(.27f, .29f, .30f));
        var head = Node("Head", neck);
        Part("Skull", head, shape, lightHide, Vector3.zero,
            burrower ? new Vector3(.33f, .24f, .31f) : new Vector3(.26f, .26f, .30f));
        Part("Brow", head, shape, hide, new Vector3(0, .07f, .06f), new Vector3(.31f, .11f, .20f));
        Part("Muzzle", head, shape, hide, new Vector3(0, -.025f, .15f),
            burrower ? new Vector3(.28f, .12f, .23f) : new Vector3(.17f, .115f, .23f));
        Part("Nose", head, shape, dark, new Vector3(0, -.005f, .245f), new Vector3(.13f, .08f, .07f));
        Part("MouthInterior", head, shape, dark, new Vector3(0, -.075f, .145f), new Vector3(.21f, .075f, .235f));
        var jaw = Node("Jaw", head);
        Part("LowerJaw", jaw, shape, hide, new Vector3(0, -.035f, .13f), new Vector3(.19f, .08f, .24f));
        Part("Gum", jaw, shape, flesh, new Vector3(0, .003f, .13f), new Vector3(.17f, .025f, .20f));
        for (int side = -1; side <= 1; side += 2)
        {
            Part("Eye" + side, head, shape, eye, new Vector3(side * .122f, .035f, .105f), new Vector3(.035f, .028f, .05f));
            for (int tooth = 0; tooth < 3; tooth++)
            {
                var fang = Part("UpperFang" + side + tooth, head, wedge, horn,
                    new Vector3(side * .083f, -.075f, .08f + tooth * .052f), new Vector3(.028f, .065f, .035f));
                fang.localRotation = Quaternion.Euler(180, 0, 0);
                Part("LowerFang" + side + tooth, jaw, wedge, horn,
                    new Vector3(side * .070f, .016f, .08f + tooth * .05f), new Vector3(.023f, .042f, .029f));
            }
            var ear = Part("Ear" + side, head, wedge, hide,
                new Vector3(side * .115f, .10f, -.08f),
                burrower ? new Vector3(.085f, .08f, .10f) : new Vector3(.105f, .19f, .10f));
            ear.localRotation = Quaternion.Euler(burrower ? -25 : -10, 0, side * -15);
            Part("NeckScar" + side, neck, wedge, red, new Vector3(side * .135f, .015f, .04f), new Vector3(.012f, .16f, .06f));
            Part("FlankScar" + side, body, wedge, red, new Vector3(side * (burrower ? .252f : .158f), .01f, -.08f),
                new Vector3(.012f, .14f, .21f)).localRotation = Quaternion.Euler(0, 0, side * 18);
        }
        for (int leg = 0; leg < 4; leg++)
        {
            var root = Node("Leg" + leg, visual);
            Part("Upper", root, limb, lightHide, Vector3.zero, Vector3.one);
            Part("Lower", root, limb, hide, Vector3.zero, Vector3.one);
            Part("Ankle", root, limb, hide, Vector3.zero, Vector3.one);
            Part("Joint", root, shape, lightHide, Vector3.zero, Vector3.one);
            Part("AnkleJoint", root, shape, hide, Vector3.zero, Vector3.one);
            var paw = Part("Paw", root, shape, hide, Vector3.zero,
                burrower && leg < 2 ? new Vector3(.21f, .10f, .22f) : new Vector3(.12f, .085f, .17f));
            for (int c = 0; c < 3; c++)
            {
                var claw = Part("Claw" + c, paw, wedge, horn,
                    new Vector3((c - 1) * .24f, -.10f, .49f), new Vector3(.16f, .29f, .39f));
                // 足爪相对足掌缩放，尖端朝前且稍向地面。
                claw.localRotation = Quaternion.Euler(77, 0, 0);
            }
        }
        var tail = Node("Tail", body);
        Part("TailBase", tail, limb, hide, new Vector3(0, 0, -.10f), new Vector3(.10f, .11f, .25f));
        var tailTip = Node("Tip", tail);
        Part("TailTip", tailTip, limb, hide, new Vector3(0, 0, -.10f), new Vector3(.055f, .06f, .24f));
        if (burrower)
        {
            var shell = Material("破裂泥土壳", new Color(.36f, .29f, .17f), owned);
            Part("EarthCarapace", body, shape, shell, new Vector3(0, .14f, -.04f), new Vector3(.47f, .26f, .64f));
            for (int s = 0; s < 5; s++)
            {
                var spine = Part("BackSpine" + s, body, wedge, horn,
                    new Vector3(0, .23f, .24f - s * .135f), new Vector3(.09f, .16f - s * .014f, .15f));
                spine.localRotation = Quaternion.Euler(-24, 0, s % 2 == 0 ? 8 : -8);
            }
            var earth = Node("Earth", actor.transform);
            var soil = Material("土块", new Color(.39f, .30f, .19f), owned);
            var dust = Material("浅尘", new Color(.58f, .48f, .31f), owned);
            for (int d = 0; d < 20; d++)
                Part("Clod" + d, earth, shape, d % 3 == 0 ? dust : soil, Vector3.zero, Vector3.one * .1f);
            for (int d = 0; d < 10; d++)
                Part("Ridge" + d, earth, shape, soil, Vector3.zero, Vector3.one * .1f);
        }
        Sample(actor, kind, 0);
        return actor;
    }

    /// <summary>直接从时间重建姿态；不改调用方安排的平面坐标和朝向，也不累计骨骼旋转。</summary>
    public static void Sample(GameObject actor, int kind, float time)
    {
        if (actor == null) return;
        float duration = kind == 4 ? BurrowerSeconds : HoundSeconds;
        float t = Mathf.Clamp(time, 0, duration);
        var visual = actor.transform.Find("Visual");
        var body = visual.Find("Body");
        var neck = body.Find("Neck");
        var head = neck.Find("Head");
        var jaw = head.Find("Jaw");
        var tail = body.Find("Tail");
        if (kind == 4)
        {
            // 0–1.35 下潜，1.35–3.0 地下原位扰动，3.0–4.45 撑起，随后爬行与一次普通近身咬击。
            float sink = Smooth(.40f, 1.35f, t);
            float emerge = Smooth(3.0f, 4.45f, t);
            float underground = sink * (1 - emerge);
            float brace = Smooth(2.9f, 3.42f, t) * (1 - Smooth(4.0f, 4.7f, t));
            float biteOpen = Smooth(6.65f, 7.05f, t) * (1 - Smooth(7.32f, 7.51f, t));
            float biteThrust = Smooth(7.16f, 7.40f, t) * (1 - Smooth(7.57f, 8.05f, t));
            float bitePause = Smooth(6.45f, 6.75f, t) * (1 - Smooth(7.9f, 8.2f, t));
            float crawl = Smooth(4.65f, 5.15f, t) * (1 - Smooth(8.55f, 9.5f, t)) * (1 - bitePause);
            float cycle = PlanarDistance(4, t) / .23f * Mathf.PI * 2;
            visual.localPosition = new Vector3(0, -.79f * underground, 0);
            visual.localRotation = Quaternion.identity;
            body.localPosition = new Vector3(0, .34f + .022f * Mathf.Sin(cycle * 2) * crawl + .09f * brace - .025f * biteThrust, .055f * biteThrust);
            body.localRotation = Quaternion.Euler(9 * sink * (1 - emerge) - 12 * brace + 7 * biteThrust, 0, 2.5f * Mathf.Sin(cycle) * crawl);
            neck.localPosition = new Vector3(0, -.015f, .30f);
            neck.localRotation = Quaternion.Euler(-8 * brace + 6 * crawl * Mathf.Sin(cycle + .4f), 0, 0);
            head.localPosition = new Vector3(0, .015f, .115f + .07f * biteThrust);
            head.localRotation = Quaternion.Euler(4 - 12 * brace - 9 * biteOpen + 7 * biteThrust, 3 * crawl * Mathf.Sin(cycle * .5f), 0);
            jaw.localPosition = new Vector3(0, -.088f, .015f);
            jaw.localRotation = Quaternion.Euler(6 + 19 * brace + 6 * crawl * (.5f + .5f * Mathf.Sin(cycle)) + 33 * biteOpen, 0, 0);
            tail.localPosition = new Vector3(0, -.035f, -.31f);
            tail.localRotation = Quaternion.Euler(12, 7 * Mathf.Sin(cycle) * crawl, 0);
            tail.Find("Tip").localPosition = new Vector3(0, 0, -.20f);
            tail.Find("Tip").localRotation = Quaternion.Euler(0, 12 * Mathf.Sin(cycle - .5f) * crawl, 0);
            for (int i = 0; i < 4; i++) PoseBurrowerLeg(visual, body, i, t, crawl, brace, sink, emerge);
            SampleEarth(actor.transform.Find("Earth"), t, underground);
        }
        else
        {
            // 0–2.6 奔跑，2.6–3.7 四足收稳蓄势，3.7–4.7 扑咬，4.7–5.5 落地，5.5 后恢复。
            float running = (1 - Smooth(2.25f, 2.85f, t)) + Smooth(6.0f, 6.65f, t);
            float crouch = Smooth(2.75f, 3.35f, t) * (1 - Smooth(3.7f, 3.95f, t));
            float air = Mathf.Clamp01((t - 3.76f) / 1.0f);
            float leap = t >= 3.76f && t <= 4.76f ? Mathf.Sin(air * Mathf.PI) : 0;
            float land = Smooth(4.65f, 4.88f, t) * (1 - Smooth(5.0f, 5.5f, t));
            float bite = Smooth(3.62f, 4.03f, t) * (1 - Smooth(4.2f, 4.47f, t));
            float cycle = RunDistance(t) / .45f * Mathf.PI * 2;
            visual.localPosition = new Vector3(0, .24f * leap, 0);
            visual.localRotation = Quaternion.identity;
            body.localPosition = new Vector3(0, .50f + .025f * Mathf.Sin(cycle * 2) * running - .13f * crouch - .10f * land, 0);
            body.localRotation = Quaternion.Euler(3 * Mathf.Sin(cycle) * running - 8 * crouch - 10 * leap + 8 * land, 0, 2 * Mathf.Sin(cycle) * running);
            neck.localPosition = new Vector3(0, .07f - .035f * crouch, .29f);
            neck.localRotation = Quaternion.Euler(-16 + 13 * crouch + 4 * Mathf.Sin(cycle) * running - 12 * bite, 0, 0);
            head.localPosition = new Vector3(0, .055f, .105f);
            head.localRotation = Quaternion.Euler(9 - 12 * bite, 0, 0);
            jaw.localPosition = new Vector3(0, -.087f, .018f);
            jaw.localRotation = Quaternion.Euler(7 + 35 * bite - 4 * Smooth(4.14f, 4.25f, t) * (1 - Smooth(4.35f, 4.55f, t)), 0, 0);
            tail.localPosition = new Vector3(0, .02f, -.34f);
            tail.localRotation = Quaternion.Euler(-15 + 18 * crouch + 15 * leap, 11 * Mathf.Sin(cycle * .5f) * running, 0);
            tail.Find("Tip").localPosition = new Vector3(0, 0, -.18f);
            tail.Find("Tip").localRotation = Quaternion.Euler(-15, 15 * Mathf.Sin(cycle * .5f - .8f) * running, 0);
            for (int i = 0; i < 4; i++) PoseHoundLeg(visual, body, i, t, running, crouch, leap, land, air);
        }
    }

    /// <summary>前肢扩大肘部外展和抓地跨度；破土时先伸爪，再以弯肘撑起躯干。</summary>
    private static void PoseBurrowerLeg(Transform visual, Transform body, int i, float t, float crawl, float brace, float sink, float emerge)
    {
        float side = i % 2 == 0 ? -1 : 1;
        bool front = i < 2;
        float phase = PlanarDistance(4, t) / .23f + (i == 0 || i == 3 ? 0 : .5f);
        Vector2 step = Gait(phase, .23f, .065f);
        float lift = step.y * crawl;
        var hip = body.localPosition + body.localRotation * new Vector3(side * (front ? .22f : .17f), -.025f, front ? .23f : -.25f);
        var foot = new Vector3(side * (front ? .34f : .24f), (front ? .05f : .043f) + lift, (front ? .46f : -.29f) + step.x * crawl);
        // 抓地前爪在身体尚未完全出土时已高举到土面，之后仍以地面为支点。
        float buried = sink * (1 - emerge);
        if (front)
        {
            foot.y += .79f * buried * Smooth(2.95f, 3.45f, t);
            foot.z += .11f * brace;
        }
        var knee = SolveKnee(hip, foot + new Vector3(0, .075f, -.035f), front ? .235f : .17f, front ? .235f : .17f,
            new Vector3(side * (front ? .20f : .08f), .025f, front ? -.12f : .12f));
        PoseLeg(visual.Find("Leg" + i), hip, knee, foot, front ? .15f : .105f, front ? .11f : .075f,
            front ? new Vector3(.21f, .10f, .22f) : new Vector3(.12f, .085f, .17f));
    }

    /// <summary>四足错相支撑；前腿肘向后、后腿飞节反折，足掌始终朝地。</summary>
    private static void PoseHoundLeg(Transform visual, Transform body, int i, float t, float run, float crouch, float leap, float land, float air)
    {
        bool front = i < 2;
        float side = i % 2 == 0 ? -1 : 1;
        // 对角腿基本同拍，左右微错相，便于读出四条腿各自的步伐。
        float phase = RunDistance(t) / .45f + (i == 0 ? 0 : i == 1 ? .5f : i == 2 ? .55f : .05f);
        Vector2 step = Gait(phase, .45f, .105f);
        var hip = body.localPosition + body.localRotation * new Vector3(side * .145f, -.045f, front ? .25f : -.26f);
        var foot = new Vector3(side * .17f, .043f + step.y * run,
            (front ? .29f : -.29f) + step.x * run);
        foot.z += front ? .07f * crouch : -.06f * crouch;
        // 腾空时前肢先伸出再回收，后腿随推蹬伸展；该姿态不决定位移或命中。
        float extend = leap * (front ? Mathf.Lerp(.18f, -.05f, air) : -.16f);
        foot.z += extend;
        // 前足先接地，后足晚约0.23秒补落；抵消躯干升高以保持足端世界高度。
        if (t >= 3.76f && t <= 4.90f)
        {
            float progress = Mathf.Clamp01((t - 3.76f) / (front ? .87f : 1.10f));
            foot.y = .043f + Mathf.Sin(progress * Mathf.PI) * (front ? .29f : .25f) - .24f * leap;
        }
        var knee = SolveKnee(hip, foot + new Vector3(0, .075f, -.035f), front ? .25f : .245f, front ? .25f : .26f,
            new Vector3(side * .04f, .035f, front ? -.20f : .20f));
        PoseLeg(visual.Find("Leg" + i), hip, knee, foot, front ? .09f : .115f, .065f, new Vector3(.12f, .085f, .17f));
    }

    /// <summary>候选平面路径，仅用于隔离拍摄，不参与正式追踪、技能计时或判伤。</summary>
    public static float PlanarDistance(int kind, float t)
    {
        if (kind == 4) return .23f * (IntegralSmooth(4.65f, 5.15f, t) - IntegralSmooth(6.45f, 6.75f, t)
            + IntegralSmooth(7.9f, 8.2f, t) - IntegralSmooth(8.55f, 9.5f, t));
        float run = RunDistance(t);
        float jump = .8f * Mathf.Clamp01((t - 3.76f) / 1f);
        return t < 6 ? run + jump : RunDistance(6) + .8f - (run - RunDistance(6));
    }

    /// <summary>积分平滑速度，使减速和恢复步频不会随调用次数漂移。</summary>
    private static float RunDistance(float t)
    {
        return .9f * (t - IntegralSmooth(2.25f, 2.85f, t) + IntegralSmooth(6f, 6.65f, t));
    }

    private static float IntegralSmooth(float a, float b, float t)
    {
        if (t <= a) return 0;
        if (t >= b) return t - (a + b) * .5f;
        float p = (t - a) / (b - a); return (b - a) * (p * p * p - .5f * p * p * p * p);
    }

    /// <summary>六成周期落地支撑时匀速向后，抬脚后从后向前补步；与平面移动共用距离相位。</summary>
    private static Vector2 Gait(float phase, float stride, float lift)
    {
        float p = Mathf.Repeat(phase, 1), stance = .62f, half = stride * stance * .5f;
        if (p < stance) return new Vector2(half - stride * p, 0);
        float swing = (p - stance) / (1 - stance);
        return new Vector2(Mathf.Lerp(-half, half, swing), Mathf.Sin(swing * Mathf.PI) * lift);
    }

    /// <summary>解析两段腿的关节弯曲，让四足在站立和压低时仍保持真实支撑关系。</summary>
    private static Vector3 SolveKnee(Vector3 hip, Vector3 foot, float upper, float lower, Vector3 bend)
    {
        Vector3 delta = foot - hip;
        float distance = Mathf.Clamp(delta.magnitude, .025f, upper + lower - .002f);
        Vector3 direction = delta.normalized;
        float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
        float height = Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
        Vector3 normal = Vector3.ProjectOnPlane(bend, direction).normalized;
        return hip + direction * along + normal * height;
    }

    /// <summary>按绝对端点布置肢段，足掌不继承上肢旋转，避免脚趾离地翻转。</summary>
    private static void PoseLeg(Transform leg, Vector3 hip, Vector3 knee, Vector3 foot, float upperWidth, float lowerWidth, Vector3 pawSize)
    {
        var ankle = foot + new Vector3(0, .075f, -.035f);
        Segment(leg.Find("Upper"), hip, knee, upperWidth, upperWidth * .9f);
        Segment(leg.Find("Lower"), knee, ankle, lowerWidth, lowerWidth * .85f);
        Segment(leg.Find("Ankle"), ankle, foot + Vector3.up * .015f, lowerWidth * .72f, lowerWidth * .70f);
        var joint = leg.Find("Joint"); joint.localPosition = knee; joint.localScale = Vector3.one * upperWidth * 1.03f;
        var ankleJoint = leg.Find("AnkleJoint"); ankleJoint.localPosition = ankle; ankleJoint.localScale = Vector3.one * lowerWidth;
        var paw = leg.Find("Paw"); paw.localPosition = foot; paw.localRotation = Quaternion.identity; paw.localScale = pawSize;
    }

    /// <summary>地下只显示细碎原位隆起；出土瞬间少量土块向外弹落，不形成持续光球。</summary>
    private static void SampleEarth(Transform earth, float t, float underground)
    {
        float disturbance = Smooth(.7f, 1.6f, t) * (1 - Smooth(3.3f, 4.0f, t));
        for (int i = 0; i < 10; i++)
        {
            float angle = i * 2.399963f;
            float radius = .20f + .11f * (i % 3);
            var ridge = earth.Find("Ridge" + i);
            ridge.gameObject.SetActive(disturbance > .001f);
            ridge.localPosition = new Vector3(Mathf.Cos(angle) * radius, .012f + .024f * (.5f + .5f * Mathf.Sin(t * 13 + i)), Mathf.Sin(angle) * radius);
            ridge.localScale = new Vector3(.14f, .035f + .055f * disturbance, .21f) * disturbance;
            ridge.localRotation = Quaternion.Euler(i * 13, i * 41, 0);
        }
        for (int i = 0; i < 20; i++)
        {
            float start = 3.17f + (i % 5) * .045f;
            float age = t - start;
            float length = .67f + (i % 4) * .055f;
            var clod = earth.Find("Clod" + i);
            bool visible = age >= 0 && age < length;
            clod.gameObject.SetActive(visible);
            age = Mathf.Clamp(age, 0, length);
            float p = age / length;
            float angle = i * 2.399963f;
            float distance = .19f + age * (.48f + (i % 4) * .085f);
            clod.localPosition = new Vector3(Mathf.Cos(angle) * distance,
                .055f + Mathf.Sin(p * Mathf.PI) * (.22f + .045f * (i % 4)), Mathf.Sin(angle) * distance);
            clod.localScale = new Vector3(.07f, .05f, .08f) * (1 - Smooth(.72f, 1f, p));
            clod.localRotation = Quaternion.Euler(i * 17 + age * 160, i * 31 + age * 90, i * 43);
        }
    }

    /// <summary>新建无碰撞的空节点；生命周期完全由预览根持有。</summary>
    private static Transform Node(string name, Transform parent)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
    }

    /// <summary>为切面网格分配渲染器，不创建物理组件。</summary>
    private static Transform Part(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 scale)
    {
        var node = Node(name, parent);
        node.localPosition = position; node.localScale = scale;
        node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        node.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        return node;
    }

    /// <summary>肢段网格长轴为局部Z，从两端点直接重建位置及旋转。</summary>
    private static void Segment(Transform part, Vector3 a, Vector3 b, float width, float depth)
    {
        part.localPosition = (a + b) * .5f;
        part.localRotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
        part.localScale = new Vector3(width, depth, Vector3.Distance(a, b));
    }

    /// <summary>复用当前工程渲染管线可用的普通不透明材质。</summary>
    private static Material Material(string name, Color color, List<Object> owned)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = name, color = color };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .10f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .10f);
        owned.Add(material); return material;
    }

    /// <summary>生成直径一米的低面数椭圆基形；每个三角形独立法线，真正保留平面明暗。</summary>
    private static Mesh FacetedShape(int sectors, int rings, List<Object> owned)
    {
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        for (int r = 0; r < rings; r++)
        {
            float a0 = -Mathf.PI * .5f + Mathf.PI * r / rings;
            float a1 = -Mathf.PI * .5f + Mathf.PI * (r + 1) / rings;
            for (int s = 0; s < sectors; s++)
            {
                float p0 = s * Mathf.PI * 2 / sectors, p1 = (s + 1) * Mathf.PI * 2 / sectors;
                Vector3 a = ShapePoint(a0, p0), b = ShapePoint(a0, p1), c = ShapePoint(a1, p1), d = ShapePoint(a1, p0);
                // 纬度方向由南向北，顺序保持外向；极点只保留非退化三角。
                if (r > 0) Triangle(vertices, triangles, a, d, b);
                if (r < rings - 1) Triangle(vertices, triangles, b, d, c);
            }
        }
        return FinishMesh("临时切面体", vertices, triangles, owned);
    }

    /// <summary>经纬点用于统一基形；纵向尺度由各部件独立设置。</summary>
    private static Vector3 ShapePoint(float latitude, float longitude)
    {
        return new Vector3(Mathf.Cos(latitude) * Mathf.Cos(longitude), Mathf.Sin(latitude), Mathf.Cos(latitude) * Mathf.Sin(longitude)) * .5f;
    }

    /// <summary>用于尖耳、爪、脊及裂隙的小型四棱锥，尖端朝局部上方。</summary>
    private static Mesh Wedge(List<Object> owned)
    {
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        var a = new Vector3(-.5f, 0, -.5f); var b = new Vector3(.5f, 0, -.5f);
        var c = new Vector3(.5f, 0, .5f); var d = new Vector3(-.5f, 0, .5f); var top = new Vector3(0, 1, .05f);
        Triangle(vertices, triangles, a, top, b); Triangle(vertices, triangles, b, top, c);
        Triangle(vertices, triangles, c, top, d); Triangle(vertices, triangles, d, top, a);
        Triangle(vertices, triangles, a, b, d); Triangle(vertices, triangles, b, c, d);
        return FinishMesh("临时爪耳尖面", vertices, triangles, owned);
    }

    /// <summary>独立三角形顶点让重算法线不跨面平均。</summary>
    private static void Triangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        int index = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
        triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
    }

    /// <summary>统一登记临时网格，调用方清理根对象之后释放。</summary>
    private static Mesh FinishMesh(string name, List<Vector3> vertices, List<int> triangles, List<Object> owned)
    {
        var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); owned.Add(mesh); return mesh;
    }

    /// <summary>各阶段的平滑绝对时间权重，不保存可变动画状态。</summary>
    private static float Smooth(float from, float to, float t)
    {
        float p = Mathf.Clamp01((t - from) / (to - from)); return p * p * (3 - 2 * p);
    }
}
