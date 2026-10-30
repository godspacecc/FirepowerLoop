# 幸存者取消敌人时间成长
- 用户确认取消生命、攻击伤害、移动速度随生存时间增长；刷新加速、敌种时间解锁、各敌种固定倍率和无存活上限保持。
- Config/DataCsv/survivor_rule.csv三项GrowthPerMinute归零；只读配置校验由有限正数改为有限非负数，仍拒绝负数、NaN、无穷。
- Config/gen.bat双端生成成功；规则bytes SHA256均为C628FFF33DC40D4AFB5447626F4DD102C0B09240D4B8DF79068CAF842B1472DE。
- RED：真实Spawn规则3个固定随机样本，第4分钟与第60分钟生命不同，3/3按预期失败。
- GREEN：SurvivorConfigTests最多13项，13/13；SurvivorEnemyCombatTests最多9项，9/9。includePassingTests=true。三个敌种出生生命/伤害/移速恒定，原有解锁/攻击/安全出生及无存活上限测试保持通过。
- 独立只读审查无阻断；未执行Player Build，未长时间整局人工试玩。
- 工作区前置问题：已有未提交体型代码/CSV使用ScaleX/Y/Z，但schema缺少相应字段；按现有CSV补回三字段后正式生成。另修正已有未提交测试中的不存在接口ICanGetArchitecture为IBelongToArchitecture；重型敌人旧成长期望同步为固定基础值。这些体型任务相关差异保留于工作区，不混入本次取消成长的提交；本次提交仅含成长配置、校验、相关测试及记录。
