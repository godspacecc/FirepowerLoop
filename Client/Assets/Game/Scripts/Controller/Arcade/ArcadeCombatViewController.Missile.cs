using System;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>当前地图只持有一套空袭表现，随 Content 租约释放。</summary>
        private Transform missileTemplate, missileInstance, missileBody, missileRing;

        /// <summary>三个引用在进入战场之前校验，资源不完整走现有失败出口。</summary>
        private void BindMissileTemplate(Transform content)
        {
            ClearMissileVisual();
            missileTemplate=content.Find("MissileStrike");
            if(missileTemplate==null || missileTemplate.Find("Body")==null ||
                missileTemplate.Find("Warning")?.GetComponent<LineRenderer>()==null)
                throw new InvalidOperationException("导弹支援模板不完整");
        }

        /// <summary>预警圈与弹体共享锁定位置，只根据有效战斗年龄采样。</summary>
        public void SetMissileStrike(Vector2 point,float radius,float age,bool active)
        {
            if(!active)
            {
                if(missileInstance!=null)missileInstance.gameObject.SetActive(false);
                return;
            }
            if(root==null || missileTemplate==null)return;
            if(missileInstance==null)
            {
                missileInstance=UnityEngine.Object.Instantiate(missileTemplate,root.transform);
                missileRing=missileInstance.Find("Warning");
                missileBody=missileInstance.Find("Body");
            }
            missileInstance.localPosition=new Vector3(point.x,0,point.y);
            missileInstance.gameObject.SetActive(true);
            missileRing.localScale=new Vector3(radius,1,radius);
            bool descending=age>=SurvivorSystem.MissileWarningSeconds;
            missileBody.gameObject.SetActive(descending);
            if(descending)
            {
                float progress=Mathf.Clamp01((age-SurvivorSystem.MissileWarningSeconds)/SurvivorSystem.MissileDescentSeconds);
                missileBody.localPosition=Vector3.up*(.15f+6f*(1-Mathf.Pow(progress,1.5f)));
            }
        }

        /// <summary>离场或资源加载失败后，清理已创建的单一实例。</summary>
        private void ClearMissileVisual()
        {
            if(missileInstance!=null)DestroyObject(missileInstance.gameObject);
            missileTemplate=missileInstance=missileRing=missileBody=null;
        }
    }
}
