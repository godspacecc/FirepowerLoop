# 全地图旧外围墙修正（2026-10-23）

用户先要求排查并解决全部地图同类问题，再明确采用[左右对比](comparison.png)右侧，并授权“全部地图正式修复”。该图和[22主题候选总览](all-themes.png)为正式修改前的Unity临时场景实拍，已保留原始文件。

## 根因与修正

TaskSampleMap的共享底图包含旧Ground和四面实心边界。正式轮环境就绪后本来会覆盖它们，但单人及联机共用的每帧SetOpening(false)又释放海岛表现，恢复共享底图。旧逻辑只认旧Content环境，没有识别当前轮地图及商店。

修正后，旧表现释放及公共效果加载失败时，检查当前正在显示且已准备好的正式环境，继续保持共享底图隐藏。最终离场仍按每个Renderer进入前的原始状态恢复。只改变本地图共享底图的显隐；正常墙体、码头绳栏、农场正式石墙、无渲染的防越界碰撞保持原逻辑。资源未准备好时仍保留原兜底，避免提前隐藏。

## 正式复核

- [正式修复前后重点图](applied-comparison.png)、[全部22主题正式修复前后](applied-all-themes.png)，原图位于Applied。右侧是正式代码运行效果，拍摄时没有执行候选手工隐藏。
- [修复前88布局记录](audit-before.tsv)：84个非海岛布局在释放旧表现后恢复4面旧墙；海岛4轮未复现该问题。
- [修复后88布局记录](audit-after.tsv)：afterOpeningRelease全部为0，最终End后restored全部为4。候选列是独立显隐探针，不参与正式截图。
- 农场正式复拍时，另一个已获用户采用的去雪任务正在接入，地面配色已有变化；本提交不包含农场美术资源，也不把该变化归入外围墙修正。海面等动态材质会随拍摄时间变化。

这些是隔离加载真实TaskSampleMap、正式Prefab和配置后执行实际表现控制器的验证，包含正式主角比例参照；不等于自然完整通关或真人双端验收。临时场景关闭且未保存，结束时Boot场景无脏状态。未触发Player Build。

## 定向测试与审查

所有测试使用明确的非空testClass，EditMode、includePassingTests=true；未运行完整套件。共39项通过：

| 测试类 | 上限/实际 | 结果 |
| --- | --- | --- |
| ArcadeArenaBoundaryTests | 6/6 | 修复前5失败、1通过；修复后6通过 |
| ArcadeEarlyStageLoadingTests | 8/8 | 通过 |
| ArcadeObstacleVisualTests | 6/6 | 通过 |
| ArcadeRoundContentViewTests | 7/7 | 通过 |
| IslandRoundPresentationTests | 12/12 | 通过 |

新增用例覆盖全部88布局重复同步、码头／农场资源延迟、商店正式墙保留、公共加载失败、奖励房往返及重复离场原状态恢复。单双人使用同一SetOpening入口已核对；没有声明单独执行真人联机或挑战全流程。

原始结果：[修复前](tests-red.json)、[修复后](tests-green.json)、[相邻检查](tests-neighbors.json)。独立只读代码审查覆盖异步失败回滚、奖励房、商店和End恢复，未发现可操作问题。

Console保留先前工具调用／连接日志及测试预期资源失败日志；正式复查没有新增运行异常，未清空既有记录。

复现工具：Tools/Unity/ArenaBoundaryAuditPreview.cs的Run为候选生成，RunApplied为正式复查；后者不覆盖采用依据。Tools/Unity/compose-arena-boundary-preview.py --applied生成正式对照图，仅拼接截图，不修改游戏资源。
