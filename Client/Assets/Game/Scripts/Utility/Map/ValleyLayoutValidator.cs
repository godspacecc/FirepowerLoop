using System;
using System.Linq;
using UnityEngine;

namespace Game
{
    /// <summary>验证生成清单的数据身份与有界几何；真实导航连通性由候选地图构建阶段另行验证。</summary>
    public static class ValleyLayoutValidator
    {
        public static bool TryValidate(ValleyLayoutSaveData layout, ValleyGenerationProfile profile, CivilisationCatalog templates, out string error)
        {
            error = null;
            if (layout == null || profile == null || !profile.TryValidate(out error) || templates == null)
            { error ??= "河谷布局依赖缺失"; return false; }
            if (!TryValidateData(layout, out error)) return false;
            // 已保存的首版布局恢复原地形；新配置只约束新局，不把旧局重新抽成另一张地图。
            if (layout.GenerationVersion > profile.GenerationVersion || layout.NominalRouteMeters < profile.MinRouteMeters || layout.NominalRouteMeters > profile.MaxRouteMeters ||
                layout.Points.Any(p => !templates.Points.Any(t => t.Id == p.TemplateId && t.MapId == layout.MapId)))
            { error = "河谷布局版本、路线或模板引用无效"; return false; }
            return true;
        }
        /// <summary>存档边界校验：先限制集合规模，再访问元素；未知版本不能自动重新生成。</summary>
        public static bool TryValidateData(ValleyLayoutSaveData l, out string error)
        {
            error = "河谷布局数据无效";
            if (l == null || l.GenerationVersion < 1 || l.GenerationVersion > 3 || l.MapId != MapIds.CivilisationValley || l.WorldSizeMeters != 1024 || l.HeightSampleSpacingMeters != 4 || l.AttemptIndex < 0 || l.AttemptIndex >= 8 ||
                l.Heights == null || l.Heights.Length != 257*257 || l.Heights.Any(h => !Finite(h) || h < -50 || h > 250) ||
                !ValidPath(l.Route) || !ValidPath(l.AlternateRoute) || !ValidPath(l.River) || !ValidPath(l.Shortcut) ||
                l.Fords == null || l.Fords.Count == 0 || l.Fords.Count > 32 || l.Fords.Any(p => !ValidPosition(p)) ||
                !ValidPosition(l.Spawn) || !ValidPosition(l.Camp) || !Finite(l.NominalRouteMeters) || l.NominalRouteMeters < (l.GenerationVersion == 3 ? 100 : 1800) || l.NominalRouteMeters > 2400 ||
                l.Points == null || l.Points.Count < 3 || l.Points.Count > 8192 || l.Points.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.TemplateId) || !ValidPosition(p.Pose)) || l.Points.Select(p => p.Id).Distinct().Count() != l.Points.Count ||
                l.Clues == null || l.Clues.Count < 4 || l.Clues.Count > 32 || l.Clues.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.MessageKey) || c.Chain < 0 || c.Chain > 1 || !ValidPosition(c.Pose) || !ValidPosition(c.Target)) || l.Clues.Select(c => c.Id).Distinct().Count() != l.Clues.Count || l.Clues.Select(c => c.Chain).Distinct().Count() != 2 ||
                l.Edges == null || l.Edges.Count < 2 || l.Edges.Count > 128 || l.Edges.Any(e => e == null || string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.From) || string.IsNullOrWhiteSpace(e.To)) || string.IsNullOrWhiteSpace(l.ShortcutId)) return false;
            var regions=Enumerable.Range(0,l.Route.Count).Select(i=>"region."+i).ToArray();
            if(l.Edges.Any(e=>e.From==e.To||!regions.Contains(e.From)||!regions.Contains(e.To))||l.Edges.Select(e=>e.Id).Distinct().Count()!=l.Edges.Count||
                l.Points.Any(p=>!regions.Contains(p.RegionId))||l.Clues.Any(c=>c.Order<0||c.Order>1||
                (c.MessageKey!="civilisation.clue.upstream"&&c.MessageKey!="civilisation.clue.camp"))||
                l.Clues.GroupBy(c=>c.Chain).Any(g=>g.Count()!=2||g.Select(c=>c.Order).Distinct().Count()!=2)||
                l.Points.Select(p=>p.Id).Concat(l.Clues.Select(c=>c.Id)).Append(l.ShortcutId).Distinct().Count()!=l.Points.Count+l.Clues.Count+1)return false;
            if (l.GenerationVersion == 3 && (!ValidPosition(l.Extraction) || l.Fords.Count < 2 || l.Fords.Count > 3 ||
                l.BranchRoads == null || l.BranchRoads.Count < 2 || l.BranchRoads.Count > 16 || l.BranchRoads.Any(p => !ValidPath(p)) ||
                l.EnemyCount<0 || l.EnemyCount>32 || l.Enemies == null || l.Enemies.Count > 32 || (l.EnemySpawnsPrepared && l.EnemyCount!=l.Enemies.Count) || l.Enemies.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.TemplateId) || !ValidPosition(p.Pose)) ||
                l.Enemies.Select(p=>p.Id).Distinct().Count()!=l.Enemies.Count)) return false;
            if(l.GenerationVersion==3)
            {
                // 两个实际地形三角形都限制在30度以内；河床也受约束，不能把困人凹地当成正常水域。
                var limit=Mathf.Tan(30*Mathf.Deg2Rad);var squared=limit*limit;
                for(int z=0;z<256;z++)for(int x=0;x<256;x++)
                {
                    var a=l.Heights[z*257+x];var b=l.Heights[z*257+x+1];var c=l.Heights[(z+1)*257+x];var d=l.Heights[(z+1)*257+x+1];
                    if((b-a)*(b-a)+(c-a)*(c-a)>squared*16 || (d-c)*(d-c)+(d-b)*(d-b)>squared*16)
                    {error="河谷保存地形含超过30度的落差";return false;}
                }
            }
            error = null; return true;
        }
        /// <summary>读档先校验探索分区和布局的稳定身份；缺失新分区按空状态兼容。</summary>
        public static bool TryValidateExploration(ValleyLayoutSaveData layout,Newtonsoft.Json.Linq.JToken state,out string error)
        {
            error=null;
            if(!TryValidateData(layout,out error)||!new CivilisationExpeditionModel().Validate(state,out error))return false;
            if(state==null)return true;
            bool Matches(string key,System.Collections.Generic.IEnumerable<string> allowed)
            {
                var values=state[key] as Newtonsoft.Json.Linq.JArray;
                return values==null||values.All(v=>allowed.Contains((string)v));
            }
            if(!Matches("visited",layout.Points.Select(p=>p.Id))||!Matches("clues",layout.Clues.Select(c=>c.Id))||!Matches("shortcuts",new[]{layout.ShortcutId}))
            {error="探索记录与保存的河谷身份不匹配";return false;}
            return true;
        }
        private static bool ValidPath(System.Collections.Generic.List<ValleyPosition> path) => path != null && path.Count >= 2 && path.Count <= 128 && path.All(ValidPosition);
        private static bool ValidPosition(ValleyPosition p) => p != null && Finite(p.X) && Finite(p.Y) && Finite(p.Z) && Finite(p.Yaw) && Mathf.Abs(p.X) <= 512 && Mathf.Abs(p.Z) <= 512 && p.Y >= -50 && p.Y <= 250;
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
