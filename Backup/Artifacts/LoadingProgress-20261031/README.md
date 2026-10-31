# 完整入场加载进度

用户确认先覆盖所有准备阶段，平滑效果暂缓。沿用正式Prefab与美术，无资源地址、下载标签或预加载链路变更。

地图进度映射前80%，玩家控制器准备逐项上报。幸存者后续角色初始化、场地等待、战斗激活分别上报阶段；街机新局与继续入口持有遮罩到初始化交接。任何准备所有者未释放时不显示100%，失败释放不补100%。旧入口令牌不能覆盖新请求。

验证：Unity连接 E:/GitHub/FirepowerLoop/Client，Boot场景，EditMode。最初3例复现错误100%，最终ArcadeEntryPresentationTests 19/19、SurvivorEntryTests 2/2、MapSceneContractTests 3/3通过，全部includePassingTests=true，类范围上限25/2/3。见相邻JSON。参数化完整方法过滤未被工具识别后使用真实类过滤，未运行完整套件。共享编辑器曾出现其他工作编译/测试占用，等待恢复后验证。

独立代码复审发现街机失败释放默认成功，已改为显式ready并复审通过。未进行完整人工开局实录、联机实玩或Player Build。preview.gif仅为此前概念示意，不代表本次匀速实现或实测耗时。
