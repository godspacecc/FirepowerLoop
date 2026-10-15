# FirepowerLoop 本地修复

本包嵌入自项目已安装的 AI Game Developer — MCP 0.93.2，保留原许可证和资源 meta。
Unity 使用此嵌入包，不再使用 Library/PackageCache 中的注册表副本。

2026-10-15：修复 `Editor/Scripts/Utils/LinkExtractor.cs` 在构建预处理期间调用
`Client.List()` 并通过 `Thread.Sleep(10)` 无限等待的问题。
改用 Unity 2022.3 支持的 `PackageInfo.GetAllRegisteredPackages()` 读取已加载包；
link.xml 的收集、去重、合并和构建后清理规则保持原样。

定向回归检查（不启动 Unity，不执行 Player Build）：

```powershell
./Tools/McpBuildChecks/check_mcp_build.ps1 -SourcePath Client/Packages/com.ivanmurzak.unity.mcp/Editor/Scripts/Utils/LinkExtractor.cs -TestClass McpLinkExtractorRegressionTests -MaxTests 3 -IncludePassingTests
```

升级插件时需复查上游是否已移除此等待循环，再决定是否移除嵌入包。
