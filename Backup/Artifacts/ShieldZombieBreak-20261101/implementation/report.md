# 盾牌修正实施与验证

2026-11-02，按brief末尾用户“继续”的授权，完整功能和破盾立即隐藏钢板及两十字装饰均实施。

## 当前实现

- Config权威源将旧方向减伤字段替换为ShieldDurability:int，盾100/其余0；盾速度1.43→2.2，与普通相同。150生命、原阶段/权重、其余敌人参数全部保留，静态不变量见static-checks.json。
- System持有本局剩余耐久，所有方向、爆炸、步枪/无人机、真实燃烧和火区都经过ApplyModifierDamage统一扣盾。破盾当次全部吸收不溢出，后续伤害正常扣血。既有死亡/Stop清理状态。
- 完整只读投影SetSurvivorEnemyShield(id,intact)经ArcadeCombatViewController、EnemyCtrl到EnemyView.SetSurvivorShield。只控制现有ShieldSteel及两个ShieldCross Renderer，身体/碰撞不改；回池和新用途恢复，休眠唤醒完整投影重现破盾状态，重复投影不补盾。
- 无新资源/Prefab修改/动态地址变更；实际加载链沿既有敌池准备和正式资源加载，详见resource-chain.md。

## 配置生成与基线

正式Config/gen.bat双端完成退出0，客户端41表、服务端37表，日志generation.log。两端生成配置与bytes保持原始正式生成输出，无手改。当前权威源与生成产物对应。

baseline保存本次修改前真实工作区手写文件、源配置及全部双端生成产物/bytes和客户端本地化，baseline-manifest.json记录SHA256；不是HEAD替代。仅19文件相对本批基线发生变化，changes.json及review.patch可审查。

## 已执行红测

root找回Unity MCP实际endpoint后通过正式tests-run执行，先将全部生产与生成恢复到本批真实基线并逐个SHA256验证，保留新测试：

- ../rules-red.json：21项，9通过、12失败，正/侧/后、无方向、实际Rifle/Drone、爆炸、破盾/后续、持续燃烧/火区、速度及完整投影符合预期失败。
- ../visual-red.json：5项，4通过、1失败，缺少破盾装备控制方法而失败。

红测后已恢复green快照并逐个after SHA256验证，当前实现已在正式源码与生成目录。恢复只回放正式gen产物，没有手改生成。

## 初轮green验证

root已通过正式MCP执行真实非空短类：SurvivorSpecialEnemyTests 21/21、SurvivorSpecialVisualTests 5/5、SurvivorSpecialFacingTests 5/5，共31项通过。includePassingTests=true，无全套或无效过滤回退。真实收集输出位于本批implementation/collection.txt，可证明源收集地址、标签及依赖。

独立审查发现死亡分支遗漏，修复和专项再验证见下节。当前运行／发布包未验证，不打包、不模拟构建或Player Build；本代理无index、提交、worktree及Unity入口操作。

## P2死亡生命周期修复

root通过正式MCP先完成rules-green 21/21、visual-green 5/5、facing-green 5/5，共31/31。独立审查随后发现：共用ResetSurvivorSpecial被动画死亡与物理死亡调用，原SetSurvivorShield(true)会让死亡尸体重新拿回盾，原31项未覆盖该分支。

已先扩展既有正式Prefab测试为两参数用例，root执行../death-red.json，两个死亡分支2/2均按“死亡不能复原已破盾装备”失败。之后作最小修复：ResetSurvivorSpecial只清预警/抬手；盾恢复限定在BeginPresentationUse与显式回池ResetPhysicalFeedback。死亡、禁用保持当前装备可见状态。

新增EnemyView.cs修改前真实工作区基线已补baseline及manifest；P2修复前3文件保存在pre-p2-fix，最小修复差异fix.patch，完整19文件差异见review.patch和changes.json。静态确认SetSurvivorShield(true)只在新用途与显式回池两个入口调用。伤害与配置设计不变、不重生成配置。

修复后待root跑SurvivorSpecialVisualTests短类，上限6（原4项+动画/物理死亡2项），includePassingTests=true；或精确方法BrokenShieldHidesOnlyEquipmentAndReuseRestoresIt参数展开上限2。当前尚未把P2修复称为已通过测试。

## Root最终验证

通过项目MCP endpoint标准tools/call执行同一tests-run：规则21/21、朝向5/5、最终表现6/6，32个不同用例通过。死亡问题两个分支先红2/2再最终表现6/6通过，见../death-red.json及../visual-final.json。原待验证描述为历史阶段记录，以本节为准。正式View克隆截图root目检通过，场景Boot干净非Play；未完整Play/发布包检查或打包。独立复审关闭唯一P2。
