# PC热更新工具修改记录（待Unity验证）

> 后续授权（2026-10-30）：用户明确要求“提交推送”，本会话打包改动纳入本次提交；替代下文历史记录中的暂不提交安排。Unity测试继续暂缓，提交不代表编译、测试或真实出包通过。提交前对18个相关C#文件进行独立C#9语法解析，0个语法错误；这不是Unity程序集编译。证据见 `Backup/Artifacts/QuickBuildUnification-20261030/commit-syntax-check.json`。

用户在第三项分析后明确“继续”，授权菜单改名、宿主基线复用、阻断需重新出程序的变化、完整远程资源逐包校验及本项文档。继承“先保留运行，暂不测试”，本轮未调用Unity、未主动刷新或编译、未执行任何测试/Player Build/发布，也未提交或推送。

## 已写入的实现

- 菜单改为“构建热更新（代码+资源）”，保留内部工作流身份以兼容重载恢复。
- 完整包及启动包成功后，在游戏批次的HostBaseline目录保存入包AOT副本、运行清单、原始Game.dll、宿主输入与实际Player文件指纹。构建前后比较输入及入包代码bytes，防止中途变化后记录错误基线；目录不可覆盖。
- Host手动及CI流程统一先校验对应Player、冻结的AOT与底层输入，只编译Game，读取旧AOT；不再GenerateAll，不覆盖基线。旧记录缺少新基线时要求重新生成宿主。
- 新Game含直接原生调用、回调或calli且DLL发生变化时保守拒绝。比较原始Game与新Game的外部类型/成员及具体泛型调用需求，超出旧需求集合时也要求新宿主。该规则保守，不声称动态反射需求已获兼容性证明。
- 远程包真实解析清单、逐Bundle验证长度/MD5，并在启动服务前复核资源指纹与宿主基线。内置目录仍保持不变；不生成Player、安装器或自动同步Release发布目录。
- 独立PC基线随既有CI宿主归档复制与恢复，状态路径重新定位；外部Unity引用按逻辑名称和内容哈希识别，避免机器路径导致误拒绝。
- 使用说明补第三项7步位置、失败补做、保守兼容边界；第一/二项相应补新增基线位置及用途，均标记待验。

## 静态检查与审查

本任务相关差异检查、文档步骤位置及源码链接检查通过。独立审查指出的机器路径绑定、原生桥接需求及裁剪后AOT新需求缺口已修正，最终只读复审未发现新的明确P1/P2。审查不能代替编译和运行验证。

## 恢复验证时的限定范围

当前测试仅已编写，没有RED/GREEN执行证据。用户允许测试后，均使用includePassingTests=true，禁止全套或无效过滤回退：

- PcHotUpdateBaselineTests：最多22项，临时文件基线、宿主输入、跨目录迁移、实际DLL的原生标记与外部需求集合。
- PcBuiltinResourceTests：最多29项，包含原完整包、第二项启动包及第三项远程包检查。
- HostBaselineArchiveTests：仅运行新增完整方法 Game.Tests.HostBaselineArchiveTests.PcHotBaselinePathIsPreservedAndRebased，最多1项；按需再选择已有平台搬迁方法的真实限定用例。
- Game.Tests.FoundationFlowTests.PcPackagingMenusAreGrouped：最多1项。
- Game.Tests.BuildWorkflowTests.WindowsSmallHostBaseBuildCreatesManifestOnlyPlayerAndPublishesMatchingResources：最多1项。
- PcFullBuildFlowTests：最多3项，既有完整入口回归。

实际旧Player下载安装/运行、原生桥接与动态需求验收未进行。当前禁止打包的项目约束继续生效。第二、三项都未提交，不能报告整体完成。
