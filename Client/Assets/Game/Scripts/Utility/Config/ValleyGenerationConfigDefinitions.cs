namespace Game
{
    /// <summary>河谷生成的配置快照，单位为米；只提供规则参数，不保存当局随机状态。</summary>
    public sealed class ValleyGenerationProfile
    {
        public string MapId { get; set; }
        /// <summary>1为走廊，2为开放低地，3为双岸缓坡及自动游泳；旧清单按自身版本恢复。</summary>
        public int GenerationVersion { get; set; }
        public int WorldSizeMeters { get; set; }
        public int ChunkSizeMeters { get; set; }
        public int HeightSampleSpacingMeters { get; set; }
        /// <summary>实际最短可走路径下限，单位米；开放版用于避免入口紧邻营地，不强制长距离绕行。</summary>
        public float MinRouteMeters { get; set; }
        public float MaxRouteMeters { get; set; }
        public int MaxAttempts { get; set; }
        public string KitAddress { get; set; }
        /// <summary>章节敌人清单抽样数量范围，不读取玩家装备；版本1/2忽略此范围。</summary>
        public int MinEnemies { get; set; }
        public int MaxEnemies { get; set; }

        /// <summary>拒绝不支持的采样规模，避免损坏配置造成超量分配或不闭合地表。</summary>
        public bool TryValidate(out string error)
        {
            error = null;
            if (MapId != MapIds.CivilisationValley || (GenerationVersion < 1 || GenerationVersion > 3) || WorldSizeMeters != 1024 ||
                ChunkSizeMeters != 64 || HeightSampleSpacingMeters != 4 || MaxAttempts != 8 ||
                float.IsNaN(MinRouteMeters) || float.IsInfinity(MinRouteMeters) ||
                float.IsNaN(MaxRouteMeters) || float.IsInfinity(MaxRouteMeters) ||
                MinRouteMeters <= 0 || MaxRouteMeters < MinRouteMeters || string.IsNullOrWhiteSpace(KitAddress) ||
                (GenerationVersion==3 && (MinEnemies<0 || MaxEnemies<MinEnemies || MaxEnemies>32)))
            { error = "河谷生成配置无效或采样版本不受支持"; return false; }
            return true;
        }
    }
}
