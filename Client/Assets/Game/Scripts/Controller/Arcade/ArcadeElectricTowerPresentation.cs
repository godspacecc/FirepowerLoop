using System;
using System.Linq;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    /// <summary>已采用码头电塔的只读相位投影；由机关拥有实例，无独立计时、伤害或 Update。</summary>
    internal sealed class ArcadeElectricTowerPresentation
    {
        // 所有材质与网格借用正式模板，释放由外层机关根与内容资源句柄负责。
        private readonly Transform charge;
        private readonly Renderer status;
        private readonly LineRenderer boundary;
        private readonly LineRenderer[] corona, charging, discharge;
        private readonly Material green, red, cyan;
        private float boundaryRadius=-1;

        public ArcadeElectricTowerPresentation(Transform root)
        {
            charge=root.Find("塔顶聚电核");status=root.Find("状态灯").GetComponent<Renderer>();
            boundary=root.Find("危险边界").GetComponent<LineRenderer>();
            var lines=root.GetComponentsInChildren<LineRenderer>(true);
            corona=lines.Where(l=>l.name=="塔顶聚电火花").ToArray();
            charging=lines.Where(l=>l.name=="向上聚电").ToArray();
            discharge=lines.Where(l=>l.name=="分叉放电").ToArray();
            if(charge==null||corona.Length!=3||charging.Length!=4||discharge.Length!=12)
                throw new InvalidOperationException("码头电塔蓄能或放电模板缺失");
            green=status.sharedMaterial;red=root.Find("状态红灯材质").GetComponent<Renderer>().sharedMaterial;cyan=charging[0].sharedMaterial;
        }

        /// <summary>相同权威时间产生相同画面；安全熄弧、预警向上聚电、通电塔顶分叉。</summary>
        public void Sync(ArcadeHazardParameters p,double elapsed)
        {
            double time=Math.Max(0,elapsed);
            ArcadeHazardRules.Evaluate(p,time,out int phase,out _);
            float t=(float)(time%p.CycleSeconds);
            float progress=phase==1?(t-p.IdleSeconds)/p.WarningSeconds:phase==2?1:0;
            status.sharedMaterial=phase==0?green:red;boundary.sharedMaterial=phase==0?green:phase==1?red:cyan;
            boundary.widthMultiplier=phase==2?.10f:.06f;status.enabled=phase!=1||((int)(t*6)%2==0);
            if(boundaryRadius!=p.Radius)
            {
                boundaryRadius=p.Radius;
                for(int i=0;i<boundary.positionCount;i++){float a=i*Mathf.PI*2/boundary.positionCount;boundary.SetPosition(i,new Vector3(Mathf.Cos(a)*p.Radius,.055f,Mathf.Sin(a)*p.Radius));}
            }
            charge.gameObject.SetActive(phase!=0);charge.localScale=Vector3.one*(.12f+progress*.26f+Mathf.Sin(t*22)*.025f);
            int tick=(int)(time*18);
            for(int i=0;i<corona.Length;i++)
            {
                var arc=corona[i];arc.enabled=phase==2||phase==1&&progress>i*.25f;
                for(int j=0;j<arc.positionCount;j++)
                {
                    float a=j*Mathf.PI*2/arc.positionCount;float r=.13f+progress*.19f+Noise(tick,i,j)*.055f;
                    var v=Quaternion.Euler(i*57,20+i*43,t*90)*new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,0);
                    arc.SetPosition(j,new Vector3(0,2.75f,0)+v);
                }
            }
            for(int i=0;i<charging.Length;i++)
            {
                var arc=charging[i];arc.enabled=phase==2||phase==1&&i<1+(int)(progress*3);
                for(int j=0;j<arc.positionCount;j++)
                {
                    float u=j/(float)(arc.positionCount-1);float a=u*Mathf.PI*4+t*8+i*Mathf.PI*.5f;
                    float r=.37f+Noise(tick,i,j)*.09f;arc.SetPosition(j,new Vector3(Mathf.Cos(a)*r,.8f+u*1.55f,Mathf.Sin(a)*r));
                }
            }
            for(int i=0;i<discharge.Length;i++)
            {
                var arc=discharge[i];arc.enabled=phase==2||phase==1&&progress>.35f&&i<2&&tick%3!=0;
                float a=(i%8)*Mathf.PI/4+Noise(tick,i,9)*.24f;bool branch=i>=8;
                Vector3 start=branch?new Vector3(Mathf.Cos(a)*p.Radius*.45f,1.5f,Mathf.Sin(a)*p.Radius*.45f):new Vector3(Mathf.Cos(a)*.43f,2.39f,Mathf.Sin(a)*.43f);
                Vector3 end=phase==1?new Vector3((i==0?-1:1)*.77f,1.5f,0):new Vector3(Mathf.Cos(a)*p.Radius*(branch?.78f:.96f),.12f,Mathf.Sin(a)*p.Radius*(branch?.78f:.96f));
                for(int j=0;j<arc.positionCount;j++)
                {
                    float u=j/(float)(arc.positionCount-1);var v=Vector3.Lerp(start,end,u);
                    if(j>0&&j<arc.positionCount-1)v+=new Vector3(Noise(tick,i,j)*.14f,Noise(tick+3,i,j)*.13f,Noise(tick+7,i,j)*.14f);
                    arc.SetPosition(j,v);
                }
                arc.widthMultiplier=phase==1?.023f:i%3==0?.033f:.045f;
            }
        }
        /// <summary>确定性折线扰动，不读取Unity全局随机源。</summary>
        private static float Noise(int tick,int i,int j)=>Mathf.Sin(tick*17.137f+i*31.73f+j*11.53f);
    }
}
