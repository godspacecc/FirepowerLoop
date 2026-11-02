using System;
using System.Linq;
using UnityEngine;
namespace Game
{
    /// <summary>来自任务权威表的不可变参数；坐标为相对开局点偏移。</summary>
    public sealed class MissionConfiguration
    {
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public int SupplyCount {get;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public int AmbushCount {get;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public float InteractionRadius {get;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        private readonly Vector2[] supplies;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public Vector2 Exit {get;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public Vector2 Supply(int index)=>supplies[index];
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public MissionConfiguration(global::Tables tables)
        {
            var row=tables.TbMissionRule.DataList.Single();
            if(row.SupplyCount!=3||row.AmbushCount<=0||row.InteractionRadius<=0||float.IsNaN(row.InteractionRadius)||float.IsInfinity(row.InteractionRadius))throw new InvalidOperationException("任务配置无效");
            SupplyCount=row.SupplyCount;AmbushCount=row.AmbushCount;InteractionRadius=row.InteractionRadius;
            supplies=new[]{new Vector2(row.Supply1X,row.Supply1Y),new Vector2(row.Supply2X,row.Supply2Y),new Vector2(row.Supply3X,row.Supply3Y)};Exit=new Vector2(row.ExitX,row.ExitY);
            foreach(var point in supplies.Concat(new[]{Exit}))if(float.IsNaN(point.x)||float.IsInfinity(point.x)||float.IsNaN(point.y)||float.IsInfinity(point.y))throw new InvalidOperationException("任务坐标无效");
        }
    }
}
