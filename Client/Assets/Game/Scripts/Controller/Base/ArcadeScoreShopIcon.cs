using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>采用版商店的线形武器／无人机图标，只绘制UI，不持有商品或战斗状态。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ArcadeScoreShopIcon : MaskableGraphic
    {
        /// <summary>商品为现有无人机时切换轮廓，其余空投武器使用枪形。</summary>
        public bool Drone;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if(Drone)
            {
                Circle(mesh,29,29,10);Circle(mesh,9,9,6);Circle(mesh,49,9,6);Circle(mesh,9,49,6);Circle(mesh,49,49,6);
                Line(mesh,14,14,22,22);Line(mesh,36,36,44,44);Line(mesh,14,44,22,36);Line(mesh,36,22,44,14);
                Line(mesh,24,29,34,29);Line(mesh,29,24,29,34);
            }
            else
            {
                Line(mesh,5,22,47,22);Line(mesh,47,22,47,34);Line(mesh,47,34,5,34);Line(mesh,5,34,5,22);
                Line(mesh,47,26,57,26);Line(mesh,13,34,8,49);Line(mesh,8,49,18,49);Line(mesh,18,49,25,34);
                Line(mesh,29,34,29,43);Line(mesh,29,43,37,43);Line(mesh,37,43,37,34);Line(mesh,15,17,33,17);Line(mesh,15,17,15,22);
            }
        }
        /// <summary>图标坐标与预览统一为60单位，从左上向下；线宽随矩形缩放。</summary>
        private void Line(VertexHelper mesh,float x1,float y1,float x2,float y2)
        {
            var rect=GetPixelAdjustedRect();var a=new Vector2(rect.xMin+x1/60*rect.width,rect.yMax-y1/60*rect.height);
            var b=new Vector2(rect.xMin+x2/60*rect.width,rect.yMax-y2/60*rect.height);
            var normal=new Vector2(-(b-a).y,(b-a).x).normalized*Mathf.Min(rect.width,rect.height)*.022f;int start=mesh.currentVertCount;
            mesh.AddVert(a-normal,color,Vector2.zero);mesh.AddVert(a+normal,color,Vector2.zero);mesh.AddVert(b+normal,color,Vector2.zero);mesh.AddVert(b-normal,color,Vector2.zero);
            mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
        }
        private void Circle(VertexHelper mesh,float x,float y,float radius)
        {
            for(int i=0;i<20;i++){float a=i*Mathf.PI/10,b=(i+1)*Mathf.PI/10;Line(mesh,x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius,x+Mathf.Cos(b)*radius,y+Mathf.Sin(b)*radius);}
        }
    }
}
