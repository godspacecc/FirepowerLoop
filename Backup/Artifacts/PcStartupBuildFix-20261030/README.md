# PC启动包修改记录（待Unity验证）

> 后续授权（2026-10-30）：用户明确要求“提交推送”，本会话打包改动纳入本次提交；替代下文历史记录中的暂不提交安排。Unity测试继续暂缓，提交不代表编译、测试或真实出包通过。提交前对18个相关C#文件进行独立C#9语法解析，0个语法错误；这不是Unity程序集编译。证据见 `Backup/Artifacts/QuickBuildUnification-20261030/commit-syntax-check.json`。

用户确认三项：菜单改为“启动包（首次下载资源）”、删除额外预备Player构建、校验实际导出的启动包及完整远程资源。随后明确“先保留运行，暂不测试”。因此不结束Play Mode、不主动刷新或编译、不运行Unity测试、不打包；未验证前不提交、不推送。

## 已写入代码

- BuildUtility仅调整第二项菜单/确认框；保留内部工作流身份以兼容跨重载恢复。以播放器设置替代额外预备构建，保留StripAOT自身必要构建；取消该入口的预备目录清理。
- PcBuiltinResourceValidator新增启动包模式：完整远程资源逐Bundle检查长度与MD5；启动包清单与远程版本/清单/hash逐字节一致；真实解析二进制catalog并要求空登记。冻结后检查实际Player文件，拒绝误带资源（包括子目录），同时复核远程源没有变化。
- 校验放在保存成功状态和生成安装器之前。完整包原有Capture入口保留，需回归。
- 修改已有菜单/流程合同测试，并在PcBuiltinResourceTests中增加真实YooAsset序列化器及临时目录用例。

## 尚未验证

测试已编写，但尚未执行RED或GREEN；没有Unity编译通过或实际出包成功结论。

允许测试后按下列精确范围运行，均includePassingTests=true，禁止完整套件或失败后扩大过滤：

- PcBuiltinResourceTests：最多26项（原完整包12项及启动包14项）。
- Game.Tests.BuildWorkflowTests.WindowsSmallHostBaseBuildCreatesManifestOnlyPlayerAndPublishesMatchingResources：最多1项。
- Game.Tests.FoundationFlowTests.PcPackagingMenusAreGrouped：最多1项。
- PcFullBuildFlowTests：最多3项，完整打包入口回归。

代码静态差异检查已通过；不将静态检查替代上述Unity验证。第二节正式使用文档和后续工具未修改。

独立静态审查未发现新增P1/P2，核对了已安装HybridCLR内部构建及YooAsset真实清单复制集合；按审查建议补充“同版本、双方清单各自合法但内容不同”的直接用例，尚未执行。
