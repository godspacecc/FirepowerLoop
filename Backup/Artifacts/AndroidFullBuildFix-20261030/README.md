# Android完整打包静态交付

菜单统一完整打包，删除额外预备APK，保留专用AOT生成并后置校验。最终APK校验通过即保存成功记录，发布指定资源后逐包及清单核对，再发布APK；失败保留有效APK。内部跨重载身份不变，启动包与渠道流程未调整。

静态断言核对准备/最终APK/保存/资源发布/验证/APK发布顺序及范围；任务差异检查通过。仅静态检查，未Unity编译、测试或实际打包发布。不得声称运行验收通过。

使用说明见../../../Docs/打包工具使用说明.md。后续获准验证时使用真实单方法Game.Tests.AndroidHostBuildTests.HostBaseBuildUsesHostPlayerAndKeepsFullBuiltinFallback，上限1、includePassingTests=true；该既有用例不替代实际发布验收，禁止全套或无效过滤回退。
