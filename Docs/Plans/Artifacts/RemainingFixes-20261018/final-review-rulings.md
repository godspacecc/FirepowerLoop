# 本轮一次只读终审与处理裁定

审查范围07514c2f..472e4b1e，独立只读审查员未改文件、未运行Unity或构建。原裁定Ready to merge: No，Critical 1、Important 2、Minor 0。依技能要求在一轮修正中处理，不安排重复审查；以下通过情况为实施者实际验证，不冒称审查员重新批准。

1. Critical：唯一快照和journal放Client/Temp，Unity正常退出清理后丢失恢复保障。采纳。整体迁移到仓库Release/CiWorkspaceRecovery/<Client路径哈希>/，包含快照、进行中标记及恢复隔离材料，并更新路径保护、README。RecoverySurvivesUnityTempRemoval实际0/1红→通过，最终取消恢复类19/19。
2. Important：已消耗Windows台账保留后，调用脚本及Jenkins仍拒绝脏工作区，不能同Commit重试。采纳。恢复生成精确内容凭据，两个入口共用严格检查：原Client／Commit、唯一路径及哈希匹配、全部身份字段对比Git基线不变、序号不降低，仅允许这一项未暂存差异。Jenkins已有仓库先检查，禁止SCM插件先reset台账；有恢复台账不能换Commit。临时Git仓库11/11，未运行Unity或构建；C#另验证凭据及报告保护。
3. Important：Loading／Heavy观察超时会在地图仍在途时恢复正式依赖。采纳。隐藏菜单使未提交请求失效，等待场景与地图busy结束及开局续体，再放弃临时局、卸载并恢复依赖；超时或卸载失败保留临时隔离和running状态。原受控busy窗口复现Loading端口提前恢复；首次诊断启动早于Boot资源就绪，另产生4条空DefaultPackage／Start Prefab错误，原红日志和Console证据保留。诊断器补等待Boot前提；最终两工具受控busy窗口与实际LoadAsync在途收尾均通过，正常加载流程再次通过。

边界：未判断自然刷怪分布、实体机表现、真实长构建调用响应／进程树或已部署Jenkins；未扩展恢复范围到Library、HybridCLRData或任意开发者菜单；既有8项材质字体差异不纳入提交。
