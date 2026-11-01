# 幸存者弹夹互斥与生命恢复

2026-11-01，用户对本会话短玩法方案回复“可以”，批准实施。

本局快速弹夹／扩容弹夹二选一，静养／休整／再生三选一；首次选择锁定同组路线，另一组独立，已选路线继续升级。基础弹夹30发，扩容每级基础容量+10%；快速弹夹每级减0.2秒，最多5级，从2秒到1秒。

恢复均5级，按当前最大生命比例每秒恢复：静养连续站定2秒后每级1%，移动重计；休整连续5秒未受实际伤害后每级0.6%，伤害重计；再生每级0.3%持续恢复。触发阈值只计算之后的时间，零点几生命累积到整数后入账。暂停、选卡、加载等待不推进；满血丢弃余量、死亡不复活、退出清除进度。静养同时检查移动输入与实际位置变化。

配置唯一权威为Config/Defines/beans.xml与DataCsv/survivor_upgrade.csv。新增ExclusiveGroup、ActivationDelaySeconds字段及三条恢复数据，旧编号保持，新增17—19；经Config/gen.bat双端生成。UI只复用现有卡片及中文／英文本地化，不新增Prefab、HUD、特效或资源寻址。

## 验证

- 红阶段：SurvivorRecoveryTests 17项全部按预期失败，两个弹夹可共存，恢复配置缺失；red.json。
- 最终SurvivorRecoveryTests：18/18，包含真实接触伤害打断恢复；recovery-green.json。最多24项。
- SurvivorConfigTests：17/17，含比例、延迟与分组、无效延迟拒绝；config-green.json。最多24项。
- SurvivorRuntimeTests：74/74；runtime-green.json。声明最多70项，实际参数／方法展开74，超估4项，本报告不掩盖该范围估算错误；未运行全项目套件。
- SurvivorUiTests：12/12；ui-green.json。最多20项。文案首轮高度104.26超过100，已精简，最终中英首／末级恢复与弹夹16种样例高度均69.51，原卡片400×100，未改布局。
- SurvivorEnemyCombatTests：15/15；enemy-green.json。最多16项。
- 所有上述Unity测试均EditMode、真实短testClass过滤、includePassingTests=true。
- Config/gen.bat双端成功：generation.log；排除共享未提交金币与昼夜配置后，使用已提交Config权威加本任务源、同一gen.bat再次生成：generation-scoped.log。仅提交SurvivorUpgradeConfig及其bytes，不夹带其它配置产物。
- 本任务独立索引的全部Game运行C#使用Unity现有编译响应与Roslyn编译，退出0：scoped-compile.log。仅C#编译，无Unity资源构建或Player Build。存在项目已有的隐藏成员、过时API和未使用字段警告。
- 请求只读审查代理review_survivor_recovery：未发现确定的重要问题；其建议补充真实攻击链已由RealContactDamageRestartsSafeRecovery覆盖并通过。

共享工作区在最终检查中一度因其它昼夜任务的SetSurvivorVisibility接口尚未补齐而无法编译；未修改或撤销该项工作。编译恢复后复跑恢复18项与UI12项通过。本任务的源码与Config产物使用独立索引按最新已提交HEAD叠加，保留其它未提交改动。

验证边界：136个不同的限定检查通过；未整局人工试玩，未验证发布包或设备，未打包。既有动态资源地址与收集规则无变更，本次纯规则／数值不引入动态资源。
