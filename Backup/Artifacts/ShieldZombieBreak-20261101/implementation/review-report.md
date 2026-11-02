# 盾牌破坏独立审查

2026-11-02。范围：brief.md 末尾继续授权、implementation/changes.json 和 review.patch 的变化及其真实调用链；包括P2修复后增加的EnemyView.cs（合计19文件）及fix.patch。按 code-role、luban-config、resource-load-check 审查。未操作 Unity、未重跑测试、未修改代码或 index。

## 结论

Spec：伤害、配置与已采用的破盾立即隐藏表现符合要求。初审1项P2已经修复并复审关闭，当前没有未解决P1/P2。

Quality：状态归属 System、完整快照与表现转发方向合理，生成边界及原有资源加载事务保留。最小修复使死亡清理与新用途装备恢复分离，未发现修复引入新的P1/P2。代码审查通过；正式clone截图采样与运行/发布包边界仍由root分别说明。

## 已关闭 [P2] 破盾后的死亡管线重新显示盾牌

位置：`Client/Assets/Game/Scripts/Controller/View/Map/EnemyView.SpecialZombies.cs:40`，新增 `SetSurvivorShield(true)`。

初审触发：玩家先打掉盾，再造成致命伤。`ApplyModifierDamage` 调用 `view.RemoveEnemy(id,true)`；`EnemyCtrl.RemoveEnemy`（401–402）调用 `TryPlayPhysicalDeathWithImpact` 或 `TryPlayDeathAnimation`。两个方法分别在修复前 `EnemyView.cs:802`、`:775` 调用 `ResetSurvivorSpecial()`。当时这个共用清理方法会重新开启 ShieldSteel 和两个 ShieldCross Renderer。随后尸体被保留用于死亡表现，故玩家会看到已经打掉的盾在死亡瞬间重新出现。此时实体已从权威敌人字典移除，不会再收到破盾快照纠正。

复审：已检查fix.patch和当前源码。`ResetSurvivorSpecial()`仅关闭预警并清抬手记录，不再恢复盾；`SetSurvivorShield(true)`仅位于`BeginPresentationUse`（当前EnemyView.cs:210）与显式`ResetPhysicalFeedback`（:830）。死亡两个入口仍清特殊前摇，但保持三个装备当前可见性；禁用也不再意外补盾。`EnemyCtrl.RecycleArcadeEnemy`明确调用显式复位，池复用及新用途仍恢复完整盾。其它显式复位调用属于NextStages演员准备/阶段隐藏，不是幸存者盾实体死亡管线。权威盾耐久及完整快照没有改动。

已有正式Prefab回归扩展为动画死亡/物理死亡两个参数用例，必须实际进入死亡分支，并断言破盾装备仍隐藏，显式回池和新用途分别恢复。root的death-red.json中两个用例均以“死亡不能复原已破盾装备”失败；修复后visual-final.json完整6/6通过，包含两死亡分支、三种正式Prefab及真实收集器检查。该P2关闭。

## 已核对

- `ApplyModifierDamage` 是弹丸、爆炸、真实灼烧与燃烧瓶火区共同扣血入口；`ShieldDamage` 无来源/角度过滤，耐久>0时先扣盾并返回0，破盾当次不向生命溢出，随后伤害正常扣血。
- SpecialState 仅首次建立读取配置；重复完整投影不补盾。死亡、Stop 的权威状态清理存在。近场唤醒在通用敌人表现之后投影盾快照。
- 正式显示方法仅筛选 ShieldSteel / ShieldCross，不隐藏身体或关闭碰撞。复用初始化可恢复三个装备。
- Config权威源以 ShieldDurability:int 替换旧减伤/角度列，盾100、其余0。盾150HP、阶段权重及其它敌人参数保持；MoveSpeed=2.2、模式倍率1，与普通一致。
- 双端生成类型均以 ReadInt 读取 ShieldDurability；generation.log 有正式双端生成完成记录。无现役 ShieldReduction/ShieldHalfAngle 引用。本审查未重新生成。
- 实际地址仍为 Prefabs/Characters/ArcadeZombieShield；既有 EnemyCtrl 准备事务枚举独立敌池并走正式资源加载及句柄清理。本次未新增地址或修改Prefab。

## 验证证据及边界

已读取 root 产物 rules-green.json（21/21）、visual-green.json（初轮5/5）、facing-green.json（5/5）、death-red.json（两死亡回归预期2失败）、visual-final.json（修复后6/6）及 implementation/collection.txt。当前有效限定覆盖为21规则+5朝向+6表现，共32项通过，死亡缺口已由修复后的正式Prefab回归覆盖。真实 BeginCollect 结果含唯一盾Prefab地址、arcade标签、bundle及依赖；可证明源收集规则。本审查未执行这些测试。

rules-red / visual-red 及death-red的先失败再恢复过程由root执行并记录，本审查未重复。根代理另行采样正式clone可见截图，本报告不将静态审查和EditMode证据说成整局实玩验收。

当前运行/发布包未由本审查验证；未触发打包、模拟构建或Player Build。没有提出新玩法、资源或审批要求。
