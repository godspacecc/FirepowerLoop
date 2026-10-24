# 三关规则、路线与存档验证

已采用内容：农场之后追加墓园、神庙庭院、集市，新局13关52轮，旧存档保持40轮；竞速暂停。用户预览及确认记录见执行总清单本任务分区。

## 定向检查

以下均使用真实短testClass或完整testMethod，includePassingTests=true；无完整套件、Player Build或资源打包。

| 范围 | 限定检查 | 通过证据 |
| --- | --- | --- |
| 单人特色敌人 | NextStageEnemyTests原6项＋隐藏阶段拥挤及紫霰弹回归各1项 | 原6项通过；新增拥挤实际RED后，完整方法1/1 GREEN。共8项均有通过证据 |
| 固定地图 | ArcadeRoundLayoutTests，最多5项 | 5/5 |
| 客户端完整新局 | ArcadeEarlyStageRouteTests，最多1项 | 13关/52轮真实结算及文件保存，1/1 |
| 码头/农场兼容 | ArcadeEarlyStageConfigTests，最多3项 | 新后继23及旧版本后继5，3/3 |
| 独立候选路线 | NextRouteOnline，最多10项 | 10/10 |
| 独立候选敌人 | NextEnemyOnline，最多12项 | 12/12 |
| 独立候选奖励及过关 | NextStageFlowOnline，最多3项 | 三关每关4轮，重复清场、金币箱拾取门禁、传送门、保存下一关，3/3 |
| 独立候选Slayer返回 | NextSlayerRouteOnline，最多2项 | 新局返回墓园、旧局返回原地图5，2/2 |
| 联机攻击反馈 | NextStageAttackOnline，最多1项 | 真实挥击、隐藏/冷却门禁及Clone/MemoryPack，1/1 |
| 联机阶段推力门禁 | NextStagePushOnline，最多1项 | 墓穴/乌鸦/蓝影保持原位，活动后可击退，1/1 |
| 版本化文件保存 | RouteSave，最多8项 | 8/8 |
| 新三关轮前文件节点 | NextNodeSave，最多3项 | 每项覆盖该关4轮，共12节点，3/3 |

服务器及保存40项在只包含本任务差异的临时候选源码中编译运行，以确认本任务选择性提交在现有已提交基线上可独立编译运行。逐项输出见`candidate-*.txt`；此导出不创建Git工作树、不复制Unity项目Library，只复制保存检查引用的现有Newtonsoft程序集。

权威源再次通过`Tools/gen-protocol.ps1`及`Config/gen.bat`生成：双端协议的完整输出与候选一致，四组新地图表双端bytes一致且与CSV生成结果一致；SHA256见`candidate-generation-hashes.json`。

真实失败回归包括未实现特色阶段、隐藏实体受拥挤分离、旧轮前地图上限、旧Slayer返回上限、旧路线断言，以及新演员攻击/附火表现；修正后才记为通过。早期RED日志保留，不将历史Console错误记录视为当前编译失败。正式表现验证另见NextStageViewValidation、NextThreeStagesApplied和NextStagesControllerApplied目录。

最终生成文案修正：通关提示改为“全部轮次与最终 Boss 已完成”，兼容旧40轮和新52轮。经Config权威源及正式gen.bat发布，9个源键的中英文值逐项与运行时JSON相符，生成exit=0。没有新增HUD或改变布局。

最终选择性客户端候选亦使用Unity现有编译响应文件/依赖，单独调用C#编译器编译Game库，exit=0（只有既有warning）。所有游戏业务源码均来自本任务Git候选树，避免同目录其他未提交牛群/喷火改动掩盖缺依赖；仅输出Temp，不做Unity打包或Player Build。见client-candidate-compile.txt。选择性合并时移动死亡方向计算的删除行一并保留，已编译确认没有重复局部变量。
