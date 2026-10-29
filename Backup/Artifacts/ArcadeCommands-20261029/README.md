# 街机测试指令补充

用户确认范围：道具列表、跳关跳轮、无敌、指定刷怪。旧目录整理和其它候选指令不在本批范围。

| 单人指令 | 用途 |
| --- | --- |
| `#arcade drops` | 列出实际掉落ID与名称；名称缺失时显示明确类型 |
| `#arcade goto 2 3` | 进入当前路线第2关第3轮临时测试现场，保留正常局存档 |
| `#arcade god on` / `#arcade god off` | 开关当前现场无敌 |
| `#arcade spawn enemy.arcade.basic 1` | 生成指定敌人，单次1至20且不超过当前存活上限 |

双人使用相同子命令的`$arcade`入口，只允许房主请求、服务器权威执行。无敌只作用房主。状态不允许或参数无效时明确拒绝。某些特殊现场不支持指定刷怪；旧分支路线无法唯一确定目标时拒绝跳关。

跳关不发放跳过关卡奖励，测试现场后续读写与正常续局节点隔离。当前检查使用真实领域逻辑和替代显示端口，不代表真人双客户端画面验收。

## 验证证据

- `final-0.json`：限定TestCmdTests，22/22通过，包含通过项；相机旧夹具已改为现役街机就绪事件，不修改相机行为。
- `final-1.json`及`server-command-check.log`：限定ArcadeCommandOnline，1项检查内包含伤害、房主作用范围、刷怪队列/容量、跳关映射和保存隔离断言。
- `goto-review-fix-red.json` / `goto-review-fix-green.json`：连续跳关同步异常的代际保护回归。
- `config-generation.log`：Config/gen.bat权威生成成功；指令表属客户端组，服务端目录由处理器下发。
- `baseline-hashes.json`：任务前原有海岛、字体、ProjectSettings三份差异保护基线。

未运行完整套件、Player Build或真人双端试玩；完整网络路由和双方广播的动态验收未包含在上述独立Actor检查中。

最终审查与Git收尾以执行总清单和主执行者后续记录为准。

## 最终复审与验证

- 独立审查修复4项：单人连续跳关异常丢失正常存储入口、双人重复跳关代次复用、恢复正常节点版本倒退、购物区域换图无敌残留；针对性复审全部通过，无剩余P1/P2。
- 最终24个不同Unity用例通过（TestCmdTests22、OnlineArcadeCommandGateTests2）；服务端ArcadeCommandOnline1项通过。最新服务端与门禁输出见verified-fix-0.json、verified-fix-1.json。
- 真实生成bytes回读确认新增5条目录，三份原有工作区文件SHA256保持。恢复正常联机记录本身不写盘，之后正常保存可以更新版本号，仍不混入测试进度。
- 未运行完整套件、Player Build或真人双客户端传输验收。准备按项目授权中文提交推送。
