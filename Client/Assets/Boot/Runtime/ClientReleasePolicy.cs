namespace Boot
{
    /// <summary>由稳定 Boot 程序集持有发布模式；热更代码读取属性，不能把编译常量内联为另一种模式。</summary>
    public static class ClientReleasePolicy
    {
#if UNITY_INCLUDE_TESTS
        /// <summary>仅供限定测试覆盖模式；测试结束必须恢复为 null，不作为玩家配置或持久数据。</summary>
        public static bool? OfflineOverrideForTests { get; set; }
#endif
        /// <summary>纯离线首发只能由本次 Player 的显式构建标记启用，内部和联网候选流程保持原模式。</summary>
        public static bool OfflineSinglePlayer
        {
            get
            {
#if UNITY_INCLUDE_TESTS
                if (OfflineOverrideForTests.HasValue) return OfflineOverrideForTests.Value;
#endif
#if FIREPOWERLOOP_OFFLINE_SINGLE_PLAYER
                return true;
#else
                return false;
#endif
            }
        }
    }
}
