# 潜地怪／恶犬原审查发现复核

2026-10-25，十关路线子代理按根代理要求独立只读复核。范围为Creatures/Applied/README.md列明的两项P2与一项P3，不替代真人双端对局或新的Unity实测。

结论：在当前源码与既有交接证据中，原三项发现均已关闭。本次未发现这些修复被十关路线或巨石资源接入覆盖。

| 原发现 | 当前实现与证据 | 复核结论 |
| --- | --- | --- |
| P2：场地早于聚合演员模板时提前创建holder并抛异常 | `ArcadeCombatViewController.RoundContent.cs:44`将CreatureDependenciesReady纳入就绪；`:503`在创建前阻止未就绪路径；`:516-517`实例装配失败销毁holder。`Creatures.cs:18-25`验证模板及正式动画就绪。原MissingCreatureTemplates测试覆盖迟到／残缺模板、重复更新不留节点及恢复后采样当前年龄和位置。 | 原因与失败回滚均有对应修复。 |
| P2：恶犬技能身体采样覆盖真实近战口部 | `Creatures.cs:85、90、100`在预警／扑击／移动身体姿态之后叠加BiteFace；`BurrowerHoundBake.cs:80`把该片段曲线限制为颈、头、颌，保留身体和根权威。原两参数近战测试明确以潜地怪／恶犬比较下颌变化及根位置。 | 原覆盖顺序问题已修复，没有改判伤或移动规则。 |
| P3：重复烘焙复制后未释放临时源AnimationClip | `BurrowerHoundBake.cs:133`在复制并保存既有持久资源后DestroyImmediate临时AnimationClip；新资源由AssetDatabase.CreateAsset接管，原owned网格／材质清理另有finally。原bake-repeat.json记录临时AnimationClip数量0→0→0。 | 原成功重烘焙泄漏路径已关闭。 |

本次通过SHA256比对manifest，四个审查核心文件（Creatures.cs、RoundContent.cs、BurrowerHoundBake.cs、CreaturePresentationTests.cs）均与已交接版本完全一致。client-green.json记录CreaturePresentationTests 14/14，其中模板依赖及两种近战参数均通过；review-melee-red.json保留恶犬下颌变化最初为0的真实失败。

未执行Unity MCP、烘焙、测试、构建或资源修改。报告只陈述独立源码复核与现存验证证据，不将历史测试说成本次新运行。
