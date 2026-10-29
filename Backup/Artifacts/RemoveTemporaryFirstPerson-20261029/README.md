# 临时第一人称完整移除验收

2026-10-29 用户明确采用 layout-before.png / layout-after.png 删除预览，授权继续完整删除。

## 交付

- 清除拾取入口、专用指令目录项、掉落类型及solo专表、计时与镜头接线、恢复字段和专用输入。
- HUD及触屏布局移除模式切换、返回俯视、专用开火和剩余时间；ReferenceCollector清理后通过UICodeSpawner生成并绑定View。
- 旧局与旧触屏设置忽略退休字段，后续保存不再写入；普通射击释放闸门、武器计时、双摇杆及单/双人规则保留。
- 8个专用本地化键退休，2条设置说明移除过时内容；权威源发布，Texts由正式生成器生成。
- 工具不再重建该功能；按项目要求保留美术资源和.meta，原道具美术不再配置或接入。独立 #camera firstperson 诊断及共用表现仍保留。
- formal-layout.png 为正式Prefab在隔离场景中的复核图，不是实机操作录像；已确认无过时提示。

## 验证

最终限定Unity测试72项全部通过，includePassingTests=true：
- MobileControlLayoutTests：30/30，预声明上限35。
- ArcadeCompactHudTests：27/27，最初误预估上限22；参数化展开实际27，如实记录此次偏差，未运行其他类或完整套件。
- TemporaryCameraRetirementTests：3/3，上限3。初始旧字段序列化RED见implementation-report.md。
- IslandRoundSaveTests：8/8，上限8。首轮7/8，旧测试下一关配置编号2与原有权威路线21不符；修测试后重跑通过，未改产品路线。
- IslandRoundFlowTests两个具名方法：各1/1。
- PlayerCameraTests.FirstPersonFollowsEyeAndRestoresLocalPresentation：1/1。
- MobileInputAndScreenAdaptationTests.ArcadeJoystickPrefab_HasGeneratedViewValidReferencesAndRaycastSurfaces：1/1。

Config/gen.bat双端成功（客户端34表、服务端30表）；Unity最终编译成功。未Player Build、未完整套件、未真人移动端/双客户端验收。
旧辅助dotnet保存检查工程缺已有依赖、未启动测试，失败日志保留；以真实Unity存档测试作为验证，不宣称旧辅助工程通过。

## 审查与保护

独立审查未发现残余P1/P2；测试留下的TimeScale=0已恢复1，TimeManager无差异，Boot编辑态未脏。最后静态检查和git diff --check通过。
原有海岛、字体和ProjectSettings三份文件SHA256与baseline-hashes.json一致，仅提交本任务。审查详情见review.md。
