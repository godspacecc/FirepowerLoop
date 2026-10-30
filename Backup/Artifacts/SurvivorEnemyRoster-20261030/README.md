# 幸存者两类僵尸接入（最终范围）

用户先确认四种敌人的同框外观与尺寸，随后明确“后面两个敌人先不加入吧”。最终只接入普通僵尸和瘦削僵尸，弓箭手与重型精英暂缓；本批未修改正式动画、美术或共享敌人原值。

## 最终行为

- 保留原基础近战敌人；普通僵尸开局混入，瘦削僵尸120活动秒后加入。暂停及选择强化不推进时间。
- 权重基础60、普通僵尸40、瘦削25；瘦削本模式生命倍率0.55、速度1.7，其他倍率1；生成时再叠加既有时间成长。例2分钟瘦削生命74、速度约3.1824。
- 总存活上限60、画面外安全通道出生、经验与分数规则保留。新增僵尸追到共用抓挠射程，按原前摇起手、离开取消、命中后原0.55秒收招停步；消费既有抓挠表现。
- 新独立Luban敌池、运行时配置提供者、SurvivorSystem出生与攻击接通；EnemyCtrl准备阶段显式并入独立敌池ID，沿原预载缓存与取消/释放流程持有句柄。配置坏引用拒绝开局，不伪造替代值。
- 弓箭远程、精英数量与间隔相关候选及代码已移除。弓箭手落地动画只做过临时副本预览，未修改正式动画导入或资源。

## 采用与预览

[原同框动态预览](roster-preview.gif)及[静态预览](comparison.png)是临时隔离场景、正式主角同框、原Prefab尺寸，非正式对局。最终采用其中普通与瘦削僵尸；右侧两类不接入。制作源码Preview.cs.txt、render-preview.py保留以便追溯。正式Prefabs及.meta、模型、材质、贴图、动画均复用。

## 验证

所有Unity测试均EditMode真实非空短类，includePassingTests=true；未跑完整套件。

| 短类 | 最终结果 | 报告 |
| --- | --- | --- |
| SurvivorEnemyRosterTests | 8/8 | roster-final.json |
| SurvivorEnemyCombatTests | 4/4 | combat-final.json |
| SurvivorRuntimeTests | 30/30 | runtime-final.json |
| GameplayResourceDistributionTests | 8/8 | resources-final.json |

合计50项。独立复核指出精确攻击边界浮点误差，新增真实SurvivorNavigation与TickEnemies边界用例先3通过1失败（boundary-red.json），沿闯关+.0001f容差修复后4/4；复核无剩余阻断。最初敌池/战斗实现亦先复现缺失再实现，旧候选测试记录仅代表当时方案，不计入最终50项。

Config/gen.bat按最新两类范围双端39/35表生成成功（generation-two-enemies.log）；生成代码及bytes来自权威源。首次新增表时旧计数护栏拦截，调整表数后重新生成成功，历史日志保留。

真实BeginCollect("DefaultPackage",true,false)读取新tbsurvivorenemy.bytes、基础敌人和两类僵尸的精确地址及路径（collected-final.txt）。新表为startup，敌人为arcade；Boot原本下载startup+arcade。已核对源Prefab/.meta及序列化依赖文件存在；未将AssetDatabase渲染作为加载通过证据。资源回归直接枚举独立敌池，避免仅查闯关表遗漏。新表按现有Config目录收集，无额外修改收集设置。

未资源打包、未Player Build、未刷新发布清单；源收集通过不证明旧发布包包含新表，实际运行/发布包及包内依赖未验证。未进行整局人工试玩。Boot场景检查保持未脏，编译错误标志false；Console历史错误保留。

## 审查及提交

代码独立审查及边界修正复核均完成，无剩余阻断。只提交本任务文件和本节确认记录，保留工作区其它任务的未完成修改与暂存状态；提交推送结果以Git回执为准。
