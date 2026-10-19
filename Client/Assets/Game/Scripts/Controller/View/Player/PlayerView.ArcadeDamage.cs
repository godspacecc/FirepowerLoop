using UnityEngine;

namespace Game
{
    public sealed partial class PlayerView
    {
        /// <summary>正式 Prefab 显式携带受击、落尘与救援依赖，不使用运行时 Shader.Find。</summary>
        [SerializeField] private GameObject arcadeDamageTemplate;
        /// <summary>当前角色独占的受击倒地实例，沿用 PlayerView 的启停和反馈帧。</summary>
        private ArcadePlayerDamagePresentation arcadeDamage;
        /// <summary>进场时准备模板，受击期间不重复创建资源。</summary>
        private void PrepareArcadeDamage()
        {
            if(arcadeDamage==null&&arcadeDamageTemplate!=null)
                arcadeDamage=new ArcadePlayerDamagePresentation(transform,arcadeDamageTemplate,presentationAlive);
        }
        /// <summary>只镜像现有服务器可救援状态，倒地动画本身不能推导救援资格。</summary>
        public void SetArcadeRescueActive(bool active)
        {
            if(!arcadePresentationActive)return;
            PrepareArcadeDamage();arcadeDamage?.SetRescue(active&&isActiveAndEnabled);
        }
    }
}
