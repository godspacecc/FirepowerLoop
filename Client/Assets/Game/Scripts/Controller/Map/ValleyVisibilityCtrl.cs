using System;
using UnityEngine;

namespace Game
{
    /// <summary>在共享生命周期中裁剪远处生成块渲染；不关闭碰撞、导航或玩法对象。</summary>
    public sealed class ValleyVisibilityCtrl:IDisposable
    {
        private readonly Renderer[] blocks;
        private readonly Renderer[] resources;
        /// <summary>合批草灌独立使用120米裁剪距离；只裁剪渲染，网格由地图句柄持有。</summary>
        private readonly Renderer[] groundCover;
        // 生成地图中的物件不会移动，缓存中心避免每帧查询数千个 Renderer 的世界包围盒。
        private readonly Vector3[] blockCenters, resourceCenters, coverCenters;
        private float nextRefresh;
        private bool registered;
        /// <summary>缓存当前候选的静止渲染器中心；注册由地图活动状态管理，释放时解除共享生命周期回调。</summary>
        public ValleyVisibilityCtrl(Renderer[] blocks, Renderer[] resources = null, Renderer[] groundCover = null)
        {
            this.blocks=blocks;this.resources=resources??Array.Empty<Renderer>();
            this.groundCover=groundCover??Array.Empty<Renderer>();
            blockCenters=Centers(blocks);resourceCenters=Centers(this.resources);
            coverCenters=Centers(this.groundCover);
        }
        private static Vector3[] Centers(Renderer[] renderers)
        {var result=new Vector3[renderers.Length];for(var i=0;i<result.Length;i++)if(renderers[i]!=null)result[i]=renderers[i].bounds.center;return result;}
        public void SetActive(bool active)
        {
            if(active==registered)return;
            registered=active;
            nextRefresh=0;
            if(active)LifeCycle.RegisterLateUpdate(Refresh);else LifeCycle.UnregisterLateUpdate(Refresh);
        }
        private void Refresh()
        {
            var camera=Camera.main;if(camera==null)return;
            if(Time.realtimeSinceStartup<nextRefresh)return;
            nextRefresh=Time.realtimeSinceStartup+.25f;
            var position=camera.transform.position;
            Cull(blocks,blockCenters,position,450);
            Cull(resources,resourceCenters,position,180);
            // 合批块中心与边缘最多相差约46米，保留余量以免相机附近草灌整块消失。
            Cull(groundCover,coverCenters,position,166);
        }
        /// <summary>仅调整渲染距离，保持树木采集身份、碰撞与砍伐后的停用状态。</summary>
        private static void Cull(Renderer[] renderers,Vector3[] centers,Vector3 position,float distance)
        {for(var i=0;i<renderers.Length;i++)if(renderers[i]!=null)renderers[i].enabled=(centers[i]-position).sqrMagnitude<distance*distance;}
        public void Dispose()=>SetActive(false);
    }
}
