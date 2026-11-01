using UnityEngine;
using UnityEngine.UI;
namespace Game
{
    /// <summary>强化界面的序列化切角面片及图标；仅绘图，不持有规则或创建资源。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SurvivorUpgradeShape : MaskableGraphic
    {
        /// <summary>Prefab保存的归一化轮廓，按环绕顺序连接，不在运行时重建布局。</summary>
        public Vector2[] Points;
        /// <summary>根据当前矩形绘制已序列化的凸多边形。</summary>
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();if(Points==null||Points.Length<3)return;
            var rect=GetPixelAdjustedRect();mesh.AddVert(rect.center,color,Vector2.zero);
            foreach(var point in Points)mesh.AddVert(new Vector2(rect.xMin+point.x*rect.width,rect.yMin+point.y*rect.height),color,Vector2.zero);
            for(int i=0;i<Points.Length;i++)mesh.AddTriangle(0,i+1,(i+1)%Points.Length+1);
        }
    }
}
