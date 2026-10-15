using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    /// <summary>将首章敌人配置偏移转换为当局遗物附近的位置；不创建实体，不修改已保存敌人快照。</summary>
    public static class ValleyChapterSpawns
    {
        /// <summary>配置 ID 使用“搜索模板 ID.guard”；旧布局缺少该遗物时不新增敌人，其余出生点保持原样。</summary>
        public static IReadOnlyList<MapSpawnDefinition> Resolve(ValleyLayoutSaveData layout, IReadOnlyList<MapSpawnDefinition> spawns)
        {
            if (layout == null) return spawns;
            if(layout.GenerationVersion==3)
            {
                if(!layout.EnemySpawnsPrepared)
                {
                    // 只在新候选中建立初始清单；既有世界快照由EnemyCtrl恢复死亡与位置，不补刷。
                    var random=new Random(unchecked(layout.Seed*397+layout.AttemptIndex));
                    foreach(var enemy in spawns.Where(s=>s.ObjectType==MapSpawnObjectType.Enemy && s.MapId==layout.MapId).OrderBy(_=>random.Next()).Take(layout.EnemyCount))
                    {
                        var template=enemy.Id.EndsWith(".guard",StringComparison.Ordinal)?enemy.Id.Substring(0,enemy.Id.Length-6):null;
                        var anchor=layout.Points.FirstOrDefault(p=>p.TemplateId==template);
                        // 可选遗物本局未出现时，敌人仍可在其它既有探索现场活动，不补造被省略的遗物。
                        if(anchor==null)
                        {
                            var sites=layout.Points.Where(p=>p.TemplateId.StartsWith("han.",StringComparison.Ordinal)).ToArray();
                            if(sites.Length==0)continue;anchor=sites[random.Next(sites.Length)];
                        }
                        var p=anchor.Pose.ToVector();p.x+=(p.x<0?-1:1)*(10+random.Next(8));p.z+=random.Next(-8,9);
                        p.y=layout.HeightAt(p.x,p.z)+.1f;
                        layout.Enemies.Add(new ValleyPointData{Id=enemy.Id,TemplateId=enemy.ConfigId,RegionId=anchor.RegionId,Pose=ValleyPosition.From(p)});
                    }
                    layout.EnemyCount=layout.Enemies.Count;layout.EnemySpawnsPrepared=true;
                }
                var resolved=spawns.Where(s=>s.ObjectType!=MapSpawnObjectType.Enemy && s.ObjectType!=MapSpawnObjectType.Extraction).ToList();
                foreach(var e in layout.Enemies)
                    resolved.Add(new MapSpawnDefinition(e.Id,layout.MapId,MapSpawnObjectType.Enemy,e.TemplateId,e.Pose.X,e.Pose.Y,e.Pose.Z,e.Pose.Yaw));
                var exit=layout.Extraction;
                resolved.Add(new MapSpawnDefinition("extraction.valley.main",layout.MapId,MapSpawnObjectType.Extraction,"extraction.valley.main",exit.X,exit.Y,exit.Z,exit.Yaw));
                return resolved;
            }
            var result = new List<MapSpawnDefinition>();
            foreach (var spawn in spawns)
            {
                if (spawn.MapId != MapIds.CivilisationValley || spawn.ObjectType != MapSpawnObjectType.Enemy || !spawn.Id.EndsWith(".guard", StringComparison.Ordinal))
                { result.Add(spawn); continue; }
                var templateId = spawn.Id.Substring(0, spawn.Id.Length - ".guard".Length);
                var anchor = layout.Points.FirstOrDefault(p => p.TemplateId == templateId);
                if (anchor == null) continue;
                var position = anchor.Pose.ToVector() + spawn.LocalPosition;
                position.y = layout.HeightAt(position.x, position.z) + spawn.LocalPosition.y;
                result.Add(new MapSpawnDefinition(spawn.Id, spawn.MapId, spawn.ObjectType, spawn.ConfigId,
                    position.x, position.y, position.z, spawn.LocalRotation.eulerAngles.y));
            }
            return result;
        }
    }
}
