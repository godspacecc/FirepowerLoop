# FirepowerLoop Unity MCP 恢复与本项目入口

本机服务使用本项目Client/Library/mcp-server/win-x64/gamedev-mcp-server.exe与Client/UserSettings/AI-Game-Developer-Config.json，端口21032；不共用另一工程的配置、Library、服务器数据或端口。本次服务已由Unity管理器接管，插件/服务调用超时180000ms。

另发现Codex用户级配置只有node_repl，无Unity入口。本机已新增被Git忽略的`.codex/config.toml`，内容参考本目录config.example.toml；`codex mcp list/get`已实际识别unity_firepowerloop、Streamable HTTP、localhost:21032/mcp、启动和调用超时180秒。

项目级MCP配置及超时字段依据[OpenAI官方MCP文档](https://learn.chatgpt.com/docs/extend/mcp?surface=cli)与[配置参考](https://learn.chatgpt.com/docs/config-file/config-reference)。配置仅在受信任项目加载，未改用户级配置、认证或其他项目。

当前长会话最初没有原生Unity工具清单，因此继续使用已实际验证的`Tools/PickupFeedbackChecks/Invoke-UnityMcp.ps1`直连同一MCP服务完成场景、Console、脚本和限定测试。新项目入口已被CLI读到，不等于当前会话原生工具清单已刷新；该刷新尚未现场确认。没有为刷新入口重启Codex或中断其他会话。

Unity域重载期间连接约45–55秒断开，随后自动握手，属于本次现场观察。等待日志connected/handshake恢复再调用，不将其误判为服务退出，也不重启另一工程Unity。操作前核对Application.dataPath=E:/GitHub/FirepowerLoop/Client/Assets与当前场景，测试声明真实短类/完整方法、明确上限、includePassingTests=true，禁止回退完整套件。
