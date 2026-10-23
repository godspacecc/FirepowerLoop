using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>只缓存当前地图只读姿态，异步模板就绪后重放；不推进业务时间。</summary>
        private long herdViewMap;
        private ArcadeHerdSnapshot[] herdSnapshots=System.Array.Empty<ArcadeHerdSnapshot>();
        private readonly Dictionary<long,ArcadeHerdVisual> herdViews=new();
        public void SetHerds(long mapId,IReadOnlyList<ArcadeHerdSnapshot> cows)
        {
            if(mapId<=0||earlyStage?.HerdCount<=0||earlyStage==null||roundContentRoot==null||herdViewMap!=0&&herdViewMap!=mapId)return;
            herdViewMap=mapId;herdSnapshots=cows?.Where(c=>c.MapInstanceId==mapId&&c.Id>0).Take(earlyStage.HerdCount).ToArray()??System.Array.Empty<ArcadeHerdSnapshot>();
            ProjectHerds();
        }
        private void ProjectHerds()
        {
            if(earlyHerdTemplate==null||roundContentRoot==null)return;
            var present=new HashSet<long>();
            foreach(var snapshot in herdSnapshots)
            {
                if(snapshot.Phase==ArcadeHerdPhase.Expired||!present.Add(snapshot.Id))continue;
                if(!herdViews.TryGetValue(snapshot.Id,out var visual))
                {
                    var go=Object.Instantiate(earlyHerdTemplate,roundContentRoot.transform,false);go.name="Herd_"+snapshot.Id;
                    visual=go.GetComponent<ArcadeHerdVisual>();herdViews.Add(snapshot.Id,visual);
                }
                // 地图隐藏会解除View绑定，返回后必须再次绑定，不能继承无效身份。
                visual.Bind(herdViewMap,snapshot.Id);
                if(!visual.Project(snapshot)){visual.Release();present.Remove(snapshot.Id);}
            }
            foreach(var id in herdViews.Keys.Where(id=>!present.Contains(id)).ToArray())
            {DestroyRoundVisual(herdViews[id].gameObject);herdViews.Remove(id);}
            SyncHerdFeedback();
        }
        private void ClearHerdViews()
        {
            ReleaseHerdFeedback();
            foreach(var visual in herdViews.Values)if(visual!=null)DestroyRoundVisual(visual.gameObject);
            herdViews.Clear();herdViewMap=0;herdSnapshots=System.Array.Empty<ArcadeHerdSnapshot>();
        }
    }
}
