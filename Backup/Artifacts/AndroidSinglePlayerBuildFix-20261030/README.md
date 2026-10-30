# Android打单机包静态交付

按用户修正名称改为打单机包；正式签名及离线规则保留，取消该入口额外预备APK，专用AOT生成后校验。单机包复用渠道完整资源冻结、APK逐文件比对及前后源目录复核。报告保存Backup/Artifacts/AndroidSinglePlayerBuild-<批次>/offline-apk-check.json；不自动发布，内部跨重载身份保持。

使用说明第十一项补齐输入输出、报告用途及失败补做，第十项同步链接单机说明。C#9独立语法解析、源码顺序/范围断言及任务git diff --check通过。未Unity编译、测试、打包、SDK检查或断网实玩；不声明运行验收通过。
