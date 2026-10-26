using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>已采用小龙概念的临时三维候选。只生成内存网格与绝对时间姿态，不写正式资源或驱动战斗。</summary>
public static class FinalDragonCandidate
{
    /// <summary>供隔离拍摄持有的骨节及材料；全部临时对象由调用方销毁。</summary>
    public sealed class Rig
    {
        public GameObject Root;
        public Transform Body, Neck, Head, Jaw;
        public readonly List<Transform> Tail = new List<Transform>();
        public readonly List<Transform> Wings = new List<Transform>();
        public readonly List<Leg> Legs = new List<Leg>();
        public readonly List<Object> Owned = new List<Object>();
        public Material Skin, Ridge, Belly, Horn, Membrane, Dark, Eye;
    }
    /// <summary>两节腿和脚掌采用平面逆运动学，站立阶段脚底保持在地面。</summary>
    public sealed class Leg
    {
        public Transform Upper, Lower, Foot;
        public Vector3 Hip;
        public float LengthA, LengthB, Phase;
        public bool Front;
    }

    public static Rig Create(Transform parent)
    {
        var r = new Rig(); r.Root = new GameObject("TEMP_FinalDragon_v1");
        r.Root.transform.SetParent(parent, false);
        r.Skin = Mat(r, "RockBronze", new Color(.43f,.33f,.24f));
        r.Ridge = Mat(r, "Ridge", new Color(.29f,.21f,.16f));
        r.Belly = Mat(r, "Throat", new Color(.48f,.37f,.25f));
        r.Horn = Mat(r, "Ivory", new Color(.69f,.60f,.43f));
        r.Membrane = Mat(r, "WarmWing", new Color(.59f,.28f,.15f));
        r.Dark = Mat(r, "Mouth", new Color(.055f,.025f,.018f));
        r.Eye = Mat(r, "AmberEye", new Color(1,.45f,.025f));
        if(r.Eye.HasProperty("_EmissionColor")){r.Eye.EnableKeyword("_EMISSION");r.Eye.SetColor("_EmissionColor",new Color(1,.35f,.01f)*.8f);}
        r.Body = Joint(r.Root.transform,"Body",new Vector3(0,1.52f,0));
        Tube(r,r.Body,"Torso",new[]{new Vector3(0,-.05f,-1.25f),new Vector3(0,.02f,-.85f),new Vector3(0,.04f,-.15f),new Vector3(0,.18f,.65f),new Vector3(0,.12f,1.03f)},new[]{.22f,.65f,.79f,.73f,.36f},r.Skin,12,.88f);
        Tube(r,r.Body,"Chest",new[]{new Vector3(0,-.31f,-.6f),new Vector3(0,-.36f,.25f),new Vector3(0,-.22f,.95f)},new[]{.24f,.43f,.18f},r.Belly,8,.55f);
        r.Neck=Joint(r.Body,"Neck",new Vector3(0,.16f,.8f));
        Tube(r,r.Neck,"NeckMesh",new[]{new Vector3(0,0,0),new Vector3(0,.39f,.31f),new Vector3(0,.68f,.66f),new Vector3(0,.78f,1.14f)},new[]{.42f,.37f,.30f,.27f},r.Skin,10,.92f);
        Tube(r,r.Neck,"Throat",new[]{new Vector3(0,-.14f,.12f),new Vector3(0,.27f,.56f),new Vector3(0,.68f,.85f),new Vector3(0,.79f,1.13f)},new[]{.22f,.20f,.16f,.13f},r.Belly,7,.65f);
        r.Head=Joint(r.Neck,"Head",new Vector3(0,.78f,1.14f));r.Head.localScale=Vector3.one*1.23f;
        Tube(r,r.Head,"Skull",new[]{new Vector3(0,.04f,-.28f),new Vector3(0,.08f,0),new Vector3(0,.0f,.35f),new Vector3(0,-.12f,.68f),new Vector3(0,-.14f,.83f)},new[]{.17f,.33f,.27f,.20f,.09f},r.Skin,8,.66f);
        Tube(r,r.Head,"BrowBridge",new[]{new Vector3(0,.22f,-.12f),new Vector3(0,.24f,.22f),new Vector3(0,.04f,.69f)},new[]{.12f,.17f,.025f},r.Ridge,5,.5f);
        r.Jaw=Joint(r.Head,"Jaw",new Vector3(0,-.16f,-.02f));
        Tube(r,r.Jaw,"LowerJaw",new[]{new Vector3(0,0,0),new Vector3(0,-.02f,.35f),new Vector3(0,-.015f,.72f)},new[]{.22f,.17f,.07f},r.Belly,7,.32f);
        Tube(r,r.Jaw,"MouthInterior",new[]{new Vector3(0,.045f,.10f),new Vector3(0,.04f,.5f),new Vector3(0,.02f,.72f)},new[]{.17f,.13f,.03f},r.Dark,6,.12f);
        foreach(int s in new[]{-1,1})
        {
            Tube(r,r.Head,"CrownHorn"+s,new[]{new Vector3(s*.24f,.15f,-.14f),new Vector3(s*.42f,.48f,-.39f),new Vector3(s*.46f,.66f,-.68f)},new[]{.125f,.08f,.004f},r.Horn,5,1);
            Tube(r,r.Head,"CheekHorn"+s,new[]{new Vector3(s*.25f,-.03f,-.14f),new Vector3(s*.49f,.03f,-.38f),new Vector3(s*.56f,.19f,-.51f)},new[]{.095f,.055f,.003f},r.Horn,5,1);
            Pebble(r,r.Head,"EyeSocket"+s,new Vector3(s*.271f,.08f,.20f),new Vector3(.055f,.09f,.13f),r.Dark);
            Pebble(r,r.Head,"Eye"+s,new Vector3(s*.306f,.095f,.205f),new Vector3(.023f,.052f,.075f),r.Eye);
            Tube(r,r.Head,"HeavyBrow"+s,new[]{new Vector3(s*.18f,.20f,-.03f),new Vector3(s*.34f,.19f,.21f),new Vector3(s*.23f,.11f,.38f)},new[]{.065f,.07f,.015f},r.Ridge,5,.7f);
            for(int i=0;i<4;i++)
            {
                float z=.2f+i*.13f,x=s*Mathf.Lerp(.20f,.10f,i/3f);
                Tube(r,r.Head,"UpperFang",new[]{new Vector3(x,-.12f,z),new Vector3(x*.96f,-.23f,z+.025f)},new[]{.035f,.002f},r.Horn,4,1);
                Tube(r,r.Jaw,"LowerTooth",new[]{new Vector3(x,.04f,z),new Vector3(x,.11f,z)},new[]{.026f,.002f},r.Horn,4,1);
            }
            MakeLeg(r,s,true); MakeLeg(r,s,false); MakeWing(r,s);
        }
        Transform tailParent=r.Body;
        for(int i=0;i<7;i++)
        {
            var node=Joint(tailParent,"Tail"+i,i==0?new Vector3(0,-.02f,-1.03f):new Vector3(0,0,-.49f));
            float radius=Mathf.Lerp(.34f,.035f,i/6f);
            Tube(r,node,"TailMesh",new[]{Vector3.zero,new Vector3(0,0,-.25f),new Vector3(0,0,-.53f)},new[]{radius,radius*.88f,radius*.71f},r.Skin,7,.85f);
            if(i<6)Spike(r,node,"TailSpine",new Vector3(0,radius*.75f,-.22f),new Vector3(0,radius+.18f,-.36f),radius*.42f,r.Horn);
            r.Tail.Add(node);tailParent=node;
        }
        Spike(r,tailParent,"TailTip",new Vector3(0,0,-.45f),new Vector3(0,.07f,-.81f),.12f,r.Ridge);
        for(int i=0;i<7;i++)
        {
            float z=-.88f+i*.28f; float y=.50f+.12f*Mathf.Sin(i*.5f);
            Spike(r,r.Body,"DorsalPlate",new Vector3(0,y,z),new Vector3(0,y+.28f,z-.14f),.14f,i%2==0?r.Ridge:r.Horn);
            foreach(int s in new[]{-1,1})
                Pebble(r,r.Body,"ShoulderScale",new Vector3(s*(.55f+.08f*Mathf.Sin(i)),.44f,z),new Vector3(.23f,.10f,.25f),r.Ridge);
        }
        for(int i=0;i<3;i++)Spike(r,r.Neck,"NeckSpine",new Vector3(0,.20f+i*.3f,.05f+i*.19f),new Vector3(0,.44f+i*.3f,-.12f+i*.19f),.13f,r.Horn);
        Sample(r,0);
        return r;
    }

    /// <summary>12秒候选动作：呼吸、步行、蓄力前扑、张口、回到待机；不执行AI、飞行或伤害。</summary>
    public static void Sample(Rig r,float t)
    {
        float walk = Mathf.Clamp(t-1.5f,0,3), advance=walk*.52f;
        float crouch = Pulse(t,4.5f,5.4f,6.1f), lunge=Smooth(5.4f,6.1f,t)*1.05f;
        float roar=Pulse(t,7.4f,8.3f,9.5f), show=Pulse(t,9.5f,10.4f,11.7f);
        float hop=t>=5.4f&&t<=6.1f?Mathf.Sin((t-5.4f)/.7f*Mathf.PI)*.24f:0;
        bool stepping=t>=1.5f&&t<4.5f;
        r.Root.transform.localPosition=new Vector3(0,0,-1.30f+advance+lunge);
        r.Body.localPosition=new Vector3(0,1.52f+.025f*Mathf.Sin(t*2.5f)-(crouch*.19f)+hop,0);
        r.Body.localRotation=Quaternion.Euler(crouch*7,stepping?Mathf.Sin(walk*5)*1.7f:0,0);
        r.Neck.localRotation=Quaternion.Euler(crouch*17-roar*22,show*13,0);
        r.Head.localRotation=Quaternion.Euler(-4+roar*9,show*5,0);
        r.Jaw.localRotation=Quaternion.Euler(4+roar*31+crouch*5,0,0);
        for(int i=0;i<r.Tail.Count;i++)r.Tail[i].localRotation=Quaternion.Euler(i<2?7: -3,Mathf.Sin(t*1.6f-i*.58f)*(i+1)*1.65f,0);
        for(int i=0;i<r.Wings.Count;i++)
        {
            float s=i==0?-1:1;
            r.Wings[i].localRotation=Quaternion.Euler(-4+Mathf.Sin(t*1.8f)*2+crouch*13-roar*8, s*(-crouch*9-show*7),s*(Mathf.Sin(t*1.8f)*3-roar*8-show*10));
        }
        foreach(var leg in r.Legs)
        {
            float cycle=walk/1.05f+leg.Phase, phase=cycle-Mathf.Floor(cycle);
            float lift=stepping&&phase>.62f?Mathf.Sin((phase-.62f)/.38f*Mathf.PI)*.20f:0;
            float strideHalf=.52f*1.05f*.62f*.5f;
            float gaitBlend=Smooth(1.5f,1.8f,t)*(1-Smooth(4.2f,4.5f,t));
            float offset=stepping?(phase<=.62f?Mathf.Lerp(strideHalf,-strideHalf,phase/.62f):Mathf.Lerp(-strideHalf,strideHalf,(phase-.62f)/.38f))*gaitBlend:0;
            lift*=gaitBlend;
            // 前后脚错开蹬地与落地。接触阶段锁定世界落点，离地阶段才随前扑前移。
            float takeoff=leg.Front?5.35f:5.47f, landing=leg.Front?6.05f:6.16f;
            if(t>=5.35f)
            {
                float jumpPhase=Mathf.Clamp01((t-takeoff)/(landing-takeoff));
                offset+=Smooth(takeoff,landing,t)*1.05f-lunge;
                lift+=Mathf.Sin(jumpPhase*Mathf.PI)*.30f;
            }
            // 脚目标在根坐标地平面，身体下压由膝节吸收；不直接把整条腿抬离地面。
            Vector3 foot=new Vector3(leg.Hip.x*1.32f,.13f+lift,leg.Hip.z+(leg.Front?.27f:-.15f)+offset);
            SolveLeg(r,leg,foot);
        }
    }
    private static float Smooth(float a,float b,float t)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,t));
    private static float Pulse(float t,float a,float b,float c)=>Smooth(a,b,t)*(1-Smooth(b,c,t));
    private static void SolveLeg(Rig r,Leg leg,Vector3 targetRoot)
    {
        Vector3 hip=leg.Hip, target=r.Body.InverseTransformPoint(r.Root.transform.TransformPoint(targetRoot));
        Vector3 delta=target-hip;float d=Mathf.Clamp(delta.magnitude,.1f,leg.LengthA+leg.LengthB-.015f);
        Vector3 axis=delta.normalized, bend=Vector3.Cross(Vector3.right,axis).normalized*(leg.Front?1:-1);
        float a=(leg.LengthA*leg.LengthA-leg.LengthB*leg.LengthB+d*d)/(2*d),h=Mathf.Sqrt(Mathf.Max(0,leg.LengthA*leg.LengthA-a*a));
        Vector3 knee=hip+axis*a+bend*h;
        leg.Upper.localPosition=hip; leg.Upper.localRotation=Quaternion.FromToRotation(Vector3.down,knee-hip);
        leg.Lower.localPosition=knee; leg.Lower.localRotation=Quaternion.FromToRotation(Vector3.down,target-knee);
        leg.Foot.position=r.Root.transform.TransformPoint(targetRoot); leg.Foot.rotation=r.Root.transform.rotation;
    }
    private static void MakeLeg(Rig r,int s,bool front)
    {
        var leg=new Leg{Hip=new Vector3(s*(front?.48f:.43f),front?-.04f:-.10f,front?.73f:-.82f),LengthA=front?.82f:.86f,LengthB=front?.80f:.78f,Phase=(s<0?0:.5f)+(front?0:.5f),Front=front};
        string id=(front?"Fore":"Hind")+(s<0?"L":"R");
        leg.Upper=Joint(r.Body,id+"Upper",leg.Hip);leg.Lower=Joint(r.Body,id+"Lower",Vector3.zero);leg.Foot=Joint(r.Root.transform,id+"Foot",Vector3.zero);
        Tube(r,leg.Upper,"UpperMesh",new[]{Vector3.zero,new Vector3(0,-leg.LengthA*.4f,0),new Vector3(0,-leg.LengthA,0)},new[]{front?.29f:.40f,front?.25f:.34f,.15f},r.Skin,9,1);
        Tube(r,leg.Lower,"Shin",new[]{Vector3.zero,new Vector3(0,-leg.LengthB*.65f,0),new Vector3(0,-leg.LengthB,0)},new[]{.16f,.105f,.14f},r.Skin,7,1);
        Pebble(r,leg.Foot,"FootPad",new Vector3(0,0,.12f),new Vector3(.23f,.13f,.33f),r.Skin);
        for(int i=-1;i<=1;i++)
        {
            Tube(r,leg.Foot,"Toe",new[]{new Vector3(i*.12f,0,.17f),new Vector3(i*.145f,-.025f,.36f),new Vector3(i*.15f,-.055f,.50f-Mathf.Abs(i)*.04f)},new[]{.08f,.06f,.003f},r.Horn,5,1);
        }
        r.Legs.Add(leg);
    }
    private static void MakeWing(Rig r,int s)
    {
        var wing=Joint(r.Body,s<0?"WingL":"WingR",new Vector3(s*.48f,.33f,.47f));r.Wings.Add(wing);
        Vector3 elbow=new Vector3(s*.77f,.83f,-.1f), wrist=new Vector3(s*1.38f,1.60f,.18f);
        Tube(r,wing,"WingArm",new[]{Vector3.zero,elbow,wrist},new[]{.20f,.13f,.075f},r.Skin,7,1);
        Spike(r,wing,"Thumb",wrist,wrist+new Vector3(s*.16f,.32f,.12f),.10f,r.Horn);
        var tips=new[]{new Vector3(s*3.12f,1.16f,.95f),new Vector3(s*3.02f,.63f,-.43f),new Vector3(s*2.48f,-.03f,-1.63f),new Vector3(s*1.42f,-.43f,-2.0f),new Vector3(s*.17f,-.38f,-1.30f)};
        for(int i=0;i<tips.Length;i++)
        {
            Vector3 mid=Vector3.Lerp(wrist,tips[i],.48f)+new Vector3(0,.12f,0);
            Tube(r,wing,"WingFinger"+i,new[]{wrist,mid,tips[i]},new[]{.064f,.045f,.009f},i==0?r.Horn:r.Ridge,5,1);
            if(i==tips.Length-1)continue;
            // 每片翼膜有内凹弧边与中部微隆起，三角形明暗对应真实分面法线。
            Vector3 a=tips[i],b=tips[i+1], notch=Vector3.Lerp(a,b,.5f); notch=Vector3.Lerp(notch,wrist,.13f);
            Vector3 center=(wrist+a+b)/3+new Vector3(0,.12f,0);
            var v=new[]{wrist,a,Vector3.Lerp(a,notch,.55f),notch,Vector3.Lerp(notch,b,.45f),b,center};
            var tri=new List<int>();for(int j=0;j<6;j++){tri.Add(6);tri.Add(j);tri.Add((j+1)%6);}
            MeshObject(r,wing,"WingMembrane"+i,v,tri.ToArray(),r.Membrane,true);
        }
    }
    private static Transform Joint(Transform p,string name,Vector3 position){var g=new GameObject(name);g.transform.SetParent(p,false);g.transform.localPosition=position;return g.transform;}
    private static Material Mat(Rig r,string name,Color c)
    {
        var sh=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");var m=new Material(sh){name="TEMP_Dragon_"+name,color=c};
        if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",c);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.12f);
        if(m.HasProperty("_Cull"))m.SetFloat("_Cull",2);r.Owned.Add(m);return m;
    }
    private static void Spike(Rig r,Transform p,string name,Vector3 a,Vector3 b,float radius,Material m)=>Tube(r,p,name,new[]{a,Vector3.Lerp(a,b,.55f),b},new[]{radius,radius*.55f,.002f},m,5,1);
    private static void Pebble(Rig r,Transform p,string name,Vector3 center,Vector3 size,Material m)
    {
        Tube(r,p,name,new[]{center-new Vector3(0,0,size.z),center-new Vector3(0,0,size.z*.35f),center+new Vector3(0,0,size.z*.35f),center+new Vector3(0,0,size.z)},new[]{.01f,size.x,size.x,.01f},m,7,size.y/size.x);
    }
    /// <summary>沿曲线截面放样真实低多边形网格，端部封口；不使用碰撞原始体。</summary>
    private static void Tube(Rig r,Transform parent,string name,Vector3[] centers,float[] radius,Material mat,int sides,float vertical)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();
        for(int i=0;i<centers.Length;i++)
        {
            Vector3 tangent=(centers[Mathf.Min(i+1,centers.Length-1)]-centers[Mathf.Max(0,i-1)]).normalized;
            Vector3 right=Vector3.Cross(Mathf.Abs(Vector3.Dot(tangent,Vector3.up))>.9f?Vector3.forward:Vector3.up,tangent).normalized;
            Vector3 up=Vector3.Cross(tangent,right).normalized;
            for(int j=0;j<sides;j++){float angle=(j+.5f)*Mathf.PI*2/sides;vertices.Add(centers[i]+radius[i]*(right*Mathf.Cos(angle)+up*Mathf.Sin(angle)*vertical));}
        }
        for(int i=0;i<centers.Length-1;i++)for(int j=0;j<sides;j++)
        {int a=i*sides+j,b=i*sides+(j+1)%sides,c=a+sides,d=b+sides;triangles.AddRange(new[]{a,b,c,b,d,c});}
        for(int j=1;j<sides-1;j++){triangles.AddRange(new[]{0,j+1,j});int n=(centers.Length-1)*sides;triangles.AddRange(new[]{n,n+j,n+j+1});}
        MeshObject(r,parent,name,vertices.ToArray(),triangles.ToArray(),mat,false);
    }
    private static void MeshObject(Rig r,Transform parent,string name,Vector3[] v,int[] tri,Material mat,bool doubleSided)
    {
        var fv=new List<Vector3>();var ft=new List<int>();
        for(int i=0;i<tri.Length;i+=3)
        {
            int n=fv.Count;fv.Add(v[tri[i]]);fv.Add(v[tri[i+1]]);fv.Add(v[tri[i+2]]);ft.AddRange(new[]{n,n+1,n+2});
            // 双面膜必须独立背面顶点，避免反向三角形共享顶点导致法线互相抵消。
            if(doubleSided){n=fv.Count;fv.Add(v[tri[i+2]]);fv.Add(v[tri[i+1]]);fv.Add(v[tri[i]]);ft.AddRange(new[]{n,n+1,n+2});}
        }
        var mesh=new Mesh{name="TEMP_Dragon_"+name};mesh.SetVertices(fv);mesh.SetTriangles(ft,0);mesh.RecalculateNormals();mesh.RecalculateBounds();r.Owned.Add(mesh);
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=mat;
    }
}
