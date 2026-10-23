using System.Linq;
using UnityEngine;
namespace Game
{
    /// <summary>已采用空手抓挠的纯骨骼投影；不写位置、骨长、生命或战斗时间。</summary>
    public sealed class ArcadeZombieGrabVisual
    {
        private sealed class Rig
        {
            public GameObject Root;
            public Transform Abdomen, Torso, Head, LeftUpper, LeftLower, LeftHand, RightUpper, RightLower, RightHand;
        }
        private Animator bound;
        private bool inspected, applied;
        private Rig rig;
        private Transform[] changed;
        private Quaternion[] original;
        public bool Ready => rig != null;

        /// <summary>每个表现实例只解析一次正式Generic骨架，未知骨架保持原动画。</summary>
        public bool Bind(Transform owner, Animator animator)
        {
            if (bound == animator && inspected) return Ready;
            Clear(); bound=animator;inspected=true;
            if(owner==null||animator==null)return false;
            var bones=animator.GetComponentsInChildren<Transform>(true);
            Transform Find(string name)=>bones.FirstOrDefault(t=>t.name==name);
            var candidate=new Rig {Root=owner.gameObject,Abdomen=Find("Abdomen"),Torso=Find("Torso"),Head=Find("Head"),
                LeftUpper=Find("UpperArm.L"),LeftLower=Find("LowerArm.L"),LeftHand=Find("Middle1.L"),
                RightUpper=Find("UpperArm.R"),RightLower=Find("LowerArm.R"),RightHand=Find("Middle1.R")};
            changed=new[]{candidate.Abdomen,candidate.Torso,candidate.Head,candidate.LeftUpper,candidate.LeftLower,candidate.RightUpper,candidate.RightLower};
            if(changed.Any(t=>t==null)||candidate.LeftHand==null||candidate.RightHand==null){changed=null;return false;}
            rig=candidate;original=new Quaternion[changed.Length];return true;
        }

        /// <summary>动画求值前撤销上一帧叠加；重复LateUpdate同样不会累积旋转。</summary>
        public void Restore()
        {
            if(applied&&changed!=null)for(int i=0;i<changed.Length;i++)if(changed[i]!=null)changed[i].localRotation=original[i];
            applied=false;
        }
        public void Clear(){Restore();bound=null;inspected=false;rig=null;changed=null;original=null;}

        /// <summary>进度由权威前摇/收招给出，cancel仅在视觉上平滑撤手。</summary>
        public void Apply(float seconds,float cancel=0)
        {
            Restore();if(!Ready)return;
            for(int i=0;i<changed.Length;i++)original[i]=changed[i].localRotation;
            ApplyWideGrab(rig,Mathf.Max(0,seconds),Mathf.Clamp01(cancel));applied=true;
        }
    /// <summary>已采用大幅抓挠：0.3秒后拉，随后0.1秒前抓，0.4秒处达到接触姿态，再向内下方撕扯。</summary>
    private static void ApplyWideGrab(Rig actor, float time, float cancel)
    {
        var idleL = new Vector3(-.32f, 1.0f, .13f); var idleR = new Vector3(.32f, 1.0f, .13f);
        var rearL = new Vector3(-.48f, 1.48f, -.24f); var rearR = new Vector3(.48f, 1.48f, -.24f);
        var reachL = new Vector3(-.20f, 1.25f, .58f); var reachR = new Vector3(.20f, 1.30f, .58f);
        var tearL = new Vector3(-.08f, 1.02f, .45f); var tearR = new Vector3(.08f, 1.02f, .45f);
        Vector3 left, right; float lean, twist;
        if (time < .3f)
        {
            float p = Mathf.SmoothStep(0, 1, time / .3f);
            left = Vector3.Lerp(idleL, rearL, p); right = Vector3.Lerp(idleR, rearR, p); lean = -10 * p; twist = -12 * p;
        }
        else if (time < .4f)
        {
            float p = Mathf.SmoothStep(0, 1, (time - .3f) / .1f);
            left = Vector3.Lerp(rearL, reachL, p); right = Vector3.Lerp(rearR, reachR, p); lean = Mathf.Lerp(-10, 28, p); twist = Mathf.Lerp(-12, 10, p);
        }
        else if (time < .58f)
        {
            float p = Mathf.SmoothStep(0, 1, (time - .4f) / .18f);
            left = Vector3.Lerp(reachL, tearL, p); right = Vector3.Lerp(reachR, tearR, p); lean = Mathf.Lerp(28, 16, p); twist = Mathf.Lerp(10, -8, p);
        }
        else
        {
            float p = Mathf.SmoothStep(0, 1, (time - .58f) / .32f);
            left = Vector3.Lerp(tearL, idleL, p); right = Vector3.Lerp(tearR, idleR, p); lean = 16 * (1-p); twist = -8 * (1-p);
        }
        left = Vector3.Lerp(left, idleL, cancel); right = Vector3.Lerp(right, idleR, cancel);
        lean *= 1-cancel; twist *= 1-cancel;
        var axis = actor.Root.transform.right;
        actor.Abdomen.rotation = Quaternion.AngleAxis(lean * .45f, axis) * actor.Abdomen.rotation;
        actor.Torso.rotation = Quaternion.AngleAxis(twist, Vector3.up) * Quaternion.AngleAxis(lean * .55f, axis) * actor.Torso.rotation;
        actor.Head.rotation = Quaternion.AngleAxis(-lean * .25f, axis) * actor.Head.rotation;
        AimArm(actor, actor.LeftUpper, actor.LeftLower, actor.LeftHand, left, -1);
        AimArm(actor, actor.RightUpper, actor.RightLower, actor.RightHand, right, 1);
    }

    /// <summary>只转动两段手臂骨骼，让手掌走清楚的抓挠轨迹；骨长与角色尺寸不变。</summary>
    private static void AimArm(Rig actor, Transform upper, Transform lower, Transform hand, Vector3 localTarget, float side)
    {
        Vector3 a = upper.position, b = lower.position, c = hand.position;
        float ab = Vector3.Distance(a, b), bc = Vector3.Distance(b, c);
        Vector3 wanted = actor.Root.transform.TransformPoint(localTarget), delta = wanted-a;
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(ab-bc)+.001f, ab+bc-.001f);
        var direction = delta.normalized; wanted = a + direction * distance;
        Vector3 pole = actor.Root.transform.right * side - actor.Root.transform.forward * .25f;
        pole = (pole - direction * Vector3.Dot(pole, direction)).normalized;
        float cosine = Mathf.Clamp((ab*ab+distance*distance-bc*bc)/(2*ab*distance), -1, 1);
        Vector3 elbow = a + direction * (ab*cosine) + pole * (ab*Mathf.Sqrt(1-cosine*cosine));
        upper.rotation = Quaternion.FromToRotation(b-a, elbow-a) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position-lower.position, wanted-lower.position) * lower.rotation;
    }

    }
}
