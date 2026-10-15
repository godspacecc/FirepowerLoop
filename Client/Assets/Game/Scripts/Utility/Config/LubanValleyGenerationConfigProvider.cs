using System;
using System.Linq;

namespace Game
{
    public sealed partial class LubanRuntimeConfigProvider
    {
        /// <summary>从正式客户端表转换生成参数，不以领域默认值补救缺失或错误配置。</summary>
        public bool TryGetValleyGenerationProfile(string mapId, out ValleyGenerationProfile value, out string error)
        {
            value = null;
            if (!EnsureValid(out error)) return false;
            var row = Tables.TbCivilisationValleyGeneration.GetOrDefault(mapId);
            if (row == null) { error = "河谷生成配置不存在：" + mapId; return false; }
            var candidate = new ValleyGenerationProfile { MapId = row.MapId, GenerationVersion = row.GenerationVersion,
                WorldSizeMeters = row.WorldSizeMeters, ChunkSizeMeters = row.ChunkSizeMeters,
                HeightSampleSpacingMeters = row.HeightSampleSpacingMeters, MinRouteMeters = row.MinRouteMeters,
                MaxRouteMeters = row.MaxRouteMeters, MaxAttempts = row.MaxAttempts, KitAddress = row.KitAddress,
                MinEnemies=row.MinEnemies,MaxEnemies=row.MaxEnemies };
            if (!candidate.TryValidate(out error)) return false;
            if(candidate.GenerationVersion==3 && candidate.MaxEnemies>Tables.TbMapSpawn.DataList.Count(s=>s.MapId==mapId && s.ObjectType==MapSpawnObjectType.Enemy))
            {error="河谷章节敌人数量超出有效配置清单";return false;}
            value = candidate; return true;
        }
    }
}
