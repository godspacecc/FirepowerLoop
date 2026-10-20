# FirepowerLoop 本地修复

本包基于 OpenUPM 发布的 AI Game Developer — MCP 1.2.0，保留原许可证和资源 meta。
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

2026-10-20：从 0.93.2 升级到 OpenUPM 1.2.0。对照两个官方发布包，
本项目仅有 LinkExtractor 修复及本说明；1.2.0 的 LinkExtractor 与旧版相同，
仍会调用 Client.List() 并无限等待，因此继续保留修复及嵌入方式。
manifest.json 的依赖版本同步为 1.2.0；后续升级仍需先核对并迁移本修复，
不能直接使用插件更新按钮覆盖嵌入包。

本次验证：3 项独立回归检查通过；Unity EditMode 短类
`McpBuildPreprocessorTests` 仅 1 项通过，`includePassingTests=true`，未执行 Player Build。
Unity 已实际加载 1.2.0，脚本编译无失败，Boot 场景未变脏。
现有 42 个 NuGet DLL 的 meta GUID 保持不变；新增 RQL 依赖由插件的 NuGet 解析器生成。
配套 GameDev-MCP-Server 从 9.2.9 更新至插件指定的 9.2.11，官方 ZIP 已按发布的
SHA256SUMS 校验，原项目端口 21032 保持不变，Unity 管理器确认 Running。
升级时清理了占用旧可执行文件的本项目遗留服务进程；没有改动其他工程。

1.2.0 使用 `rql-query` 查询场景与 Console，替代旧查询工具：

```text
scenes | select name path isDirty rootCount
console | where logType == "Error" | sort sequence desc | first 5 | select message timestamp
```
