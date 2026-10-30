# Android渠道打包静态交付

菜单简化为渠道打包，内部跨重载身份保留。仅联网渠道跳过预备APK，保留专用裁剪AOT及校验；纯离线候选原流程不变。构建前冻结完整资源，构建后前后核对目录并逐文件验证APK实际内置内容，检查失败保留APK供排查，不冒认通过。

自动渠道报告改存Backup/Artifacts/AndroidChannelBuild-<批次>/channel-apk-check.json；APK保留原Client/Build/Android位置。正式签名、公网检查及不发布行为保留，使用说明第十项补齐位置与失败补做。

C#9独立语法解析通过，源码步骤/范围断言与本任务git diff --check通过。未Unity编译、测试、打包、SDK检查或设备验收，不声明运行验证通过。
