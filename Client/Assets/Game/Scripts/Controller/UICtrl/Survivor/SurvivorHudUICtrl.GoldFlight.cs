using System.Collections.Generic;
using QFramework;
using UnityEngine;
namespace Game
{
    public sealed partial class SurvivorHudUICtrl
    {
        /// <summary>沿用闯关有界飞行与到账轻跳节奏；超限合并视觉增量。</summary>
        private const int GoldFlightLimit=16;
        private const float GoldFlightSeconds=.55f,GoldPulseSeconds=.3f;
        /// <summary>仅保存未到达的表现增量，不拥有真实金币。</summary>
        private readonly List<GoldFlight> goldFlights=new(GoldFlightLimit);
        private float goldPulseAge=GoldPulseSeconds;
        /// <summary>恢复Prefab的文字尺寸，并检测新局数值回退。</summary>
        private Vector3 goldTextScale=Vector3.one;
        private long lastAuthoritativeGold;
        /// <summary>每份飞行的起点、增量与表现进度。</summary>
        private sealed class GoldFlight
        {
            public Vector2 Start;
            public long Amount;
            public float Age;
        }
        /// <summary>只接收成功保存后的拾取通知；镜头外拾取从最近可见边缘飞入。</summary>
        public bool TryPlayGoldPickup(long amount,Vector2 screenPosition)
        {
            if(!IsShow()||UI==null||amount<=0||float.IsNaN(screenPosition.x)||float.IsNaN(screenPosition.y)||
                float.IsInfinity(screenPosition.x)||float.IsInfinity(screenPosition.y))return false;
            var rect=(RectTransform)UI.GoldFlightList.transform;
            var canvas=rect.GetComponentInParent<Canvas>().rootCanvas;
            var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,screenPosition,camera,out var start))return false;
            start.x=Mathf.Clamp(start.x,rect.rect.xMin+24,rect.rect.xMax-24);
            start.y=Mathf.Clamp(start.y,rect.rect.yMin+24,rect.rect.yMax-24);
            if(goldFlights.Count==GoldFlightLimit){
                var last=goldFlights[goldFlights.Count-1];
                if(last.Amount>long.MaxValue-amount)return false;
                last.Amount+=amount;
            }else goldFlights.Add(new GoldFlight{Start=start,Amount=amount});
            RefreshGoldFlights();RefreshGoldNumber();return true;
        }
        /// <summary>由HUD已有可见期回调驱动，金币已结算，暂停不阻塞到账表现。</summary>
        private void AdvanceGoldPickups(float seconds)
        {
            if(!IsShow()||UI==null||(goldFlights.Count==0&&goldPulseAge>=GoldPulseSeconds))return;
            seconds=Mathf.Max(0,seconds);float arrivedAge=float.PositiveInfinity;
            for(int i=goldFlights.Count-1;i>=0;i--){
                var flight=goldFlights[i];flight.Age+=seconds;
                if(flight.Age>=GoldFlightSeconds){arrivedAge=Mathf.Min(arrivedAge,flight.Age-GoldFlightSeconds);goldFlights.RemoveAt(i);}
            }
            goldPulseAge=float.IsPositiveInfinity(arrivedAge)?goldPulseAge+seconds:arrivedAge;
            RefreshGoldFlights();RefreshGoldNumber();
            UI.ScoreText.transform.localScale=goldTextScale*(1+.22f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(goldPulseAge/GoldPulseSeconds)));
        }
        /// <summary>列表组件持有并复用外部Box，控制器只提供数量与轨迹。</summary>
        private void RefreshGoldFlights(){UI.GoldFlightList.totalCount=goldFlights.Count;UI.GoldFlightList.RefreshCells();}
        /// <summary>每次复用完整重设位移、旋转、缩放、透明度，实时跟随金币栏落点。</summary>
        private void RefreshGoldFlight(Transform transform,int index)
        {
            var box=transform.GetComponent<ArcadeScoreFlightBoxUI>();var flight=goldFlights[index];
            var target=UI.ScoreText.rectTransform;
            var end=(Vector2)UI.GoldFlightList.transform.InverseTransformPoint(target.TransformPoint(target.rect.center));
            float t=Mathf.Clamp01(flight.Age/GoldFlightSeconds);
            var point=Vector2.Lerp(flight.Start,end,t*t*(3-2*t));
            point.y+=Mathf.Sin(Mathf.PI*t)*Mathf.Min(90,Vector2.Distance(flight.Start,end)*.2f);
            box.Motion.anchoredPosition=point;box.Motion.localRotation=Quaternion.Euler(0,0,-20+200*t);
            float size=Mathf.Lerp(1.15f,.45f,t*t);
            box.Motion.localScale=new Vector3(size*(.8f+.2f*Mathf.Abs(Mathf.Cos(t*Mathf.PI*2))),size,1);
            box.Opacity.alpha=1-Mathf.Clamp01((t-.9f)*10);
        }
        /// <summary>只暂扣飞行中的显示增量；真实余额不变，回退取消旧局动画。</summary>
        private void RefreshGoldNumber()
        {
            long gold=this.GetModel<SurvivorModel>().CollectedGold;
            if(gold<lastAuthoritativeGold)ClearGoldPickupFeedback();
            lastAuthoritativeGold=gold;
            foreach(var flight in goldFlights)gold-=System.Math.Min(gold,flight.Amount);
            UI.ScoreText.text=Loc.T("ui.survivor.hud.score",gold.ToString("N0"));
        }
        /// <summary>隐藏、卸载和地图结束清空视觉队列与尺寸，不重播历史领取。</summary>
        public void ClearGoldPickupFeedback()
        {
            goldFlights.Clear();goldPulseAge=GoldPulseSeconds;lastAuthoritativeGold=0;
            if(UI==null)return;
            UI.GoldFlightList.ClearCells();UI.ScoreText.transform.localScale=goldTextScale;
        }
    }
}
