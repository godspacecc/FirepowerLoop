# 三关场景与四种角色正式接入计划

> 使用 subagent-driven-development 与 dispatching-parallel-agents 执行；实际编辑器操作串行。用户明确“七项全部采用并正式接入”，无需再次批准技术方案。

**目标：** 将TenStageEnvironments-v1-20261025中的7/10/20三场四轮环境，及TenStageCreatures-v1-20261025中的Spider/Ghoul/RedMangler/ZRex原样持久化并接入实际状态。

**依据：** [执行总清单本次确认](执行总清单.md)，两组已采用预览；小龙保留525e828e版本，巨猿不采用。

**职责与技术：** 采用源位于Tools/Unity/TenStageEnvironmentCandidates.cs、TenStageCreatureCandidates.cs；烘焙工具持久化资源，既有地图/聚合模板加载事务拥有资源，Controller创建/销毁，View只读取权威状态采样。Unity C#、AnimationClip、既有MCP与限定EditMode测试。配置仅Config权威源及gen.bat；不新增规则和伤害。

## 约束与任务交叉检查

| 任务 | 产物与范围 | 交叉边界及裁定 |
|---|---|---|
| A 三场环境 | 新烘焙/回读工具、正式四轮Prefab、美术、原EarlyStages加载路径与必要Config映射、环境专项测试 | A独占Config及EarlyStages，包含LubanRuntimeConfigProvider精确九场目录、ServerConfig服务端目录与ArcadeEarlyStageParameters接受范围；不得改RoundContent公共文件或角色聚合Prefab；采用原布局足迹 |
| B 四种角色 | 新烘焙/回读工具、正式演员和片段、Content聚合模板、Controller/View与单双人快照表现、角色专项测试 | B独占RoundContent及角色接口/运行投影；不得改Config/环境资源；新环境配置只读取 |
| A+B | Unity域与资源数据库共享 | Unity独占权由主代理分配，测试/烘焙/拍摄串行；不默认建worktree |
| 统筹与收尾 | 总清单、验证记录、最终复核、中文提交推送 | 保留原33项本机差异；只提交本任务。独立任务验证不等于自然完整通关 |

## 执行

- [x] A：先限定环境契约RED；全部12布局采用几何/材质与足迹验证后持久化；接原加载流程；限定GREEN及正式同框回读。
- [x] B：先限定角色契约RED；原模型/局部姿态持久化；走实际移动、技能、攻击、收招/死亡入口，重复快照/暂停/重用/离场正确；限定GREEN与四条正式可播放回读。
- [x] A/B各自只读复核并修复；统筹交叉检查，旧怪物/小龙/轮级View/路线必要回归。
- [x] 对照采用源核对正式资源，发布可查看的画面与报告。

提交策略：检查与审查通过后自动使用中文提交推送origin/main；Git最终结果为准，不将本地提交等同远端成功。
- [ ] 单人自然十关通关、真实双端及实体设备实玩：用户最新“实际验证先不做”，现暂缓，保留未完成状态；本批不再启动实玩观察。

证据分目录：Backup/Artifacts/TenStageEnvironmentsApplied-20261026、TenStageCreaturesApplied-20261026、SevenVisualsApplied-20261026。遵从项目目录规范，协作临时记录也留在该批目录（临时文件忽略），不另建用户任务台账。

不做Player Build、平台发布、商店门禁开启、火山/竞速恢复或新玩法扩展。
