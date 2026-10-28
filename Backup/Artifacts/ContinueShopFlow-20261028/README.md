# 继续本局经过积分商店后的流程修正

2026-10-28。用户实玩反馈继续后先弹商店、取消后又回到开始界面和本局进度。通过Unity MCP确认连接本工程，保留[现场截图](live.png)及[状态](live-state.txt)：Main运行态暂停，本局SafeCheckpoint、下一场2、商店已完成；Start和ArcadeResume可见，商店隐藏。不是加载页闪回，而是继续链条停在进度窗口。

根因：单人进度Render会自动弹出商店并禁用继续按钮，商店购买/跳过成功只重绘进度，没有后续进入关卡意图。

修正仅在手写ArcadeResumeUICtrl中：浏览进度不弹商店；点击继续才处理未完成商店，并保留当前RunId的继续意图。成功落盘后等待当前事件派发结束，核验窗口代次、本局、阶段及商店完成，再恢复按钮并接回原流程。窗口隐藏/重开/卸载或换局取消旧意图；防重复提交、失败重试及挑战选择保留。双人原准备流程、商店价格/商品/积分、存档格式、Prefab及外观布局未改。

定向Unity EditMode检查，includePassingTests=true；未运行完整套件、PlayMode测试或Player Build：

| 范围 | 每次上限 | 最终结果 |
| --- | --- | --- |
| ScoreShopResumeFlowTests | 最终9（初版6、补充7/8） | [9/9通过](flow-final.json) |
| ArcadeEntryPresentationTests | 16 | [14/14通过](entry-regression.json) |
| ScoreShopControllerTests | 4 | [4/4通过](shop-regression.json) |

新增检查先在旧代码真实复现4项失败、2项通过（[red.json](red.json)）；初版修正后[6/6通过](green.json)。最终9项覆盖购买/跳过自动接续、防重复加载、无继续意图不加载、关闭和换局失效、准备失败重试、事件派发后才开始进关、挑战准备失败后继续按钮恢复。测试使用真实Prefab和商店事务、独立临时存档；准备端口挂起或返回失败以避免加载真实地图，不能据此声称已完整实玩下一关。

独立审查发现的旧事件隐藏新HUD及挑战失败后按钮禁用问题均已修复并复审关闭，最终无未解决问题。Console保留首次测试夹具缺EventSystem的历史错误；夹具已补齐，最终三组27项均通过，无编译错误。此前换局测试使用非法RunId也已更正为正式合法身份。

正式存档SHA256与[测试基线](save-before.json)一致，3份原有美术/字体/ProjectSettings差异指纹与[无关基线](unrelated-before.json)一致，不夹带提交。已退出用户当时的暂停运行以完成编译和验证；测试结束Boot编辑态未脏。按项目授权中文提交并推送当前origin/main，Git结果为准。
