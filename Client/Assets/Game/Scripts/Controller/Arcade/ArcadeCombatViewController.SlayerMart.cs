using System;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>墓穴购物区域只拥有场景表现，现金与领取身份始终由运行态裁决。</summary>
        private GameObject slayerMartRegionRoot;
        private int slayerMartRegionId;
        private bool slayerMartRegionReady;
        private string slayerMartRegionError;
        private readonly Dictionary<int,GameObject> slayerMartCashBoxes=new();
        public bool IsSlayerMartRegionReady=>slayerMartRegionReady&&root!=null&&contentTemplates!=null&&!contentLoadFailed;
        public string SlayerMartRegionLoadError=>slayerMartRegionError??(contentLoadFailed?"商店公共场景资源加载失败":null);
        /// <summary>复用正式轮级几何材质所有权；全部几何建成且公共资源就绪后才公开准备完成。</summary>
        public void SetSlayerMartRegion(ArcadeSlayerMartRegionLayout layout,IReadOnlyList<int> remainingIds)
        {
            if(root==null||layout==null)return;
            try
            {
                if(slayerMartRegionRoot==null||slayerMartRegionId!=layout.Id)
                {
                    ReleaseSlayerMartRegion();ReleaseRoundContent();slayerMartRegionId=layout.Id;
                    slayerMartRegionRoot=RoundGroup(root.transform,"SlayerMartCrypt_"+layout.Id);var t=slayerMartRegionRoot.transform;
                    RoundBox(t,"CryptFloor",0,-.15f,0,layout.HalfWidth*2,.3f,layout.HalfHeight*2,0x383D48FFu);
                    RoundBox(t,"NorthWall",0,1.6f,layout.HalfHeight+.2f,layout.HalfWidth*2,3.2f,.4f,0x646678FFu);
                    RoundBox(t,"WestWall",-layout.HalfWidth-.2f,1.6f,0,.4f,3.2f,layout.HalfHeight*2,0x646678FFu);
                    RoundBox(t,"EastWall",layout.HalfWidth+.2f,1.6f,0,.4f,3.2f,layout.HalfHeight*2,0x646678FFu);
                    RoundBox(t,"ShopCounter",0,.6f,layout.HalfHeight-1,4,1.2f,1,0x8B7053FFu);
                    RoundBox(t,"CounterTrim",0,1.25f,layout.HalfHeight-1,4.2f,.15f,1.2f,0xC5A65DFFu);
                    foreach(float x in new[]{-layout.HalfWidth+.7f,layout.HalfWidth-.7f})
                        foreach(float y in new[]{-layout.HalfHeight+1.5f,layout.HalfHeight-1.5f})
                        {RoundCylinder(t,"CryptPillar",x,1.2f,y,.75f,2.4f,0x88899AFFu);RoundBox(t,"PillarCap",x,2.5f,y,1,.25f,1,0xB0ACB6FFu);}
                    RoundCylinder(t,"ReturnGate",layout.ExitX,.04f,layout.ExitY,1.5f,.08f,0x6AA68FFFu);
                    for(int index=0;index<layout.Boxes.Count;index++)
                    {
                        var box=layout.Boxes[index];var item=RoundGroup(t,"CashBox_"+(index+1));item.transform.localPosition=new Vector3(box.X,0,box.Y);
                        RoundBox(item.transform,"Chest",0,.3f,0,.8f,.6f,.65f,0xA27335FFu);
                        RoundBox(item.transform,"GoldLid",0,.65f,0,.85f,.15f,.7f,0xE7BE4AFFu);slayerMartCashBoxes.Add(index+1,item);
                    }
                    slayerMartRegionReady=true;
                }
                var remaining=new HashSet<int>(remainingIds??Array.Empty<int>());
                foreach(var box in slayerMartCashBoxes)box.Value.SetActive(remaining.Contains(box.Key));
                HideIslandBaseArena();if(contentEnvironment!=null)contentEnvironment.SetActive(false);
            }
            catch(Exception exception){slayerMartRegionError=exception.Message;slayerMartRegionReady=false;}
        }
        /// <summary>地图结束先回收区域根，所用材质由既有轮级释放流程随后统一释放。</summary>
        private void ReleaseSlayerMartRegion()
        {
            if(slayerMartRegionRoot!=null)slayerMartRegionRoot.SetActive(false);
            DestroyObject(slayerMartRegionRoot);slayerMartRegionRoot=null;slayerMartRegionId=0;
            slayerMartCashBoxes.Clear();slayerMartRegionReady=false;slayerMartRegionError=null;
        }
    }
}
