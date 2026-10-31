# 幸存者进阶丧尸：保留不明显的弱点

2026-10-31用户澄清“有弱点 但是不明显”，随后对以下方案回复“可以 就这样”。普通丧尸100生命/10伤害/2.2移速/1秒冷却；瘦削90生命/12伤害/3.06移速/1秒冷却；笨重300生命/15伤害/1.98移速/1秒冷却。

瘦削比普通少10%生命，但伤害和速度更高；笨重慢10%，但生命与伤害更高。保持已确认体型、2/4分钟解锁、40/25/10权重、固定属性、刷新加速及无存活上限。本次不修改模型、动画、资源地址或共用enemy.csv。

权威改动：survivor_enemy.csv瘦削倍率0.9/1.5/1.7，笨重15/1.5/0.9；新增模式独立AttackCooldownSeconds列，三类均1。Schema/生成Bean/只读定义和TickMeleeEnemy串联，有限正数校验；不改闯关瘦削原2秒冷却。

实际Spawn回归发现倍率0.9末尾整数截断使90变89，使用Math.Round(...,MidpointRounding.AwayFromZero)后仍保留int.MaxValue钳制，实际出生为90；伤害计算不变。独立静态审查及该最小修正复核无阻断。

验证：Unity连接FirepowerLoop/Client，Boot未脏；限定EditMode短类、includePassingTests=true，SurvivorEnemyCombatTests最多14项、14/14通过，SurvivorEnemyRosterTests最多9项、9/9通过，共23项。最初14项11过3失败，确认旧55生命/10伤害/2秒冷却；修改后13过1失败，定位90→89取整问题，修复后全过。记录分别见tests-red.json、rounding-red.json、combat-green.json、roster-green.json。共享工作区含其它已授权农场爬出改动，本轮测试在组合工作区执行；独立提交仅含数值、冷却、取整及对应回归，不夹带农场逻辑或显示界面。

Config/gen.bat双端生成成功，敌池bytes一致，SHA256：9e4d28644ab5c890931e825ffb119f6145cc6236ca15c3e7fd06064615e3acb2。生成日志generation.log。未新增动态加载地址，沿既有加载链，不以配置读取测试宣称发布包已更新。未完整套件、未资源打包、未Player Build、未运行/发布包及整局试玩验收。

仅提交本轮相关改动及任务记录，保留其它工作区和暂存内容。提交推送以Git回执为准。
