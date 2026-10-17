using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>每名玩家独立拥有风暴根与材质，随既有表现同步回收。</summary>
        private sealed class StormVisual
        {
            public GameObject Root;
            public Material Material;
            public LineRenderer Range,Lightning;
            public int SeenRevision;
        }
        private readonly Dictionary<long,StormVisual> stormVisuals=new();
        private readonly List<long> staleStormOwners=new();
        /// <summary>独立风暴几何只显示真实范围；不创建普通电球或碰撞器。</summary>
        public void SetSlayerMartStorm(long ownerPlayerId,Vector2 position,float radius)
        {
            if(root==null||radius<=0||float.IsNaN(radius)||float.IsInfinity(radius))return;
            if(!stormVisuals.TryGetValue(ownerPlayerId,out var visual))
            {
                var shader=Shader.Find("Universal Render Pipeline/Unlit")??Shader.Find("Sprites/Default");
                if(shader==null)return;
                visual=new StormVisual{Root=new GameObject("SlayerMartStorm_"+ownerPlayerId),Material=new Material(shader)};
                visual.Material.name="SlayerMartStormMaterial_"+ownerPlayerId;
                visual.Material.SetColor("_BaseColor",new Color(.15f,.85f,1f,1f));
                if(visual.Material.HasProperty("_Color"))visual.Material.color=new Color(.15f,.85f,1f,1f);
                visual.Root.transform.SetParent(root.transform,false);
                visual.Range=StormLine(visual,"Range",64,true,.1f);
                visual.Lightning=StormLine(visual,"Lightning",41,false,.055f);
                stormVisuals.Add(ownerPlayerId,visual);
            }
            visual.SeenRevision=effectSyncRevision;
            visual.Root.transform.localPosition=new Vector3(position.x,.12f,position.y);
            for(int index=0;index<64;index++)
            {
                float angle=index*Mathf.PI*2/64;
                visual.Range.SetPosition(index,new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius));
            }
            // 闪电摆动只改变内侧线条，外圈始终保持权威半径。
            for(int arm=0;arm<8;arm++)
            {
                float angle=arm*Mathf.PI/4+Time.unscaledTime*.5f;
                visual.Lightning.SetPosition(arm*5,Vector3.zero);
                for(int step=1;step<=4;step++)
                {
                    float zigzag=step%2==0?.1f:-.1f;
                    float direction=angle+zigzag*Mathf.Sin(Time.unscaledTime*13+arm);
                    visual.Lightning.SetPosition(arm*5+step,new Vector3(Mathf.Cos(direction),0,Mathf.Sin(direction))*radius*(step*.22f));
                }
            }
            visual.Lightning.SetPosition(40,Vector3.zero);
        }
        private static LineRenderer StormLine(StormVisual visual,string name,int count,bool loop,float width)
        {
            var item=new GameObject(name);item.transform.SetParent(visual.Root.transform,false);
            var line=item.AddComponent<LineRenderer>();line.useWorldSpace=false;line.loop=loop;line.positionCount=count;
            line.widthMultiplier=width;line.sharedMaterial=visual.Material;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
            return line;
        }
        /// <summary>死亡、过期或地图卸载统一释放专属材质，不能只销毁场景子对象。</summary>
        private void ReleaseStormVisual(long owner)
        {
            if(!stormVisuals.TryGetValue(owner,out var visual))return;
            if(visual.Root!=null)visual.Root.SetActive(false);
            DestroyObject(visual.Root);DestroyObject(visual.Material);stormVisuals.Remove(owner);
        }
        private void EndStormSync(bool releaseAll=false)
        {
            staleStormOwners.Clear();
            foreach(var pair in stormVisuals)if(releaseAll||pair.Value.SeenRevision!=effectSyncRevision)staleStormOwners.Add(pair.Key);
            foreach(long owner in staleStormOwners)ReleaseStormVisual(owner);
            staleStormOwners.Clear();
        }
    }
}
