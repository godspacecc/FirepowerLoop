# 已采用十二张背景正式接入计划

> 执行方式：当前会话直接实施，按 executing-plans 与 test-driven-development 验证，最后独立审查。已有采用与接续授权，不再请求技术方案审批。

**目标：**主菜单、大厅采用 v2 图片；完整加载页根据即将进入的真实场地配置编号显示十关背景，未知场地回退海岛图。

**依据：**[执行总清单](执行总清单.md)首节及 [v2 预览](../../Backup/Artifacts/UIBackgroundPreview-v2-20261026/index.html)。

**架构：**LoadingUICtrl 只读取单人存档或联机快照选择场地；Prefab 中的 ArcadeLoadingArtwork 持有 Image 与序列化图片引用并更新显示。保持原加载事务、输入阻断、错误重试与局内黑幕。无需配置或协议变更。

**约束：**当前工作区 main，不创建 worktree；保留既有差异和资源/meta；不运行实玩、PlayMode、Player Build。产物统一 Backup/Artifacts/UIBackgroundApplied-20261027。完成后选择性中文提交推送 origin/main。

## 执行项

- [x] 新增 UiBackgroundTests，先验证旧版无法满足十关真实编号、未知图回退及主菜单/大厅资源契约。
- [x] 将已采用十二图原样复制到 Assets/Game/Art/UI/ArcadeBackgrounds；新增 Controller/View/Common/ArcadeLoadingArtwork.cs，提供 ShowArena(int arenaConfigNumber)，序列化默认图及十关映射。
- [x] 通过 Unity 编辑器修改 Home/Start、Online/OnlineRoom、Common/Loading Prefab；ReferenceCollector 增加 LoadingArtwork，执行项目 UICodeSpawner 生成并回读字段。
- [x] LoadingUICtrl 在完整地图加载与相关快照变化时读取场地身份；新局无存档/未知旧场地/下载显示使用默认图，联机活动战斗优先其权威配置编号。销毁重开和语言重绘不残留旧图。
- [x] 限定 EditMode：UiBackgroundTests 最多8项、ArcadeRoundTransitionTests 最多5项；必要时 UnifiedMainMenuTests 中真实单方法最多4项。每次 includePassingTests=true，不完整套件、不无效过滤回退。
- [x] 从正式 Prefab 隔离渲染主菜单、大厅、十张加载页，并检查裁切、文字与进度条；覆盖16:9及窄/宽屏。检查原资源/meta及既有差异指纹。
- [x] 独立只读审查、修复实际问题、同步总清单、选择性提交推送。

## 风险核对

- 配置编号与关卡序号不同：码头21、农场22等不得用序号2/3选图。
- 联机活动快照与残留单人存档同时存在时，使用联机配置编号。
- 下载或未知场地不得保留上次加载图片。
- 普通局内切轮仍使用黑幕，不新增强制加载页。
- Prefab字段须经生成器绑定并在Unity反序列化后验证，图片不拦截按钮。

## 执行记录

- 2026-10-27 接续：Unity MCP 直连确认 F:/GitHub/FirepowerLoop/Client/Assets，Boot未脏、非Play/编译。原Console错误为HengeAppliedTests缺轮测试的预期日志。原会话只登记采用并检查代码，尚未接入。

- 编号回退、联机优先、下载重置、正式图引用与生成绑定6项已RED→GREEN。生成器延迟绑定未落盘导致首轮1/6，调用其已有绑定方法并回读后6/6。真实黑幕fixture为5项，已据源码修正上限。

- 最终7项背景＋5项黑幕＋4个加载方法共16项通过；旧顺序局回退问题独立复审关闭。18张正式截图、12图原样一致与9份既有实质差异指纹验证通过。详见 Backup/Artifacts/UIBackgroundApplied-20261027/README.md。提交推送按Git最终记录。
