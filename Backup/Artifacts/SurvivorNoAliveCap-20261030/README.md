# 幸存者取消存活数量上限
- 用户要求：幸存者模式不设置敌人存活上限。
- 实现：删除 Spawn 中的存活预算拦截；每次仍只生成1只，时间加速、暂停、安全画面外出生和最小距离保持原逻辑。
- 兼容：旧配置字段 MaxAliveEnemies 保留数据格式，但不再参与幸存者刷怪裁决；未修改配置和生成产物。
- RED：新增真实方法 Game.Tests.SurvivorRuntimeTests.SpawnContinuesBeyondFormerAliveLimit，最多1项、includePassingTests=true；尝试125只，旧实现实际60只，断言失败。
- GREEN：SurvivorRuntimeTests，最多40项、includePassingTests=true；实际31/31通过，包含125只累积、安全出生、暂停及战斗回归。见 red.json、green.json。
- Unity路径 E:/GitHub/FirepowerLoop/Client/Assets，Boot编辑态；Console查询前的两次反射方法调用失败为诊断调用问题，正确查询后计数0/18/0。
- 未执行Player Build，未进行长时间海量敌人性能验收，不宣称无限数量时性能已验证。
- 补充审查：已同步敌池旧上限测试，SurvivorEnemyCombatTests最多4项、includePassingTests=true，4/4通过；合计35项通过，独立复审无阻断。
