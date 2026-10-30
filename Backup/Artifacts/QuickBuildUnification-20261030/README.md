# 两平台快速打包统一：静态交付记录

> 后续授权（2026-10-30）：用户明确要求“提交推送”，本会话打包改动纳入本次提交；替代下文历史记录中的暂不提交安排。Unity测试继续暂缓，提交不代表编译、测试或真实出包通过。提交前对18个相关C#文件进行独立C#9语法解析，0个语法错误；这不是Unity程序集编译。证据见 `Backup/Artifacts/QuickBuildUnification-20261030/commit-syntax-check.json`。

日期：2026-10-30。用户授权：两平台均只保留“复用 AOT 打包”和“复用代码和资源打包”，检查并修正流程。

## 当前状态

已写入实现、限定用例及使用说明；仅进行源码和差异检查，未进行Unity编译、刷新、测试、实际Player构建或发布。用户此前“先保留运行，暂不测试”继续有效；未提交、未推送。前轮启动包和热更新改动仍未验证，不能用本报告替代它们的验收。

## 实现与审查

- 两平台各两个菜单，顺序均为复用AOT、复用代码和资源；取消PC原“复用代码打包”菜单，旧批处理方法明确拒绝旧语义。
- AOT模式重新编Game和资源；仅复用已冻结的AOT及对应生成资料，新增无法证明兼容的外部或原生需求拒绝。
- 代码资源模式不编Game、不生成AOT、不重建资源、不分配新资源版本，只生成最终程序及交付产物；APK应用身份仍按既有规则递增。
- 普通完整包/启动包在最终程序前保存准备记录，AOT快速模式生成新的准备记录；包型沿用，Android渠道包不混用。
- 源码、资源、全部设置、原生依赖、生成资料和实际包内容分层检查；在资源构建前捕获DLL/生成指纹，保存前再次比较，防止中途变更被登记成可信组合。
- PC核对实际Player资源；Android核对APK内文件数量与字节；下载资源用真实清单逐包检查。最终程序成功后保存成功记录，PC重新保存与实际游戏绑定的热更新基线。
- PC发布改用明确资源目录，冻结内容并复核发布目录，避免旧批次补做时误发较新的资源。Android复用原有指定版本发布路径并强化逐包检查。
- 独立静态审查发现并修复：PC自动递增发布序号误判；远端包仅检查存在任一bundle；Android非主ProjectSettings准备窗口漏检；生成目录根meta初次创建误判；补齐Boot资源依赖、Resources和非YooAsset内置配置。最终复审未发现新的明确P1/P2。
- PC准备索引原子替换；Android索引与版本旁不可变记录比对。记录不是所有产物备份，其他平台构建改写共享生成/内置目录时会保守拒绝复用。

## 验证证据与范围

执行过：限定任务文件 `git diff --check`；源码检索四个菜单路径、准备记录的全部调用点；新.meta GUID与文档相对链接检查。工具输出未出现差异空白错误，换行转换提示不代表编译结果。

未执行：以下全部Unity用例、Unity编译、设备/Windows运行验收、安装器、发布脚本。新增用例没有RED/GREEN执行证据。

恢复测试时逐次指定真实非空过滤，全部 `includePassingTests=true`；不执行全套，不允许无效过滤回退，不触发Player Build：

| 指定过滤 | 最大数量 | 目的 |
| --- | ---: | --- |
| testClass `PcQuickBuildTests` | 15 | 身份、路径、指纹、资源前后输入绑定、旧入口拒绝 |
| testClass `AndroidQuickBuildTests` | 28 | APK身份、冻结资料、准备记录、包型/平台、资源实包、设置/宿主资源输入 |
| testClass `PcBuiltinResourceTests` | 29 | 两平台共用的严格清单与资源验证 |
| testClass `PcHotUpdateBaselineTests` | 22 | 原有PC基线及原生/托管兼容性门禁 |
| testMethod `Game.Tests.FoundationFlowTests.QuickPackagingMenusExposeOnlyTwoReuseLevels` | 2 | 两平台实际菜单仅两种 |
| testMethod `Game.Tests.FoundationFlowTests.PcPackagingMenusAreGrouped` | 1 | PC全菜单合同 |
| testMethod `Game.Tests.BuildWorkflowTests.PlayerWorkflowsPublishInstallerAfterPlayerValidation` | 1 | 成功输出后再发布 |
| testMethod `Game.Tests.BuildWorkflowTests.ReuseCodeAndResourcesWorkflowDoesNotRebuildPreparedPackage` | 1 | 复用代码资源不重建包 |
| testMethod `Game.Tests.BuildWorkflowTests.WindowsPublicationRejectsWrongPlatformAndVersion` | 4 | PC发布目录/版本边界 |
| testMethod `Game.Tests.BuildWorkflowTests.WindowsSmallHostBaseBuildCreatesManifestOnlyPlayerAndPublishesMatchingResources` | 1 | 启动包回归 |
| testClass `PcFullBuildFlowTests` | 3 | 完整包必要回归 |

任务改动文件与前轮待验改动有重叠，提交前须按总清单核对，仅暂存打包任务；不可夹带并行幸存者模式和资源修改。

使用说明：[两种快速打包](../../../Docs/打包工具使用说明.md)。实施记录：[计划](../../../Docs/Plans/两平台快速打包统一-20261030.md)、[总清单](../../../Docs/Plans/执行总清单.md)。
