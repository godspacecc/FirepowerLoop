# PC 安装器与整包更新使用说明

Windows x64 使用当前用户安装器，首次可选择本地磁盘上的独立空目录，默认 `%LOCALAPPDATA%\Programs\FirepowerLoop`；所选目录必须对当前用户可写。固定 AppId 保持同一卸载入口，后续升级自动沿用已注册目录，拒绝通过 `/DIR` 把升级变成迁移。旧固定目录安装也能被识别。需要迁移时先卸载再安装到新目录，LocalLow 存档仍保留。安装和卸载均不清理存档。

## 首次发布

1. 安装 Inno Setup 6.3 或更新版本。编译器路径存入设备 EditorPrefs；构建前自动检查，未配置时选择一次 `ISCC.exe`，批处理缺配置直接失败。
2. 用户手动执行 `Tools/打包/PC/最新代码资源完整打包`。首次引入更新功能或修改 Boot 必须生成新的 Player。四个 Player 入口（完整、首包、最近 Host、复用 AOT 快速）都在 Player 校验后自动生成安装器、同步 PC 资源服并发布安装器清单；快速入口仍只适用于临时验证。
3. 安装器和清单输出到仓库 `Release/WindowsInstaller/Installer_时间戳_随机批次`，并发布到最近的 PC 资源服目录 `AppUpdates/Windows`。失败保留 Player 和安装器批次，成功不额外消耗发布序号。仅资源/Host 更新不生成安装器。
4. 使用新生成的安装器首次安装，选择目录。已安装用户升级自动沿用原目录；旧散装包玩家需要手动安装一次。

已有 Player 仍可通过 `Tools/打包/PC/辅助工具/为已有Player生成当前用户安装器` 单独转换。这个手动入口不自动发布；按下文脚本发布。`Tools/服务器/PC/启动资源服` 优先启动已发布的完整资源服，避免遗漏 PC 安装包路由；资源版本不一致时先更新资源服。已有运行中的完整资源服读取原子切换后的更新清单，无需因每次出安装器重启。

也可在仓库根目录手动调用转换脚本，输出目录必须尚不存在且位于 Player 目录之外：

```powershell
& .\Tools\WindowsInstaller\build_installer.ps1 `
  -PlayerDirectory 'D:\Packages\FirepowerLoop' `
  -Compiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' `
  -OutputDirectory 'D:\Packages\Installer_001'
```

转换工具不调用 Unity。它要求 `FirepowerLoop.exe`、`UnityPlayer.dll`、`GameAssembly.dll`、有效热更程序集注册及内置 PC 身份，排除运行时 `FirepowerLoop_Data\yoo` 缓存、调试资料和额外顶层用户文件。不要在构建输出中手工替换 DLL 或发布身份后继续生成安装器。

## 版本与后续更新

`Client/Assets/Editor/Build/windows-release-version.json` 是共享 PC 版本台账。发布前可调整 `versionName`（三段数字），不要降低 `releaseNumber`。每次正式 PC Player 成功构建并通过程序集及符号分离检查后，序号递增一次；构建失败恢复原台账及 StreamingAssets 身份。准备 Player、仅热更资源、转换安装器不会消耗新序号。成功构建后应把台账变化同步到版本库，跨机器出包必须使用最新台账。

正式构建把身份写到 `FirepowerLoop_Data\StreamingAssets\windows-release.json`；安装器版本只读取这个身份。显示版本与 Android `Application.version`、Android code 独立。完整安装器包含所有托管程序文件，因此可从序号 1 直接更新到序号 10，更新后的 Boot 继续比较序号 11、12 等。

版本台账与源 StreamingAssets 身份在准备、提交和回滚时均使用同目录临时文件原子替换，不原地截断 Unity 可能仍在映射的 TextAsset，`.meta` 与 GUID 保留。准备失败且恢复失败时同时保留原始异常和恢复异常。2026-10-08 的 Win32 1224 回归已在临时内存映射文件中复现，修复后 `WindowsReleaseIdentityTests` 6 项通过；项目真实资产准备后回滚也验证了原字节和 GUID 恢复，没有执行 Player 构建。

PC 启动在加载 Game.dll 前检查新版；开始界面主动“检查更新”也可提示新版，确认后重启进入 Boot 下载。可稍后更新或取消下载。下载存入 LocalLow 的 `Updates\Windows`，采用临时文件、长度和 SHA256 校验；复用缓存及交接前重新校验。启动安装器成功后游戏立即停止启动并退出；进程启动失败保留游戏并允许重试。

启动安装器使用 Win32 `CreateProcessW`，当前游戏 PID 与创建时间使用 `GetCurrentProcessId` / `GetProcessTimes` 获取。不能使用 `System.Diagnostics.Process`：本项目 Unity 2022.3 的 IL2CPP 未实现其进程创建和部分进程信息查询，编辑器 Mono 测试无法证明该 API 在 Player 中可用。

2026-10-08 首次实机升级发现此限制：序号 1 已下载序号 2，但安装交接抛出 `NotSupportedException`，两者都含旧启动实现。修复位于 Boot，必须手动生成新的 PC Player 并转换安装器；已安装的序号 1、2 需要手动安装修正版一次。之后再以修正版为升级起点验证自动更新，不能用原序号 1、2 验收修复。

安装器通过 PID 和创建时间等待发起游戏退出，不按名称强杀进程。其他实例占用文件时由安装向导处理，请关闭实例再继续。向导会备份旧托管文件、记录恢复日志，全部新文件 SHA256 验证后才提交并删除旧版本已移除文件；普通失败尝试恢复，异常中断后再次运行安装器先恢复旧程序。注册表和快捷方式不属于文件事务，失败后可用同一 AppId 安装器修复注册信息。不要手动删除 `.update-backup`，不要在安装目录中使用 junction 或符号链接。

安装完成首次启动时，Boot 使旧宿主资源缓存及 `FirepowerLoop.LastSuccessfulHostVersion` 标记一起失效，然后按新宿主重新初始化资源。缓存删除失败会终止本轮启动，不混用旧宿主资源；存档不受影响。

## 发布到资源服务器

使用当前 PC 发布包中的新 `resource_server.ps1`。旧资源服包需要通过现有“更新资源服”流程更新支持脚本，或手动同步新脚本后重新启动服务。已有运行中的 PowerShell 服务不会自动加载源码变化。

把转换工具生成的安装器和同目录清单发布到 PC 资源服包：

```powershell
& .\Server\ResourceServer\publish_windows_update.ps1 `
  -Installer 'D:\Packages\Installer_001\FirepowerLoop-Setup-1.exe' `
  -Manifest 'D:\Packages\Installer_001\app-update-windows.json' `
  -Bundle 'D:\ResourceServerBundle_20261008_120000'
```

文件存放于 `AppUpdates\Windows`。客户端访问 `/app-update-windows.json` 与 `/app-updates/windows/单一文件名.exe`，支持 GET 和 HEAD，安装器不允许外部地址、嵌套路径或非 EXE 文件。旧安装器保留，复制并校验完成后原子切换最新清单；拒绝发布降级及同序号不同字节。不要手工修改清单或覆盖已发布 EXE。

安卓仍使用 `/app-update.json` 与 `AppUpdates\Android`。PC 与安卓 AssetBundle 必须分别发布。当前用户可以选择稍后更新，所以资源服仍需保留对旧宿主的兼容性；不要把只兼容新宿主的热更 DLL 直接推给仍在线的旧宿主。本功能尚未实现按宿主分流或最低宿主版本门禁。

当前流程沿用可信局域网资源服模型；长度和哈希证明传输完整性，不证明发布者身份。公开分发前需另行部署 HTTPS、可信发布源及代码签名。

## 验证记录与人工验收

自动验证只使用明确的 Unity 测试类/方法及临时目录服务器，不执行 Unity 构建、真实安装或真实资源服发布。

自定义目录及一键发布接入通过 `BuildWorkflowTests` 24 项 EditMode 测试，包含四个 Player 入口的接入检查、发布序号/哈希拒绝及原有构建流程回归；`test_installer_input.ps1 -Compiler <ISCC.exe>` 在临时输入目录编译了新安装器模板，没有运行安装器。真实目录选择、跨盘安装、原目录升级及一键构建全链路仍需用户出包验收。

- PC 清单：身份、平台、架构、跨版本、降级、文件名和缓存篡改。
- PC HTTP：404/无效/超大清单、取消、有效与无效下载、临时文件清理、按钮恢复。
- 启动交接：退出前不加载资源程序集或 Main。
- PC 发布身份事务：失败恢复旧身份，成功只递增一次。
- PowerShell：安装器输入筛选、PC 发布与真实 GET/HEAD、目录隔离、不可变文件、降级拒绝及安卓回归。

当前设备已安装 Inno Setup 6.7.3，序号 1、2 的安装器已真实编译成功，序号 2 已发布到本机测试资源服，HTTP 下载长度及 SHA256 一致。原生进程交接修复通过 `WindowsInstallerUpdateTests` 共 15 项 EditMode 测试，其中验证了当前进程 PID/创建时间和启动无安装行为的 Windows 程序；这不等同于修复后 IL2CPP Player 验收。尚需用户手动出包并验证：首装 → N+1 更新 → 再次 N+2 更新；跨版本更新；安装前取消；下载取消后重试；文件占用/空间不足；安装期间取消和异常中断后的恢复；存档保留；已移除 DLL 清理；卸载不删除存档。**这些验收未完成前不要作为已验证的正式外发安装器发布。**
