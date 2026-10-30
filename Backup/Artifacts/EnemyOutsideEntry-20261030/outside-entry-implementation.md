# 普通敌人场外接近实施记录

日期：2026-10-30。该记录替代 entry-route-audit.md 中“仅候选、未接线”的实施状态；原核查报告保留为证据。

## 已接线

- 新增 ArcadeOutsideEntryState/Rules：有限线段连续步进，完成帧保留控制权；无无敌字段，池复用清零。
- 新增 ArcadeOutsideRouteRules：布局入口中心线按敌人半径检查所有真实障碍。墓地/神庙/市场仅允许 EnemyMouths 已核验的入口薄挡条，尺寸/位置/地图 ID 均白名单匹配；其他 MovementOnly 障碍仍阻挡。
- Config/Defines/beans.xml 和 arcade_round_layout.csv 新增 OutsideEntryDistance，全布局为4米；正式 Config/gen.bat 双端生成。岛/码头实际特殊入场由权威状态判断保留，距离配置也供它们的普通挑战兼容模式使用。
- 单人 SpawnQueuedEnemy、TickEnemies 和主题普通增援；服务器 TrySpawnEnemy、TickEnemies 和主题普通增援；幸存者 Spawn、Tick 均接入。
- 旧顺序/无布局挑战使用该场地图的实际障碍，在原出生方向优先寻找四边通路；场外起点及场内交接点整段都检查实体圆。无合法通路开场预检明确拒绝，不静默退回场内出生。
- 墓穴/蓝影活动阶段、潜地种类4/11、重甲空降13、实际Boss保留特殊登场。背桶等行走身份不被排除。码头普通Boss增援复用已有海路状态。
- 普通入场阶段参与原受伤流程，受控冻结；通用挤压、击退和吸引不将其夹回场内。掉落普通道具、挑战金币及幸存者经验落在已验证的场内交接点。
- 服务端广播位置已足够呈现普通走路入场，不新增协议字段。断线冻结继续沿现有战斗生命周期；不保存瞬时敌人到轮前节点。

## 验证

1. ArcadeOutsideEntryChecks：10项纯步进检查，RED 8失败/2通过，GREEN 10通过，日志 approach-checks/red.log、green.log。
2. OutsideRoutes：指定 testClass=OutsideRoutes maxTests=10 includePassingTests=true；通过10项。包括100配置布局的普通入口遍历（岛/码头另验证特殊保留）、全部arena原障碍fallback、封闭失败、薄挡条白名单、方向、真实服务端出生/冻结/载桶/码头普通Boss增援。日志 approach-checks/routes-integrated.log。
3. Unity待父任务执行：OutsideEntryFlowTests maxTests=3；完整方法 Game.Tests.SurvivorRuntimeTests.OutsideSpawnPauseAndEarlyKillLandExperienceInsideEntry maxTests=1；均 includePassingTests=true。未在本子任务调用Unity或声称这些测试已通过。
4. 配置生成成功记录 approach-checks/config-generation-final.log。服务器依赖随定向检查构建成功。

## 审查边界

未修改正式模型/动画/材质/Prefab，未提交、推送、打包。场外地面承托与实际镜头可见性由父任务动态实景验证；纯配置走廊和服务器检查不能证明视觉效果。码头动作/HitGeometry由其他切片处理，本切片未改其命中函数。

检查仍需关注普通兼容场次与主线的可见场地差异，以及控制效果在入场中保持轨迹的体验。纯规则沿直线走到入口后移交原AI，不承诺场外追击或动态绕行。

补充：幸存者Spawn防御已取消旧入口Resolve回退；路线校验失败设置LastError、暂停、禁用输入并Publish，当前Tick立即停止。新增限定方法 OutsideSpawnRejectsInvalidRouteWithoutCreatingEnemy，等待父任务Unity验证。
