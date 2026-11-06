# 幸存者直接入口（2026-11-06）

用户确认跳过模式选择：开始游戏直接打开现有幸存者选角；取消返回主菜单。未改正式 Prefab 和视觉资源。

测试工具输入：
- `#arcade solo`：主菜单打开闯关入口，仍先确认/准备资源，然后沿用新局或已有存档流程。
- `#arcade online`：主菜单打开双人登录/大厅，未认证时沿用登录。
- `#arcade crater` 原有入口保留。

指令窗先隐藏，再提交上述两种入口，避免顶层输入遮挡下载确认或登录。非主菜单/正在准备时拒绝，错误参数不打开界面。

## 验证
6个指定 EditMode 方法通过，均 includePassingTests=true：
- UnifiedMainMenuTests.StartOpensSurvivorSelectionWithoutModePageOrCampaignDownload：真实菜单按钮直达选角、关闭重开、不触发闯关准备。
- UnifiedMainMenuTests.SoloTestEntryPreparesResourcesAndRejectsDuplicateOrHiddenMenu：资源失败重试、重复提交和隐藏后的过期回调。
- TestCmdTests.HiddenModeCommandsRequireMainMenuAndExactSyntax：两指令语法及非菜单拒绝。
- TestCmdUiTests.HiddenModesCloseCommandWindowBeforeOpeningEntry：实际指令窗提交，两入口交接、下载开始时指令窗已关闭。
- StartMenuPresentationTests.EveryMenuButtonRequestsOneUiClickSoundWithoutReplacingBusinessAction：菜单点击声与行为绑定。
- GameplayResourceDistributionTests.ModeSpecificDownloadsExcludeOtherModes：既有模式下载范围。

前三项已先确认修改前失败，再修改后通过。参数化命令测试首次未匹配，改为单个明确 Test 方法后运行，没有扩大到全套。

源收集：正式 BeginCollect 查询的三个精确地址及标签见 collection.txt，均包含 startup。没有修改下载标签、资源路径和预加载事务。
运行/发布边界：UI交互在隔离 EditMode 夹具中验证，下载用可控准备器；未执行真实网络下载、两端联机、APK或资源打包，未验证旧发布包。

审查发现并修复：选角、登录、大厅或存档详情覆盖主菜单时拒绝模式指令，防止跨模式请求作废旧选角回调。选角覆盖断言先失败后通过，相关3个入口检查复验通过，独立静态复核无新增缺陷。results.json 保存5项原始工具结果；命令语法检查另在会话工具结果中通过。
