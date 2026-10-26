# Task A 独立审查

审查范围：`task-a-brief.md`、实现报告、`task-a-review.diff`（525e828e → 未提交 Task A），以及其直接调用方、采用源、配置生成和验证证据。使用 requesting-code-review 的审查模板，并按 code-role / luban-config 检查运行时职责及配置边界。没有调用 Unity、运行测试、修改生产文件、索引或分支；只写本报告。

## Strengths

- 运行时改动小且集中：`ArcadeCombatViewController.EarlyStages.cs:56-87` 将原巨石四轮校验/选择扩展到 7/10/20，保留原轮根所有权、迟到回调代次检查、失败清理及重试路径。`RoundContent.cs:60-88` 中原加载事务未另建并行发布路径；三场跳过旧程序主题，入口仍沿用原生成规则。
- `ArcadeEarlyStageParameters.cs:34` 的共享白名单和客户端/服务端目录校验一致，使用排序序列精确拒绝缺失、重复及多余项。CSV 只新增三行，19.5 半宽/半高、零牛群和零传送门不引入玩法分支；真实战斗 EarlyStage 仍限 21–25。
- 采用源文件 SHA256 重新读取为 `DF1DF570926D6DF510A3CD602D930A2441B34630E4C9A08F6776DAD266E51A44`，与批准源及烘焙 guard 一致。烘焙先验证十二轮再持久化，保留既有资产/meta，按语义障碍分组合并而未丢失独立足迹。
- `TenStageEnvironmentBake.cs` 与 `TenStageEnvironmentReadback.cs` 对十二轮记录逐面世界顶点/法线、材质颜色/阴影摘要、足迹和外景位置，并独立从持久 prefab 回读。材质在批准源中由颜色唯一命名、固定 shader/光滑度/金属度生成；在本次已固定源下，按材质名共享没有发现误合并风险。
- 测试使用真实 prefab 与配置，替换的只是资源获取边界；覆盖四轮切换、旧回调释放一次、缺轮发布前失败及重试、目录缺失/重复。没有仅验证实现内部布尔值的空壳测试。

## Issues

### Critical

无。

### Important

无已确认的实现缺陷。提交前验证仍有两项未闭环，见下述门禁；不将缺证据等同于已发生回归。

### Minor

无需要为本任务修改代码的问题。

## 验证证据与限制

- 已读取 `tests-green.json`：限定 `TenStageEnvironmentAppliedTests`，17/17 通过、0 跳过；已读取服务端日志：限定 `TenStageEnvironmentOnline`，3/3 通过。未重复运行。
- 已读取双端生成日志的正式完成输出。当前客户端和服务端 `tbarcadeearlystage.bytes` SHA256 同为 `7F2D6DB9EAD80E0351EA5A49F8679331CAF6872B7B37506F5EA9DF6A00F0A08E`。
- 三份回读 TSV 均有四轮 `persistent-equivalence` 和 `FitsArena=true`，共十二轮；摘要与 bake 记录相符。回读是隔离编辑态、正式资源及生产轮级路径，不代表真实联机或自然通关。
- `README.md` 的“尚需统筹核实的编辑器收尾”如实说明：`henge-regression.json` 为另有测试运行中的拒绝响应，`final-probe.json` 为连接失败。不能宣称巨石旧场地回归已通过或最终 Console 已干净。主代理修复连接后应按原限定范围完成 HengeAppliedTests（最多8项、includePassingTests=true）及本项目最终场景/Console/空闲状态探测；不要扩大测试过滤或运行套件。在这两项闭环以前保持不提交/不推送。
- 静态审查可确认 Task A diff 没有删除原资源或改写候选源，但初始33项差异及并行 Task B 导入副作用的最终提交归属仍须主代理用最终路径清单核对；不能仅凭本审查替代最终暂存区检查。

## Declined to judge

- Task B 角色/小龙实现与其导入变化：明确由并行任务拥有，未纳入本次 A 的 scoped diff。
- 实体设备性能、真实双端联机和自然全局通关：不是本次约定的验证交付，本报告不声称已覆盖。
- 最终编辑器运行状态和 Henge 实测结果：本审查被要求不调用 Unity，现有失败响应不能支持结论，交由主代理补证据，未静默忽略。

## Assessment

规格符合性：通过当前静态和既有证据审查。代码质量：通过，未发现可复现的 Critical / Important / Minor 缺陷。

**Ready to merge? With verification completed.** 当前尚不可据此提交：先完成已声明的 Henge 回归与最终 Unity 探测，再由主代理核对本次精确暂存范围。无需为了本审查重跑已通过的17+3项。
