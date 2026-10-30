# Android启动包静态交付

- 名称统一为启动包（首次下载资源）；去掉额外预备APK，专用AOT生成后校验。
- APK通过检查后仅保存一次成功记录，再复制说明；CI归档后返回，不自动发布。交互流程发布本次目录与版本，核对远端完整资源和清单后发布APK、启动资源服。
- 源码断言验证步骤顺序、唯一成功记录、CI位置、其他流程保持及跨重载身份；本任务git diff --check通过。使用说明第八项已补齐位置与失败补做。
- 未Unity编译/测试/实际打包发布，不声明运行验收通过。
- 恢复验证时仅使用真实testMethod Game.Tests.AndroidHostBuildTests.SmallHostBaseBuildCreatesManifestOnlyApkAndPublishesMatchingResources，上限1、includePassingTests=true；禁止全套及无效过滤回退。现有用例已同步但未运行，不替代首次下载或设备验收。
