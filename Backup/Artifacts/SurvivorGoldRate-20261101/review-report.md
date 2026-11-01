# 幸存者金币掉率独立审查

## Spec：概率行为通过

实际TryDropGold在共享Chance完成强度计算和封顶后再乘GoldDropMultiplier，源配置0.1，因此所有敌人最终概率降为原1/10，50%封顶变5%。击杀调用集中进入TryDropGold；每份1、经验、账本与共享闯关规则没有本任务行为改动。倍率配置拒绝NaN/Infinity及范围外数值，0和1边界合法。tests-result.json为指定真实方法1/1通过，generation.log记录双端完成。未重跑测试或操作Unity。

## Quality：需修复一项P2，并隔离提交材料

P2：Config/Defines/beans.xml:631把GoldDropMultiplier插到Id前，表未显式配置索引，导致两端生成TbSurvivorRule.cs:16、29、33的主键由string Id变为float GoldDropMultiplier，Get/GetOrDefault及索引器参数也从string变float。这不是掉率参数改动应改变的表身份契约；当前DataList读取测试无法发现此回归。请将新增字段放在Id之后（或显式保持Id主键），仅经Config/gen.bat重新生成两端，核对DataMap及Get仍用string、记录仍按Id索引。

提交材料：review.patch并非纯任务增量，包含SurvivorConfigTests中RecoveryStill/RecoverySafe/RecoveryAlways与16→19断言，以及执行总清单的夜间范围预览记录。它们显然属于其他任务，本审查不评价其正确性，提交前必须排除，不能整份patch暂存。另有换行噪声，建议恢复原换行以便复核；这不影响概率语义。

验证边界：测试用真实bytes证明配置与数学关系，并用源码子串验证乘法位置，未执行实际随机掉落路径；小型纯倍率修改的静态调用审查支持概率结论，不宣称实机统计验收。待主键修复后可范围复审，不需扩大到其它功能。

## P2范围复审：已关闭

最终Spec通过；Quality通过本任务源码范围，无新增重要问题。GoldDropMultiplier已置于Id之后，两端TbSurvivorRule恢复Dictionary<string,...>、按_v.Id入表及Get(string)。新增真实bytes加载后的Get("survivor.default")断言验证身份契约，tests-fix-passed.json对应方法1/1通过。未重跑测试或操作Unity。

最新review.patch已移除回血断言、夜间范围说明及换行噪声。当前两端bytes含并行NightAimRange，报告也明确标注，不能把该联合产物配上仅金币字段的生成C#提交。提交前须等待相关任务完整提交，或通过权威Config/gen.bat针对隔离源重新生成匹配的C#与bytes；不得手改二进制或生成代码。本次通过不等于已经核验最终暂存树的生成一致性。
