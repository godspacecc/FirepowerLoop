using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>当前地图唯一持有的炸弹池；离场先释放实例再释放 Content。</summary>
        private ArcadeBombBlastPresentation bombBlasts;
        /// <summary>资源刚加载时的短暂延迟，过期或离场后不补播旧爆炸。</summary>
        private readonly Queue<(Vector2 position,float time)> pendingBombBlasts=new();
        private void PlayBombVisual(Vector2 position)
        {
            if(root==null)return;
            if(bombBlasts==null)
            {
                if(pendingBombBlasts.Count==4)pendingBombBlasts.Dequeue();
                pendingBombBlasts.Enqueue((position,Time.unscaledTime));return;
            }
            float x=Mathf.Abs(position.x)+roundHalfWidth,z=Mathf.Abs(position.y)+roundHalfHeight;
            bombBlasts.Play(position,Mathf.Sqrt(x*x+z*z));
        }
        /// <summary>只在已有 Content 代次检查通过后绑定，不能复活旧地图。</summary>
        private void BindBombVisual(Transform template)
        {
            bombBlasts?.Dispose();bombBlasts=new ArcadeBombBlastPresentation(root.transform,template);
            while(pendingBombBlasts.Count>0)
            {
                var pending=pendingBombBlasts.Dequeue();
                if(Time.unscaledTime-pending.time<=.5f)PlayBombVisual(pending.position);
            }
        }
        private void ClearBombVisuals()
        {bombBlasts?.Dispose();bombBlasts=null;pendingBombBlasts.Clear();}
    }
}
