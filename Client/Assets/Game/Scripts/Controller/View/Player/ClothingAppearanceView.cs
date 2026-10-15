using UnityEngine;

namespace Game
{
    /// <summary>七部位与整套同骨架外观、第一人称手臂绑定；只消费快照，不拥有装备或测试选择权威。</summary>
    public sealed class ClothingAppearanceView : MonoBehaviour
    {
        [System.Serializable] public struct Part
        {
            public string AppearanceId;
            public GameObject Root;
            public Mesh SleeveMesh;
        }
        [SerializeField] GameObject baseTorso;
        [SerializeField] GameObject baseLegs;
        [SerializeField] Mesh baseSleeves;
        [SerializeField] Part[] tops;
        [SerializeField] Part[] pants;
        [SerializeField] GameObject baseHands;
        [SerializeField] GameObject baseHeadCover;
        [SerializeField] GameObject baseFeet;
        [SerializeField] Part[] heads;
        [SerializeField] Part[] hands;
        [SerializeField] Part[] feet;
        [SerializeField] Part[] backs;
        [SerializeField] Part[] armors;
        [System.Serializable] public struct SleeveVariant
        {
            public string TorsoId;
            public string HandsId;
            public Mesh Mesh;
        }
        /// <summary>手套与袖口的已适配组合；缺失组合时拒绝整次表现切换。</summary>
        [SerializeField] SleeveVariant[] gloveSleeves;
        [System.Serializable] public struct WholeBodyVariant
        {
            public GameObject Root;
            public Mesh Sleeves;
        }
        /// <summary>现有分部位网格的共同父节点；整套造型生效时隐藏，装备快照仍持续更新。</summary>
        [SerializeField] GameObject modularRoot;
        /// <summary>测试编号 1～9 对应的整套网格及手臂；0 代表分部位模式，不替换骨骼或 Animator。</summary>
        [SerializeField] WholeBodyVariant[] wholeBodies;
        int wholeBodyIndex;
        Mesh clothingSleeves;
        public Mesh CurrentSleeves => wholeBodyIndex == 0 ? clothingSleeves : wholeBodies[wholeBodyIndex - 1].Sleeves;
        /// <summary>编号完整校验后一次切换；非法编号或缺失绑定保持原状态，0 恢复最新衣物快照。</summary>
        public bool ApplyWholeBody(int index)
        {
            if (index < 0 || index > (wholeBodies?.Length ?? 0) || modularRoot == null) return false;
            foreach (var variant in wholeBodies)
                if (variant.Root == null || variant.Sleeves == null) return false;
            for (int i = 0; i < wholeBodies.Length; i++) wholeBodies[i].Root.SetActive(i + 1 == index);
            modularRoot.SetActive(index == 0);
            wholeBodyIndex = index;
            return true;
        }
        /// <summary>空 ID 显示基础内搭；未知 ID 拒绝整次切换，保留旧可见状态。</summary>
        public bool Apply(string torso,string legs,string head="",string gloves="",string boots="",string back="",string armor="")
        {
            int a=Find(tops,torso),b=Find(pants,legs);
            int h=Find(heads,head),g=Find(hands,gloves),f=Find(feet,boots),k=Find(backs,back),r=Find(armors,armor);
            if(a==-2||b==-2||h==-2||g==-2||f==-2||k==-2||r==-2||baseTorso==null||baseLegs==null||baseHands==null||baseFeet==null||baseHeadCover==null)return false;
            Mesh sleeves=a<0?baseSleeves:tops[a].SleeveMesh;
            if(g>=0)
            {
                sleeves=null;
                foreach(var variant in gloveSleeves??System.Array.Empty<SleeveVariant>())
                    if(variant.TorsoId==(torso??"")&&variant.HandsId==gloves){sleeves=variant.Mesh;break;}
            }
            if(sleeves==null)return false;
            baseTorso.SetActive(a<0);baseLegs.SetActive(b<0);
            baseHands.SetActive(g<0);baseFeet.SetActive(f<0);
            baseHeadCover.SetActive(h<0);
            Toggle(tops,a);Toggle(pants,b);Toggle(heads,h);Toggle(hands,g);Toggle(feet,f);Toggle(backs,k);Toggle(armors,r);
            clothingSleeves=sleeves;
            return true;
        }
        /// <summary>将固定槽位的物品 ID 解析为外观；未知物品不作为空槽处理。</summary>
        public bool ApplyItems(IRuntimeConfigProvider config,params int[] items)
        {
            if(items==null||items.Length!=ClothingEquipmentModel.SlotCount)return false;
            var ids=new string[items.Length];
            for(int i=0;i<items.Length;i++)
            {
                if(items[i]==0){ids[i]="";continue;}
                if(!config.TryGetItem(items[i],out var item,out _)||item.ItemType!=global::ItemType.Clothing||(int)item.WearSlot!=i+1)return false;
                ids[i]=item.AppearanceId;
            }
            return Apply(ids[0],ids[1],ids[2],ids[3],ids[4],ids[5],ids[6]);
        }
        static void Toggle(Part[] parts,int active)
        {foreach(var part in parts??System.Array.Empty<Part>())part.Root.SetActive(part.AppearanceId==((active>=0)?parts[active].AppearanceId:null));}
        static int Find(Part[] parts,string id)
        {if(string.IsNullOrEmpty(id))return -1;for(int i=0;i<(parts?.Length??0);i++)if(parts[i].AppearanceId==id&&parts[i].Root!=null)return i;return -2;}
    }
}
