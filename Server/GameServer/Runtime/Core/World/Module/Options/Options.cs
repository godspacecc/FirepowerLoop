using CommandLine;
using System;
using System.Collections.Generic;

namespace ET
{
    public enum AppType
    {
        Server,
        Watcher, // 每台物理机一个守护进程，用来启动该物理机上的所有进程
        GameTool,
        Proto2CS,
        BenchmarkClient,
        BenchmarkServer,
        OnlineTest,
        
        Demo,
        LockStep,
    }
    
    public class Options: Singleton<Options>
    {
        [Option("AppType", Required = false, Default = AppType.Server, HelpText = "AppType enum")]
        public AppType AppType { get; set; }

        [Option("StartConfig", Required = false, Default = "Localhost")]
        public string StartConfig { get; set; }

        [Option("Process", Required = false, Default = 1)]
        public int Process { get; set; }
        
        [Option("Develop", Required = false, Default = 0, HelpText = "develop mode, 0正式 1开发 2压测")]
        public int Develop { get; set; }

        [Option("LogLevel", Required = false, Default = 0)]
        public int LogLevel { get; set; }
        
        [Option("Console", Required = false, Default = 0)]
        public int Console { get; set; }
        
        /// <summary>隔离联调只监听回环，默认沿用场景配置；不改变生产端口。</summary>
        [Option("OnlineLoopbackOnly", Required = false, Default = false)]
        public bool OnlineLoopbackOnly { get; set; }

        [Option("OnlineStore", Required = false, Default = "../Store/Online/identity-store.json")]
        public string OnlineStore { get; set; }

        /// <summary>空值使用运行程序集所在 Bin 的上一级 Store/Diagnostics；显式覆盖仅供隔离测试实例。</summary>
        [Option("DiagnosticStoreRoot", Required = false, Default = "")]
        public string DiagnosticStoreRoot { get; set; }

        /// <summary>仅当 TCP 来源等于此 IP 时信任代理覆盖的 X-Real-IP；空值表示直连模式。</summary>
        [Option("DiagnosticTrustedProxyIp", Required = false, Default = "")]
        public string DiagnosticTrustedProxyIp { get; set; }

        [Option("OnlineArcadeStore", Required = false, Default = "../Store/Online/arcade-runs.json")]
        public string OnlineArcadeStore { get; set; }
        
        [Option("OnlineTestAddress", Required = false, Default = "127.0.0.1:31001")]
        public string OnlineTestAddress { get; set; }
        
        [Option("OnlineTestMode", Required = false, Default = "Primary")]
        public string OnlineTestMode { get; set; }
        
        [Option("OnlineTestUser", Required = false, Default = "")]
        public string OnlineTestUser { get; set; }
        
        [Option("OnlineTestExpectedAccountId", Required = false, Default = 0L)]
        public long OnlineTestExpectedAccountId { get; set; }
        
        [Option("OnlineTestExpectedPlayerId", Required = false, Default = 0L)]
        public long OnlineTestExpectedPlayerId { get; set; }

        [Option("OnlinePlayerGraceMilliseconds", Required = false, Default = 60000)]
        public int OnlinePlayerGraceMilliseconds { get; set; }

        [Option("OnlineRoomFailCloseCount", Required = false, Default = 0)]
        public int OnlineRoomFailCloseCount { get; set; }

        [Option("OnlineRoomFailLeaveCount", Required = false, Default = 0)]
        public int OnlineRoomFailLeaveCount { get; set; }

        [Option("OnlineRoomFailConnectionSyncCount", Required = false, Default = 0)]
        public int OnlineRoomFailConnectionSyncCount { get; set; }

        [Option("OnlineRoomFailGetCurrentCount", Required = false, Default = 0)]
        public int OnlineRoomFailGetCurrentCount { get; set; }

        [Option("OnlineChatFailSyncCount", Required = false, Default = 0)]
        public int OnlineChatFailSyncCount { get; set; }

        [Option("OnlineChatFailCloseCount", Required = false, Default = 0)]
        public int OnlineChatFailCloseCount { get; set; }

        [Option("OnlineArcadeFailSaveCount", Required = false, Default = 0)]
        public int OnlineArcadeFailSaveCount { get; set; }

    }
}
