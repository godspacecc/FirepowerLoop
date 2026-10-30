# 幸存者移除持剑基础敌人

2026-10-30 用户以“那就去掉原来的基础敌人”替代前一句“把基础敌人也改成空手”。仅删除幸存者独立敌池的enemy.arcade.basic候选，不改闯关敌人、不改模型或武器资源；普通丧尸开局、瘦削120活动秒后加入，沿用已确认空手资源与动作。

权重保留普通40、瘦削25；普通从开局持续可选，无空敌池。刷怪总频率、属性成长及当前无存活上限规则不变。旧survivor_rule.EnemyId为兼容读取仍保留，不作为实际敌池抽取候选；不因此生成持剑基础敌人。

已从Config/DataCsv/survivor_enemy.csv删除候选，经Config/gen.bat双端生成，客户端与服务端tbsurvivorenemy.bytes SHA256同为4C9E60F129D8FCDC016C06892BCD5793932D19B07192C26D3BF470FDF007ABBB。enemy.csv及survivor_rule.csv相对本轮HEAD无工作区改动，未影响闯关。

限定EditMode短类且includePassingTests=true：SurvivorEnemyRosterTests最多8项，先6通过2失败，删除候选并生成后8/8；SurvivorEnemyCombatTests最多4项，4/4；GameplayResourceDistributionTests最多8项，8/8。合计20项通过，覆盖仅两类候选、开局/120秒解锁、实际生成属性、抓挠与取消/距离边界、真实BeginCollect地址和下载标签。未完整套件、未资源打包、未Player Build；源收集通过不代表旧发布包包含更新后的表，运行/发布包及整局试玩未验。

独立只读审查未发现阻断；最终绿色结果由主代理实际运行。Unity连接FirepowerLoop/Client，Boot场景未脏，编译失败标志false；保留历史Console错误。本次只提交本轮配置/生成bytes/回归期望/说明和任务记录，不夹带其它工作区或暂存变化；提交推送结果以Git回执为准。
