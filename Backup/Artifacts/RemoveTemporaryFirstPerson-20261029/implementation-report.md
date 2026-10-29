# 临时第一人称运行时退役

## 已实施

- 删除临时掉落入口、独立时钟、水平相机输入、HUD动作身份、地图相机租约、跨轮／宝藏房／挑战恢复及存档计时字段。
- 原 FirstPerson partial 改为 Input partial，保留文件 meta 身份及普通射击释放闸门。
- 移除仅用于临时相机的表现端口与弹丸高度路径；通用 PlayerCameraMode.FirstPerson、诊断命令、手臂及装备表现保持。
- Config 权威源删除 solo 专表和 Kind=7，保留其余枚举显式值；客户端34表／服务端30表由 Config/gen.bat 生成成功，日志 config-final.log。
- 八个专用本地化键加入退役权威清单并发布；文本常量已经通过正式 Unity 生成器生成，强制重导入语言文件后确认专用键及 mode_arcade 常量均不存在。
- 删除旧专用测试，保留 meta 身份并替换为 TemporaryCameraRetirementTests；保留其它轮级、普通输入及诊断相机测试。

## 测试证据

- RED：限定 TemporaryCameraRetirementTests，最大2项，includePassingTests=true。实际2项：普通输入释放闸门通过；OldCheckpointIgnoresRetiredCameraAndKeepsWeapon按预期失败，旧JSON仍序列化 FirstPersonRemaining=20。
- 新增正式目录／枚举／相机桥退役契约，最终该类3项；GREEN已3/3通过（runtime-retirement-tests.json）。
- 辅助旧 Tools/ArcadeRoundSaveChecks，限定 IslandRoundSave 最大12项，在测试启动前编译失败：已有工程缺少 ArcadeScoreShopState、Cysharp、Unity引用（save-checks.log）。没有运行任何套件或 Player Build；将以 Unity 正式 IslandRoundSaveTests 验证同一保存路径，未扩展修理旧辅助工程。

## 协作边界

UI Controllers、布局 Model/Geometry、正式 Prefab/生成 View、UI专用测试及制作工具由主任务持有；本报告不据运行时清理声称这些已完成。

## 最终限定验证

- TemporaryCameraRetirementTests：3/3。
- IslandRoundSaveTests：最终8/8（runtime-save-tests-final.json）。首轮7/8：原RoundFour测试把首关后继硬编码2，现役源表为21，修正测试完成输入后整类重跑通过；旧文件证据保留runtime-save-tests.json。
- IslandRoundFlowTests.TreasureWaitFreezesThenActivitiesConsumeTimeAndExitCommitsFourthMap：1/1。
- IslandRoundFlowTests.RoundRetryRestoresNodeWithoutReplenishingConsumedValues：1/1。
- PlayerCameraTests.FirstPersonFollowsEyeAndRestoresLocalPresentation：1/1，共用诊断相机表现未退役。
- 每次运行均使用真实短类或完整方法、预声明最大数量、includePassingTests=true，原始结果JSON在本批目录。
- 主任务已报告UI限定回归MobileControlLayoutTests 30/30、ArcadeCompactHudTests 27/27，本代理未重复运行。
- 所有测试结束后Time.timeScale恢复1，TimeManager.asset无差异，Boot从磁盘重载，编辑态且未脏。
- 后续接手清理了UI专用测试与Tools/Unity工具；正式Prefab/生成View及产品UI源由主任务完成。
